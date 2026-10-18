using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Gltf;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdCompiledBoneTests
{
	[Fact]
	public void CompilerSafeSkeletonKeepsDistinctNamesAnatomyAndSkinBindings()
	{
		var bytes = MmdFixture.Pmx();
		var original = MmdImporter.Import( bytes ).Skeleton;
		var target = MmdBoneNames.EncodeSkeleton( original );
		Assert.Equal( target.Count, target.Bones.Select( bone => bone.Name ).Distinct().Count() );
		Assert.All( target.Bones, bone => Assert.Matches( "^[a-z0-9_]+$", bone.Name ) );
		var map = Retargeter.ResolveMapping( target ).Map;
		Assert.Equal( "mmd", map.ProfileName );
		Assert.Equal( MmdBoneNames.Encode( "左足" ), target[ map.RoleToBone[ BoneRole.UpperLegL ] ].Name );
		Assert.Equal( MmdBoneNames.Encode( "右ひざ" ), target[ map.RoleToBone[ BoneRole.LowerLegR ] ].Name );
		Assert.True( RestNormalizer.IsAnatomicalRest( target, map, target.RestWorld ) );
		RestNormalizer.Normalize( target, map );
		var gltf = MmdImporter.ConvertModelToGltf( bytes, engineSafeBoneNames: true );
		using var document = JsonDocument.Parse( gltf );
		var names = document.RootElement.GetProperty( "nodes" ).EnumerateArray()
			.Select( node => node.GetProperty( "name" ).GetString() ).ToHashSet();
		Assert.All( target.Bones, bone => Assert.Contains( bone.Name, names ) );
		Assert.Contains( MmdBoneNames.Encode( "左足" ), GltfModelDmxWriter.Write( gltf, target, "test" ) );
	}

	[Fact]
	public void JapaneseMotionConvertsAndExportsPlaybackOnEncodedTarget()
	{
		var bytes = MmdFixture.Pmx();
		var skeleton = MmdBoneNames.EncodeSkeleton( MmdImporter.Import( bytes ).Skeleton );
		var target = new RetargetTargetSpec
		{
			Rig = TargetRig.FromSkeleton( skeleton, Retargeter.ResolveMapping( skeleton ).Map ),
			VmdlScale = RetargetTargetSpec.SboxSourceScale,
		};
		var result = Retargeter.Convert( new RetargetRequest
		{
			SourceData = MmdFixture.Vmd(), SourceFileName = "motion.vmd", SkeletonData = bytes,
		}, target );
		Assert.Empty( result.Errors );
		var clip = Assert.Single( result.Clips );
		Assert.True( clip.Success, clip.Error );
		Assert.Contains( MmdBoneNames.Encode( "左足" ), clip.DmxContent );
		var playback = MmdPlaybackData.Create( bytes, target, clip );
		Assert.Equal( clip.SolvedFrames!.Count, playback.FrameCount );
	}

	[Fact]
	public void EncodedTargetKeepsAuxiliaryBoneMotion()
	{
		var source = MmdImporter.Import( MmdFixture.Vmd(), MmdFixture.Pmx() );
		var helper = source.Skeleton.IndexOf( "左足ＩＫ" );
		source.Clips[ 0 ].Frames[ 1 ][ helper ].Rot = Quaternion.CreateFromAxisAngle( Vector3.UnitY, 0.5f );
		var skeleton = MmdBoneNames.EncodeSkeleton( source.Skeleton );
		var target = TargetRig.FromSkeleton( skeleton, Retargeter.ResolveMapping( skeleton ).Map );
		var frames = source.Clips[ 0 ].Frames.Select( _ => Pose.Rest( skeleton ).Locals ).ToList();
		MmdAuxiliaryMotion.Apply( source, target, frames, 0 );
		var targetHelper = skeleton.IndexOf( MmdBoneNames.Encode( "左足ＩＫ" ) );
		Assert.Equal( source.Clips[ 0 ].Frames[ 1 ][ helper ].Rot, frames[ 1 ][ targetHelper ].Rot );
	}

	[Fact]
	public void PlaybackReversesCompilerBindBasis()
	{
		var bytes = MmdFixture.Pmx();
		var source = MmdImporter.Import( bytes ).Skeleton;
		var basis = Quaternion.CreateFromAxisAngle( Vector3.UnitY, MathF.PI / 2 );
		var skeleton = HumanoidRetargeter.Core.Skeleton.Skeleton.Create( source.Bones.Select( bone => new BoneDefinition(
			MmdBoneNames.Encode( bone.Name ), bone.ParentIndex < 0 ? null : MmdBoneNames.Encode( source[ bone.ParentIndex ].Name ),
			bone.ParentIndex < 0 ? new XForm( Vector3.Transform( bone.RestLocal.Pos, basis ), basis ) : bone.RestLocal ) ).ToArray() );
		var pose = Pose.Rest( skeleton ).Locals;
		for ( var i = 0; i < skeleton.Count; i++ )
			if ( skeleton[ i ].ParentIndex < 0 )
				pose[ i ].Pos += Vector3.Transform( new Vector3( 8, 16, -24 ), basis );
		var target = new RetargetTargetSpec { Rig = TargetRig.FromSkeleton( skeleton, Retargeter.ResolveMapping( skeleton ).Map ), VmdlScale = RetargetTargetSpec.SboxSourceScale };
		var clip = new ClipResult { ClipName = "test", SourceFileName = "test.vmd", Success = true, Fps = 30, SolvedFrames = new() { pose } };
		var state = new MmdPlaybackState( MmdPlaybackData.Create( bytes, target, clip ) );
		state.Evaluate( 0 );
		Assert.True( Vector3.Distance( state.Positions[ 0 ], new Vector3( 1, 12, 2 ) ) < 0.0001f );
	}
}
