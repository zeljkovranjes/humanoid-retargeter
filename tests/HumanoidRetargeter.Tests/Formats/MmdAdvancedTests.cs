using System.Numerics;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class MmdAdvancedTests
{
	[Fact]
	public void PmxReaderRetainsAdvancedMorphsSoftBodiesAndPins()
	{
		var model = MmdModelReader.Read( MmdFixture.AdvancedModel() );
		Assert.Equal( new Vector4( 0.2f, 0.3f, 0, 0 ), model.Morphs[ 0 ].Uvs[ 0 ].Offset );
		Assert.Equal( 1, model.Morphs[ 1 ].Uvs[ 0 ].Channel );
		Assert.Equal( -1, model.Morphs[ 2 ].Materials[ 0 ].Material );
		Assert.Equal( 2.8f, model.Morphs[ 2 ].Materials[ 0 ].Toon.W );
		Assert.True( model.Morphs[ 3 ].Impulses[ 0 ].Local );
		Assert.Equal( new Vector3( 4, 5, 6 ), model.Morphs[ 3 ].Impulses[ 0 ].Torque );
		var soft = Assert.Single( model.SoftBodies );
		Assert.Contains( 0, soft.Pins );
		Assert.Equal( (0, 1, false), Assert.Single( soft.Anchors ) );
		Assert.Throws<FormatException>( () => MmdModelReader.Read( MmdFixture.AdvancedModel()[ ..^1 ] ) );
	}

	[Theory]
	[InlineData( 3 )]
	[InlineData( 4 )]
	public void SphericalAndDualQuaternionSkinningPreserveTwistingRadius( byte deform )
	{
		var vertex = new MmdVertex { Position = Vector3.UnitX, Normal = Vector3.UnitX, Deform = deform };
		vertex.Bones[ 1 ] = 1;
		vertex.Weights[ 0 ] = vertex.Weights[ 1 ] = 0.5f;
		var palette = new[]
		{
			new XForm( new Vector3( 2, 3, 0 ), Quaternion.CreateFromAxisAngle( Vector3.UnitZ, MathF.PI / 3 ) ),
			new XForm( new Vector3( 2, 3, 0 ), Quaternion.CreateFromAxisAngle( Vector3.UnitZ, -MathF.PI / 3 ) ),
		};
		MmdSkinning.Deform( vertex, vertex.Position, palette, out var position, out var normal );
		Assert.True( Vector3.Distance( position, new Vector3( 3, 3, 0 ) ) < 0.0001f );
		Assert.True( Vector3.Distance( normal, Vector3.UnitX ) < 0.0001f );
		vertex.Deform = 1;
		MmdSkinning.Deform( vertex, vertex.Position, palette, out var linear, out _ );
		Assert.InRange( linear.X, 2.49f, 2.51f );
	}

	[Fact]
	public void SdefCentersPreserveRestPoseWithUnequalWeights()
	{
		var vertex = new MmdVertex
		{
			Deform = 3, Position = new Vector3( 4, 5, 6 ), Normal = Vector3.UnitY,
			SdefC = new Vector3( 1, 2, 3 ), SdefR0 = new Vector3( 3, -1, 4 ), SdefR1 = new Vector3( -2, 7, 3 ),
		};
		vertex.Bones[ 1 ] = 1;
		vertex.Weights[ 0 ] = 0.2f;
		vertex.Weights[ 1 ] = 0.8f;
		MmdSkinning.Deform( vertex, vertex.Position, new[] { XForm.Identity, XForm.Identity }, out var position, out _ );
		Assert.True( Vector3.Distance( position, vertex.Position ) < 0.0001f );
	}

	[Fact]
	public void GroupUvAndMaterialMorphsAreAppliedWithoutLosingAdditionalUv()
	{
		var model = MmdModelReader.Read( MmdFixture.Pmx() );
		var parent = new MmdMorph { Name = "group" };
		parent.Groups.Add( (1, 0.5f) );
		var child = new MmdMorph { Name = "uv" };
		child.Uvs.Add( (0, 0, new Vector4( 0.4f, 0.2f, 0, 0 )) );
		child.Uvs.Add( (0, 1, new Vector4( 1, 2, 3, 4 )) );
		child.Materials.Add( new MmdMaterialMorph { Material = -1, Add = true, Diffuse = new Vector4( 0, 0, 0, -1 ) } );
		model.Morphs.AddRange( new[] { parent, child } );
		var data = IdentityData( model, 1 );
		data.Morphs[ "group" ] = new[] { 1f };
		var state = new MmdPlaybackState( data, model );
		state.Evaluate( 0 );
		Assert.Equal( new Vector4( 0.2f, 0.35f, 0, 0 ), state.Uvs[ 0 ][ 0 ] );
		Assert.Equal( new Vector4( 0.5f, 1, 1.5f, 2 ), state.Uvs[ 1 ][ 0 ] );
		Assert.InRange( state.Materials[ 0 ].Diffuse.W, 0.2999f, 0.3001f );
		state.Evaluate( 0 );
		Assert.InRange( state.Materials[ 0 ].Diffuse.W, 0.2999f, 0.3001f );
	}

	[Fact]
	public void MaterialAdditionsAreNotScaledByLaterMultiplyMorphs()
	{
		var model = MmdModelReader.Read( MmdFixture.Pmx() );
		var add = new MmdMorph { Name = "add" };
		add.Materials.Add( new MmdMaterialMorph { Material = 0, Add = true, Diffuse = Vector4.One } );
		var multiply = new MmdMorph { Name = "multiply" };
		multiply.Materials.Add( new MmdMaterialMorph { Material = 0, Diffuse = new Vector4( 2 ) } );
		model.Morphs.AddRange( new[] { add, multiply } );
		var data = IdentityData( model, 1 );
		data.Morphs[ "add" ] = data.Morphs[ "multiply" ] = new[] { 1f };
		var state = new MmdPlaybackState( data, model );
		state.Evaluate( 0 );
		Assert.Equal( 2f, state.Materials[ 0 ].Diffuse.X );
	}

	[Fact]
	public void ImpulseMorphChangesTheExportedRigidBodyMotion()
	{
		var model = MmdModelReader.Read( MmdFixture.PhysicsModel( 1 ) );
		var skeleton = model.CreateSkeleton();
		var morph = new MmdMorph { Name = "push" };
		morph.Impulses.Add( (0, false, new Vector3( 1, 0, 0 ), Vector3.Zero) );
		model.Morphs.Add( morph );
		var clip = new Clip( "push", 30, false, Enumerable.Range( 0, 31 ).Select( _ => Pose.Rest( skeleton ).Locals ).ToList() );
		clip.MorphWeights[ "push" ] = Enumerable.Repeat( 1f, 31 ).ToArray();
		MmdPhysics.Bake( model, skeleton, clip );
		var start = skeleton.RestWorld[ skeleton.IndexOf( "左腕" ) ].Pos;
		var end = new Pose( clip.Frames[ ^1 ] ).ToWorld( skeleton )[ skeleton.IndexOf( "左腕" ) ].Pos;
		Assert.True( end.X > start.X + 30 );
	}

	[Fact]
	public void SoftBodyBakePreservesPinsAndCanBeSoughtDeterministically()
	{
		var model = MmdModelReader.Read( MmdFixture.Pmx() );
		model.Vertices[ 2 ].Position = new Vector3( 1, 9, -1 );
		var soft = new MmdSoftBody { Material = 0, Mass = 1 };
		soft.Pins.Add( 0 );
		soft.Iterations[ 1 ] = 12;
		soft.Stiffness[ 0 ] = 1;
		model.SoftBodies.Add( soft );
		var data = IdentityData( model, 31 );
		MmdSoftBodyBake.Apply( data, model );
		var state = new MmdPlaybackState( data, model );
		state.Evaluate( 30 );
		var final = state.Positions.ToArray();
		Assert.Equal( model.Vertices[ 0 ].Position, final[ 0 ] );
		Assert.True( final[ 1 ].Y < model.Vertices[ 1 ].Position.Y - 0.2f );
		Assert.InRange( Vector3.Distance( final[ 0 ], final[ 1 ] ), 0.98f, 1.02f );
		state.Evaluate( 0 );
		state.Evaluate( 30 );
		Assert.Equal( final, state.Positions );
	}

	[Fact]
	public void PlaybackExportRoundTripPreservesNativeModelAndSolvedTranslation()
	{
		var bytes = MmdFixture.Pmx( weightType: 3 );
		var source = MmdImporter.Import( bytes );
		var frames = new List<XForm[]> { Pose.Rest( source.Skeleton ).Locals, Pose.Rest( source.Skeleton ).Locals };
		frames[ 1 ][ 0 ].Pos += new Vector3( 8, 16, -24 );
		var target = new RetargetTargetSpec { Rig = TargetRig.FromSkeleton( source.Skeleton, Retargeter.ResolveMapping( source.Skeleton ).Map ), VmdlScale = RetargetTargetSpec.SboxSourceScale };
		var clip = new ClipResult { ClipName = "test", SourceFileName = "test.vmd", Success = true, SolvedFrames = frames, Fps = 30 };
		var data = MmdPlaybackData.FromJson( MmdPlaybackData.Create( bytes, target, clip ).ToJson() );
		Assert.Equal( bytes, data.Model );
		var state = new MmdPlaybackState( data );
		state.Evaluate( 1 );
		Assert.True( Vector3.Distance( state.Positions[ 0 ], new Vector3( 1, 12, 2 ) ) < 0.0001f );
	}

	private static MmdPlaybackData IdentityData( MmdModel model, int count )
	{
		var data = new MmdPlaybackData { FrameCount = count, Fps = 30, Transforms = new float[ count * model.Bones.Count * 7 ] };
		for ( var i = 6; i < data.Transforms.Length; i += 7 )
			data.Transforms[ i ] = 1;
		return data;
	}

	[Theory]
	[InlineData( 3 )]
	[InlineData( 4 )]
	public void UnusedNegativeBoneIndicesDoNotBreakSpecialSkinning( byte deform )
	{
		var vertex = new MmdVertex { Deform = deform, Normal = Vector3.UnitY };
		vertex.Bones[ 0 ] = -1;
		vertex.Bones[ 1 ] = 0;
		vertex.Weights[ 1 ] = 1;
		MmdSkinning.Deform( vertex, Vector3.UnitX, new[] { XForm.Identity }, out var position, out _ );
		Assert.Equal( Vector3.UnitX, position );
	}

	[Fact]
	public void EngineAxisTargetProducesTheSameMmdPlaybackCoordinates()
	{
		var bytes = MmdFixture.Pmx();
		var source = MmdImporter.Import( bytes ).Skeleton;
		var basis = Quaternion.CreateFromAxisAngle( Vector3.UnitX, MathF.PI / 2 );
		var scale = RetargetTargetSpec.SboxSourceScale;
		var definitions = source.Bones.Select( bone => new BoneDefinition( bone.Name,
			bone.ParentIndex < 0 ? null : source[ bone.ParentIndex ].Name,
			bone.ParentIndex < 0
				? new XForm( Vector3.Transform( bone.RestLocal.Pos, basis ) * scale, basis )
				: new XForm( bone.RestLocal.Pos * scale, bone.RestLocal.Rot ) ) );
		var skeleton = HumanoidRetargeter.Core.Skeleton.Skeleton.Create( definitions.ToArray() );
		var frames = new List<XForm[]> { Pose.Rest( skeleton ).Locals };
		frames[ 0 ][ 0 ].Pos += new Vector3( 8, 24, 16 ) * scale;
		var target = new RetargetTargetSpec
		{
			Rig = TargetRig.FromSkeleton( skeleton, Retargeter.ResolveMapping( skeleton ).Map ),
			VmdlScale = 1, UpAxis = TargetUpAxis.ZUpEngine,
		};
		var clip = new ClipResult { ClipName = "test", SourceFileName = "test.vmd", Success = true, SolvedFrames = frames, Fps = 30 };
		var state = new MmdPlaybackState( MmdPlaybackData.Create( bytes, target, clip ) );
		state.Evaluate( 0 );
		Assert.True( Vector3.Distance( state.Positions[ 0 ], new Vector3( 1, 12, 2 ) ) < 0.0001f );
	}

	[Fact]
	public void TrimmedPreviewKeepsMorphSamplesAligned()
	{
		var clip = new ClipResult
		{
			ClipName = "test", SourceFileName = "test.vmd", Success = true,
			MorphWeights = new Dictionary<string, float[]> { ["blink"] = new[] { 0f, 0.25f, 0.75f, 1f } },
		};
		var trimmed = clip.WithFrames( new List<XForm[]> { new XForm[ 1 ], new XForm[ 1 ] }, new[] { 1, 3 } );
		Assert.Equal( new[] { 0.25f, 1f }, trimmed.MorphWeights[ "blink" ] );
		Assert.Equal( 4, clip.MorphWeights[ "blink" ].Length );
	}
}
