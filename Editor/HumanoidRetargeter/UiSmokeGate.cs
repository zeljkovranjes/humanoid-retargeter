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
			&& (!Result.augmentMode || Result.augmentOk);
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
			// clip, enable, re-apply the frame and require the overlay to have drawn lines.
			try
			{
				preview.SetSourceGhost( entry.Scene.Skeleton, entry.Scene.Clips[0], entry.Mapping );
				preview.ShowSourceGhost = true;
				preview.ApplyCurrentFrame();
				Result.previewGhostOk = preview.HasSourceGhost && preview.GhostLineCount > 0;
				Note( $"source ghost: hasGhost={preview.HasSourceGhost} lines={preview.GhostLineCount} "
					+ $"ok={Result.previewGhostOk}" );
			}
			catch ( Exception e )
			{
				Result.previewGhostOk = false;
				Note( $"source ghost FAILED: {e}" );
			}
			Result.previewWidgetOk &= Result.previewGhostOk;

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

		// ---- 9. augment mode: the EXACT Convert-All window path -----------------
		var augmentTarget = Environment.GetEnvironmentVariable( "HR_UI_SMOKE_AUGMENT" );
		if ( !string.IsNullOrWhiteSpace( augmentTarget ) )
			await RunAugmentAsync( entry, target, augmentTarget );
	}

	/// <summary>Assets-relative DMX folder for the augment run (separate from the
	/// standalone run's folder so its writes never re-trigger that vmdl's compile).</summary>
	const string AugmentDmxFolder = OutputFolder + "/augment";

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
			var requests = new[]
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
				&& Result.installGuardRejected;

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

		public bool refusedWrongProject { get; set; }
		public bool completed { get; set; }
		public bool passed { get; set; }
		public System.Collections.Generic.List<string> log { get; set; } = new();
	}
}
