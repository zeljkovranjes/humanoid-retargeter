#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>What can be dropped on the retargeter: animation files from disk or the asset browser.</summary>
internal static class RtDrop
{
	static readonly string[] Extensions = { ".fbx", ".bvh", ".glb", ".gltf", ".vrm", ".anm", ".an5", ".cba" };

	public static bool IsAnimationFile( string path )
		=> !string.IsNullOrEmpty( path ) && Extensions.Contains( System.IO.Path.GetExtension( path ).ToLowerInvariant() );

	public static IReadOnlyList<string> Paths( DragData data )
	{
		var paths = new List<string>();
		if ( data is null )
			return paths;
		try
		{
			if ( data.Files is { Length: > 0 } files )
				paths.AddRange( files.Where( IsAnimationFile ) );
			else if ( data.HasFileOrFolder && IsAnimationFile( data.FileOrFolder ) )
				paths.Add( data.FileOrFolder );
			if ( data.Assets is { Count: > 0 } assets )
			{
				foreach ( var asset in assets )
				{
					var path = asset?.AssetPath;
					if ( IsAnimationFile( path ) && AssetSystem.FindByPath( path )?.AbsolutePath is { } absolute )
						paths.Add( absolute );
				}
			}
		}
		catch ( Exception )
		{
			// A drag with nothing we understand.
		}
		return paths.Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
	}
}
