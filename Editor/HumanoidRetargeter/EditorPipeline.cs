using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.Editor;

/// <summary>
/// Editor-side glue around the engine-agnostic <see cref="Retargeter"/> facade: locates the
/// committed s&amp;box target-rig JSON, writes the facade's output strings to disk under the
/// project's Assets folder, registers them with the asset system and compiles, surfacing
/// compiler state. All file IO of the pipeline lives here - the facade itself is pure.
/// </summary>
/// <remarks>
/// Threading: <see cref="AssetSystem"/> / <see cref="Asset"/> calls are engine state and are
/// only safe on the editor main thread. Our async flows hop threads (<c>Task.Run</c> for the
/// pure conversion math, <c>Task.Delay</c> in poll loops), so every engine call below is
/// preceded by <see cref="SwitchToMainThread"/> - the awaitable queues its continuation via
/// the sanctioned <see cref="MainThread.Queue"/> dispatcher (the same pattern the official
/// tools use, e.g. AssetBrowser). Touching engine objects from a pool thread is a native
/// crash, not a managed exception - it must be prevented structurally.
/// </remarks>
public static class EditorPipeline
{
	// ============================================================== threading

	/// <summary>
	/// Awaitable hop to the editor main thread. Completes synchronously when already
	/// there; otherwise the continuation is queued via <see cref="MainThread.Queue"/>.
	/// Await this before touching engine objects (AssetSystem, Asset, widgets) in any
	/// flow that has been on a background task or resumed from <c>Task.Delay</c>.
	/// </summary>
	public static MainThreadAwaitable SwitchToMainThread() => default;

	/// <summary>Awaiter behind <see cref="SwitchToMainThread"/>.</summary>
	public readonly struct MainThreadAwaitable : INotifyCompletion
	{
		public MainThreadAwaitable GetAwaiter() => this;
		public bool IsCompleted => ThreadSafe.IsMainThread;
		public void OnCompleted( Action continuation ) => MainThread.Queue( continuation );
		public void GetResult() { }
	}

	// ====================================================== install-path guard

	/// <summary>Root folder of the s&amp;box installation (directory of the running
	/// sbox-dev.exe), or null when it cannot be determined.</summary>
	internal static string EngineRootPath
	{
		get
		{
			try
			{
				var exeDir = Path.GetDirectoryName( Environment.ProcessPath );
				return exeDir is null ? null : Path.GetFullPath( exeDir );
			}
			catch
			{
				return null;
			}
		}
	}

	/// <summary>
	/// True when <paramref name="path"/> resolves to somewhere inside the s&amp;box
	/// installation (the running editor's exe directory, with a literal
	/// <c>steamapps\common\sbox</c> fallback). Writing/compiling assets there has the
	/// engine's own content watcher fighting our writes and has crashed the editor
	/// natively - conversion outputs and augment targets must live in a user project.
	/// </summary>
	public static bool IsUnderEngineInstall( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return false;

		try
		{
			var full = Path.GetFullPath( path );

			// The CURRENT PROJECT is always writable, even when it physically lives inside
			// the install tree (e.g. the shipped sample projects under <sbox>/samples/...).
			// The guard exists to protect ENGINE content (core/, addons/, citizen, ...),
			// not the project the user deliberately opened.
			var project = Sandbox.Project.Current?.GetRootPath();
			if ( !string.IsNullOrWhiteSpace( project ) )
			{
				var projPrefix = Path.GetFullPath( project ).TrimEnd( Path.DirectorySeparatorChar )
					+ Path.DirectorySeparatorChar;
				if ( full.StartsWith( projPrefix, StringComparison.OrdinalIgnoreCase ) )
					return false;
			}

			var root = EngineRootPath;
			if ( root is not null )
			{
				var prefix = root.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
				if ( full.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
					return true;
			}

			// Fallback: a Steam-installed s&box that is not the running process (or the
			// process path was unavailable).
			return full.Replace( '/', '\\' ).IndexOf(
				@"steamapps\common\sbox\", StringComparison.OrdinalIgnoreCase ) >= 0;
		}
		catch
		{
			return false;
		}
	}
	/// <summary>Assets-relative path of the committed s&amp;box target rig definition.</summary>
	public const string TargetRigJsonRelative = "humanoid_retargeter/target_rig_sbox.json";

	/// <summary>
	/// Finds a file shipped in this library's Assets folder. Works both when the library
	/// is the open project and when it is installed under <c>Libraries/</c> of a game
	/// project (the editor mounts libraries from there). Returns null when not found.
	/// </summary>
	public static string FindLibraryAssetFile( string assetsRelativePath )
	{
		var relative = assetsRelativePath.Replace( '/', Path.DirectorySeparatorChar );
		var root = Project.Current?.GetRootPath();
		if ( root is null )
			return null;

		var candidates = new List<string>
		{
			Path.Combine( root, "Assets", relative ),
		};

		var librariesDir = Path.Combine( root, "Libraries" );
		if ( Directory.Exists( librariesDir ) )
		{
			foreach ( var lib in Directory.EnumerateDirectories( librariesDir ) )
				candidates.Add( Path.Combine( lib, "Assets", relative ) );
		}

		return candidates.FirstOrDefault( File.Exists );
	}

	/// <summary>
	/// Loads the shipped s&amp;box default target (rig JSON → <see cref="RetargetTargetSpec.SboxDefault"/>).
	/// Throws <see cref="FileNotFoundException"/> when the rig JSON is not reachable from
	/// the current project.
	/// </summary>
	public static RetargetTargetSpec LoadSboxDefaultTarget()
	{
		var path = FindLibraryAssetFile( TargetRigJsonRelative )
			?? throw new FileNotFoundException(
				$"Target rig definition not found ({TargetRigJsonRelative}). Is the humanoid_retargeter library installed?" );
		// DL weights ride along when the committed model asset exists (enables the
		// deep-learning fallback solver; null keeps everything else working without it).
		return RetargetTargetSpec.SboxDefault( File.ReadAllText( path ), DlAssets.TryLoadWeights() );
	}

	/// <summary>Disk + asset-system outcome of one conversion run.</summary>
	public sealed class WriteResult
	{
		/// <summary>Absolute path of the written vmdl (standalone or augmented original).</summary>
		public string VmdlPath { get; set; }

		/// <summary>The registered vmdl asset; null when registration failed.</summary>
		public Asset VmdlAsset { get; set; }

		/// <summary>Whether the vmdl compiled to a .vmdl_c.</summary>
		public bool Compiled { get; set; }

		/// <summary>Absolute path of the compiled file when known.</summary>
		public string CompiledFile { get; set; }

		/// <summary>Number of DMX files written.</summary>
		public int DmxFilesWritten { get; set; }

		/// <summary>IO / registration / compile problems (conversion errors are reported
		/// separately on the batch result).</summary>
		public List<string> Errors { get; } = new();
	}

	/// <summary>Delay between the last output write and triggering the vmdl compile. The
	/// engine refuses to compile while an asset's input files keep changing ("Waited too
	/// long (1270ms) for quiet inputs ... abandoning recompile!"); waiting longer than that
	/// watcher window after our final write guarantees a quiet compile.</summary>
	const int InputSettleDelayMs = 2000;

	/// <summary>
	/// Writes a batch result to disk and compiles it. DMX files go to
	/// <c>Assets/&lt;dmxFolderRelative&gt;/</c> (must match the
	/// <see cref="BatchOptions.DmxFolderRelative"/> the batch ran with, since the vmdl's
	/// AnimFile entries reference them by that assets-relative path). In standalone mode a
	/// new vmdl is written next to the DMX files; in augment mode (<paramref name="augmentVmdlPath"/>
	/// non-null and the batch produced <see cref="RetargetBatchResult.AugmentedVmdl"/>) the
	/// ORIGINAL vmdl is overwritten non-destructively: a <c>.vmdl.bak</c> backup is written
	/// next to it first (design §9).
	/// Sequencing: ALL files are written first, then registered once, then - after
	/// <see cref="InputSettleDelayMs"/> so the engine's input watcher goes quiet - the vmdl
	/// is compiled once (with a single retry if the engine still abandoned the recompile).
	/// Safe to call from any thread; engine calls are marshalled to the main thread.
	/// </summary>
	public static async Task<WriteResult> WriteAndCompileAsync(
		RetargetBatchResult batch, string dmxFolderRelative, string augmentVmdlPath = null,
		string standaloneVmdlName = "retargeted_animations" )
	{
		var result = new WriteResult();
		var assetsPath = Project.Current?.GetAssetsPath();
		if ( assetsPath is null )
		{
			result.Errors.Add( "No project is open." );
			return result;
		}

		// ---- 0. never write into the s&box installation ---------------------------------
		// The engine owns and watches that content; compiling our writes there fought the
		// watcher ("quiet inputs" abandons) and ended in a native editor crash.
		if ( augmentVmdlPath is not null && IsUnderEngineInstall( augmentVmdlPath ) )
		{
			result.Errors.Add(
				$"Cannot modify models inside the s&box installation ({augmentVmdlPath}). "
				+ "Copy the model into your project's Assets folder and pick that copy instead." );
			return result;
		}
		if ( IsUnderEngineInstall( assetsPath ) )
		{
			result.Errors.Add(
				$"Cannot write conversion outputs inside the s&box installation ({assetsPath}). "
				+ "Open a project that lives outside the s&box install folder." );
			return result;
		}

		var successful = batch.Clips.Where( c => c.Success ).ToList();
		if ( successful.Count == 0 )
		{
			result.Errors.Add( "No clip converted successfully - nothing written." );
			return result;
		}

		// Augment requested but the batch produced no augmented vmdl (parse failure, name
		// collision, ...): FAIL the whole operation before anything touches disk. Silently
		// falling back to a standalone vmdl would look green while ignoring the user's
		// explicit "add to existing vmdl" choice.
		if ( augmentVmdlPath is not null && batch.AugmentedVmdl is null )
		{
			result.Errors.Add(
				$"Augmenting {Path.GetFileName( augmentVmdlPath )} failed - nothing was written "
				+ "(standalone fallback is disabled when augmenting was requested)." );
			result.Errors.AddRange( batch.Errors.Where(
				e => e.Contains( "augment", StringComparison.OrdinalIgnoreCase ) ) );
			return result;
		}

		// ---- 1. write ALL files first (DMX, then .bak, then the vmdl LAST) -------------
		// Nothing may touch the asset system until every input file is final - the engine
		// abandons recompiles whose inputs keep changing.
		var dmxPaths = new List<string>();
		try
		{
			var dmxDir = Path.Combine( assetsPath, dmxFolderRelative.Replace( '/', Path.DirectorySeparatorChar ) );
			Directory.CreateDirectory( dmxDir );
			foreach ( var clip in successful )
			{
				var dmxPath = Path.Combine( dmxDir, clip.DmxFileName );
				File.WriteAllText( dmxPath, clip.DmxContent );
				dmxPaths.Add( dmxPath );
				result.DmxFilesWritten++;
			}

			if ( augmentVmdlPath is not null )
			{
				File.Copy( augmentVmdlPath, augmentVmdlPath + ".bak", overwrite: true );
				File.WriteAllText( augmentVmdlPath, batch.AugmentedVmdl );
				result.VmdlPath = augmentVmdlPath;
			}
			else
			{
				result.VmdlPath = Path.Combine( dmxDir, standaloneVmdlName + ".vmdl" );
				File.WriteAllText( result.VmdlPath, batch.StandaloneVmdl );
			}
		}
		catch ( Exception e )
		{
			result.Errors.Add( $"Writing outputs failed: {e.Message}" );
			return result;
		}

		// ---- 2. register each new file once (engine state - main thread only) ----------
		await SwitchToMainThread();

		foreach ( var dmxPath in dmxPaths )
			Try( () => AssetSystem.RegisterFile( dmxPath ) );

		result.VmdlAsset = AssetSystem.RegisterFile( result.VmdlPath );
		if ( result.VmdlAsset is null )
		{
			result.Errors.Add( $"Asset registration failed for {result.VmdlPath}." );
			return result;
		}

		// ---- 3. settle, then compile ONCE (single retry on abandoned recompile) --------
		await Task.Delay( InputSettleDelayMs );

		var vmdlFileName = Path.GetFileName( result.VmdlPath );
		var logOffset = SboxLogLength();
		result.Compiled = await CompileAndWaitAsync( result.VmdlAsset );

		if ( !result.Compiled && LogSliceShowsAbandonedRecompile( logOffset, vmdlFileName ) )
		{
			Log.Warning( $"[humanoid-retargeter] engine abandoned the recompile of {vmdlFileName} "
				+ "(inputs not quiet) - retrying once after a settle delay" );
			await Task.Delay( InputSettleDelayMs );
			logOffset = SboxLogLength();
			result.Compiled = await CompileAndWaitAsync( result.VmdlAsset );
		}

		await SwitchToMainThread();
		result.CompiledFile = Try( () => result.VmdlAsset.GetCompiledFile( true ) );
		if ( !result.Compiled )
		{
			var detail = TryReadCompileErrors( logOffset,
				successful.Select( c => c.DmxFileName )
					.Append( vmdlFileName ).ToList() );
			result.Errors.Add( detail is null
				? $"vmdl did not compile: {result.VmdlAsset.Path} (see console for resourcecompiler output)."
				: $"vmdl did not compile: {result.VmdlAsset.Path}\n{detail}" );
		}

		return result;
	}

	// ---- compile-error capture --------------------------------------------------------
	// The asset system exposes no Asset.LastCompileError-style API (verified against
	// Sandbox.Tools.xml), but resourcecompiler output lands in <sbox>/logs/sbox-dev.log -
	// the same per-run log slice dev/editor-rig/run_ui_smoke.ps1 scrapes. Reading the slice
	// written since our Compile() call gives the actual error text for the UI.

	static string SboxLogFile()
	{
		try
		{
			// The editor runs with the s&box root as working directory; the exe also lives
			// directly under it (sbox-dev.exe), so probe both.
			var candidates = new List<string>
			{
				Path.Combine( Environment.CurrentDirectory, "logs", "sbox-dev.log" ),
			};
			var exeDir = Path.GetDirectoryName( Environment.ProcessPath );
			if ( exeDir is not null )
				candidates.Add( Path.Combine( exeDir, "logs", "sbox-dev.log" ) );
			return candidates.FirstOrDefault( File.Exists );
		}
		catch
		{
			return null;
		}
	}

	internal static long SboxLogLength()
	{
		try
		{
			var file = SboxLogFile();
			return file is null ? -1 : new FileInfo( file ).Length;
		}
		catch
		{
			return -1;
		}
	}

	/// <summary>Reads the raw log slice written since <paramref name="fromOffset"/>, or null
	/// when the log is unreachable.</summary>
	internal static string ReadLogSlice( long fromOffset )
	{
		try
		{
			var file = SboxLogFile();
			if ( file is null || fromOffset < 0 )
				return null;

			using var fs = File.Open( file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
			if ( fs.Length < fromOffset )
				fromOffset = 0; // log was truncated/recreated
			fs.Seek( fromOffset, SeekOrigin.Begin );
			using var reader = new StreamReader( fs );
			return reader.ReadToEnd();
		}
		catch
		{
			return null;
		}
	}

	/// <summary>True when the per-run log slice since <paramref name="fromOffset"/> shows
	/// the engine abandoning the recompile of <paramref name="vmdlFileName"/> because its
	/// inputs would not go quiet.</summary>
	internal static bool LogSliceShowsAbandonedRecompile( long fromOffset, string vmdlFileName )
	{
		var slice = ReadLogSlice( fromOffset );
		if ( slice is null )
			return false;

		return slice.Split( '\n' ).Any( l =>
			l.Contains( "abandoning recompile", StringComparison.OrdinalIgnoreCase )
			&& l.Contains( vmdlFileName, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>Reads the log slice written since <paramref name="fromOffset"/> and returns
	/// the lines that look like compiler errors for our files, or null when none found.</summary>
	static string TryReadCompileErrors( long fromOffset, IReadOnlyList<string> fileNames )
	{
		try
		{
			var raw = ReadLogSlice( fromOffset );
			if ( raw is null )
				return null;
			var slice = raw.Split( '\n' );

			var interesting = slice
				.Select( l => l.TrimEnd( '\r' ) )
				.Where( l => l.Length > 0
					&& (l.Contains( "error", StringComparison.OrdinalIgnoreCase )
						|| l.Contains( "failed", StringComparison.OrdinalIgnoreCase ))
					&& (fileNames.Any( f => l.Contains( f, StringComparison.OrdinalIgnoreCase ) )
						|| l.Contains( "resourcecompiler", StringComparison.OrdinalIgnoreCase )
						|| l.Contains( "ModelDoc", StringComparison.OrdinalIgnoreCase )) )
				.ToList();

			if ( interesting.Count == 0 )
				return null;
			return string.Join( "\n", interesting.TakeLast( 12 ) );
		}
		catch
		{
			return null; // error capture must never take the pipeline down
		}
	}

	/// <summary>Kicks a full compile and polls until the compiled file exists or the asset
	/// reports failure (same strategy as the M0 gate - the most honest signal is the
	/// .vmdl_c on disk). Safe to call from any thread: the compile trigger and every
	/// <see cref="Asset"/> state query are marshalled to the editor main thread
	/// (<see cref="SwitchToMainThread"/>) because <c>Task.Delay</c> continuations may
	/// resume on a pool thread, and engine objects must never be touched there.</summary>
	public static async Task<bool> CompileAndWaitAsync( Asset asset, float timeoutSeconds = 120 )
	{
		await SwitchToMainThread();
		try
		{
			asset.Compile( full: true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] asset.Compile threw: {e.Message}" );
		}

		var compiledAbs = Try( () => asset.GetCompiledFile( true ) );

		var sw = Stopwatch.StartNew();
		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			await SwitchToMainThread();
			if ( Try( () => asset.IsCompileFailed ) )
				return false;
			if ( Try( () => asset.IsCompiled && asset.HasCompiledFile ) )
				return true;
			if ( compiledAbs is not null && File.Exists( compiledAbs ) )
				return true;

			await Task.Delay( 250 );
		}

		return compiledAbs is not null && File.Exists( compiledAbs );
	}

	static T Try<T>( Func<T> getter )
	{
		try { return getter(); }
		catch { return default; }
	}
}
