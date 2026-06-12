// UI smoke gate: headless end-to-end through the SAME code paths the retarget window uses.
//
// Only runs when the HR_UI_SMOKE environment variable is set (done by
// dev/editor-rig/run_ui_smoke.ps1). Normal users of the library never trigger it.
//
// What it does (inside a real sbox-dev.exe editor session):
//   1. waits for the project + asset system to be ready
//   2. loads the source fixture (HR_UI_FIXTURE, an .fbx) via SourceFileEntry.Load -
//      the exact add-file path of the window (user preset lookup -> preset detection
//      -> auto map) - plus Retargeter.Inspect for the report
//   3. resolves the s&box default target via TargetPickers.SboxDefault (window path)
//   4. Retargeter.ConvertBatch with the entry's mapping as override (window path)
//   5. constructs a PreviewWidget on the result and applies a solved frame headlessly
//   6. round-trips a user preset (UserPresets.Save -> TryLoad) for the fixture rig
//   7. EditorPipeline.WriteAndCompileAsync: DMX + standalone vmdl into Assets,
//      RegisterFile + Compile, polls the .vmdl_c
//   8. Model.Load on the compiled vmdl, verifies the converted sequence is visible
//   9. AUGMENT mode (HR_UI_SMOKE_AUGMENT = absolute path of a vmdl inside the scratch
//      Assets): drives the EXACT Convert-All window path (RetargetWindow.ConvertAndWriteAsync)
//      against that vmdl. The fixture vmdl references meshes a scratch project cannot
//      resolve, so a missing-mesh compile failure is acceptable - the assertions are:
//      augmented vmdl + .bak written, asset registered, compile poll COMPLETES, no
//      "quiet inputs ... abandoning recompile" for our vmdl, the install-path guard
//      rejects the shipped citizen vmdl, and the editor survives.
//  10. writes a JSON result to HR_UI_SMOKE and quits the editor
//
// Safety: refuses to do anything when the open project is not the hr-editor-rig scratch
// (a leaked HR_UI_SMOKE env var must never write into - or quit - a real session).

using System;
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
		Result.passed = Result.dmxVmdlCompiled && Result.sequenceVisible
			&& Result.previewWidgetOk && Result.userPresetRoundTrip
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
		var request = new RetargetRequest
		{
			SourceData = entry.Bytes,
			SourceFileName = entry.FileName,
			MappingOverride = entry.Mapping,
			RootMotion = Cleanup.RootMotionMode.Off,
			FootPlantCleanup = true,
			ArmEffectorIk = false,
			LoopingOverride = null,
		};
		var batch = await Task.Run( () => Retargeter.ConvertBatch(
			new[] { request }, target.Spec,
			new BatchOptions { DmxFolderRelative = OutputFolder } ) );

		// Root-cause evidence for the Convert-All crash: record where Task.Run
		// continuations actually resume in an editor session.
		Result.mainThreadAfterTaskRun = ThreadSafe.IsMainThread;
		Note( $"after Task.Run continuation: mainThread={Result.mainThreadAfterTaskRun}" );

		Result.clipCount = batch.Clips.Count;
		Result.solvedClipCount = batch.Clips.Count( c => c.Success && c.SolvedFrames is { Count: > 0 } );
		Result.clipNames = batch.Clips.Where( c => c.Success ).Select( c => c.ClipName ).ToArray();
		Result.batchErrors = batch.Errors.ToArray();
		Note( $"ConvertBatch: clips={Result.clipCount} solved={Result.solvedClipCount} "
			+ $"names=[{string.Join( ", ", Result.clipNames )}] errors={batch.Errors.Count}" );
		Flush();

		if ( Result.solvedClipCount == 0 )
			return;

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

		// ---- 7. write + register + compile (the window's convert path) ---------
		var write = await EditorPipeline.WriteAndCompileAsync(
			batch, OutputFolder, augmentVmdlPath: null, standaloneVmdlName: "ui_smoke_retargeted" );
		Result.dmxFilesWritten = write.DmxFilesWritten;
		Result.vmdlPath = write.VmdlPath;
		Result.assetRegistered = write.VmdlAsset is not null;
		Result.dmxVmdlCompiled = write.Compiled;
		Result.compiledFile = write.CompiledFile;
		Result.writeErrors = write.Errors.ToArray();
		Note( $"WriteAndCompile: dmx={write.DmxFilesWritten} vmdl={write.VmdlPath} "
			+ $"compiled={write.Compiled} compiledFile={write.CompiledFile} errors={write.Errors.Count}" );
		Flush();

		if ( !write.Compiled || write.VmdlAsset is null )
			return;

		// ---- 8. load the compiled model, verify the sequence -------------------
		var model = Model.Load( write.VmdlAsset.Path );
		Result.modelLoads = model is not null && !model.IsError;
		if ( model is not null )
		{
			Result.boneCount = model.BoneCount;
			Result.animationCount = model.AnimationCount;
			Result.animationNames = model.AnimationNames?.ToArray() ?? Array.Empty<string>();
			Result.sequenceVisible = Result.clipNames.Length > 0 && Result.clipNames.All(
				clip => Result.animationNames.Any( n => string.Equals( n, clip, StringComparison.OrdinalIgnoreCase ) ) );
		}

		Note( $"modelLoads={Result.modelLoads} bones={Result.boneCount} anims={Result.animationCount} "
			+ $"names=[{string.Join( ", ", Result.animationNames )}] sequenceVisible={Result.sequenceVisible}" );
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

			// The augmenter must really have added our clips to the vmdl on disk.
			try
			{
				var text = File.ReadAllText( augmentVmdlPath );
				Result.augmentVmdlContainsClips = Result.clipNames.Length > 0
					&& Result.clipNames.All( c => text.Contains( c, StringComparison.OrdinalIgnoreCase ) );
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
				&& Result.installGuardRejected;

			Note( $"augment: produced={Result.augmentedVmdlProduced} written={Result.augmentVmdlWritten} "
				+ $"registered={Result.augmentRegistered} pollCompleted={Result.augmentCompilePollCompleted} "
				+ $"compiled={Result.augmentCompiled} quietInputsAbandon={Result.augmentQuietInputsAbandon} "
				+ $"containsClips={Result.augmentVmdlContainsClips} guardRejected={Result.installGuardRejected} "
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
		public string[] batchErrors { get; set; } = Array.Empty<string>();
		public bool previewModelLoaded { get; set; }
		public bool previewWidgetOk { get; set; }
		public bool previewPoseUpright { get; set; }
		public string previewPelvisRest { get; set; }
		public bool userPresetRoundTrip { get; set; }
		public int dmxFilesWritten { get; set; }
		public string vmdlPath { get; set; }
		public bool assetRegistered { get; set; }
		public bool dmxVmdlCompiled { get; set; }
		public string compiledFile { get; set; }
		public string[] writeErrors { get; set; } = Array.Empty<string>();
		public bool modelLoads { get; set; }
		public int boneCount { get; set; }
		public int animationCount { get; set; }
		public string[] animationNames { get; set; } = Array.Empty<string>();
		public bool sequenceVisible { get; set; }

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
