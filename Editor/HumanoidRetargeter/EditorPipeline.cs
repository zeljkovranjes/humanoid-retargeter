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
	/// <remarks>
	/// EXEMPTION (deliberate, see also <see cref="WriteAndCompileAsync"/>): paths under the
	/// CURRENT project are never reported as engine-install paths, even when the project
	/// physically lives inside the install tree (the shipped sample projects under
	/// <c>&lt;sbox&gt;/samples/...</c> - users do convert there, e.g. samples/sweeper). The
	/// guard exists to protect ENGINE content (core/, addons/, citizen, ...), not the
	/// project the user deliberately opened. This re-opens engine-watcher exposure for
	/// install-resident projects - the original native-crash class - so the write/compile
	/// path does NOT rely on this guard alone: when the project root itself is under the
	/// install (<see cref="IsUnderEngineInstallIgnoringProject"/>), WriteAndCompileAsync
	/// switches to a defensive profile (longer input settle + an extra verified-compile
	/// retry). The crash itself was fixed by main-thread sequencing; this is defense in
	/// depth, not user blocking.
	/// </remarks>
	public static bool IsUnderEngineInstall( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return false;

		try
		{
			var full = Path.GetFullPath( path );

			// The CURRENT PROJECT is always writable (see remarks above).
			var project = Sandbox.Project.Current?.GetRootPath();
			if ( !string.IsNullOrWhiteSpace( project ) )
			{
				var projPrefix = Path.GetFullPath( project ).TrimEnd( Path.DirectorySeparatorChar )
					+ Path.DirectorySeparatorChar;
				if ( full.StartsWith( projPrefix, StringComparison.OrdinalIgnoreCase ) )
					return false;
			}

			return IsUnderEngineInstallIgnoringProject( full );
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// The raw install-tree test behind <see cref="IsUnderEngineInstall"/>, WITHOUT the
	/// current-project exemption. Used by <see cref="WriteAndCompileAsync"/> to detect
	/// install-resident projects (samples/...) and harden the compile sequencing there -
	/// such paths are allowed (the user opened that project) but the engine's content
	/// watcher still watches them.
	/// </summary>
	internal static bool IsUnderEngineInstallIgnoringProject( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return false;

		try
		{
			var full = Path.GetFullPath( path );

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

	/// <summary>Assets-relative path of the committed classic (4-finger) citizen target rig definition.</summary>
	public const string CitizenTargetRigJsonRelative = "humanoid_retargeter/target_rig_sbox_citizen.json";

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

	/// <summary>
	/// Loads the classic (4-finger) s&amp;box citizen target
	/// (rig JSON → <see cref="RetargetTargetSpec.SboxCitizen"/>). Throws
	/// <see cref="FileNotFoundException"/> when the rig JSON is not reachable from
	/// the current project.
	/// </summary>
	public static RetargetTargetSpec LoadSboxCitizenTarget()
	{
		var path = FindLibraryAssetFile( CitizenTargetRigJsonRelative )
			?? throw new FileNotFoundException(
				$"Citizen target rig definition not found ({CitizenTargetRigJsonRelative}). Is the humanoid_retargeter library installed?" );
		return RetargetTargetSpec.SboxCitizen( File.ReadAllText( path ), DlAssets.TryLoadWeights() );
	}

	/// <summary>
	/// Custom-FBX-target preparation, called before <see cref="Retargeter.ConvertBatch"/>
	/// when the picked target is a raw FBX (<see cref="TargetPickers.ResolvedTarget.FbxAbsolutePath"/>):
	/// copies the FBX into <c>Assets/&lt;outputFolderRelative&gt;/</c> (skipped when the
	/// source and destination are the same file) and points
	/// <see cref="RetargetTargetSpec.MeshFilePath"/>/<see cref="RetargetTargetSpec.MeshImportScale"/>
	/// at it, so the generated standalone vmdl embeds the mesh as a RenderMeshFile node.
	/// Without this the vmdl has no base model and no mesh — it compiles into an empty
	/// model (0 bones, 0 sequences) and playing it does nothing. Returns false with
	/// <paramref name="error"/> set when the copy fails; no-op for non-FBX targets.
	/// </summary>
	public static bool PrepareFbxTargetMesh(
		TargetPickers.ResolvedTarget target, string outputFolderRelative, out string error )
	{
		error = null;
		if ( target?.FbxAbsolutePath is null )
			return true;

		var assetsPath = Project.Current?.GetAssetsPath();
		if ( assetsPath is null )
		{
			error = "No project is open.";
			return false;
		}

		try
		{
			// Sanitized destination name: spaces and exotic characters in source-file names
			// travel into the vmdl's RenderMeshFile node and the compiled asset path -
			// underscores keep both on the safe side of ModelDoc's node naming.
			var stem = Path.GetFileNameWithoutExtension( target.FbxAbsolutePath );
			var safeStem = new string( stem.Select( c => char.IsLetterOrDigit( c ) ? c : '_' ).ToArray() );
			var fileName = safeStem + Path.GetExtension( target.FbxAbsolutePath ).ToLowerInvariant();
			var relative = string.IsNullOrEmpty( outputFolderRelative )
				? fileName
				: outputFolderRelative.TrimEnd( '/' ) + "/" + fileName;
			var destination = Path.GetFullPath( Path.Combine(
				assetsPath, relative.Replace( '/', Path.DirectorySeparatorChar ) ) );

			if ( !string.Equals( Path.GetFullPath( target.FbxAbsolutePath ), destination,
				StringComparison.OrdinalIgnoreCase ) )
			{
				Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
				File.Copy( target.FbxAbsolutePath, destination, overwrite: true );
			}

			// Sidecar textures FIRST: FBX exports commonly ship a "textures" folder next to
			// the file (or next to its parent "source" folder) plus loose images - copy them
			// along so the compiled model has a chance to resolve its skins (user report:
			// "it has a source with the .fbx and a folder named textures"). Best-effort:
			// references the FBX makes to files that do not exist anywhere (e.g. .tga names
			// shipped as .png conversions) cannot be resolved by any importer.
			CopySidecarTextures( Path.GetDirectoryName( target.FbxAbsolutePath ),
				Path.GetDirectoryName( destination ) );

			// Auto-generate a vmat per FBX material that has none: the compiler otherwise
			// reports 'Missing vmat "<name>.vmat"' per mesh and the model renders with
			// placeholder materials. Textures are matched from the copied sidecars by
			// name-token overlap (_d/_diffuse -> color, _n/_normal -> normal map). Must
			// happen BEFORE the FBX is registered - registration can kick off the mesh
			// compile immediately, baking placeholder materials in. The returned remaps
			// go into every vmdl generated for this target: FBX materials are BARE names
			// the compiler cannot resolve as resource paths ("Trying to load an illegal
			// resource name X.vmat"), so the vmdl must remap them to the real files - the
			// same MaterialGroupList mechanism the shipped citizen vmdl uses.
			target.Spec.MaterialRemaps = GenerateMissingVmats( destination );

			// The compiler resolves the RenderMeshFile input through the asset system - a
			// freshly copied file it has never seen fails the whole vmdl compile with
			// "Node 'X' resolve failure" (observed in the custom-target gate). Register it
			// like WriteAndCompileAsync registers the DMX outputs.
			Try( () => AssetSystem.RegisterFile( destination ) );

			target.Spec.MeshFilePath = relative;
			target.Spec.MeshImportScale = target.FbxUnitScaleCm;
			return true;
		}
		catch ( Exception e )
		{
			error = $"Could not copy the target FBX into the project: {e.Message}";
			return false;
		}
	}

	// ====================================================== auto-vmat generation

	/// <summary>
	/// Writes a stub <c>.vmat</c> next to the copied target FBX for every FBX material
	/// that has none: the compiler otherwise logs 'Missing vmat "&lt;name&gt;.vmat"' per
	/// mesh and the model renders with placeholder materials. Texture assignment is
	/// best-effort by name-token overlap against the copied sidecar images
	/// (<c>mi_danteDark_lowerBody</c> → <c>t_danteDark_lowerBody_d.png</c> color +
	/// <c>…_n.png</c> normal). Existing vmats are never touched; failures never fail the
	/// conversion. Returns the material remap table for the generated vmdls (bare
	/// material reference → assets-relative vmat path), or null when there is nothing
	/// to remap.
	/// </summary>
	static IReadOnlyDictionary<string, string> GenerateMissingVmats( string fbxAbsolutePath )
	{
		try
		{
			var assetsPath = Project.Current?.GetAssetsPath();
			if ( assetsPath is null )
				return null;

			var materials = ExtractFbxMaterialNames( File.ReadAllBytes( fbxAbsolutePath ) );
			if ( materials.Count == 0 )
				return null;

			var directory = Path.GetDirectoryName( fbxAbsolutePath );
			var textures = new List<string>();
			foreach ( var pattern in new[] { "*.png", "*.jpg", "*.jpeg", "*.tga" } )
			{
				textures.AddRange( Directory.GetFiles( directory, pattern ) );
				var texturesDir = Path.Combine( directory, "textures" );
				if ( Directory.Exists( texturesDir ) )
					textures.AddRange( Directory.GetFiles( texturesDir, pattern, SearchOption.AllDirectories ) );
			}

			var remaps = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var material in materials )
			{
				var vmatPath = Path.Combine( directory, material + ".vmat" );
				// Remap the BARE reference the mesh carries to the real file, generated or
				// pre-existing (resource paths are lowercase by engine convention).
				remaps[material.ToLowerInvariant() + ".vmat"] =
					Path.GetRelativePath( assetsPath, vmatPath ).Replace( '\\', '/' ).ToLowerInvariant();
				if ( File.Exists( vmatPath ) )
					continue;

				var color = BestTextureMatch( material, textures,
					new[] { "_d", "_dm", "_diffuse", "_albedo", "_basecolor", "_c" } );
				var normal = BestTextureMatch( material, textures, new[] { "_n", "_normal" } );

				var builder = new System.Text.StringBuilder();
				builder.AppendLine( "// Auto-generated by humanoid-retargeter from the target FBX's material list." );
				builder.AppendLine( "// Best-effort texture assignment - adjust freely; this file is never overwritten." );
				builder.AppendLine( "Layer0" );
				builder.AppendLine( "{" );
				builder.AppendLine( "\tshader \"shaders/complex.shader\"" );
				builder.AppendLine( $"\tTextureColor \"{(color ?? "materials/default/default_color.tga")}\"" );
				builder.AppendLine( $"\tTextureNormal \"{(normal ?? "materials/default/default_normal.tga")}\"" );
				builder.AppendLine( "\tTextureRoughness \"materials/default/default_rough.tga\"" );
				builder.AppendLine( "}" );
				File.WriteAllText( vmatPath, builder.ToString() );
				Try( () => AssetSystem.RegisterFile( vmatPath ) );
				Log.Info( $"[humanoid-retargeter] generated material {Path.GetFileName( vmatPath )} "
					+ $"(color: {color ?? "default"}, normal: {normal ?? "default"})" );
			}

			return remaps.Count > 0 ? remaps : null;

			string BestTextureMatch( string material, List<string> candidates, string[] suffixes )
			{
				var materialTokens = NameTokens( material );
				string best = null;
				var bestScore = 0;
				foreach ( var candidate in candidates )
				{
					var stem = Path.GetFileNameWithoutExtension( candidate ).ToLowerInvariant();
					if ( !suffixes.Any( s => stem.EndsWith( s, StringComparison.Ordinal ) ) )
						continue;
					var score = NameTokens( stem ).Count( materialTokens.Contains );
					if ( score > bestScore )
					{
						bestScore = score;
						best = candidate;
					}
				}
				// One shared token is too weak (every texture of a character set shares its
				// base name - the vest would get the face texture); demand two.
				if ( best is null || bestScore < 2 )
					return null;
				return Path.GetRelativePath( assetsPath, best ).Replace( '\\', '/' );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] vmat generation failed: {e.Message}" );
			return null;
		}
	}

	/// <summary>Lower-case name tokens split on separators and camelCase boundaries,
	/// with generic prefixes (mi_/m_/t_/tex_) dropped.</summary>
	static HashSet<string> NameTokens( string name )
	{
		var tokens = new HashSet<string>( StringComparer.Ordinal );
		var current = new System.Text.StringBuilder();
		void Commit()
		{
			if ( current.Length > 0 )
			{
				var token = current.ToString().ToLowerInvariant();
				if ( token is not ("mi" or "m" or "t" or "tex") )
					tokens.Add( token );
				current.Clear();
			}
		}
		for ( var i = 0; i < name.Length; i++ )
		{
			var c = name[i];
			if ( !char.IsLetterOrDigit( c ) )
			{
				Commit();
				continue;
			}
			if ( char.IsUpper( c ) && current.Length > 0 && char.IsLower( name[i - 1] ) )
				Commit();
			current.Append( c );
		}
		Commit();
		return tokens;
	}

	/// <summary>
	/// Material object names from an FBX: binary FBX stores object names as
	/// <c>"&lt;name&gt;\0\x01Material"</c>, ASCII as <c>"Material::&lt;name&gt;"</c>.
	/// </summary>
	internal static List<string> ExtractFbxMaterialNames( byte[] data )
	{
		var names = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void Add( string name )
		{
			name = name.Trim();
			if ( name.Length is > 0 and <= 80 && seen.Add( name ) )
				names.Add( name );
		}

		// Binary: <name> 0x00 0x01 "Material"
		var marker = System.Text.Encoding.ASCII.GetBytes( "\0\u0001Material" );
		for ( var i = IndexOfBytes( data, marker, 0 ); i >= 0; i = IndexOfBytes( data, marker, i + 1 ) )
		{
			var start = i;
			while ( start > 0 && data[start - 1] >= 0x20 && data[start - 1] < 0x7f )
				start--;
			if ( i - start is > 0 and <= 80 )
				Add( System.Text.Encoding.ASCII.GetString( data, start, i - start ) );
		}

		// ASCII: Material::<name>
		var ascii = System.Text.Encoding.ASCII.GetBytes( "Material::" );
		for ( var i = IndexOfBytes( data, ascii, 0 ); i >= 0; i = IndexOfBytes( data, ascii, i + 1 ) )
		{
			var start = i + ascii.Length;
			var end = start;
			while ( end < data.Length && data[end] != (byte)'"' && data[end] >= 0x20 && data[end] < 0x7f )
				end++;
			if ( end - start is > 0 and <= 80 )
				Add( System.Text.Encoding.ASCII.GetString( data, start, end - start ) );
		}

		return names;
	}

	static int IndexOfBytes( byte[] haystack, byte[] needle, int from )
	{
		for ( var i = Math.Max( from, 0 ); i <= haystack.Length - needle.Length; i++ )
		{
			var match = true;
			for ( var j = 0; j < needle.Length; j++ )
			{
				if ( haystack[i + j] != needle[j] ) { match = false; break; }
			}
			if ( match )
				return i;
		}
		return -1;
	}

	static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".dds", ".vmat", ".vtex" };

	/// <summary>Copies texture sidecars of a picked target FBX into the output folder:
	/// loose image files next to the FBX, and a "textures" folder next to it or next to its
	/// parent (the source/-plus-textures/ layout). Per-file best effort - a failed texture
	/// must never fail the conversion.</summary>
	static void CopySidecarTextures( string sourceDir, string destDir )
	{
		try
		{
			if ( sourceDir is null || destDir is null )
				return;
			sourceDir = Path.GetFullPath( sourceDir );
			destDir = Path.GetFullPath( destDir );
			if ( string.Equals( sourceDir, destDir, StringComparison.OrdinalIgnoreCase ) )
				return;

			foreach ( var file in Directory.GetFiles( sourceDir ) )
			{
				if ( TextureExtensions.Contains( Path.GetExtension( file ).ToLowerInvariant() ) )
					Try( () => { File.Copy( file, Path.Combine( destDir, Path.GetFileName( file ) ), true ); return true; } );
			}

			foreach ( var candidate in new[]
			{
				Path.Combine( sourceDir, "textures" ),
				Path.Combine( Path.GetDirectoryName( sourceDir ) ?? sourceDir, "textures" ),
			} )
			{
				if ( !Directory.Exists( candidate ) )
					continue;
				var destTextures = Path.Combine( destDir, "textures" );
				Directory.CreateDirectory( destTextures );
				foreach ( var file in Directory.GetFiles( candidate, "*", SearchOption.AllDirectories ) )
				{
					var relative = Path.GetRelativePath( candidate, file );
					var destFile = Path.Combine( destTextures, relative );
					Try( () =>
					{
						Directory.CreateDirectory( Path.GetDirectoryName( destFile ) );
						File.Copy( file, destFile, true );
						return true;
					} );
				}
				break; // first existing candidate wins
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] sidecar texture copy failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Gives a custom-FBX target a REAL skinned preview (user request: "I want to see the
	/// preview of the FBX model"): copies the FBX into the output folder
	/// (<see cref="PrepareFbxTargetMesh"/>), writes a mesh-only vmdl next to it
	/// (<c>&lt;stem&gt;_preview.vmdl</c> — RenderMeshFile + ScaleAndMirror, no animations),
	/// compiles it, and on success points the target's
	/// <see cref="TargetPickers.ResolvedTarget.PreviewModelPath"/> at it so the preview
	/// dialog shows the actual skinned model instead of the wireframe skeleton. False when
	/// anything failed (the caller keeps the skeleton-view fallback); no-op true for
	/// non-FBX targets.
	/// </summary>
	public static async Task<bool> CompileFbxTargetPreviewAsync(
		TargetPickers.ResolvedTarget target, string outputFolderRelative )
	{
		if ( target?.FbxAbsolutePath is null )
			return true;

		if ( !PrepareFbxTargetMesh( target, outputFolderRelative, out var error ) )
		{
			Log.Warning( $"[humanoid-retargeter] target FBX preview: {error}" );
			return false;
		}

		var assetsPath = Project.Current?.GetAssetsPath();
		if ( assetsPath is null )
			return false;

		try
		{
			var meshRelative = target.Spec.MeshFilePath;
			var vmdlRelative = meshRelative.Substring( 0, meshRelative.LastIndexOf( '.' ) )
				+ "_preview.vmdl";
			var vmdlAbsolute = Path.GetFullPath( Path.Combine(
				assetsPath, vmdlRelative.Replace( '/', Path.DirectorySeparatorChar ) ) );

			var text = HumanoidRetargeter.Target.VmdlWriter.GenerateStandalone(
				"", System.Array.Empty<HumanoidRetargeter.Target.AnimEntry>(),
				target.Spec.VmdlScale, target.Spec.DefaultRootBone,
				meshFilePath: meshRelative, meshImportScale: target.Spec.MeshImportScale,
				materialRemaps: target.Spec.MaterialRemaps );
			File.WriteAllText( vmdlAbsolute, text );
			var sourceWriteUtc = File.GetLastWriteTimeUtc( vmdlAbsolute );

			await SwitchToMainThread();
			var asset = AssetSystem.RegisterFile( vmdlAbsolute );
			if ( asset is null )
				return false;

			// The asset system often auto-compiles a freshly registered vmdl BEFORE we ask:
			// CompileAndWaitAsync's stale-file protection (result must be newer than its
			// own start) then treats the legitimate output as pre-existing and waits out
			// the whole timeout (observed with the user's 27 MB character - the compile had
			// finished in 2 s). A compiled file newer than OUR source write IS this run's.
			bool CompiledFresh()
			{
				try
				{
					var compiledPath = vmdlAbsolute + "_c";
					return File.Exists( compiledPath )
						&& File.GetLastWriteTimeUtc( compiledPath ) >= sourceWriteUtc;
				}
				catch
				{
					return false;
				}
			}

			await Task.Delay( InputSettleDelayMs );
			if ( !CompiledFresh() )
			{
				// Short triggering poll only: the result usually comes from the asset
				// system's own register-time compile, which CompileAndWaitAsync's
				// stale-file guard can never credit (it demands a file newer than its own
				// start; observed as a full-timeout wait while the compiled file already
				// existed). Watch for a FRESH file ourselves for the remainder.
				var triggered = await CompileAndWaitAsync( asset, timeoutSeconds: 20 );
				var stopwatch = Stopwatch.StartNew();
				while ( !triggered && !CompiledFresh()
					&& stopwatch.Elapsed.TotalSeconds < MeshCompileTimeoutSeconds )
				{
					await Task.Delay( 1000 );
				}
				if ( !triggered && !CompiledFresh() )
				{
					Log.Warning( $"[humanoid-retargeter] target FBX preview vmdl did not compile: {asset.Path}" );
					return false;
				}
			}

			target.PreviewModelPath = asset.Path;
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] target FBX preview failed: {e.Message}" );
			return false;
		}
	}

	/// <summary>Compile-poll timeout for vmdls that embed a mesh source (the compiler
	/// imports the whole FBX - skin and skeleton - on top of the animation work).</summary>
	public const float MeshCompileTimeoutSeconds = 300f;

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

	/// <summary>Settle delay used when the open project physically lives inside the s&amp;box
	/// installation (samples/...): the engine's own content watcher also watches those paths
	/// (the current-project exemption in <see cref="IsUnderEngineInstall"/> deliberately lets
	/// writes through there), so give the watcher extra room before compiling. Defense in
	/// depth - see the exemption remarks on <see cref="IsUnderEngineInstall"/>.</summary>
	const int DefensiveInputSettleDelayMs = 4000;

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
		string standaloneVmdlName = "retargeted_animations", float compileTimeoutSeconds = 120f )
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

		// POLICY (replaces a former hard reject of install-resident assetsPath, which the
		// current-project exemption in IsUnderEngineInstall had made unreachable dead code):
		// the open project's paths are always allowed - users genuinely work in the shipped
		// sample projects under <sbox>/samples/ - but those paths are also watched by the
		// engine's own content watcher, the original native-crash class. The crash was fixed
		// by strict main-thread write→register→settle→compile sequencing; as defense in
		// depth, install-resident projects get a LONGER settle and one EXTRA verified
		// (timestamp-checked) compile retry instead of being blocked.
		var installResident = IsUnderEngineInstallIgnoringProject( assetsPath )
			|| (augmentVmdlPath is not null && IsUnderEngineInstallIgnoringProject( augmentVmdlPath ));
		var settleDelayMs = installResident ? DefensiveInputSettleDelayMs : InputSettleDelayMs;

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

		// ---- 3. settle, then compile ONCE (retry on abandoned recompile: one normally,
		// two for install-resident projects - see the policy comment above) ---------------
		await Task.Delay( settleDelayMs );

		var vmdlFileName = Path.GetFileName( result.VmdlPath );
		var maxAbandonRetries = installResident ? 2 : 1;
		var logOffset = SboxLogLength();
		result.Compiled = await CompileAndWaitAsync( result.VmdlAsset,
			timeoutSeconds: compileTimeoutSeconds, logOffset: logOffset,
			watchFileName: vmdlFileName );

		for ( var retry = 0; retry < maxAbandonRetries && !result.Compiled
			&& LogSliceShowsAbandonedRecompile( logOffset, vmdlFileName ); retry++ )
		{
			Log.Warning( $"[humanoid-retargeter] engine abandoned the recompile of {vmdlFileName} "
				+ $"(inputs not quiet) - retrying ({retry + 1}/{maxAbandonRetries}) after a settle delay" );
			await Task.Delay( settleDelayMs );
			logOffset = SboxLogLength();
			result.Compiled = await CompileAndWaitAsync( result.VmdlAsset,
				timeoutSeconds: compileTimeoutSeconds, logOffset: logOffset,
				watchFileName: vmdlFileName );
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

	/// <summary>Kicks a full compile and polls until THIS compile demonstrably produced a
	/// compiled file, or the asset reports failure. The most honest signal is the .vmdl_c
	/// on disk - but its mere EXISTENCE proves nothing: augment targets always come with a
	/// pre-existing .vmdl_c, which would satisfy a naive existence poll and report a stale
	/// (possibly abandoned) compile as success. The pre-existing file's LastWriteTimeUtc is
	/// therefore snapshotted BEFORE the compile is triggered, and success requires the
	/// compiled file to be NEWER than that snapshot (or to newly exist). Safe to call from
	/// any thread: the compile trigger and every <see cref="Asset"/> state query are
	/// marshalled to the editor main thread (<see cref="SwitchToMainThread"/>) because
	/// <c>Task.Delay</c> continuations may resume on a pool thread, and engine objects must
	/// never be touched there.</summary>
	/// <param name="asset">The vmdl asset to compile.</param>
	/// <param name="timeoutSeconds">Poll timeout.</param>
	/// <param name="logOffset">Optional <see cref="SboxLogLength"/> snapshot taken before
	/// the compile run: with <paramref name="watchFileName"/>, the poll exits early when the
	/// engine logs an abandoned recompile for that file (so the caller's retry path runs
	/// without waiting out the full timeout).</param>
	/// <param name="watchFileName">File name to watch for in abandoned-recompile log lines.</param>
	public static async Task<bool> CompileAndWaitAsync(
		Asset asset, float timeoutSeconds = 120, long logOffset = -1, string watchFileName = null )
	{
		await SwitchToMainThread();

		// Resolves the compiled-file path: GetCompiledFile when the asset system can answer
		// (it may resolve to null until a compile has run), else the engine's
		// <source>_c sibling convention (file.vmdl -> file.vmdl_c) so a PRE-EXISTING
		// compiled file is still seen and snapshotted before the compile is triggered.
		string ResolveCompiledPath()
		{
			var path = Try( () => asset.GetCompiledFile( true ) );
			if ( path is not null )
				return path;
			var source = Try( () => asset.AbsolutePath );
			return string.IsNullOrEmpty( source ) ? null : source + "_c";
		}

		// Snapshot the pre-existing compiled file BEFORE triggering the compile.
		var compiledAbs = ResolveCompiledPath();
		DateTime? preexistingWriteUtc = null;
		try
		{
			if ( compiledAbs is not null && File.Exists( compiledAbs ) )
				preexistingWriteUtc = File.GetLastWriteTimeUtc( compiledAbs );
		}
		catch
		{
			// unreadable timestamp: treat as no pre-existing file (existence will satisfy)
		}

		try
		{
			asset.Compile( full: true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] asset.Compile threw: {e.Message}" );
		}

		bool CompiledFileFresh()
		{
			try
			{
				if ( compiledAbs is null || !File.Exists( compiledAbs ) )
					return false;
				return preexistingWriteUtc is not { } stamp
					|| File.GetLastWriteTimeUtc( compiledAbs ) > stamp;
			}
			catch
			{
				return false;
			}
		}

		var sw = Stopwatch.StartNew();
		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			await SwitchToMainThread();
			if ( Try( () => asset.IsCompileFailed ) )
				return false;
			// GetCompiledFile may only become authoritative once the compile has run -
			// prefer it over the convention fallback as soon as it resolves. The freshness
			// snapshot stays valid either way (both spellings name the same sibling file).
			compiledAbs = Try( () => asset.GetCompiledFile( true ) ) ?? compiledAbs;
			if ( CompiledFileFresh() )
				return true;
			// The asset's own compiled flags are trusted only when no stale .vmdl_c could
			// be feeding them (no pre-existing file and no resolvable compiled path).
			if ( compiledAbs is null && preexistingWriteUtc is null
				&& Try( () => asset.IsCompiled && asset.HasCompiledFile ) )
				return true;
			// Abandoned recompile ("quiet inputs"): the file will never freshen this run -
			// bail out so the caller's settle-and-retry path is actually reachable.
			if ( logOffset >= 0 && watchFileName is not null
				&& LogSliceShowsAbandonedRecompile( logOffset, watchFileName ) )
				return false;

			await Task.Delay( 250 );
		}

		return CompiledFileFresh();
	}

	static T Try<T>( Func<T> getter )
	{
		try { return getter(); }
		catch { return default; }
	}
}
