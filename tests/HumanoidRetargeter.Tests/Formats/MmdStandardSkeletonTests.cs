using System.Numerics;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdStandardSkeletonTests
{
	[Theory]
	[InlineData( false )]
	[InlineData( true )]
	public void StandaloneMotionsRetargetToHumanWithoutModel( bool pose )
	{
		var target = RetargetTargetSpec.SboxDefault( File.ReadAllText(
			TestUtil.RepoFile( "Assets", "data", "humanoid_retargeter", "target_rig_sbox.json" ) ) );
		var result = Retargeter.Convert( new RetargetRequest
		{
			SourceData = pose ? MmdFixture.Vpd() : MmdFixture.Vmd(),
			SourceFileName = pose ? "pose.vpd" : "motion.vmd",
		}, target );
		Assert.Empty( result.Errors );
		var clip = Assert.Single( result.Clips );
		Assert.True( clip.Success, clip.Error );
		Assert.Equal( pose ? 1 : 31, clip.SolvedFrames!.Count );
		Assert.All( clip.SolvedFrames.SelectMany( frame => frame ), transform =>
		{
			Assert.True( float.IsFinite( transform.Pos.LengthSquared() ) );
			Assert.InRange( transform.Rot.Length(), 0.999f, 1.001f );
		} );
		Assert.NotEmpty( clip.DmxContent );
	}

	[Fact]
	public void StandardRigHasPlausibleMappedRestAndKeepsMotionOffsets()
	{
		var scene = Retargeter.ImportSource( MmdFixture.Vmd(), "motion.vmd", useStandardMmdSkeleton: true );
		var map = Retargeter.ResolveMapping( scene.Skeleton ).Map;
		Assert.Equal( "mmd", map.ProfileName );
		Assert.True( map.Confidence >= 0.8f );
		Assert.True( RestNormalizer.IsAnatomicalRest( scene.Skeleton, map, scene.Skeleton.RestWorld ) );
		var center = scene.Skeleton.IndexOf( "センター" );
		Assert.Equal( new Vector3( 16, 8, 8 ), scene.Clips[ 0 ].Frames[ ^1 ][ center ].Pos - scene.Skeleton[ center ].RestLocal.Pos );
		Assert.Contains( scene.Notes, note => note.Contains( "built-in standard" ) );
	}

	[Fact]
	public void ExplicitModelTakesPrecedenceOverStandardRig()
	{
		var model = MmdFixture.Pmx();
		var exact = MmdImporter.Import( MmdFixture.Vmd(), model );
		var optional = MmdImporter.Import( MmdFixture.Vmd(), model, useStandardSkeleton: true );
		Assert.Equal( exact.Skeleton.Count, optional.Skeleton.Count );
		Assert.Equal( exact.Clips[ 0 ].Frames[ ^1 ], optional.Clips[ 0 ].Frames[ ^1 ] );
		Assert.DoesNotContain( optional.Notes, note => note.Contains( "built-in standard" ) );
	}

	[Theory]
	[InlineData( "左足ＩＫ" )]
	[InlineData( "右足IK" )]
	public void FootIkMovesAnkleToAnimatedController( string controller )
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd( controller ), useStandardSkeleton: true );
		var world = new Pose( scene.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( scene.Skeleton );
		var ankle = scene.Skeleton.IndexOf( controller[ 0 ] + "足首" );
		var goal = scene.Skeleton.IndexOf( controller );
		Assert.InRange( Vector3.Distance( world[ ankle ].Pos, world[ goal ].Pos ), 0, 0.3f );
	}

	[Fact]
	public void FkPoseIsNotPulledBackByUnanimatedIk()
	{
		var scene = MmdImporter.Import( MmdFixture.Vpd( "左ひざ" ), useStandardSkeleton: true );
		var knee = scene.Skeleton.IndexOf( "左ひざ" );
		var actual = scene.Clips[ 0 ].Frames[ 0 ][ knee ].Rot;
		var expected = Quaternion.CreateFromAxisAngle( Vector3.UnitZ, MathF.PI / 2 );
		Assert.True( MathF.Abs( Quaternion.Dot( expected, actual ) ) > 0.9999f );
	}

	[Fact]
	public void DisabledIkKeepsAnkleAtRest()
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd( "左足ＩＫ", disableIk: true ), useStandardSkeleton: true );
		var world = new Pose( scene.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( scene.Skeleton );
		var ankle = scene.Skeleton.IndexOf( "左足首" );
		Assert.Equal( scene.Skeleton.RestWorld[ ankle ].Pos, world[ ankle ].Pos );
	}

	[Fact]
	public void UnknownBoneTracksStillRequestMatchingModel()
	{
		var error = Assert.Throws<FormatException>( () => MmdImporter.Import( MmdFixture.Vmd( "custom_tail" ), useStandardSkeleton: true ) );
		Assert.Contains( "PMX/PMD", error.Message );
	}
}
