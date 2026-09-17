#nullable enable
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.IO;

namespace HumanoidRetargeter.Editor;

/// <summary>Engine-independent bridge to the embedded, extraction-only VRF source.</summary>
internal static class CompiledAssetRecovery
{
	internal static bool IsDependency( string path ) => Path.GetExtension( path ).ToLowerInvariant()
		is ".vmdl" or ".vmesh" or ".vmat" or ".vphys" or ".vagrp" or ".vanim" or ".vanmgrph";

	internal static string Recover( string input, string assetPath, string root, string assetFolder,
		IReadOnlyDictionary<string, string> paths, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();
		using var resource = new Resource { FileName = assetPath };
		resource.Read( input );
		if ( assetPath.EndsWith( ".vanmgrph", StringComparison.OrdinalIgnoreCase ) )
			return resource.DataBlock?.ToString() ?? throw new InvalidDataException( "Missing animgraph data." );
		if ( resource.DataBlock is not HumanoidRetargeterVrf.ResourceTypes.Model )
			throw new InvalidDataException( "Smart Port recovery requires a model or animgraph." );
		using var loader = new Loader( paths, token );
		using var content = new ModelExtract( resource, loader ).ToContentFile();
		var text = Encoding.UTF8.GetString( content.Data );
		var rebase = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var modelDirectory = Path.GetDirectoryName( content.FileName )?.Replace( '\\', '/' ) ?? "";
		foreach ( var file in content.SubFiles )
		{
			token.ThrowIfCancellationRequested();
			var relative = (modelDirectory + "/" + file.FileName).TrimStart( '/' );
			var output = ConfinedPath( root, relative );
			if ( !rebase.TryAdd( relative, assetFolder + "/" + relative ) )
				throw new InvalidDataException( "Recovery produced duplicate files: " + relative );
			Directory.CreateDirectory( Path.GetDirectoryName( output )! );
			using var stream = new FileStream( output, FileMode.CreateNew, FileAccess.Write );
			stream.Write( file.Extract() );
		}
		return SmartPortSetup.Rebase( text, rebase );
	}

	internal static string ConfinedPath( string root, string relative )
	{
		foreach ( var part in relative.Split( '/', '\\' ) )
			if ( part.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 )
				throw new InvalidDataException( "Invalid recovered filename." );
		var fullRoot = Path.GetFullPath( root ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) + Path.DirectorySeparatorChar;
		var full = Path.GetFullPath( Path.Combine( fullRoot, relative ) );
		if ( Path.IsPathRooted( relative ) || !full.StartsWith( fullRoot, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "Recovered file escapes its output folder." );
		return full;
	}

	sealed class Loader( IReadOnlyDictionary<string, string> paths, CancellationToken token ) : IFileLoader, IDisposable
	{
		readonly List<Resource> opened = new();
		public Resource LoadFileCompiled( string file ) => LoadFile( file );
		public Resource LoadFile( string file )
		{
			token.ThrowIfCancellationRequested();
			file = file.Replace( '\\', '/' );
			if ( file.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) file = file[..^2];
			if ( !paths.TryGetValue( file, out var physical ) || string.IsNullOrEmpty( physical ) || !File.Exists( physical ) )
				throw new FileNotFoundException( "Install the missing Smart Port dependency: " + file );
			var resource = new Resource { FileName = file };
			opened.Add( resource );
			resource.Read( physical );
			return resource;
		}
		public void Dispose() { foreach ( var resource in opened ) resource.Dispose(); }
	}
}
