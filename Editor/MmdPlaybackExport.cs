using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Resources;

namespace HumanoidRetargeter.EditorTools;

internal static class MmdPlaybackExport
{
	internal static async Task WriteAsync( TargetPickers.ResolvedTarget target, RetargetBatchResult batch,
		string folder, EditorPipeline.WriteResult result )
	{
		if ( target.ModelFilePath is null || Path.GetExtension( target.ModelFilePath ).ToLowerInvariant() is not (".pmx" or ".pmd") )
			return;
		var bytes = File.ReadAllBytes( target.ModelFilePath );
		var assets = Project.Current.GetAssetsPath();
		foreach ( var clip in batch.Clips.Where( clip => clip.Success ) )
		{
			try
			{
				var payload = await Task.Run( () => MmdPlaybackData.Create( bytes, target.Spec, clip ).ToJson() );
				await EditorPipeline.SwitchToMainThread();
				var materials = new List<Material>();
				var outlines = new List<Material>();
				for ( var i = 0; target.Spec.MaterialRemaps.TryGetValue( $"mmd_material_{i}", out var path ); i++ )
				{
					materials.Add( Material.Load( PlaybackMaterial( assets, path ) ) );
					outlines.Add( target.Spec.MaterialRemaps.TryGetValue( $"mmd_material_{i}_outline", out var outline )
						? Material.Load( PlaybackMaterial( assets, outline ) ) : null );
				}
				var destination = Path.Combine( assets, folder, Path.GetFileNameWithoutExtension( clip.DmxFileName ) + ".mmdanim" );
				var asset = AssetSystem.FindByPath( destination ) ?? AssetSystem.CreateResource( "mmdanim", destination );
				if ( asset is null )
					throw new InvalidOperationException( "Could not create the MMD animation resource." );
				asset.SaveToDisk( new MmdAnimation { Payload = payload, Materials = materials.ToArray(), Outlines = outlines.ToArray() } );
				result.MmdAnimationFiles.Add( Path.GetRelativePath( assets, destination ).Replace( '\\', '/' ) );
				Log.Info( $"MMD playback exported: {Path.GetRelativePath( assets, destination )}. Add MMD Playback to a GameObject and select this animation." );
			}
			catch ( Exception error )
			{
				result.Errors.Add( $"MMD playback export for '{clip.ClipName}' failed: {error.Message}" );
			}
		}
	}

	private static string PlaybackMaterial( string assets, string path )
	{
		var absolute = Path.Combine( assets, path );
		var output = Path.ChangeExtension( absolute, null ) + "_playback.vmat";
		var text = File.ReadAllText( absolute ).Replace( "F_ALPHA_TEST 1", "F_ALPHA_TEST 0" );
		if ( !text.Contains( "F_TRANSLUCENT 1", StringComparison.Ordinal ) )
			text = text.Replace( "Layer0\n{", "Layer0\n{\n\tF_TRANSLUCENT 1" );
		File.WriteAllText( output, text );
		AssetSystem.RegisterFile( output );
		return Path.GetRelativePath( assets, output ).Replace( '\\', '/' );
	}
}
