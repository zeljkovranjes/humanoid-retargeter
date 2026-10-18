using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Formats.Gltf;
using HumanoidRetargeter.Core.Formats.Dmx;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdMorphTests
{
	[Fact]
	public void MorphOnlyMotionSamplesWeightsAndExportsFlexChannels()
	{
		var scene = MmdImporter.Import( MmdFixture.MorphMotion(), MmdFixture.MorphModel() );
		var clip = scene.Clips[ 0 ];
		Assert.Equal( 31, clip.Frames.Count );
		Assert.Equal( 0.5f, clip.MorphWeights[ "笑い" ][ 15 ], 5 );
		var dmx = DmxWriter.Write( scene.Skeleton, clip, new DmxWriteOptions() );
		Assert.Contains( "flexWeight", dmx );
		Assert.Contains( DmxWriter.MorphName( "笑い" ), dmx );
	}

	[Fact]
	public void VertexMorphsSurviveGltfAndModelDmx()
	{
		var model = MmdFixture.MorphModel();
		var scene = MmdImporter.Import( model );
		var gltf = MmdImporter.ConvertModelToGltf( model );
		var dmx = GltfModelDmxWriter.Write( gltf, scene.Skeleton, "morph_test" );
		Assert.Contains( "DmeVertexDeltaData", dmx );
		Assert.Contains( "combinationOperator", dmx );
		Assert.Contains( DmxWriter.MorphName( "笑い" ), dmx );
		Assert.Contains( "8 16 -24", dmx );
	}
}
