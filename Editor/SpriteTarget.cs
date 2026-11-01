using System;
using System.IO;
using System.Linq;
using Editor;
using HumanoidRetargeter.Core.Sprites;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Turns a sprite file into a model the custom-target path can take: reads the
/// image, and writes the rigged GLB where it stays with the project but out of its assets.</summary>
internal static class SpriteTarget
{
	const string SourceFolder = ".sbox/humanoid_retargeter_sprites";
	const int MinimumLongSide = 512;

	/// <summary>A decoded sprite: the image file and its alpha, one byte per pixel.</summary>
	internal sealed record Image( string Name, byte[] Png, byte[] Alpha, int Width, int Height );

	/// <summary>Reads a PNG, or the first frame of a Sprite resource.</summary>
	/// <exception cref="FormatException">The file has no readable image.</exception>
	/// <exception cref="InvalidOperationException">A Sprite resource is outside the project's assets.</exception>
	internal static Image Load( string path )
	{
		var name = Path.GetFileNameWithoutExtension( path );
		var png = Path.GetExtension( path ).Equals( ".sprite", StringComparison.OrdinalIgnoreCase )
			? FirstFramePng( path )
			: File.ReadAllBytes( path );

		using var decoded = SkiaSharp.SKBitmap.Decode( png )
			?? throw new FormatException( "The file is not a readable image." );
		// Checked here: the padding below would give any image a transparent background.
		if ( decoded.Pixels.All( pixel => pixel.Alpha >= SpriteRig.OpaqueAlpha ) )
			throw new FormatException( "The sprite has no transparent background: its silhouette cannot be read. Export it with transparency." );
		using var bitmap = PaddedToPowerOfTwo( decoded );
		var alpha = new byte[bitmap.Width * bitmap.Height];
		var pixels = bitmap.Pixels;
		for ( var i = 0; i < alpha.Length; i++ )
			alpha[i] = pixels[i].Alpha;
		return new Image( name, Encode( bitmap ), alpha, bitmap.Width, bitmap.Height );
	}

	/// <summary>Whole-pixel upscaling keeps small art crisp once the texture is filtered, and
	/// the texture compiler refuses to filter an alpha-tested texture that is not a power of
	/// two, so the sprite goes on a transparent canvas that is one.</summary>
	static SkiaSharp.SKBitmap PaddedToPowerOfTwo( SkiaSharp.SKBitmap source )
	{
		var factor = Math.Max( 1, MinimumLongSide / Math.Max( source.Width, source.Height ) );
		var width = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)(source.Width * factor) );
		var height = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)(source.Height * factor) );
		var padded = new SkiaSharp.SKBitmap( width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul );
		for ( var y = 0; y < source.Height * factor; y++ )
		{
			for ( var x = 0; x < source.Width * factor; x++ )
				padded.SetPixel( x, y, source.GetPixel( x / factor, y / factor ) );
		}
		return padded;
	}

	static byte[] Encode( SkiaSharp.SKBitmap bitmap )
	{
		using var image = SkiaSharp.SKImage.FromBitmap( bitmap );
		using var encoded = image.Encode( SkiaSharp.SKEncodedImageFormat.Png, 100 );
		return encoded.ToArray();
	}

	static byte[] FirstFramePng( string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( "The sprite must be an asset of this project." );
		if ( !asset.TryLoadResource<Sprite>( out var sprite ) )
			throw new FormatException( "The Sprite resource could not be loaded." );
		var texture = sprite.Animations.SelectMany( animation => animation.Frames )
			.Select( frame => frame.Texture ).FirstOrDefault( frame => frame.IsValid() && frame.Width > 0 )
			?? throw new FormatException( "The Sprite resource has no frames." );
		return texture.GetBitmap( 0 ).ToPng();
	}

	/// <summary>Writes the rigged model and returns its path.</summary>
	internal static string WriteModel( Image image, SpriteRig rig )
	{
		var root = Project.Current?.GetRootPath() ?? throw new InvalidOperationException( "No project is open." );
		var folder = Path.Combine( root, SourceFolder );
		Directory.CreateDirectory( folder );
		var stem = new string( image.Name.Select( c => char.IsLetterOrDigit( c ) ? char.ToLowerInvariant( c ) : '_' ).ToArray() );
		var path = Path.Combine( folder, stem + ".glb" );
		File.WriteAllBytes( path, SpriteGlb.Write( rig, image.Alpha, image.Png, stem ) );
		return path;
	}
}
