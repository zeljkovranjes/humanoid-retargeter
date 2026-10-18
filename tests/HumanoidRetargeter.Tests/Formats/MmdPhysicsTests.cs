using System.Numerics;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdPhysicsTests
{
	[Fact]
	public void DynamicBodyFallsAndCollidesWithGround()
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd(), MmdFixture.PhysicsModel( 1 ) );
		var bone = scene.Skeleton.IndexOf( "左腕" );
		var end = new Pose( scene.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( scene.Skeleton )[ bone ];
		Assert.InRange( end.Pos.Y, 0.5f, 2f );
	}

	[Theory]
	[InlineData( 0 )]
	[InlineData( 2 )]
	public void AnimationFollowingModesPreserveAnimatedBonePosition( int mode )
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd(), MmdFixture.PhysicsModel( mode ) );
		var reference = MmdImporter.Import( MmdFixture.Vmd(), MmdFixture.Pmx() );
		var bone = scene.Skeleton.IndexOf( "左腕" );
		var end = new Pose( scene.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( scene.Skeleton )[ bone ];
		var expected = new Pose( reference.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( reference.Skeleton )[ bone ];
		Assert.True( Vector3.Distance( expected.Pos, end.Pos ) < 0.01f );
	}

	[Fact]
	public void SixDofJointKeepsDynamicBodyAttachedToAnimatedAnchor()
	{
		var scene = MmdImporter.Import( MmdFixture.Vmd(), MmdFixture.PhysicsModel( 1, true ) );
		var end = new Pose( scene.Clips[ 0 ].Frames[ ^1 ] ).ToWorld( scene.Skeleton );
		var arm = end[ scene.Skeleton.IndexOf( "左腕" ) ];
		var shoulder = end[ scene.Skeleton.IndexOf( "左肩" ) ];
		Assert.InRange( Vector3.Distance( arm.Pos, shoulder.Pos ), 7.8f, 8.2f );
	}

	[Fact]
	public void SeparateImportsProduceDeterministicPhysics()
	{
		var data = MmdFixture.PhysicsModel( 1, true );
		var first = MmdImporter.Import( MmdFixture.Vmd(), data );
		var second = MmdImporter.Import( MmdFixture.Vmd(), data );
		Assert.Equal( first.Clips[ 0 ].Frames[ ^1 ], second.Clips[ 0 ].Frames[ ^1 ] );
	}
}
