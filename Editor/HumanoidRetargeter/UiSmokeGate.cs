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
//   9. writes a JSON result to HR_UI_SMOKE and quits the editor

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
			&& Result.previewWidgetOk && Result.userPresetRoundTrip;
		Flush();
		Note( $"UI smoke gate finished, passed={Result.passed}" );

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

		var assetsPath = Project.Current.GetAssetsPath();
		Note( $"project={Project.Current.GetRootPath()} assets={assetsPath}" );

		// ---- 2. fixture in via the window's add-file path ----------------------
		var fixture = Environment.GetEnvironmentVariable( "HR_UI_FIXTURE" );
		if ( string.IsNullOrWhiteSpace( fixture ) || !File.Exists( fixture ) )
		{
			Note( $"fixture not found (HR_UI_FIXTURE='{fixture}')" );
			Flush();
			return;
		}

		var inspect = Retargeter.Inspect( File.ReadAllBytes( fixture ), Path.GetFileName( fixture ) );
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
				null, target.Spec.Rig, target.PreviewModelPath, target.PreviewPositionScale );
			Result.previewModelLoaded = preview.HasModel;
			preview.SetClip( clip );
			preview.Scrub( Math.Min( 5, preview.FrameCount - 1 ) );
			preview.ApplyCurrentFrame();
			Result.previewWidgetOk = true;
			Note( $"PreviewWidget: hasModel={preview.HasModel} frames={preview.FrameCount} frame applied OK" );
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
		public bool completed { get; set; }
		public bool passed { get; set; }
		public System.Collections.Generic.List<string> log { get; set; } = new();
	}
}
