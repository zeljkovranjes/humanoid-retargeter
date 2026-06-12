using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
public static class EditorPipeline
{
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
		return RetargetTargetSpec.SboxDefault( File.ReadAllText( path ) );
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

	/// <summary>
	/// Writes a batch result to disk and compiles it. DMX files go to
	/// <c>Assets/&lt;dmxFolderRelative&gt;/</c> (must match the
	/// <see cref="BatchOptions.DmxFolderRelative"/> the batch ran with, since the vmdl's
	/// AnimFile entries reference them by that assets-relative path). In standalone mode a
	/// new vmdl is written next to the DMX files; in augment mode (<paramref name="augmentVmdlPath"/>
	/// non-null and the batch produced <see cref="RetargetBatchResult.AugmentedVmdl"/>) the
	/// ORIGINAL vmdl is overwritten non-destructively: a <c>.vmdl.bak</c> backup is written
	/// next to it first (design §9).
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

		// ---- 1. DMX files -------------------------------------------------------------
		try
		{
			var dmxDir = Path.Combine( assetsPath, dmxFolderRelative.Replace( '/', Path.DirectorySeparatorChar ) );
			Directory.CreateDirectory( dmxDir );
			foreach ( var clip in successful )
			{
				File.WriteAllText( Path.Combine( dmxDir, clip.DmxFileName ), clip.DmxContent );
				result.DmxFilesWritten++;
			}

			// ---- 2. vmdl ---------------------------------------------------------------
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

		// ---- 3. register + compile ----------------------------------------------------
		result.VmdlAsset = AssetSystem.RegisterFile( result.VmdlPath );
		if ( result.VmdlAsset is null )
		{
			result.Errors.Add( $"Asset registration failed for {result.VmdlPath}." );
			return result;
		}

		var logOffset = SboxLogLength();
		result.Compiled = await CompileAndWaitAsync( result.VmdlAsset );
		result.CompiledFile = Try( () => result.VmdlAsset.GetCompiledFile( true ) );
		if ( !result.Compiled )
		{
			var detail = TryReadCompileErrors( logOffset,
				successful.Select( c => c.DmxFileName )
					.Append( Path.GetFileName( result.VmdlPath ) ).ToList() );
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

	static long SboxLogLength()
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

	/// <summary>Reads the log slice written since <paramref name="fromOffset"/> and returns
	/// the lines that look like compiler errors for our files, or null when none found.</summary>
	static string TryReadCompileErrors( long fromOffset, IReadOnlyList<string> fileNames )
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
			var slice = reader.ReadToEnd().Split( '\n' );

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
	/// .vmdl_c on disk).</summary>
	public static async Task<bool> CompileAndWaitAsync( Asset asset, float timeoutSeconds = 120 )
	{
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
