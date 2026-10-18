using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Editor;
using Sandbox;
using HumanoidRetargeter.Core.Formats.Mmd;

namespace HumanoidRetargeter.EditorTools;

internal static class MmdModelTarget
{
	internal static byte[] Convert( byte[] model, string sourcePath, string destinationFolder, List<string> report )
	{
		var sourceFolder = Path.GetFullPath( Path.GetDirectoryName( sourcePath ) );
		var root = sourceFolder.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
		var textureFolder = Path.Combine( destinationFolder, "textures" );
		Directory.CreateDirectory( textureFolder );
		var textureCount = 0;
		var gltf = MmdImporter.ConvertModelToGltf( model, reference =>
		{
			var relative = reference.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar );
			if ( Path.IsPathRooted( relative ) )
				throw new FormatException( $"MMD texture must be relative to the model: '{reference}'." );
			var source = Path.GetFullPath( Path.Combine( sourceFolder, relative ) );
			if ( !source.StartsWith( root, StringComparison.OrdinalIgnoreCase ) )
				throw new FormatException( $"MMD texture escapes the model folder: '{reference}'. Keep the model and its textures together." );
			if ( !File.Exists( source ) )
				throw new FileNotFoundException( $"MMD texture '{reference}' was not found beside the model. Extract the complete model archive, including texture folders.", source );
			var extension = Path.GetExtension( source ).ToLowerInvariant();
			var convert = extension is ".bmp" or ".jpeg" or ".webp" or ".sph" or ".spa";
			if ( !convert && extension is not (".png" or ".jpg" or ".tga" or ".dds") )
				throw new FormatException( $"Unsupported MMD diffuse texture '{reference}'. Convert it to PNG, JPG, BMP, TGA or DDS." );
			var name = $"mmd_texture_{textureCount++}{(convert ? ".png" : extension)}";
			var destination = Path.Combine( textureFolder, name );
			if ( convert )
			{
				using var bitmap = SkiaSharp.SKBitmap.Decode( source )
					?? throw new FormatException( $"Cannot decode MMD texture '{reference}'." );
				using var image = SkiaSharp.SKImage.FromBitmap( bitmap );
				using var encoded = image.Encode( SkiaSharp.SKEncodedImageFormat.Png, 100 );
				using var stream = File.Create( destination );
				encoded.SaveTo( stream );
			}
			else
			{
				File.Copy( source, destination, true );
			}
			return "textures/" + name;
		}, engineSafeBoneNames: true );
		report?.Add( $"Imported MMD mesh, morphs, toon/sphere materials, outlines and {textureCount} textures. Export also writes an MMD animation resource for native SDEF/QDEF, UV/material morph and soft-body playback." );
		return gltf;
	}

	internal static IReadOnlyDictionary<string, string> CreateMaterials( string modelPath )
	{
		using var document = JsonDocument.Parse( File.ReadAllBytes( modelPath ) );
		var json = document.RootElement;
		var folder = Path.GetDirectoryName( modelPath );
		var assets = Project.Current.GetAssetsPath();
		var remaps = new Dictionary<string, string>();
		foreach ( var material in json.GetProperty( "materials" ).EnumerateArray() )
		{
			var name = material.GetProperty( "name" ).GetString();
			var pbr = material.GetProperty( "pbrMetallicRoughness" );
			var settings = material.GetProperty( "extras" ).GetProperty( "mmd" );
			var output = Path.Combine( folder, name + ".vmat" );
			var builder = new StringBuilder( "// Auto-generated MMD material.\nLayer0\n{\n\tshader \"shaders/humanoid_retargeter/mmd.shader\"\n" );
			var alpha = material.GetProperty( "alphaMode" ).GetString();
			if ( alpha == "MASK" )
				builder.AppendLine( "\tF_ALPHA_TEST 1" );
			if ( alpha == "BLEND" )
				builder.AppendLine( "\tF_TRANSLUCENT 1" );
			if ( material.GetProperty( "doubleSided" ).GetBoolean() )
				builder.AppendLine( "\tF_RENDER_BACKFACES 1" );
			builder.AppendLine( "\tg_vDiffuse \"[" + string.Join( " ", pbr.GetProperty( "baseColorFactor" ).EnumerateArray().Select( value => value.GetRawText() ) ) + "]\"" );
			if ( pbr.TryGetProperty( "baseColorTexture", out var diffuse ) )
			{
				var texture = json.GetProperty( "textures" )[ diffuse.GetProperty( "index" ).GetInt32() ];
				AddTexture( "Diffuse", json.GetProperty( "images" )[ texture.GetProperty( "source" ).GetInt32() ].GetProperty( "uri" ).GetString() );
			}
			AddTexture( "Sphere", settings.GetProperty( "sphereTexture" ).GetString() );
			AddTexture( "Toon", settings.GetProperty( "toonTexture" ).GetString() );
			builder.AppendLine( $"\tg_flSphereMode {settings.GetProperty( "sphereMode" ).GetInt32()}" );
			builder.AppendLine( $"\tg_flHasToon {(string.IsNullOrEmpty( settings.GetProperty( "toonTexture" ).GetString() ) ? 0 : 1)}" );
			builder.AppendLine( $"\tg_flUnlit {(settings.TryGetProperty( "outline", out var outline ) && outline.GetBoolean() ? 1 : 0)}" );
			builder.AppendLine( "}" );
			File.WriteAllText( output, builder.ToString() );
			AssetSystem.RegisterFile( output );
			remaps[ name ] = Path.GetRelativePath( assets, output ).Replace( '\\', '/' );

			void AddTexture( string input, string relative )
			{
				if ( string.IsNullOrEmpty( relative ) )
					return;
				var texturePath = Path.Combine( folder, relative );
				AssetSystem.RegisterFile( texturePath );
				var assetPath = Path.GetRelativePath( assets, texturePath ).Replace( '\\', '/' );
				builder.AppendLine( $"\t{input} \"{assetPath}\"" );
			}
		}
		return remaps;
	}
}
