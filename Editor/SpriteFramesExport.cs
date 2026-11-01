using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Saves baked frames as a Sprite resource: one PNG a frame, beside a <c>.sprite</c>
/// that plays them in a SpriteRenderer, with no model involved.</summary>
internal static class SpriteFramesExport
{
	/// <summary>Whole-pixel upscaling, so the art stays crisp however the sprite is filtered.</summary>
	const int PixelScale = 8;
	const float CompileTimeoutSeconds = 60f;

	/// <summary>Writes the sprite and returns its assets-relative path.</summary>
	/// <exception cref="InvalidOperationException">No project is open, or the sprite did not load.</exception>
	internal static async Task<string> WriteAsync(
		string name, IReadOnlyList<byte[]> frames, int width, int height, float framesPerSecond, bool isLooping, string outputFolder )
	{
		var assets = Project.Current?.GetAssetsPath() ?? throw new InvalidOperationException( "No project is open." );
		var stem = new string( name.Select( c => char.IsLetterOrDigit( c ) ? char.ToLowerInvariant( c ) : '_' ).ToArray() );
		var folder = $"{outputFolder}/sprites/{stem}".TrimStart( '/' );
		var absolute = Path.Combine( assets, folder.Replace( '/', Path.DirectorySeparatorChar ) );
		Directory.CreateDirectory( absolute );

		var textures = new List<object>( frames.Count );
		for ( var i = 0; i < frames.Count; i++ )
		{
			var file = $"{stem}_{i:000}.png";
			File.WriteAllBytes( Path.Combine( absolute, file ), EncodeFrame( frames[i], width, height ) );
			textures.Add( new { Texture = $"{folder}/{file}" } );
		}

		await EditorPipeline.SwitchToMainThread();
		foreach ( var file in Directory.EnumerateFiles( absolute, "*.png" ) )
			AssetSystem.RegisterFile( file );

		var spritePath = Path.Combine( absolute, stem + ".sprite" );
		File.WriteAllText( spritePath, JsonSerializer.Serialize( new
		{
			Animations = new[]
			{
				new
				{
					Name = "Default",
					FrameRate = framesPerSecond,
					Origin = "0.5,0.5",
					LoopMode = isLooping ? "Loop" : "None",
					Frames = textures,
				},
			},
			__version = 1,
		} ) );

		var asset = AssetSystem.RegisterFile( spritePath )
			?? throw new InvalidOperationException( $"Could not register {spritePath}." );
		await EditorPipeline.CompileAndWaitAsync( asset, CompileTimeoutSeconds );
		if ( !asset.TryLoadResource<Sprite>( out var sprite ) || sprite.Animations.Sum( a => a.Frames.Count ) != frames.Count )
			throw new InvalidOperationException( $"The sprite {asset.Path} did not load with its {frames.Count} frames." );
		return asset.Path;
	}

	static byte[] EncodeFrame( byte[] rgba, int width, int height )
	{
		using var bitmap = new SkiaSharp.SKBitmap( width * PixelScale, height * PixelScale,
			SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul );
		for ( var y = 0; y < height * PixelScale; y++ )
		{
			for ( var x = 0; x < width * PixelScale; x++ )
			{
				var i = (y / PixelScale * width + x / PixelScale) * 4;
				bitmap.SetPixel( x, y, new SkiaSharp.SKColor( rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3] ) );
			}
		}
		using var image = SkiaSharp.SKImage.FromBitmap( bitmap );
		using var data = image.Encode( SkiaSharp.SKEncodedImageFormat.Png, 100 );
		return data.ToArray();
	}
}
