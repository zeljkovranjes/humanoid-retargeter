// M0 editor-process compile gate.
//
// This only runs when the HR_M0_RESULT environment variable is set (done by
// dev/editor-rig/run_editor_gate.ps1). Normal users of the library never trigger it.
//
// What it does (inside a real sbox-dev.exe editor session):
//   1. waits for the project + asset system to be ready
//   2. copies a known-good DMX animation fixture into the scratch project's Assets
//   3. generates a Base-Model .vmdl referencing it (citizen as base model)
//   4. registers + compiles the asset, polls for the .vmdl_c
//   5. loads the compiled model, records bone count / animation names
//   6. writes a JSON result file to HR_M0_RESULT and quits the editor

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.M0;

public static class M0Gate
{
	static bool _started;
	static readonly M0Result Result = new();
	static string _resultPath;

	[EditorEvent.Frame]
	public static void Tick()
	{
		if ( _started )
			return;

		_started = true;

		_resultPath = Environment.GetEnvironmentVariable( "HR_M0_RESULT" );
		if ( string.IsNullOrWhiteSpace( _resultPath ) )
			return; // not a gate run - do nothing, ever

		_ = RunAsync();
	}

	static async Task RunAsync()
	{
		Note( "M0 gate starting" );
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
		Result.passed = Result.dmxVmdlCompiled && Result.sequenceVisible;
		Flush();
		Note( $"M0 gate finished, passed={Result.passed}" );

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

		var project = Project.Current;
		Result.projectPath = project.GetRootPath();

		// Never touch a real session: only the hr-editor-rig scratch project is fair game.
		// (Observed failure mode: gate env vars leaking into a user-launched editor wrote
		// m0/ outputs into whatever project happened to be open - including the citizen
		// addon inside the s&box install.)
		if ( (Result.projectPath ?? "").IndexOf( "hr-editor-rig", StringComparison.OrdinalIgnoreCase ) < 0 )
		{
			Result.refusedWrongProject = true;
			Note( $"REFUSING to run: open project '{Result.projectPath}' is not the hr-editor-rig "
				+ "scratch (leaked HR_M0_RESULT env var?) - aborting without touching the project" );
			Flush();
			return;
		}

		var assetsPath = project.GetAssetsPath();
		Note( $"project={Result.projectPath} assets={assetsPath}" );

		// ---- 2. copy fixture dmx + generate vmdl -------------------------------
		var dmxSource = Environment.GetEnvironmentVariable( "HR_M0_FIXTURE" );
		if ( string.IsNullOrWhiteSpace( dmxSource ) || !File.Exists( dmxSource ) )
		{
			Note( $"fixture dmx not found (HR_M0_FIXTURE='{dmxSource}')" );
			Flush();
			return;
		}

		var m0Dir = Path.Combine( assetsPath, "m0" );
		Directory.CreateDirectory( m0Dir );

		var dmxDest = Path.Combine( m0Dir, "ref_idlepose.dmx" );
		File.Copy( dmxSource, dmxDest, overwrite: true );

		var vmdlPath = Path.Combine( m0Dir, "m0_test.vmdl" );
		File.WriteAllText( vmdlPath, BuildVmdl( baseModel: "models/citizen/citizen.vmdl" ) );
		Result.vmdlVariant = "citizen_base";
		Result.fixtureCopied = true;
		Note( $"fixture copied -> {dmxDest}, vmdl generated -> {vmdlPath}" );
		Flush();

		// ---- 3. register + compile ---------------------------------------------
		var asset = AssetSystem.RegisterFile( vmdlPath );
		Result.assetRegistered = asset is not null;
		Note( $"assetRegistered={Result.assetRegistered} path={asset?.Path}" );
		Flush();

		if ( asset is null )
			return;

		var compiled = await CompileAndWait( asset );

		if ( !compiled )
		{
			// Experiment: retry without a base model (standalone AnimationList).
			Note( "citizen_base variant failed to compile, retrying standalone (base_model_name=\"\")" );
			File.WriteAllText( vmdlPath, BuildVmdl( baseModel: "" ) );
			Result.vmdlVariant = "standalone";
			Flush();

			compiled = await CompileAndWait( asset );
		}

		Result.dmxVmdlCompiled = compiled;
		Result.compiledFile = TryGet( () => asset.GetCompiledFile( true ) );
		Note( $"dmxVmdlCompiled={compiled} variant={Result.vmdlVariant} compiledFile={Result.compiledFile}" );
		Flush();

		if ( !compiled )
			return;

		// ---- 4. load the model, inspect ----------------------------------------
		var model = Model.Load( asset.Path );
		Result.modelLoads = model is not null && !model.IsError;
		if ( model is not null )
		{
			Result.boneCount = model.BoneCount;
			Result.animationCount = model.AnimationCount;
			Result.animationNames = model.AnimationNames?.ToArray() ?? Array.Empty<string>();
			Result.sequenceVisible = Result.animationNames.Any( n =>
				string.Equals( n, "m0_test", StringComparison.OrdinalIgnoreCase ) );
		}

		Note( $"modelLoads={Result.modelLoads} bones={Result.boneCount} anims={Result.animationCount} " +
			$"names=[{string.Join( ", ", Result.animationNames )}] sequenceVisible={Result.sequenceVisible}" );
		Flush();
	}

	static async Task<bool> CompileAndWait( Asset asset )
	{
		bool compileReturned = false;
		try
		{
			compileReturned = asset.Compile( full: true );
		}
		catch ( Exception e )
		{
			Note( $"asset.Compile threw: {e}" );
		}

		Result.compileReturned = compileReturned;
		Note( $"asset.Compile(true) returned {compileReturned}" );
		Flush();

		var compiledAbs = TryGet( () => asset.GetCompiledFile( true ) );

		var ok = await WaitUntil( () =>
		{
			if ( asset.IsCompileFailed )
				return false;
			if ( asset.IsCompiled && asset.HasCompiledFile )
				return true;
			return compiledAbs is not null && File.Exists( compiledAbs );
		}, timeoutSeconds: 120 );

		Result.compileFailedFlag = TryGet( () => asset.IsCompileFailed );

		// final direct check on disk, the most honest signal
		if ( !ok && compiledAbs is not null && File.Exists( compiledAbs ) )
			ok = true;

		return ok && !Result.compileFailedFlag;
	}

	static string BuildVmdl( string baseModel )
	{
		// Mirrors addons/citizen/Assets/models/m0_retarget_test/m0_test.vmdl,
		// with source_filename pointing at the fixture inside the scratch project.
		return $$"""
			<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->
			{
				rootNode =
				{
					_class = "RootNode"
					children =
					[
						{
							_class = "ModelModifierList"
							children =
							[
								{
									_class = "ModelModifier_ScaleAndMirror"
									scale = 0.3937
									mirror_x = false
									mirror_y = false
									mirror_z = false
									flip_bone_forward = false
									swap_left_and_right_bones = false
								},
							]
						},
						{
							_class = "AnimationList"
							children =
							[
								{
									_class = "AnimFile"
									name = "m0_test"
									activity_name = ""
									activity_weight = 1
									weight_list_name = ""
									fade_in_time = 0.2
									fade_out_time = 0.2
									looping = false
									delta = false
									worldSpace = false
									hidden = false
									anim_markup_ordered = false
									disable_compression = false
									disable_interpolation = false
									enable_scale = false
									source_filename = "m0/ref_idlepose.dmx"
									start_frame = -1
									end_frame = -1
									framerate = -1.0
									take = 0
									reverse = false
								},
							]
							default_root_bone_name = "pelvis"
						},
					]
					model_archetype = ""
					primary_associated_entity = ""
					anim_graph_name = ""
					base_model_name = "{{baseModel}}"
				}
			}
			""";
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

	static T TryGet<T>( Func<T> getter )
	{
		try { return getter(); }
		catch { return default; }
	}

	static void Note( string message )
	{
		Result.log.Add( $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}" );
		Log.Info( $"[hr-m0] {message}" );
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

	class M0Result
	{
		public bool engineBooted { get; set; }
		public bool assetSystemReady { get; set; }
		public string projectPath { get; set; }
		public bool fixtureCopied { get; set; }
		public string vmdlVariant { get; set; }
		public bool assetRegistered { get; set; }
		public bool compileReturned { get; set; }
		public bool compileFailedFlag { get; set; }
		public bool dmxVmdlCompiled { get; set; }
		public string compiledFile { get; set; }
		public bool modelLoads { get; set; }
		public int boneCount { get; set; }
		public int animationCount { get; set; }
		public string[] animationNames { get; set; } = Array.Empty<string>();
		public bool sequenceVisible { get; set; }
		public bool refusedWrongProject { get; set; }
		public bool completed { get; set; }
		public bool passed { get; set; }
		public System.Collections.Generic.List<string> log { get; set; } = new();
	}
}
