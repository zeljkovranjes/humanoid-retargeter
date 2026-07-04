// UI smoke gate: headless end-to-end through the SAME code paths the retarget window uses.
//
// Only runs when the HR_UI_SMOKE environment variable is set (done by
// dev/editor-rig/run_ui_smoke.ps1). Normal users of the library never trigger it.
//
// What it does (inside a real sbox-dev.exe editor session):
//   1. waits for the project + asset system to be ready
//   2. loads the source fixture (HR_UI_FIXTURE, an .fbx) via SourceFileEntry.Load -
//      the exact add-file path of the window (user preset lookup -> preset detection
//      -> auto map) - plus Retargeter.Inspect for the report; also loads the
//      footstep-bearing fixture (HR_UI_FIXTURE_STEPS, a forward-moving mocap walk)
//      converted alongside it in the same batch
//   3. resolves the s&box default target via TargetPickers.SboxDefault (window path)
//   4. Retargeter.ConvertBatch with the entry's mapping as override (window path), with
//      footstep events + additive variants ON and locomotion-set detection ON (single
//      clip -> no family; asserts the no-set path)
//   4.5 constructs the RetargetWindow itself (stacked options layout), asserts the
//      footstep-events / mirrored-variants / additive-variants / locomotion checkboxes
//      reach BuildRequest + BuildBatchOptions, and probes the locomotion toggle's
//      smart-disable (no directional family -> disabled + forced off; 4-way -> enabled)
//   5. constructs a PreviewWidget on the result, applies a solved frame headlessly and
//      draws the source-ghost overlay (the preview's "Show source" toggle)
//   6. round-trips a user preset (UserPresets.Save -> TryLoad) for the fixture rig
//   7. EditorPipeline.WriteAndCompileAsync: DMX + standalone vmdl into Assets,
//      RegisterFile + Compile, polls the .vmdl_c; the written vmdl must carry the
//      AE_FOOTSTEP AnimEvent nodes
//   8. Model.Load on the compiled vmdl, verifies the converted sequences are visible -
//      including the additive '<clip>_delta' twins (AnimSubtract compiles in-engine)
//   9. AUGMENT mode (HR_UI_SMOKE_AUGMENT = absolute path of a vmdl inside the scratch
//      Assets): drives the EXACT Convert-All window path (RetargetWindow.ConvertAndWriteAsync)
//      against that vmdl. The fixture vmdl references meshes a scratch project cannot
//      resolve, so a missing-mesh compile failure is acceptable - the assertions are:
//      augmented vmdl + .bak written, asset registered, compile poll COMPLETES, no
//      "quiet inputs ... abandoning recompile" for our vmdl, a PLANTED stale .vmdl_c is
//      never reported as a compile success (timestamp-verified compile), the install-path
//      guard rejects the shipped citizen vmdl, and the editor survives.
//  10. writes a JSON result to HR_UI_SMOKE and quits the editor
//
// Safety: refuses to do anything when the open project is not the hr-editor-rig scratch
// (a leaked HR_UI_SMOKE env var must never write into - or quit - a real session).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Mapping;
using Sandbox;

namespace HumanoidRetargeter.Editor;

public static class UiSmokeGate
{
	static bool _started;
	static readonly SmokeResult Result = new();
	static string _resultPath;

	/// <summary>Assets-relative folder the smoke run writes its outputs to (cleaned by the
	/// driver script before each run).</summary>
	public const string OutputFolder = "humanoid_retargeter_smoke";

	[EditorEvent.Frame]
	public static void Tick()
	{
		if ( _started )
			return;

		_started = true;

		_resultPath = Environment.GetEnvironmentVariable( "HR_UI_SMOKE" );
		if ( string.IsNullOrWhiteSpace( _resultPath ) )
			return; // not a smoke run - do nothing, ever

		// One-shot arming marker, written by the driver script immediately before launch
		// and CONSUMED here. The env var alone must never arm the gate: a gate run that
		// boots Steam as its child leaks HR_UI_SMOKE into Steam's environment, and every
		// editor the user launches through that Steam afterwards inherits it - observed as
		// the user's own scratch-project session quitting itself seconds after opening
		// (same failure mode as M0Gate).
		var marker = _resultPath + ".arm";
		try
		{
			if ( !File.Exists( marker ) )
			{
				Log.Info( "[hr-ui-smoke] HR_UI_SMOKE is set but there is no arming marker - "
					+ "leaked env var (stale Steam environment?), ignoring; not a smoke run" );
				return;
			}
			File.Delete( marker );
		}
		catch
		{
			return; // cannot verify/consume the marker - err on never running
		}

		_ = RunAsync();
	}

	static async Task RunAsync()
	{
		Note( "UI smoke gate starting" );
		Result.engineBooted = true;
		Flush();

		try
		{
			await RunGateAsync();
		}
		catch ( Exception e )
		{
			Note( $"EXCEPTION: {e}" );
		}

		Result.completed = true;
		Result.passed = Result.dmxVmdlCompiled && Result.compiledFileFresh
			&& Result.sequenceVisible && Result.footstepEventsInVmdl
			&& Result.previewWidgetOk && Result.userPresetRoundTrip
			&& Result.windowConstructed && Result.optionsPlumbingOk
			&& Result.locomotionSmartDisableOk
			&& Result.dlSolverOk && Result.citizenTargetOk
			&& (!Result.augmentMode || Result.augmentOk)
			&& (!Result.customMode || Result.customOk);
		Flush();
		Note( $"UI smoke gate finished, passed={Result.passed}" );

		// A leaked env var in a real session must never quit the user's editor.
		if ( Result.refusedWrongProject )
			return;

		// Give the driver a moment to see the completed file, then exit cleanly.
		await Task.Delay( 1000 );
		try
		{
			EditorUtility.Quit( true );
		}
		catch ( Exception e )
		{
			Note( $"EditorUtility.Quit threw: {e.Message}" );
			Flush();
		}

		// Backstop if Quit() did not take the process down.
		await Task.Delay( 10_000 );
		Environment.Exit( Result.passed ? 0 : 1 );
	}

	static async Task RunGateAsync()
	{
		// ---- 1. wait for project + asset system --------------------------------
		Result.assetSystemReady = await WaitUntil(
			() => Project.Current is not null && AssetSystem.All.Any(),
			timeoutSeconds: 120 );
		Note( $"assetSystemReady={Result.assetSystemReady}" );
		Flush();

		if ( !Result.assetSystemReady )
			return;

		// Never touch a real session: only the hr-editor-rig scratch project is fair game.
		// (Observed failure mode: gate env vars leaking into a user-launched editor wrote
		// gate outputs into whatever project happened to be open.)
		var rootPath = Project.Current.GetRootPath() ?? "";
		if ( rootPath.IndexOf( "hr-editor-rig", StringComparison.OrdinalIgnoreCase ) < 0 )
		{
			Result.refusedWrongProject = true;
			Note( $"REFUSING to run: open project '{rootPath}' is not the hr-editor-rig scratch "
				+ "(leaked HR_UI_SMOKE env var?) - aborting without touching the project" );
			Flush();
			return;
		}

		var assetsPath = Project.Current.GetAssetsPath();
		Note( $"project={rootPath} assets={assetsPath} mainThread={ThreadSafe.IsMainThread}" );

		// ---- 2. fixture in via the window's add-file path ----------------------
		var fixture = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE" );
		if ( string.IsNullOrWhiteSpace( fixture ) || !File.Exists( fixture ) )
		{
			Note( $"fixture not found (HR_UI_FIXTURE='{fixture}')" );
			Flush();
			return;
		}

		var inspect = Retargeter.Inspect( File.ReadAllBytes( fixture ), Path.GetFileName( fixture ) ).Mapping;
		Result.inspectProfile = inspect.ProfileName;
		Result.inspectConfidence = inspect.Confidence;
		Result.inspectNeedsUserDecision = inspect.NeedsUserDecision;
		Result.skeletonSignature = inspect.SkeletonSignature;
		Note( $"Inspect: profile={inspect.ProfileName} conf={inspect.Confidence:0.00} "
			+ $"needsUserDecision={inspect.NeedsUserDecision} sig={inspect.SkeletonSignature}" );

		var entry = SourceFileEntry.Load( fixture, assetsPath );
		Result.entryStatus = entry.Status.ToString();
		Result.entryChip = entry.ChipText;
		Note( $"SourceFileEntry: status={entry.Status} chip='{entry.ChipText}' clips={entry.ClipCount}" );
		Flush();

		if ( entry.Scene is null || entry.Mapping is null )
		{
			Note( $"fixture entry unreadable: {entry.StatusDetail}" );
			Flush();
			return;
		}

		// Footstep-bearing second fixture (HR_UI_FIXTURE_STEPS, a forward-moving mocap
		// walk the driver supplies): the main fixture is a crawl whose feet never plant,
		// so the AE_FOOTSTEP-through-compile assertion converts this one alongside it.
		// Missing/unreadable: the batch still runs, footstepEventsInVmdl then fails.
		var stepsFixture = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_STEPS" );
		SourceFileEntry stepsEntry = null;
		if ( !string.IsNullOrWhiteSpace( stepsFixture ) && File.Exists( stepsFixture ) )
		{
			var loaded = SourceFileEntry.Load( stepsFixture, assetsPath );
			if ( loaded.Scene is not null && loaded.Mapping is not null )
				stepsEntry = loaded;
			else
				Note( $"steps fixture unreadable: {loaded.StatusDetail}" );
		}
		Note( stepsEntry is null
			? $"steps fixture unavailable (HR_UI_FIXTURE_STEPS='{stepsFixture}') - the footstep assertion will fail"
			: $"steps fixture: {stepsEntry.FileName} clips={stepsEntry.ClipCount} profile={stepsEntry.Mapping.ProfileName}" );
		Flush();

		// ---- 3. target via the window's picker path ----------------------------
		TargetPickers.ResolvedTarget target;
		try
		{
			target = TargetPickers.SboxDefault();
		}
		catch ( Exception e )
		{
			Note( $"TargetPickers.SboxDefault failed: {e.Message}" );
			Flush();
			return;
		}
		Result.targetResolved = true;
		Note( $"target: {target.Description} previewModel={target.PreviewModelPath}" );
		Flush();

		// ---- 4. ConvertBatch exactly like RetargetWindow.BuildRequest ----------
		// Footstep events + additive variants ON: the written vmdl then carries AnimEvent
		// and AnimSubtract nodes through the REAL compile below (steps 7/8 assert the
		// '<clip>_delta' sequence is visible on the compiled model and the events are in the
		// vmdl text). DetectLocomotionSets ON too - neither batch clip is directional, so
		// no family forms, which pins the no-set path against crashes.
		var request = new RetargetRequest
		{
			SourceData = entry.Bytes,
			SourceFileName = entry.FileName,
			MappingOverride = entry.Mapping,
			RootMotion = Cleanup.RootMotionMode.Off,
			FootPlantCleanup = true,
			ArmEffectorIk = false,
			GenerateFootstepEvents = true,
			CreateAdditiveVariant = true,
			LoopingOverride = null,
		};
		var requests = new List<RetargetRequest> { request };
		if ( stepsEntry is not null )
		{
			requests.Add( new RetargetRequest
			{
				SourceData = stepsEntry.Bytes,
				SourceFileName = stepsEntry.FileName,
				MappingOverride = stepsEntry.Mapping,
				RootMotion = Cleanup.RootMotionMode.Off,
				FootPlantCleanup = true,
				ArmEffectorIk = false,
				GenerateFootstepEvents = true,
				CreateAdditiveVariant = true,
				LoopingOverride = null,
			} );
		}
		var batch = await Task.Run( () => Retargeter.ConvertBatch(
			requests, target.Spec,
			new BatchOptions { DmxFolderRelative = OutputFolder, DetectLocomotionSets = true } ) );

		// Root-cause evidence for the Convert-All crash: record where Task.Run
		// continuations actually resume in an editor session.
		Result.mainThreadAfterTaskRun = ThreadSafe.IsMainThread;
		Note( $"after Task.Run continuation: mainThread={Result.mainThreadAfterTaskRun}" );

		Result.clipCount = batch.Clips.Count;
		Result.solvedClipCount = batch.Clips.Count( c => c.Success && c.SolvedFrames is { Count: > 0 } );
		Result.clipNames = batch.Clips.Where( c => c.Success ).Select( c => c.ClipName ).ToArray();
		Result.batchErrors = batch.Errors.ToArray();
		Result.additiveClipNames = batch.Clips
			.Where( c => c.Success && c.AdditiveVariantName is not null )
			.Select( c => c.AdditiveVariantName ).ToArray();
		Result.footstepEventCount = batch.Clips
			.Where( c => c.Success )
			.Sum( c => c.FootstepEvents?.Count ?? 0 );
		Result.locomotionSetReports = batch.LocomotionSets.Count; // no directional clips: expected 0, but the path must not crash
		Note( $"ConvertBatch: clips={Result.clipCount} solved={Result.solvedClipCount} "
			+ $"names=[{string.Join( ", ", Result.clipNames )}] "
			+ $"deltas=[{string.Join( ", ", Result.additiveClipNames )}] "
			+ $"footstepEvents={Result.footstepEventCount} locomotionReports={Result.locomotionSetReports} "
			+ $"errors={batch.Errors.Count}" );
		Flush();

		if ( Result.solvedClipCount == 0 )
			return;

		// ---- 4.5 RetargetWindow construction + options plumbing ------------------
		// Constructs the dock window headlessly (the BuildUi stacked-columns layout must
		// never throw) and asserts the footstep-events / mirrored-variants checkboxes
		// reach the facade request via BuildRequest (internal gate hook).
		try
		{
			var window = new RetargetWindow( null );
			Result.windowConstructed = true;

			var take = entry.Takes[0];
			var (defaults, defaultOptions) = window.BuildRequestForGate( take,
				footstepEvents: false, mirroredVariants: false,
				additiveVariants: false, detectLocomotionSets: false );
			var (flipped, flippedOptions) = window.BuildRequestForGate( take,
				footstepEvents: true, mirroredVariants: true,
				additiveVariants: true, detectLocomotionSets: true );
			Result.optionsPlumbingOk =
				!defaults.GenerateFootstepEvents && !defaults.CreateMirroredVariant
				&& !defaults.CreateAdditiveVariant && !defaultOptions.DetectLocomotionSets
				&& flipped.GenerateFootstepEvents && flipped.CreateMirroredVariant
				&& flipped.CreateAdditiveVariant && flippedOptions.DetectLocomotionSets;
			Note( $"RetargetWindow: constructed, options plumbing ok={Result.optionsPlumbingOk} "
				+ $"(defaults footsteps={defaults.GenerateFootstepEvents} mirror={defaults.CreateMirroredVariant} "
				+ $"additive={defaults.CreateAdditiveVariant} locomotion={defaultOptions.DetectLocomotionSets}; "
				+ $"flipped footsteps={flipped.GenerateFootstepEvents} mirror={flipped.CreateMirroredVariant} "
				+ $"additive={flipped.CreateAdditiveVariant} locomotion={flippedOptions.DetectLocomotionSets})" );

			// Smart-disable of the locomotion toggle: no complete directional family among
			// the take rows → disabled AND forced off with the explainer tooltip; a complete
			// 4-way family → enabled with a "Detected: …" tooltip naming the stem. Space-named
			// takes ("Walk N" …) sanitize into a complete Walk_N family during conversion, so
			// the scan must treat them the same and ENABLE the checkbox.
			var none = window.ApplyLocomotionScan( new[] { entry.Takes[0].TakeName } );
			var fourWay = window.ApplyLocomotionScan(
				new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" } );
			var spaced = window.ApplyLocomotionScan(
				new[] { "Walk N", "Walk E", "Walk S", "Walk W" } );
			var emptied = window.ApplyLocomotionScan( Array.Empty<string>() );
			Result.locomotionSmartDisableOk =
				!none.Enabled && !none.Value && none.ToolTip.Contains( "No directional animation set" )
				&& fourWay.Enabled && fourWay.ToolTip.Contains( "Walk (4-way)" )
				&& spaced.Enabled && spaced.ToolTip.Contains( "Walk (4-way)" )
				&& !emptied.Enabled && !emptied.Value;
			Note( $"locomotion smart-disable: none=({none.Enabled},{none.Value},'{none.ToolTip}') "
				+ $"fourWay=({fourWay.Enabled},'{fourWay.ToolTip}') "
				+ $"spaced=({spaced.Enabled},'{spaced.ToolTip}') "
				+ $"emptied=({emptied.Enabled},{emptied.Value}) ok={Result.locomotionSmartDisableOk}" );

			window.Destroy();
		}
		catch ( Exception e )
		{
			Result.windowConstructed = false;
			Result.optionsPlumbingOk = false;
			Result.locomotionSmartDisableOk = false;
			Note( $"RetargetWindow construction/plumbing FAILED: {e}" );
		}
		Flush();

		// ---- 5. preview widget on the solved frames (headless) -----------------
		try
		{
			var clip = batch.Clips.First( c => c.Success && c.SolvedFrames is { Count: > 0 } );
			var preview = new PreviewWidget(
				null, target.Spec.Rig, target.PreviewModelPath, target.PreviewPositionScale,
				target.Spec.UpAxis );
			Result.previewModelLoaded = preview.HasModel;
			preview.SetClip( clip );
			preview.Scrub( Math.Min( 5, preview.FrameCount - 1 ) );
			preview.ApplyCurrentFrame();
			Result.previewWidgetOk = true;
			Note( $"PreviewWidget: hasModel={preview.HasModel} frames={preview.FrameCount} frame applied OK" );

			// Source ghost (the preview dialog's "Show source" toggle): install the source
			// clip, enable, re-apply the frame and require the overlay to have drawn lines
			// AND to actually reach the screen (amber pixels - a missing line material
			// rendered both overlays as a single purple blob; user report 2026-07-04).
			try
			{
				preview.SetSourceGhost( entry.Scene.Skeleton, entry.Scene.Clips[0], entry.Mapping );
				preview.ShowSourceGhost = true;
				preview.ApplyCurrentFrame();
				Result.previewGhostPixels = preview.CountRenderedPixels(
					c => c.r > 0.45f && c.b < c.r * 0.6f );
				Result.previewGhostOk = preview.HasSourceGhost && preview.GhostLineCount > 0
					&& Result.previewGhostPixels > 20;
				preview.ShowSourceGhost = false;
				Note( $"source ghost: hasGhost={preview.HasSourceGhost} lines={preview.GhostLineCount} "
					+ $"pixels={Result.previewGhostPixels} ok={Result.previewGhostOk}" );
			}
			catch ( Exception e )
			{
				Result.previewGhostOk = false;
				Note( $"source ghost FAILED: {e}" );
			}
			Result.previewWidgetOk &= Result.previewGhostOk;

			// Wireframe-skeleton view (the dialog's "Skeleton" toggle): switching over must
			// draw the retargeted pose as stick bones - VISIBLY (cyan pixels) - and
			// switching back must re-show the model.
			try
			{
				preview.SkeletonOnly = true;
				preview.ApplyCurrentFrame();
				var lines = preview.SkeletonLineCount;
				Result.previewSkeletonPixels = preview.CountRenderedPixels(
					c => c.b > 0.45f && c.g > 0.45f && c.r < c.g * 0.8f );
				preview.SkeletonOnly = false;
				preview.ApplyCurrentFrame();
				Result.previewSkeletonViewOk = lines > 0 && !preview.SkeletonOnly
					&& Result.previewSkeletonPixels > 30;
				Note( $"skeleton view: lines={lines} pixels={Result.previewSkeletonPixels} "
					+ $"backToSkinned={!preview.SkeletonOnly} ok={Result.previewSkeletonViewOk}" );
			}
			catch ( Exception e )
			{
				Result.previewSkeletonViewOk = false;
				Note( $"skeleton view FAILED: {e}" );
			}
			Result.previewWidgetOk &= Result.previewSkeletonViewOk;

			// Axis-conversion assertions (Y-up cm rig → Z-up inch engine model). Without the
			// conversion the preview lies on its back; these pin it upright.
			if ( preview.HasModel )
			{
				var rig = target.Spec.Rig;
				var skeleton = rig.Skeleton;
				var hipsIndex = rig.BoneForRole( HumanoidRetargeter.Mapping.BoneRole.Hips ) ?? 0;
				var pelvisName = skeleton[hipsIndex].Name;

				// (a) Clip frame: the SceneModel's pelvis must match the independently
				// FK'd + converted solved frame ((x,y,z) → (x,−z,y) × 0.3937). This fails
				// when the widget skips the conversion OR overrides never reach the model.
				var frameIndex = Math.Min( 5, clip.SolvedFrames.Count - 1 );
				var rigWorld = new HumanoidRetargeter.Skeleton.Pose( clip.SolvedFrames[frameIndex] )
					.ToWorld( skeleton )[hipsIndex].Pos;
				var expected = new Vector3( rigWorld.X, -rigWorld.Z, rigWorld.Y )
					* target.PreviewPositionScale;
				var actual = preview.GetModelBoneTransform( pelvisName )?.Position;
				var frameOk = actual is { } a && a.Distance( expected ) < 0.5f;
				Note( $"preview pelvis (clip frame {frameIndex}): actual={actual} expected={expected} ok={frameOk}" );

				// (b) Rest pose: the citizen pelvis rests at y≈93 cm (Y-up) → engine
				// (0, ~0, ~36.6 in) Z-up. The preview must stand upright, not lie down.
				var rest = new HumanoidRetargeter.Maths.XForm[skeleton.Count];
				for ( var i = 0; i < skeleton.Count; i++ )
					rest[i] = skeleton[i].RestLocal;
				preview.ApplyPose( rest );
				var restPelvis = preview.GetModelBoneTransform( pelvisName )?.Position;
				var restOk = restPelvis is { } r
					&& MathF.Abs( r.x ) < 2f && MathF.Abs( r.y ) < 2f && r.z is > 30f and < 40f;
				Result.previewPelvisRest = restPelvis?.ToString() ?? "(unavailable)";
				Note( $"preview pelvis (rest pose): {Result.previewPelvisRest} expected ~(0, 0, 36.6) ok={restOk}" );

				Result.previewPoseUpright = frameOk && restOk;
				Result.previewWidgetOk &= Result.previewPoseUpright;
			}

			preview.Destroy();
		}
		catch ( Exception e )
		{
			Result.previewWidgetOk = false;
			Note( $"PreviewWidget FAILED: {e}" );
		}
		Flush();

		// ---- 6. user preset round-trip (preview confirm "Save as profile" path) -
		try
		{
			UserPresets.Save( assetsPath, entry.Signature, entry.Scene.Skeleton, entry.Mapping );
			var loaded = UserPresets.TryLoad( assetsPath, entry.Signature, entry.Scene.Skeleton );
			Result.userPresetRoundTrip = loaded is not null
				&& loaded.Source == MappingSource.UserPreset
				&& loaded.RoleToBone.Count == entry.Mapping.RoleToBone.Count
				&& loaded.RoleToBone.All( kv =>
					entry.Mapping.RoleToBone.TryGetValue( kv.Key, out var b ) && b == kv.Value );
			Note( $"user preset round-trip: {Result.userPresetRoundTrip} "
				+ $"(roles={loaded?.RoleToBone.Count ?? 0}/{entry.Mapping.RoleToBone.Count})" );
		}
		catch ( Exception e )
		{
			Result.userPresetRoundTrip = false;
			Note( $"user preset round-trip FAILED: {e}" );
		}
		Flush();

		// ---- 6.5 DL solver smoke: shipped weights load + one forward pass --------
		// (Milestone 10: the dialog's "Deep learning (experimental)" path. Kept cheap -
		// a 10-frame slice of the fixture through the full encode/decode stack.)
		if ( DlAssets.Available )
		{
			try
			{
				var weights = DlAssets.TryLoadWeights();
				var dlSolver = new HumanoidRetargeter.Dl.DlSolver( weights );
				var scene = entry.Scene;
				var sliceFrames = scene.Clips[0].Frames.Take( 10 ).ToList();
				var slice = new HumanoidRetargeter.Skeleton.SourceScene(
					scene.Skeleton,
					new[] { new HumanoidRetargeter.Skeleton.Clip( "dl_smoke", scene.Clips[0].Fps, false, sliceFrames ) },
					scene.UnitScaleCm, scene.UpAxis, scene.UpAxisSign,
					scene.FrontAxis, scene.FrontAxisSign, scene.CoordAxis, scene.CoordAxisSign );
				var dlClip = dlSolver.Solve( slice, entry.Mapping, target.Spec.Rig,
					new HumanoidRetargeter.Solve.SolveOptions() );
				Result.dlSolverOk = dlClip.FrameCount == sliceFrames.Count;
				Note( $"DL solver smoke: weights={weights?.Length ?? 0} bytes, "
					+ $"{dlClip.FrameCount} frames decoded, ok={Result.dlSolverOk}" );
			}
			catch ( Exception e )
			{
				Result.dlSolverOk = false;
				Note( $"DL solver smoke FAILED: {e}" );
			}
			Flush();
		}
		else
		{
			Result.dlSolverOk = true; // asset not installed: nothing to assert
			Note( "DL solver smoke skipped: weight asset not found" );
		}

		// ---- 6.6 classic citizen target: picker path + in-memory conversion ------
		// (Built-in 4-finger target. Kept cheap and disk-free: resolve via the window's
		// picker path and convert once in memory - the compile cycle below stays on the
		// default target so this step cannot disturb it.)
		try
		{
			var citizen = TargetPickers.SboxCitizen();
			var citizenBatch = await Task.Run( () => Retargeter.ConvertBatch(
				new[] { request }, citizen.Spec,
				new BatchOptions { DmxFolderRelative = OutputFolder } ) );
			var citizenClip = citizenBatch.Clips.FirstOrDefault( c => c.Success );
			Result.citizenTargetOk = citizenClip is not null
				&& citizenClip.DmxContent.Contains( "spine_0_p" )      // citizen channel pair present
				&& !citizenClip.DmxContent.Contains( "finger_pinky" )  // no pinky bones on this rig
				// Eyes-out-of-sockets regression guard: face bones must carry rest-local
				// channel pairs (nothing re-drives channel-less face joints in a compiled
				// sequence - ModelDoc bakes them statically and the eyes detach from the
				// moving head), while twist/helper bones stay channel-less joints (the
				// model's AnimConstraintList drives those on every evaluated frame).
				&& citizenClip.DmxContent.Contains( "\"eye_L_p\"" )
				&& citizenClip.DmxContent.Contains( "\"eye_L_o\"" )
				&& citizenClip.DmxContent.Contains( "\"eye_R_p\"" )
				&& citizenClip.DmxContent.Contains( "\"face_lid_upper_L_o\"" )
				&& !citizenClip.DmxContent.Contains( "\"arm_upper_L_twist1_p\"" )
				&& !citizenClip.DmxContent.Contains( "\"neck_clothing_o\"" );
			Note( $"citizen target: '{citizen.Description}' clips={citizenBatch.Clips.Count} "
				+ $"errors={citizenBatch.Errors.Count} ok={Result.citizenTargetOk}" );
		}
		catch ( Exception e )
		{
			Result.citizenTargetOk = false;
			Note( $"citizen target FAILED: {e}" );
		}
		Flush();

		// ---- 7. write + register + compile (the window's convert path) ---------
		var compileStartUtc = DateTime.UtcNow;
		var write = await EditorPipeline.WriteAndCompileAsync(
			batch, OutputFolder, augmentVmdlPath: null, standaloneVmdlName: "ui_smoke_retargeted" );
		Result.dmxFilesWritten = write.DmxFilesWritten;
		Result.vmdlPath = write.VmdlPath;
		Result.assetRegistered = write.VmdlAsset is not null;
		Result.dmxVmdlCompiled = write.Compiled;
		Result.compiledFile = write.CompiledFile;
		Result.writeErrors = write.Errors.ToArray();

		// A reported compile success must be backed by a compiled file written by THIS run
		// (a stale .vmdl_c from an earlier run must never count - EditorPipeline verifies
		// by timestamp; this asserts that verification end to end).
		Result.compiledFileFresh = write.Compiled
			&& write.CompiledFile is not null && File.Exists( write.CompiledFile )
			&& File.GetLastWriteTimeUtc( write.CompiledFile )
				>= compileStartUtc - TimeSpan.FromSeconds( 10 );
		// The compile-verification caveat closer: the REAL compiled vmdl must carry the
		// footstep AnimEvent nodes (event_class AE_FOOTSTEP) the request asked for.
		try
		{
			var vmdlText = write.VmdlPath is not null ? File.ReadAllText( write.VmdlPath ) : "";
			Result.footstepEventsInVmdl = Result.footstepEventCount > 0
				&& vmdlText.Contains( "AnimEvent", StringComparison.Ordinal )
				&& vmdlText.Contains( Target.FootstepEvents.FootstepEventClass, StringComparison.Ordinal );
		}
		catch ( Exception e )
		{
			Result.footstepEventsInVmdl = false;
			Note( $"vmdl footstep-event check FAILED: {e.Message}" );
		}

		Note( $"WriteAndCompile: dmx={write.DmxFilesWritten} vmdl={write.VmdlPath} "
			+ $"compiled={write.Compiled} compiledFile={write.CompiledFile} "
			+ $"fresh={Result.compiledFileFresh} footstepEventsInVmdl={Result.footstepEventsInVmdl} "
			+ $"errors={write.Errors.Count}" );
		Flush();

		if ( !write.Compiled || write.VmdlAsset is null )
			return;

		// ---- 8. load the compiled model, verify the sequences -------------------
		// Both the base clips AND their additive '<clip>_delta' twins (AnimSubtract nodes)
		// must be visible on the COMPILED model - this is what proves the additive variant
		// actually compiles in-engine rather than merely serializing.
		var model = Model.Load( write.VmdlAsset.Path );
		Result.modelLoads = model is not null && !model.IsError;
		if ( model is not null )
		{
			Result.boneCount = model.BoneCount;
			Result.animationCount = model.AnimationCount;
			Result.animationNames = model.AnimationNames?.ToArray() ?? Array.Empty<string>();
			bool Visible( string clip )
				=> Result.animationNames.Any( n => string.Equals( n, clip, StringComparison.OrdinalIgnoreCase ) );
			Result.additiveSequenceVisible = Result.additiveClipNames.Length > 0
				&& Result.additiveClipNames.All( Visible );
			Result.sequenceVisible = Result.clipNames.Length > 0 && Result.clipNames.All( Visible )
				&& Result.additiveSequenceVisible;
		}

		Note( $"modelLoads={Result.modelLoads} bones={Result.boneCount} anims={Result.animationCount} "
			+ $"names=[{string.Join( ", ", Result.animationNames )}] sequenceVisible={Result.sequenceVisible} "
			+ $"additiveSequenceVisible={Result.additiveSequenceVisible}" );
		Flush();

		// ---- 8.5 custom-target repro (HR_UI_SMOKE_CUSTOM) ------------------------
		// User report (2026-07-04): custom vmdl / custom FBX targets show no model in the
		// preview and their converted animations play NOTHING in ModelDoc (the shipped
		// citizen/human presets work). Reproduces both picker flows headlessly.
		if ( Environment.GetEnvironmentVariable( "HR_UI_SMOKE_CUSTOM" ) == "1" )
			await RunCustomTargetAsync( stepsEntry ?? entry, fixture, write );

		// ---- 9. augment mode: the EXACT Convert-All window path -----------------
		var augmentTarget = Environment.GetEnvironmentVariable( "HR_UI_SMOKE_AUGMENT" );
		if ( !string.IsNullOrWhiteSpace( augmentTarget ) )
			await RunAugmentAsync( entry, target, augmentTarget );
	}

	/// <summary>DMX folder for the custom-target runs (separate so their writes never
	/// re-trigger the standalone vmdl's compile).</summary>
	const string CustomDmxFolder = OutputFolder + "/custom";
	const string CustomFbxDmxFolder = OutputFolder + "/customfbx";

	/// <summary>
	/// Reproduces the custom-target user report end-to-end:
	/// (a) BASELINE - drives the default-target vmdl compiled in step 7 through actual
	///     sequence playback (SceneModel.CurrentSequence) and requires bones to move,
	///     proving the probe methodology on the known-good path;
	/// (b) custom MODEL target - the mounted citizen human male picked through
	///     TargetPickers.FromModelAsset (the window's "custom model" flow, ZUpEngine):
	///     preview widget must show the model and pose it, the converted clip must
	///     compile and its playback must move bones AND stay upright (pelvis height
	///     matching the solved frames - catches axis double-conversion);
	/// (c) custom FBX target - the main fixture picked through FromFbxFile: converted
	///     clip written + compiled, playback probed the same way.
	/// </summary>
	static async Task RunCustomTargetAsync(
		SourceFileEntry motionEntry, string fixturePath, EditorPipeline.WriteResult standaloneWrite )
	{
		Result.customMode = true;

		// HR_UI_FIXTURE_MOTION: animation converted onto the custom targets (user-repro
		// runs point this at their own clip; default = the steps fixture).
		var motionOverride = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_MOTION" );
		if ( !string.IsNullOrWhiteSpace( motionOverride ) && File.Exists( motionOverride ) )
		{
			var loaded = SourceFileEntry.Load( motionOverride, Project.Current.GetAssetsPath() );
			if ( loaded.Scene is not null && loaded.Mapping is not null )
				motionEntry = loaded;
			else
				Note( $"custom motion fixture unreadable ({loaded.StatusDetail}) - using the default" );
		}
		Note( $"custom-target repro: motion fixture={motionEntry.FileName}" );

		// ---- (a) baseline: the default-target vmdl must actually ANIMATE ------------
		try
		{
			if ( standaloneWrite?.VmdlAsset is not null && Result.clipNames.Length > 0 )
			{
				var baseline = Model.Load( standaloneWrite.VmdlAsset.Path );
				var probe = ProbeSequencePlayback( baseline, Result.clipNames[0], "pelvis" );
				Result.baselinePlaybackMoves = probe.Moved;
				Result.baselinePlaybackDetail = probe.Detail;
				Note( $"baseline playback: {probe.Detail} moved={probe.Moved}" );
			}
			else
				Note( "baseline playback skipped: no compiled standalone vmdl" );
		}
		catch ( Exception e )
		{
			Note( $"baseline playback FAILED: {e}" );
		}
		Flush();

		// ---- (b) custom MODEL target (FromModelAsset on the mounted citizen male) ---
		try
		{
			var asset = AssetSystem.FindByPath( RetargetTargetSpec.SboxHumanMalePath );
			if ( asset is null )
			{
				Note( $"custom model target: asset not found at {RetargetTargetSpec.SboxHumanMalePath}" );
			}
			else
			{
				var custom = TargetPickers.FromModelAsset( asset, out var error );
				Result.customModelTargetResolved = custom is not null;
				Result.customModelTargetError = error;
				Note( $"custom model target: resolved={custom is not null} error='{error}' "
					+ $"desc='{custom?.Description}' previewModel='{custom?.PreviewModelPath}'" );

				if ( custom is not null )
					await ProbeCustomTargetAsync( motionEntry, custom, CustomDmxFolder,
						"ui_smoke_custom", isModelTarget: true );
			}
		}
		catch ( Exception e )
		{
			Note( $"custom model target FAILED: {e}" );
		}
		Flush();

		// ---- (c) custom FBX target ---------------------------------------------------
		// HR_UI_FIXTURE_TARGETFBX should be a SKINNED humanoid FBX: the embedded
		// RenderMeshFile is where the compiled model's bones and skin come from, and a
		// skeleton-only animation FBX (the main fixture) embeds to an empty model.
		try
		{
			var targetFbxPath = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_TARGETFBX" );
			if ( string.IsNullOrWhiteSpace( targetFbxPath ) || !File.Exists( targetFbxPath ) )
				targetFbxPath = fixturePath;
			Note( $"custom fbx target fixture: {targetFbxPath}" );

			// A model that ships NO images renders legitimately monochrome - the
			// chromatic-pixel floor below only applies when textures exist to show.
			// Sidecars live next to the FBX or in a 'textures' folder BESIDE the
			// source folder (the layouts the picker copies from).
			var fixtureDir = Path.GetDirectoryName( targetFbxPath ) ?? ".";
			var siblingTextures = Path.Combine( Path.GetDirectoryName( fixtureDir ) ?? ".", "textures" );
			Result.customFbxHasTextures = new[] { fixtureDir, siblingTextures }
				.Where( Directory.Exists )
				.SelectMany( d => new[] { "*.png", "*.jpg", "*.jpeg", "*.tga" }
					.SelectMany( p => Directory.GetFiles( d, p, SearchOption.AllDirectories ) ) )
				.Any();
			Note( $"custom fbx sidecar textures present: {Result.customFbxHasTextures}" );
			var customFbx = TargetPickers.FromFbxFile( targetFbxPath, out var fbxError );
			Result.customFbxTargetResolved = customFbx is not null;
			Result.customFbxTargetError = fbxError;
			Note( $"custom fbx target: resolved={customFbx is not null} error='{fbxError}' "
				+ $"desc='{customFbx?.Description}' previewModel='{customFbx?.PreviewModelPath}'" );

			if ( customFbx is not null )
			{
				var fbxLogOffset = EditorPipeline.SboxLogLength();

				// The window compiles a mesh-only preview vmdl for skinned FBX targets so
				// the preview shows the ACTUAL model - same path here; on success the
				// preview probe below must find a model (customFbxPreviewHasModel).
				Result.customFbxPreviewModelCompiled = await EditorPipeline
					.CompileFbxTargetPreviewAsync( customFbx, CustomFbxDmxFolder );
				Note( $"custom fbx preview model: compiled={Result.customFbxPreviewModelCompiled} "
					+ $"path='{customFbx.PreviewModelPath}'" );

				await ProbeCustomTargetAsync( motionEntry, customFbx, CustomFbxDmxFolder,
					"ui_smoke_customfbx", isModelTarget: false );

				// Auto-generated vmats: the mesh compiles of this leg must not report any
				// 'Missing vmat' (the auto-vmat generation writes one per FBX material),
				// and the renderer must not spam unresolved-vtex lookups (unregistered
				// textures - the per-frame retry behind the user's 2 fps preview).
				var fbxSlice = EditorPipeline.ReadLogSlice( fbxLogOffset ) ?? "";
				Result.customFbxMissingVmats = fbxSlice.Split( '\n' )
					.Count( l => l.Contains( "Missing vmat", StringComparison.OrdinalIgnoreCase ) );
				Result.customFbxTextureSpamLines = fbxSlice.Split( '\n' )
					.Count( l => l.Contains( "doesn't know about texture", StringComparison.OrdinalIgnoreCase ) );
				Note( $"custom fbx missing-vmat lines: {Result.customFbxMissingVmats} "
					+ $"unresolved-vtex lines: {Result.customFbxTextureSpamLines}" );
			}
		}
		catch ( Exception e )
		{
			Note( $"custom fbx target FAILED: {e}" );
		}
		Flush();

		Result.customOk =
			Result.baselinePlaybackMoves
			&& Result.customModelTargetResolved && Result.customFbxTargetResolved
			&& Result.customModelPreviewHasModel && Result.customModelPreviewPoseOk
			&& Result.customModelSkeletonLines > 0 && Result.customModelSkeletonPixels > 30
			&& Result.customModelNoWildBones && Result.customFbxNoWildBones
			&& Result.customModelCompiled && Result.customModelSequenceVisible
			&& Result.customModelPlaybackMoves && Result.customModelPlaybackUpright
			&& Result.customFbxSkeletonLines > 0 && Result.customFbxSkeletonPixels > 30
			&& Result.customFbxPreviewModelCompiled && Result.customFbxPreviewHasModel
			&& Result.customFbxPreviewPoseOk
			&& Result.customFbxMissingVmats == 0
			// Texture ground truth on the rendered pixels: no error material anywhere,
			// real chromatic (textured) surface present (calibrated on a real character
			// with a monochrome outfit: skin tones measure ~130 chromatic pixels; an
			// all-placeholder or error-material render measures ~0), no unresolved-vtex
			// render spam. Texture-less models render legitimately monochrome.
			&& Result.customFbxErrorPixels == 0
			&& (Result.customFbxColoredPixels > 10 || !Result.customFbxHasTextures)
			&& Result.customFbxTextureSpamLines == 0
			&& Result.customFbxCompiled && Result.customFbxSequenceVisible
			&& Result.customFbxPlaybackMoves && Result.customFbxPlaybackUpright
			&& Result.customFbxEmbeddedVisible && Result.customFbxEmbeddedMoves;
		Note( $"custom-target repro => customOk={Result.customOk}" );
		Flush();
	}

	/// <summary>Saves a preview render next to the result JSON (preview_dumps/) so the
	/// harness can LOOK at what the user sees - texture resolution, overlay shapes, hand
	/// posture are all visual complaints pixel counts cannot diagnose.</summary>
	static void DumpPreviewRender( PreviewWidget preview, string name )
		=> DumpPng( () => preview.RenderToPng(), name );

	static void DumpPng( Func<byte[]> render, string name )
	{
		try
		{
			var png = render();
			if ( png is null )
				return;
			var directory = Path.Combine( Path.GetDirectoryName( _resultPath ), "preview_dumps" );
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, name + ".png" ), png );
			Note( $"preview render dumped: preview_dumps/{name}.png" );
		}
		catch ( Exception e )
		{
			Note( $"preview render dump failed ({name}): {e.Message}" );
		}
	}

	/// <summary>Converts the motion fixture onto <paramref name="target"/>, probes the
	/// PreviewWidget (model visible + posed pelvis matching the solved frame), writes +
	/// compiles a standalone vmdl and probes actual sequence playback on the compiled
	/// model. Fills the customModel*/customFbx* result fields.</summary>
	static async Task ProbeCustomTargetAsync(
		SourceFileEntry motionEntry, TargetPickers.ResolvedTarget target,
		string dmxFolder, string vmdlName, bool isModelTarget )
	{
		var tag = isModelTarget ? "custom model" : "custom fbx";

		// Custom FBX targets: same mesh-embed preparation the window's convert path runs
		// (copy the FBX into the output folder + set the spec's MeshFilePath) - without it
		// the standalone vmdl compiles into an empty model.
		if ( !EditorPipeline.PrepareFbxTargetMesh( target, dmxFolder, out var meshError ) )
			Note( $"{tag}: PrepareFbxTargetMesh FAILED: {meshError}" );
		else if ( target.FbxAbsolutePath is not null )
			Note( $"{tag}: mesh embedded as '{target.Spec.MeshFilePath}' importScale={target.Spec.MeshImportScale}" );

		var request = new RetargetRequest
		{
			SourceData = motionEntry.Bytes,
			SourceFileName = motionEntry.FileName,
			SourceId = motionEntry.FilePath,
			MappingOverride = motionEntry.Mapping,
			RootMotion = Cleanup.RootMotionMode.Off,
			FootPlantCleanup = true,
			ArmEffectorIk = false,
			LoopingOverride = null,
		};
		var requests = new List<RetargetRequest> { request };
		requests.AddRange( EditorPipeline.BuildEmbeddedTakeRequests( target ) );
		var batch = await Task.Run( () => Retargeter.ConvertBatch(
			requests, target.Spec,
			new BatchOptions { DmxFolderRelative = dmxFolder } ) );

		var clip = batch.Clips.FirstOrDefault( c => c.Success && c.SolvedFrames is { Count: > 0 } );
		Note( $"{tag}: convert clips={batch.Clips.Count} solved={clip is not null} "
			+ $"errors=[{string.Join( "; ", batch.Clips.Where( c => !c.Success ).Select( c => c.Error ) )}]" );
		if ( clip is null )
			return;

		// Expected pelvis (rig space) at a mid frame, FK'd from the solved locals.
		var rig = target.Spec.Rig;
		var hipsIndex = rig.BoneForRole( HumanoidRetargeter.Mapping.BoneRole.Hips ) ?? 0;
		var pelvisName = rig.Skeleton[hipsIndex].Name;
		var frameIndex = Math.Min( clip.SolvedFrames.Count - 1, clip.SolvedFrames.Count / 2 );
		var rigPelvis = new HumanoidRetargeter.Skeleton.Pose( clip.SolvedFrames[frameIndex] )
			.ToWorld( rig.Skeleton )[hipsIndex].Pos;
		// Engine-space expectation mirrors PreviewWidget.RigWorldToEngine (Y-up rigs get
		// the basis rotation; Z-up rigs - engine models AND Z-up FBX exports - do not).
		var expectedEngine = target.Spec.UpAxis == TargetUpAxis.YUpCm
			? new Vector3( rigPelvis.X, -rigPelvis.Z, rigPelvis.Y ) * target.PreviewPositionScale
			: new Vector3( rigPelvis.X, rigPelvis.Y, rigPelvis.Z ) * target.PreviewPositionScale;

		// ---- preview widget probe ---------------------------------------------------
		try
		{
			var preview = new PreviewWidget(
				null, rig, target.PreviewModelPath, target.PreviewPositionScale, target.Spec.UpAxis );
			var hasModel = preview.HasModel;
			preview.SetClip( clip );
			preview.Scrub( frameIndex );
			preview.ApplyCurrentFrame();
			var actual = preview.GetModelBoneTransform( pelvisName )?.Position;
			var poseOk = actual is { } a && a.Distance( expectedEngine ) < 0.5f;

			// No bone may sit farther from the pelvis than ~3 character heights: exploded
			// FK (world-as-local bind transforms) kept the pelvis exactly right while the
			// head compounded to 220in away - pelvis-only asserts can never catch it.
			var wildRadius = MathF.Max( 150f, expectedEngine.z * 3f );
			var wildReport = hasModel ? preview.WildBoneReport( pelvisName, wildRadius ) : "(no model)";
			var wildOk = !hasModel || wildReport == "(none)";
			if ( hasModel )
			{
				Note( $"{tag} wild bones (>{wildRadius:0}in from pelvis): {wildReport}" );
				Note( $"{tag} rig-vs-bind: {preview.BindMismatchReport()}" );
			}

			// TEXTURE CORRECTNESS, asserted on the FINAL RENDERED PIXELS (the fool-proof
			// ground truth): no error-magenta anywhere, and a real amount of chromatic
			// (textured) surface - a placeholder-gray or error-material model fails both.
			var errorPixels = 0;
			var coloredPixels = 0;
			if ( hasModel )
			{
				try
				{
					errorPixels = preview.CountRenderedPixels(
						c => c.r > 0.7f && c.b > 0.7f && c.g < 0.35f );
					coloredPixels = preview.CountRenderedPixels(
						c => MathF.Abs( c.r - c.g ) + MathF.Abs( c.g - c.b ) > 0.15f );
				}
				catch ( Exception e )
				{
					Note( $"{tag} texture pixel probe threw: {e.Message}" );
				}
			}
			DumpPreviewRender( preview, $"{vmdlName}_skinned" );

			// Multi-frame skinned dumps: progressive-stretch reports ("it stretched MORE
			// when playing") need more than one pose to diagnose.
			foreach ( var fraction in new[] { 0.25f, 0.75f } )
			{
				preview.Scrub( (int)((clip.SolvedFrames.Count - 1) * fraction) );
				preview.ApplyCurrentFrame();
				DumpPreviewRender( preview, $"{vmdlName}_skinned_{(int)(fraction * 100)}" );
			}
			preview.Scrub( frameIndex );
			preview.ApplyCurrentFrame();

			// The embedded take's solved pose (round-trips through the role cascade now -
			// wrist quality must hold there too, not just on retargeted clips).
			if ( !isModelTarget && target.EmbeddedTakeNames is { Count: > 0 } embeddedNames )
			{
				var embeddedClip = batch.Clips.FirstOrDefault( c =>
					c.Success && c.ClipName == embeddedNames[0] && c.SolvedFrames is { Count: > 0 } );
				if ( embeddedClip is not null )
				{
					preview.SetClip( embeddedClip );
					preview.Scrub( embeddedClip.SolvedFrames.Count / 2 );
					preview.ApplyCurrentFrame();
					DumpPreviewRender( preview, $"{vmdlName}_embedded" );
					if ( rig.BoneForRole( HumanoidRetargeter.Mapping.BoneRole.HandR ) is { } embHand )
					{
						var embBone = rig.Skeleton[embHand].Name;
						var embRadius = MathF.Max( expectedEngine.z * 0.22f, 8f );
						DumpPng( () => preview.RenderBoneCloseUpPng( embBone, embRadius ),
							$"{vmdlName}_embedded_hand_R" );
					}
					preview.SetClip( clip );
					preview.Scrub( frameIndex );
					preview.ApplyCurrentFrame();
				}
			}

			// Close-ups (user reports: "fingers and wrist still look weird", "the makeup
			// around the eye is white" - full-body renders are too small to judge).
			if ( hasModel )
			{
				foreach ( var (role, suffix, scale) in new[]
				{
					(HumanoidRetargeter.Mapping.BoneRole.HandR, "hand_R", 0.22f),
					(HumanoidRetargeter.Mapping.BoneRole.HandL, "hand_L", 0.22f),
					// Head camera needs more distance than the hands: at 0.28x the near plane
					// sat INSIDE large cartoon skulls and the dump rendered a white wall.
					(HumanoidRetargeter.Mapping.BoneRole.Head, "head", 0.4f),
				} )
				{
					if ( rig.BoneForRole( role ) is { } boneIndex )
					{
						var boneName = rig.Skeleton[boneIndex].Name;
						var radius = MathF.Max( expectedEngine.z * scale, 8f );
						DumpPng( () => preview.RenderBoneCloseUpPng( boneName, radius ),
							$"{vmdlName}_{suffix}" );
					}
				}
			}

			// Source ghost with THIS motion fixture (user reports "two bars" instead of a
			// skeleton) - rendered for visual diagnosis alongside the pose asserts.
			try
			{
				preview.SetSourceGhost( motionEntry.Scene.Skeleton, motionEntry.Scene.Clips[0],
					motionEntry.Mapping );
				preview.ShowSourceGhost = true;
				preview.ApplyCurrentFrame();
				DumpPreviewRender( preview, $"{vmdlName}_ghost" );
				preview.ShowSourceGhost = false;
			}
			catch ( Exception e )
			{
				Note( $"{tag} ghost dump failed: {e.Message}" );
			}

			// Wireframe-skeleton view: model targets must be able to switch onto it (for
			// FBX targets it is the fallback while the preview model compiles). Verified at
			// the PIXEL level - line counts cannot see a missing line material (user
			// report: the view rendered as a single purple blob).
			preview.SkeletonOnly = true;
			preview.ApplyCurrentFrame();
			var skeletonLines = preview.SkeletonLineCount;
			var skeletonPixels = 0;
			try
			{
				skeletonPixels = preview.CountRenderedPixels(
					c => c.b > 0.45f && c.g > 0.45f && c.r < c.g * 0.8f );
			}
			catch ( Exception e )
			{
				Note( $"{tag} skeleton pixel probe threw: {e.Message}" );
			}
			DumpPreviewRender( preview, $"{vmdlName}_skeleton" );
			Note( $"{tag} skeleton geometry: {preview.SkeletonDebug}" );
			preview.SkeletonOnly = false;
			preview.ApplyCurrentFrame();

			Note( $"{tag} preview: hasModel={hasModel} pelvis actual={actual} "
				+ $"expected={expectedEngine} poseOk={poseOk} skeletonLines={skeletonLines} "
				+ $"skeletonPixels={skeletonPixels} errorPixels={errorPixels} coloredPixels={coloredPixels}" );
			if ( isModelTarget )
			{
				Result.customModelPreviewHasModel = hasModel;
				Result.customModelPreviewPoseOk = poseOk;
				Result.customModelSkeletonLines = skeletonLines;
				Result.customModelSkeletonPixels = skeletonPixels;
				Result.customModelNoWildBones = wildOk;
			}
			else
			{
				Result.customFbxPreviewHasModel = hasModel;
				Result.customFbxPreviewPoseOk = poseOk;
				Result.customFbxSkeletonLines = skeletonLines;
				Result.customFbxSkeletonPixels = skeletonPixels;
				Result.customFbxErrorPixels = errorPixels;
				Result.customFbxColoredPixels = coloredPixels;
				Result.customFbxNoWildBones = wildOk;
			}
			preview.Destroy();
		}
		catch ( Exception e )
		{
			Note( $"{tag} preview FAILED: {e}" );
		}
		Flush();

		// ---- write + compile + playback probe ----------------------------------------
		var write = await EditorPipeline.WriteAndCompileAsync(
			batch, dmxFolder, augmentVmdlPath: null, standaloneVmdlName: vmdlName,
			compileTimeoutSeconds: string.IsNullOrEmpty( target.Spec.MeshFilePath )
				? 120f : EditorPipeline.MeshCompileTimeoutSeconds );
		var compiled = write.Compiled && write.VmdlAsset is not null;
		Note( $"{tag} write+compile: vmdl={write.VmdlPath} compiled={write.Compiled} "
			+ $"errors=[{string.Join( "; ", write.Errors )}]" );

		var sequenceVisible = false;
		var playbackMoved = false;
		var playbackUpright = false;
		var lateralAngle = 0f;
		string playbackDetail = null;
		if ( compiled )
		{
			var model = Model.Load( write.VmdlAsset.Path );
			var names = model?.AnimationNames?.ToArray() ?? Array.Empty<string>();
			sequenceVisible = model is not null && !model.IsError
				&& names.Any( n => string.Equals( n, clip.ClipName, StringComparison.OrdinalIgnoreCase ) );
			Note( $"{tag} compiled model: error={model?.IsError} bones={model?.BoneCount} "
				+ $"anims=[{string.Join( ", ", names )}] sequenceVisible={sequenceVisible}" );

			if ( model is not null && !model.IsError )
			{
				// The target FBX's own embedded animations must be ON the compiled model
				// and actually play (user: "it should have two animations"). Take-less
				// files (mesh-only exports) trivially satisfy the requirement.
				if ( !isModelTarget && target.EmbeddedTakeNames is not { Count: > 0 } )
				{
					Result.customFbxEmbeddedVisible = true;
					Result.customFbxEmbeddedMoves = true;
				}
				if ( !isModelTarget && target.EmbeddedTakeNames is { Count: > 0 } embedded )
				{
					Result.customFbxEmbeddedVisible = embedded.All( e =>
						names.Any( n => string.Equals( n, e, StringComparison.OrdinalIgnoreCase ) ) );
					var embeddedProbe = ProbeSequencePlayback( model, embedded[0], pelvisName );
					// A STATIC take (zero-length bind-pose AnimStack, common in Sketchfab
					// exports - the catgirl ships one) legitimately moves nothing.
					var staticTake = embeddedProbe.Detail.Contains( "duration=0s" );
					Result.customFbxEmbeddedMoves = embeddedProbe.Moved || staticTake;
					Note( $"{tag} embedded animation(s) [{string.Join( ", ", embedded )}]: "
						+ $"visible={Result.customFbxEmbeddedVisible} playback: {embeddedProbe.Detail} "
						+ $"moved={embeddedProbe.Moved} staticTake={staticTake}" );
				}

				var legL = rig.BoneForRole( HumanoidRetargeter.Mapping.BoneRole.UpperLegL );
				var legR = rig.BoneForRole( HumanoidRetargeter.Mapping.BoneRole.UpperLegR );
				var probe = ProbeSequencePlayback( model, clip.ClipName, pelvisName,
					legL is { } l ? rig.Skeleton[l].Name : null,
					legR is { } r ? rig.Skeleton[r].Name : null );
				playbackMoved = probe.Moved;
				playbackDetail = probe.Detail;
				// Upright check: compiled pelvis height must match the solved frames
				// (an axis double-conversion lays the character down / buries it).
				playbackUpright = probe.PelvisMid is { } mid
					&& MathF.Abs( mid.z - expectedEngine.z ) < 3f;

				// Axis diagnostic (informational, not gated): angle between the compiled
				// mid-frame hip line and the SOLVED mid frame's (engine-converted). A
				// GLOBAL yaw here is benign - the skin follows the bones, the character
				// just faces a different compass direction (the compiler's Z-up handling
				// includes one); per-bone axis garbage is caught by upright + preview-pose.
				if ( probe.HipLineMid is { } actualHip && legL is { } el && legR is { } er )
				{
					var world = new HumanoidRetargeter.Skeleton.Pose( clip.SolvedFrames[frameIndex] )
						.ToWorld( rig.Skeleton );
					var rigHip = world[el].Pos - world[er].Pos;
					var expectedHip = target.Spec.UpAxis == TargetUpAxis.YUpCm
						? new Vector3( rigHip.X, -rigHip.Z, rigHip.Y )
						: new Vector3( rigHip.X, rigHip.Y, rigHip.Z );
					var a = actualHip.WithZ( 0 );
					var e2 = expectedHip.WithZ( 0 );
					if ( a.Length > 0.1f && e2.Length > 0.1f )
					{
						var dot = Math.Clamp( a.Normal.Dot( e2.Normal ), -1f, 1f );
						lateralAngle = MathX.RadianToDegree( MathF.Acos( dot ) );
					}
				}

				Note( $"{tag} playback: {probe.Detail} moved={probe.Moved} "
					+ $"pelvisMid={probe.PelvisMid} expectedZ={expectedEngine.z:0.##} upright={playbackUpright} "
					+ $"solvedVsCompiledHipLine={lateralAngle:0.#}deg" );
			}
		}

		if ( isModelTarget )
		{
			Result.customModelCompiled = compiled;
			Result.customModelSequenceVisible = sequenceVisible;
			Result.customModelPlaybackMoves = playbackMoved;
			Result.customModelPlaybackUpright = playbackUpright;
			Result.customModelPlaybackDetail = playbackDetail;
			Result.customModelSolvedVsCompiledHipDeg = lateralAngle;
			Result.customModelWriteErrors = write.Errors.ToArray();
		}
		else
		{
			Result.customFbxCompiled = compiled;
			Result.customFbxSequenceVisible = sequenceVisible;
			Result.customFbxPlaybackMoves = playbackMoved;
			Result.customFbxPlaybackUpright = playbackUpright;
			Result.customFbxPlaybackDetail = playbackDetail;
			Result.customFbxSolvedVsCompiledHipDeg = lateralAngle;
			Result.customFbxWriteErrors = write.Errors.ToArray();
		}
		Flush();
	}

	/// <summary>Drives a compiled sequence directly (SceneModel.CurrentSequence - the same
	/// mechanism ModelDoc playback uses) and measures whether any bone actually moves
	/// between t=0 and the sequence midpoint. When <paramref name="lateralBoneL"/>/<paramref name="lateralBoneR"/>
	/// are given (the rig's upper legs), additionally returns the mid-frame hip line
	/// (L−R, engine space) - the caller compares it against the SOLVED frames' hip line at
	/// the same normalized time to prove the compiled animation plays in the axes the
	/// solver produced (a ~90° disagreement is the Z-up mismatch class).</summary>
	static (bool Moved, string Detail, Vector3? PelvisMid, Vector3? HipLineMid) ProbeSequencePlayback(
		Model model, string sequenceName, string pelvisName,
		string lateralBoneL = null, string lateralBoneR = null )
	{
		SceneWorld world = null;
		SceneModel sceneModel = null;
		try
		{
			world = new SceneWorld();
			sceneModel = new SceneModel( world, model, Transform.Zero );
			sceneModel.UseAnimGraph = false;
			var boneCount = model.BoneCount;
			sceneModel.CurrentSequence.Name = sequenceName;
			var duration = sceneModel.CurrentSequence.Duration;

			Vector3[] Sample( float time )
			{
				sceneModel.CurrentSequence.Time = time;
				sceneModel.Update( 0.001f );
				var positions = new Vector3[boneCount];
				for ( var i = 0; i < boneCount; i++ )
					positions[i] = sceneModel.GetBoneWorldTransform( i ).Position;
				return positions;
			}

			var start = Sample( 0f );
			var midTime = MathF.Max( duration * 0.5f, 0.05f );
			var mid = Sample( midTime );

			float maxDelta = 0;
			var maxBone = -1;
			for ( var i = 0; i < boneCount; i++ )
			{
				var d = start[i].Distance( mid[i] );
				if ( d > maxDelta ) { maxDelta = d; maxBone = i; }
			}

			Vector3? pelvisMid = null;
			var pelvisBone = model.Bones?.GetBone( pelvisName );
			if ( pelvisBone is not null )
				pelvisMid = mid[pelvisBone.Index];

			Vector3? hipLineMid = null;
			var boneL = lateralBoneL is not null ? model.Bones?.GetBone( lateralBoneL ) : null;
			var boneR = lateralBoneR is not null ? model.Bones?.GetBone( lateralBoneR ) : null;
			if ( boneL is not null && boneR is not null )
				hipLineMid = mid[boneL.Index] - mid[boneR.Index];

			var detail = $"seq='{sequenceName}' duration={duration:0.###}s bones={boneCount} "
				+ $"maxDelta={maxDelta:0.###}in (bone "
				+ $"{(maxBone >= 0 ? model.Bones.AllBones.ElementAtOrDefault( maxBone )?.Name ?? maxBone.ToString() : "none")})";
			return (maxDelta > 0.5f, detail, pelvisMid, hipLineMid);
		}
		catch ( Exception e )
		{
			return (false, $"playback probe threw: {e.Message}", null, null);
		}
		finally
		{
			sceneModel?.Delete();
			world?.Delete();
		}
	}

	/// <summary>Assets-relative DMX folder for the augment run (separate from the
	/// standalone run's folder so its writes never re-trigger that vmdl's compile).</summary>
	const string AugmentDmxFolder = OutputFolder + "/augment";

	/// <summary>Sequence name of the stale AnimFile planted into the augment target in an5
	/// mode (its DMX deliberately never exists - the user-report repro).</summary>
	const string StaleProbeName = "hr_stale_probe";

	static async Task RunAugmentAsync(
		SourceFileEntry entry, TargetPickers.ResolvedTarget target, string augmentVmdlPath )
	{
		Result.augmentMode = true;
		Result.augmentVmdlPath = augmentVmdlPath;
		Note( $"augment mode: target vmdl={augmentVmdlPath}" );

		if ( !File.Exists( augmentVmdlPath ) )
		{
			Note( "augment target vmdl not found - augment FAILED" );
			Flush();
			return;
		}

		try
		{
			// Same request the window's BuildRequest produces for this entry.
			var requests = new List<RetargetRequest>
			{
				new RetargetRequest
				{
					SourceData = entry.Bytes,
					SourceFileName = entry.FileName,
					SourceId = entry.FilePath,
					MappingOverride = entry.Mapping,
					RootMotion = Cleanup.RootMotionMode.Off,
					FootPlantCleanup = true,
					ArmEffectorIk = false,
					LoopingOverride = null,
				},
			};

			// User-report batch shape (HR_UI_FIXTURE_AN5): a BVH plus EVERY take of a
			// RenderWare .an5 bank, one request per take row exactly like the window's
			// Convert All (TakeIndex per row, companion .dff as SkeletonData). This is the
			// mixed batch that produced "vmdl did not compile" on a real citizen vmdl copy;
			// with it armed the augment gate REQUIRES the recompile to succeed.
			var an5Path = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_AN5" );
			if ( !string.IsNullOrWhiteSpace( an5Path ) && File.Exists( an5Path ) )
			{
				Result.augmentAn5Mode = true;
				var assetsPath = Project.Current.GetAssetsPath();

				var bvhPath = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_STEPS" );
				if ( !string.IsNullOrWhiteSpace( bvhPath ) && File.Exists( bvhPath ) )
				{
					var bvhEntry = SourceFileEntry.Load( bvhPath, assetsPath );
					if ( bvhEntry.Scene is not null && bvhEntry.Mapping is not null )
					{
						requests.Add( new RetargetRequest
						{
							SourceData = bvhEntry.Bytes,
							SourceFileName = bvhEntry.FileName,
							SourceId = bvhEntry.FilePath,
							MappingOverride = bvhEntry.Mapping,
							RootMotion = Cleanup.RootMotionMode.Off,
							FootPlantCleanup = true,
							ArmEffectorIk = false,
							LoopingOverride = null,
						} );
					}
					else
						Note( $"augment an5 mode: bvh fixture unreadable: {bvhEntry.StatusDetail}" );
				}

				var an5Entry = SourceFileEntry.Load( an5Path, assetsPath );
				if ( an5Entry.Scene is null || an5Entry.Mapping is null )
				{
					Note( $"augment an5 mode: an5 fixture unreadable: {an5Entry.StatusDetail} "
						+ $"(skeleton={an5Entry.SkeletonPath ?? "none"})" );
				}
				else
				{
					Result.augmentAn5Takes = an5Entry.ClipCount;
					for ( var i = 0; i < an5Entry.ClipCount; i++ )
					{
						requests.Add( new RetargetRequest
						{
							SourceData = an5Entry.Bytes,
							SourceFileName = an5Entry.FileName,
							SkeletonData = an5Entry.SkeletonBytes,
							SourceId = an5Entry.FilePath + "#" + i,
							MappingOverride = an5Entry.Mapping,
							TakeIndex = an5Entry.ClipCount > 1 ? i : null,
							RootMotion = Cleanup.RootMotionMode.Off,
							FootPlantCleanup = true,
							ArmEffectorIk = false,
							LoopingOverride = null,
						} );
					}
					Note( $"augment an5 mode: {an5Entry.FileName} takes={an5Entry.ClipCount} "
						+ $"skeleton={Path.GetFileName( an5Entry.SkeletonPath ?? "none" )} "
						+ $"profile={an5Entry.Mapping.ProfileName} requests={requests.Count}" );
				}

				// USER-REPORT REPRO: plant a stale AnimFile into the augment target BEFORE
				// converting - an entry from an "earlier batch" whose DMX no longer exists on
				// disk (the user's vmdl had accumulated animations/retargeted/*.dmx entries,
				// then the files went away). One such entry fails the ENTIRE recompile
				// ("Node 'X' resolve failure" -> ResourceCompilerSystem [FAIL]); the fix must
				// prune it so the augmented vmdl still compiles.
				try
				{
					var staleVmdl = Target.VmdlAugmenter.Augment(
						File.ReadAllText( augmentVmdlPath ),
						new[]
						{
							new Target.AnimEntry
							{
								Name = StaleProbeName,
								SourceFilename = "animations/retargeted/" + StaleProbeName + ".dmx",
							},
						},
						out _,
						new Target.AugmentOptions { DefaultRootBone = "pelvis" } );
					File.WriteAllText( augmentVmdlPath, staleVmdl );
					Result.augmentStalePlanted = true;
					Note( $"planted stale AnimFile '{StaleProbeName}' (missing dmx) into the augment target" );
				}
				catch ( Exception e )
				{
					Note( $"could not plant stale AnimFile: {e.Message}" );
				}
			}
			var options = new BatchOptions
			{
				DmxFolderRelative = AugmentDmxFolder,
				AugmentVmdlText = File.ReadAllText( augmentVmdlPath ),
			};

			// Stale-compile probe (CompileAndWaitAsync timestamp verification): augment
			// targets always carry a pre-existing .vmdl_c in real use. Plant one, backdated,
			// BEFORE the run - its mere existence must never turn into a reported compile
			// success; if a success IS reported, the compiled file must be NEWER than this.
			var staleCompiledPath = augmentVmdlPath + "_c";
			DateTime? staleStampUtc = null;
			try
			{
				if ( !File.Exists( staleCompiledPath ) )
					File.WriteAllBytes( staleCompiledPath, new byte[] { 0 } );
				File.SetLastWriteTimeUtc( staleCompiledPath, DateTime.UtcNow.AddMinutes( -10 ) );
				staleStampUtc = File.GetLastWriteTimeUtc( staleCompiledPath );
				Note( $"planted stale compiled file {staleCompiledPath} @ {staleStampUtc:O}" );
			}
			catch ( Exception e )
			{
				Note( $"could not plant stale compiled file: {e.Message}" );
			}

			var logOffset = EditorPipeline.SboxLogLength();

			// THE window path: same method Convert All invokes (Task.Run batch ->
			// main-thread write/register/settle/compile).
			var (batch, write) = await RetargetWindow.ConvertAndWriteAsync(
				requests, target, options, augmentVmdlPath );

			Result.augmentOnMainThreadAfter = ThreadSafe.IsMainThread;
			Result.augmentedVmdlProduced = batch.AugmentedVmdl is not null;
			Result.augmentWriteErrors = write?.Errors.ToArray()
				?? new[] { "write skipped (no augmented vmdl produced)" };
			Result.augmentVmdlWritten = write?.VmdlPath is not null
				&& File.Exists( write.VmdlPath ) && File.Exists( write.VmdlPath + ".bak" );
			Result.augmentRegistered = write?.VmdlAsset is not null;
			Result.augmentCompiled = write?.Compiled ?? false; // informational: the fixture's
			// meshes cannot resolve from a scratch project, a compile failure is acceptable.
			Result.augmentCompilePollCompleted = write is not null;
			Result.augmentQuietInputsAbandon = EditorPipeline.LogSliceShowsAbandonedRecompile(
				logOffset, Path.GetFileName( augmentVmdlPath ) );
			Result.augmentClipNames = batch.Clips
				.Where( c => c.Success ).Select( c => c.ClipName ).ToArray();
			Result.augmentFailedClips = batch.Clips
				.Where( c => !c.Success ).Select( c => $"{c.ClipName}: {c.Error}" ).ToArray();

			// The ACTUAL resourcecompiler verdict for the augment vmdl: every error-looking
			// line of the per-run log slice that mentions it (the editor console shows these;
			// the log carries at least the recompile ERROR line).
			var vmdlFileName = Path.GetFileName( augmentVmdlPath );
			Result.augmentCompileErrors = (EditorPipeline.ReadLogSlice( logOffset ) ?? "")
				.Split( '\n' )
				.Select( l => l.TrimEnd( '\r' ) )
				.Where( l => l.Length > 0
					&& (l.Contains( "error", StringComparison.OrdinalIgnoreCase )
						|| l.Contains( "fail", StringComparison.OrdinalIgnoreCase ))
					&& (l.Contains( vmdlFileName, StringComparison.OrdinalIgnoreCase )
						|| l.Contains( "resourcecompiler", StringComparison.OrdinalIgnoreCase )
						|| l.Contains( "ModelDoc", StringComparison.OrdinalIgnoreCase )) )
				.ToArray();

			// Stale-compile probe verdict: either the compile honestly failed/was not
			// reported (expected here - missing meshes), or it succeeded AND the compiled
			// file's timestamp advanced past the planted stale stamp. The pre-fix poll
			// returned success off the stale file's mere existence - this catches that.
			if ( write?.Compiled is not true )
			{
				Result.augmentCompileVerified = true;
			}
			else
			{
				var compiledPath = write.CompiledFile ?? staleCompiledPath;
				Result.augmentCompileVerified = File.Exists( compiledPath )
					&& (staleStampUtc is not { } stamp
						|| File.GetLastWriteTimeUtc( compiledPath ) > stamp);
			}
			Note( $"stale-compile probe: compiled={write?.Compiled} verified={Result.augmentCompileVerified}" );

			// The augmenter must really have added our clips to the vmdl on disk. (THIS
			// batch's clip names, not Result.clipNames - the standalone batch additionally
			// converts the steps fixture, which the augment run does not.)
			try
			{
				var text = File.ReadAllText( augmentVmdlPath );
				var augmentClipNames = batch.Clips.Where( c => c.Success ).Select( c => c.ClipName ).ToArray();
				Result.augmentVmdlContainsClips = augmentClipNames.Length > 0
					&& augmentClipNames.All( c => text.Contains( c, StringComparison.OrdinalIgnoreCase ) );

				// Stale-entry verdict: the planted missing-DMX AnimFile must be GONE from
				// the written vmdl and reported as a batch warning - otherwise the recompile
				// above could only have failed.
				Result.augmentWarnings = batch.Warnings.ToArray();
				if ( Result.augmentStalePlanted )
				{
					Result.augmentStalePruned =
						!text.Contains( StaleProbeName, StringComparison.OrdinalIgnoreCase )
						&& batch.Warnings.Any( w =>
							w.Contains( StaleProbeName, StringComparison.Ordinal ) );
					Note( $"stale AnimFile probe: pruned={Result.augmentStalePruned} "
						+ $"warnings=[{string.Join( " | ", batch.Warnings )}]" );
				}
			}
			catch
			{
				Result.augmentVmdlContainsClips = false;
			}

			// Install-path guard: the pipeline must refuse to touch the SHIPPED citizen
			// vmdl (this is the exact path that crashed a user session) without writing
			// anything next to it.
			var engineRoot = EditorPipeline.EngineRootPath;
			if ( engineRoot is not null )
			{
				var shipped = Path.Combine( engineRoot, "addons", "citizen", "Assets",
					"models", "citizen_human", "citizen_human_male.vmdl" );
				var guard = await EditorPipeline.WriteAndCompileAsync( batch, AugmentDmxFolder, shipped );
				Result.installGuardRejected = guard.Errors.Count > 0
					&& guard.Errors[0].Contains( "installation", StringComparison.OrdinalIgnoreCase )
					&& !File.Exists( shipped + ".bak" );
				Note( $"install guard probe: rejected={Result.installGuardRejected} "
					+ $"error='{guard.Errors.FirstOrDefault()}'" );
			}
			else
			{
				Note( "engine root unavailable - skipping install guard probe" );
				Result.installGuardRejected = true;
			}

			Result.augmentOk = Result.augmentedVmdlProduced && Result.augmentVmdlWritten
				&& Result.augmentRegistered && Result.augmentCompilePollCompleted
				&& !Result.augmentQuietInputsAbandon && Result.augmentVmdlContainsClips
				&& Result.augmentCompileVerified
				&& Result.installGuardRejected
				// The user-report repro (an5 mode) proves the augmented citizen vmdl really
				// COMPILES - the scratch project resolves the citizen addon's meshes/prefabs,
				// so a compile failure here is a genuine regression, never noise - and that
				// the planted stale (missing-DMX) entry was pruned rather than left to fail
				// the recompile.
				&& (!Result.augmentAn5Mode
					|| (Result.augmentCompiled
						&& Result.augmentStalePlanted && Result.augmentStalePruned));

			Note( $"augment: produced={Result.augmentedVmdlProduced} written={Result.augmentVmdlWritten} "
				+ $"registered={Result.augmentRegistered} pollCompleted={Result.augmentCompilePollCompleted} "
				+ $"compiled={Result.augmentCompiled} quietInputsAbandon={Result.augmentQuietInputsAbandon} "
				+ $"containsClips={Result.augmentVmdlContainsClips} compileVerified={Result.augmentCompileVerified} "
				+ $"guardRejected={Result.installGuardRejected} "
				+ $"mainThreadAfter={Result.augmentOnMainThreadAfter} => augmentOk={Result.augmentOk}" );
		}
		catch ( Exception e )
		{
			Result.augmentOk = false;
			Note( $"augment FAILED with exception: {e}" );
		}
		Flush();
	}

	// ---- plumbing ---------------------------------------------------------------

	static async Task<bool> WaitUntil( Func<bool> condition, float timeoutSeconds )
	{
		var sw = Stopwatch.StartNew();
		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			bool ok = false;
			try { ok = condition(); }
			catch { /* not ready yet */ }

			if ( ok )
				return true;

			await Task.Delay( 250 );
		}

		return false;
	}

	static void Note( string message )
	{
		Result.log.Add( $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}" );
		Log.Info( $"[hr-ui-smoke] {message}" );
	}

	static void Flush()
	{
		try
		{
			File.WriteAllText( _resultPath, JsonSerializer.Serialize( Result,
				new JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch
		{
			// never let result IO take the editor down
		}
	}

	class SmokeResult
	{
		public bool engineBooted { get; set; }
		public bool assetSystemReady { get; set; }
		public string inspectProfile { get; set; }
		public float inspectConfidence { get; set; }
		public bool inspectNeedsUserDecision { get; set; }
		public string skeletonSignature { get; set; }
		public string entryStatus { get; set; }
		public string entryChip { get; set; }
		public bool targetResolved { get; set; }
		public int clipCount { get; set; }
		public int solvedClipCount { get; set; }
		public string[] clipNames { get; set; } = Array.Empty<string>();
		public string[] additiveClipNames { get; set; } = Array.Empty<string>();
		public int footstepEventCount { get; set; }
		public int locomotionSetReports { get; set; }
		public string[] batchErrors { get; set; } = Array.Empty<string>();
		public bool previewModelLoaded { get; set; }
		public bool previewWidgetOk { get; set; }
		public bool previewPoseUpright { get; set; }
		public bool previewGhostOk { get; set; }
		public string previewPelvisRest { get; set; }
		public bool windowConstructed { get; set; }
		public bool optionsPlumbingOk { get; set; }
		public bool locomotionSmartDisableOk { get; set; }
		public bool userPresetRoundTrip { get; set; }
		public bool dlSolverOk { get; set; }
		public bool citizenTargetOk { get; set; }
		public int dmxFilesWritten { get; set; }
		public string vmdlPath { get; set; }
		public bool assetRegistered { get; set; }
		public bool dmxVmdlCompiled { get; set; }
		public bool compiledFileFresh { get; set; }
		public bool footstepEventsInVmdl { get; set; }
		public string compiledFile { get; set; }
		public string[] writeErrors { get; set; } = Array.Empty<string>();
		public bool modelLoads { get; set; }
		public int boneCount { get; set; }
		public int animationCount { get; set; }
		public string[] animationNames { get; set; } = Array.Empty<string>();
		public bool sequenceVisible { get; set; }
		public bool additiveSequenceVisible { get; set; }

		// threading evidence (Convert-All crash root cause)
		public bool mainThreadAfterTaskRun { get; set; }

		// augment mode (HR_UI_SMOKE_AUGMENT)
		public bool augmentMode { get; set; }
		public bool augmentAn5Mode { get; set; }
		public int augmentAn5Takes { get; set; }
		public string[] augmentClipNames { get; set; } = Array.Empty<string>();
		public string[] augmentFailedClips { get; set; } = Array.Empty<string>();
		public string[] augmentCompileErrors { get; set; } = Array.Empty<string>();
		public bool augmentStalePlanted { get; set; }
		public bool augmentStalePruned { get; set; }
		public string[] augmentWarnings { get; set; } = Array.Empty<string>();
		public string augmentVmdlPath { get; set; }
		public bool augmentedVmdlProduced { get; set; }
		public bool augmentVmdlWritten { get; set; }
		public bool augmentRegistered { get; set; }
		public bool augmentCompilePollCompleted { get; set; }
		public bool augmentCompiled { get; set; }
		public bool augmentQuietInputsAbandon { get; set; }
		public bool augmentCompileVerified { get; set; }
		public bool augmentVmdlContainsClips { get; set; }
		public bool installGuardRejected { get; set; }
		public bool augmentOnMainThreadAfter { get; set; }
		public string[] augmentWriteErrors { get; set; } = Array.Empty<string>();
		public bool augmentOk { get; set; }

		public bool previewSkeletonViewOk { get; set; }
		public int previewSkeletonPixels { get; set; }
		public int previewGhostPixels { get; set; }

		// custom-target repro (HR_UI_SMOKE_CUSTOM)
		public bool customMode { get; set; }
		public bool baselinePlaybackMoves { get; set; }
		public string baselinePlaybackDetail { get; set; }
		public bool customModelTargetResolved { get; set; }
		public string customModelTargetError { get; set; }
		public bool customModelPreviewHasModel { get; set; }
		public bool customModelPreviewPoseOk { get; set; }
		public int customModelSkeletonLines { get; set; }
		public int customModelSkeletonPixels { get; set; }
		public bool customModelNoWildBones { get; set; }
		public bool customFbxNoWildBones { get; set; }
		public bool customModelCompiled { get; set; }
		public bool customModelSequenceVisible { get; set; }
		public bool customModelPlaybackMoves { get; set; }
		public bool customModelPlaybackUpright { get; set; }
		public float customModelSolvedVsCompiledHipDeg { get; set; }
		public string customModelPlaybackDetail { get; set; }
		public string[] customModelWriteErrors { get; set; } = Array.Empty<string>();
		public bool customFbxTargetResolved { get; set; }
		public string customFbxTargetError { get; set; }
		public bool customFbxPreviewHasModel { get; set; }
		public bool customFbxPreviewPoseOk { get; set; }
		public int customFbxSkeletonLines { get; set; }
		public int customFbxSkeletonPixels { get; set; }
		public bool customFbxPreviewModelCompiled { get; set; }
		public int customFbxMissingVmats { get; set; }
		public int customFbxTextureSpamLines { get; set; }
		public int customFbxErrorPixels { get; set; }
		public int customFbxColoredPixels { get; set; }
		public bool customFbxHasTextures { get; set; }
		public bool customFbxEmbeddedVisible { get; set; }
		public bool customFbxEmbeddedMoves { get; set; }
		public bool customFbxCompiled { get; set; }
		public bool customFbxSequenceVisible { get; set; }
		public bool customFbxPlaybackMoves { get; set; }
		public bool customFbxPlaybackUpright { get; set; }
		public float customFbxSolvedVsCompiledHipDeg { get; set; }
		public string customFbxPlaybackDetail { get; set; }
		public string[] customFbxWriteErrors { get; set; } = Array.Empty<string>();
		public bool customOk { get; set; }

		public bool refusedWrongProject { get; set; }
		public bool completed { get; set; }
		public bool passed { get; set; }
		public System.Collections.Generic.List<string> log { get; set; } = new();
	}
}
