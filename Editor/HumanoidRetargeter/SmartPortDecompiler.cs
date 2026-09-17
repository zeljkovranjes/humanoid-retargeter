#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Target;

namespace HumanoidRetargeter.Editor;

/// <summary>Recovers compiled assets with the source-integrated parser; no executable or download.</summary>
internal static class SmartPortDecompiler
{
	internal static async Task<string> RecoverAsync( Asset asset, string folder, CancellationToken token, SmartPortRig rig = null )
	{
		await EditorPipeline.SwitchToMainThread();
		var input = asset.GetCompiledFile( true );
		if ( string.IsNullOrEmpty( input ) || !File.Exists( input ) )
			throw new InvalidOperationException( "Compiled asset is unavailable: " + asset.Path );
		var assetPath = asset.Path;
		var root = CitizenAnimationModels.ProjectFile( folder );
		// Resolve engine assets on the editor thread. Parsing and DMX writing run off-thread.
		var paths = AssetSystem.All.Where( a => CompiledAssetRecovery.IsDependency( a.Path ) )
			.GroupBy( a => a.Path, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( g => g.Key, g => g.First().GetCompiledFile( true ), StringComparer.OrdinalIgnoreCase );
		return await Task.Run( () => CompiledAssetRecovery.Recover( input, assetPath, root, folder, paths, token, rig ), token );
	}
}
