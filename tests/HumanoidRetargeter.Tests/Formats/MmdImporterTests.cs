using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Gltf;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdImporterTests
{
	[Theory]
	[InlineData( 1, false, 0 )]
	[InlineData( 2, true, 1 )]
	[InlineData( 4, false, 2 )]
	[InlineData( 1, true, 3 )]
	[InlineData( 2, false, 4 )]
	public void PmxImportsEncodingsIndexWidthsAndSkinTypes( int width, bool utf16, int skin )
	{
		var bytes = MmdFixture.Pmx( width, utf16, skin );
		var scene = Retargeter.ImportSource( bytes, "model.PMX" );
		Assert.Equal( MmdFixture.Bones.Length, scene.Skeleton.Count );
		Assert.Empty( scene.Clips );
		Assert.Equal( new Vector3( 0, 80, 0 ), scene.Skeleton.RestWorld[ scene.Skeleton.IndexOf( "下半身" ) ].Pos );
		Assert.Equal( 8f, scene.UnitScaleCm );
		var mapping = Retargeter.ResolveMapping( scene.Skeleton ).Map;
		Assert.Equal( "mmd", mapping.ProfileName );
		Assert.Equal( "下半身", scene.Skeleton[ mapping.RoleToBone[ BoneRole.Hips ] ].Name );
		Assert.Equal( "左腕", scene.Skeleton[ mapping.RoleToBone[ BoneRole.UpperArmL ] ].Name );
		Assert.DoesNotContain( scene.Skeleton.IndexOf( "左足ＩＫ" ), mapping.RoleToBone.Values );
	}

	[Fact]
	public void PmdDecodesJapaneseAndSeparatesSphereTexture()
	{
		var scene = Retargeter.ImportSource( MmdFixture.Pmd(), "model.pmd" );
		Assert.True( scene.Skeleton.IndexOf( "左ひざ" ) >= 0 );
		var seen = new List<string>();
		var gltf = MmdImporter.ConvertModelToGltf( MmdFixture.Pmd(), texture => { seen.Add( texture ); return "textures/test.png"; } );
		Assert.Equal( new[] { "face.bmp", "shine.sph" }, seen );
		Assert.NotEmpty( GltfModelDmxWriter.Write( gltf, scene.Skeleton, "pmd" ) );
	}

	[Theory]
	[InlineData( false )]
	[InlineData( true )]
	public void VmdResamplesThirtyFpsTimelineAndKeepsRestOffsets( bool old )
	{
		var scene = Retargeter.ImportSource( MmdFixture.Vmd( old: old ), "motion.vmd", 60, MmdFixture.Pmx() );
		var clip = Assert.Single( scene.Clips );
		Assert.Equal( 61, clip.FrameCount );
		Assert.Equal( 30, clip.NativeFps );
		var root = scene.Skeleton.IndexOf( "センター" );
		Assert.Equal( new Vector3( 0, 80, 0 ), clip.Frames[ 0 ][ root ].Pos );
		Assert.True( Vector3.Distance( new Vector3( 8, 84, 4 ), clip.Frames[ 30 ][ root ].Pos ) < 0.001f );
		Assert.Equal( new Vector3( 16, 88, 8 ), clip.Frames[ ^1 ][ root ].Pos );
	}

	[Fact]
	public void VmdUsesDestinationBezierPerChannel()
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd( curved: true ), MmdFixture.Pmd() );
		var root = scene.Skeleton.IndexOf( "センター" );
		// x(t) is symmetric, y(t)=t^3: halfway in time is 12.5% of the X translation.
		Assert.Equal( 2f, scene.Clips[ 0 ].Frames[ 15 ][ root ].Pos.X, 3 );
		Assert.Equal( 84f, scene.Clips[ 0 ].Frames[ 15 ][ root ].Pos.Y, 3 );
	}

	[Fact]
	public void VpdUsesJapaneseBoneNamesAndReflectsRotations()
	{
		var scene = MmdImporter.Import( MmdFixture.Vpd(), MmdFixture.Pmd() );
		var clip = Assert.Single( scene.Clips );
		Assert.Single( clip.Frames );
		var hand = scene.Skeleton.IndexOf( "左手首" );
		var offset = clip.Frames[ 0 ][ hand ].Pos - scene.Skeleton[ hand ].RestLocal.Pos;
		Assert.Equal( new Vector3( 8, 16, -24 ), offset );
		Assert.True( Vector3.Distance( Vector3.UnitY, Vector3.Transform( Vector3.UnitX, clip.Frames[ 0 ][ hand ].Rot ) ) < 0.0001f );
	}

	[Fact]
	public void MeshBridgePreservesSkinUvsTexturesMaterialsAndBind()
	{
		var model = MmdFixture.Pmx( weightType: 1 );
		var gltf = MmdImporter.ConvertModelToGltf( model, path =>
		{
			Assert.Equal( "tex\\顔.bmp", path );
			return "textures/face.png";
		} );
		using var document = JsonDocument.Parse( gltf );
		var root = document.RootElement;
		var material = root.GetProperty( "materials" )[ 0 ];
		Assert.Equal( "BLEND", material.GetProperty( "alphaMode" ).GetString() );
		Assert.True( material.GetProperty( "doubleSided" ).GetBoolean() );
		Assert.Equal( 0.5f, material.GetProperty( "pbrMetallicRoughness" ).GetProperty( "baseColorFactor" )[ 0 ].GetSingle() );
		Assert.Equal( "textures/face.png", root.GetProperty( "images" )[ 0 ].GetProperty( "uri" ).GetString() );
		var skeleton = MmdImporter.Import( model ).Skeleton;
		var dmx = GltfModelDmxWriter.Write( gltf, skeleton, "mmd_test" );
		Assert.Contains( "blendweights$0", dmx );
		Assert.Contains( "mmd_material_0", dmx );
		var position = Regex.Match( dmx, "\"position\\$0\" \"vector3_array\"\\s*\\[\\s*\"([^\"]+)\"" );
		Assert.True( position.Success );
		Assert.Equal( "0 80 8", position.Groups[ 1 ].Value );
	}

	[Fact]
	public void IkMovesAnkleToControllerAndHonorsDisableKeys()
	{
		var model = MmdFixture.Pmx( ik: true );
		var enabled = MmdImporter.Import( MmdFixture.Vmd( "左足ＩＫ" ), model );
		var disabled = MmdImporter.Import( MmdFixture.Vmd( "左足ＩＫ", disableIk: true ), model );
		var index = enabled.Skeleton.IndexOf( "左足首" );
		var controller = enabled.Skeleton.IndexOf( "左足ＩＫ" );
		var world = new Pose( enabled.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( enabled.Skeleton );
		Assert.True( Vector3.Distance( world[ index ].Pos, world[ controller ].Pos ) < 0.2f );
		var disabledWorld = new Pose( disabled.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( disabled.Skeleton );
		Assert.Equal( disabled.Skeleton.RestWorld[ index ].Pos, disabledWorld[ index ].Pos );
	}

	[Fact]
	public void MissingAndMismatchedCompanionsFailClearly()
	{
		Assert.Contains( "PMX", Assert.Throws<FormatException>( () => Retargeter.ImportSource( MmdFixture.Vmd(), "motion.vmd" ) ).Message );
		Assert.Contains( "matching", Assert.Throws<FormatException>( () => MmdImporter.Import( MmdFixture.Vmd( "unknown" ), MmdFixture.Pmd() ) ).Message );
	}

	[Fact]
	public void SniffingAcceptsAllFourFormats()
	{
		Assert.Empty( Retargeter.ImportSource( MmdFixture.Pmx(), "unknown" ).Clips );
		Assert.Empty( Retargeter.ImportSource( MmdFixture.Pmd(), "unknown" ).Clips );
		Assert.Single( Retargeter.ImportSource( MmdFixture.Vmd(), "unknown", skeletonData: MmdFixture.Pmd() ).Clips );
		Assert.Single( Retargeter.ImportSource( MmdFixture.Vpd(), "unknown", skeletonData: MmdFixture.Pmd() ).Clips );
	}

	[Theory]
	[InlineData( 0 )]
	[InlineData( -1 )]
	[InlineData( float.NaN )]
	[InlineData( 10000 )]
	public void InvalidSampleRatesAreRejected( float fps )
		=> Assert.Throws<ArgumentOutOfRangeException>( () => MmdImporter.Import( MmdFixture.Pmx(), sampleFps: fps ) );

	[Fact]
	public void TruncatedModelAndMotionFailWithoutUnboundedAllocation()
	{
		Assert.Throws<FormatException>( () => MmdImporter.Import( MmdFixture.Pmx()[ ..40 ] ) );
		Assert.Throws<FormatException>( () => MmdImporter.Import( MmdFixture.Pmd()[ ..310 ] ) );
		Assert.Throws<FormatException>( () => MmdImporter.Import( MmdFixture.Vmd()[ ..70 ], MmdFixture.Pmd() ) );
		var corrupt = MmdFixture.Vmd();
		BitConverter.GetBytes( int.MaxValue ).CopyTo( corrupt, 50 );
		Assert.Throws<FormatException>( () => MmdImporter.Import( corrupt, MmdFixture.Pmd() ) );
	}
}
