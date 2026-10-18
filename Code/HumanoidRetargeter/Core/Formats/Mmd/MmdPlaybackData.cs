using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

/// <summary>Versioned, self-contained MMD mesh and sampled animation for native playback.</summary>
public sealed class MmdPlaybackData
{
	/// <summary>Payload schema version.</summary>
	public int Version { get; set; } = 1;
	/// <summary>Original PMX/PMD bytes, preserving all deformation and material definitions.</summary>
	public byte[] Model { get; set; } = Array.Empty<byte>();
	/// <summary>Sample rate.</summary>
	public float Fps { get; set; }
	/// <summary>Number of sampled poses.</summary>
	public int FrameCount { get; set; }
	/// <summary>Whether playback repeats.</summary>
	public bool Looping { get; set; }
	/// <summary>Seven floats per bone per frame: skinning translation and quaternion in MMD coordinates.</summary>
	public float[] Transforms { get; set; } = Array.Empty<float>();
	/// <summary>Morph controls sampled at the same rate as the poses.</summary>
	public Dictionary<string, float[]> Morphs { get; set; } = new();
	/// <summary>Original vertex indices with baked soft-body positions.</summary>
	public int[] SoftVertices { get; set; } = Array.Empty<int>();
	/// <summary>Three floats per soft vertex per frame, in MMD coordinates.</summary>
	public float[] SoftPositions { get; set; } = Array.Empty<float>();

	/// <summary>Builds playback for a PMX/PMD target from the retargeter's final solved clip.</summary>
	public static MmdPlaybackData Create( byte[] modelBytes, RetargetTargetSpec target, ClipResult clip )
	{
		var model = MmdModelReader.Read( modelBytes );
		var frames = clip.SolvedFrames ?? throw new ArgumentException( "The clip's solved frames have been released." );
		var skeleton = target.Rig.Skeleton;
		var bones = model.Bones.Select( bone => MmdBoneNames.Find( skeleton, bone.Name ) ).ToArray();
		if ( bones.Any( index => index < 0 ) )
			throw new ArgumentException( "MMD playback requires the matching PMX/PMD target skeleton." );
		var result = new MmdPlaybackData
		{
			Model = modelBytes, Fps = clip.Fps, FrameCount = frames.Count, Looping = clip.Looping,
			Transforms = new float[ checked(frames.Count * bones.Length * 7) ],
			Morphs = clip.MorphWeights.ToDictionary( entry => entry.Key, entry => entry.Value ),
		};
		// ModelDoc can rotate the entire bind into its forward axis. MMD bind world
		// rotations are identity, so this anchor recovers the original model space.
		var anchor = skeleton.RestWorld[ bones[ 0 ] ];
		var basis = Quaternion.Inverse( anchor.Rot );
		var scale = target.UpAxis == TargetUpAxis.ZUpEngine ? RetargetTargetSpec.SboxSourceScale : 1f;
		var origin = MmdModel.Position( model.Bones[ 0 ].Position ) - Vector3.Transform( anchor.Pos, basis ) / scale;
		for ( var frame = 0; frame < frames.Count; frame++ )
		{
			var world = new Pose( frames[ frame ] ).ToWorld( skeleton );
			for ( var i = 0; i < bones.Length; i++ )
			{
				var current = world[ bones[ i ] ];
				var rest = skeleton.RestWorld[ bones[ i ] ];
				var rotation = Quaternion.Normalize( current.Rot * Quaternion.Inverse( rest.Rot ) );
				var position = current.Pos - Vector3.Transform( rest.Pos, rotation );
				rotation = Quaternion.Normalize( basis * rotation * Quaternion.Inverse( basis ) );
				position = Vector3.Transform( position, basis ) / scale + origin - Vector3.Transform( origin, rotation );
				position = MmdModel.Position( position ) / (MmdModel.UnitScaleCm * MmdModel.UnitScaleCm);
				rotation = MmdModel.Rotation( rotation );
				var offset = (frame * bones.Length + i) * 7;
				result.Transforms[ offset ] = position.X;
				result.Transforms[ offset + 1 ] = position.Y;
				result.Transforms[ offset + 2 ] = position.Z;
				result.Transforms[ offset + 3 ] = rotation.X;
				result.Transforms[ offset + 4 ] = rotation.Y;
				result.Transforms[ offset + 5 ] = rotation.Z;
				result.Transforms[ offset + 6 ] = rotation.W;
			}
		}
		MmdSoftBodyBake.Apply( result, model );
		return result;
	}

	/// <summary>Serializes the payload for an MMD playback resource.</summary>
	public string ToJson() => JsonSerializer.Serialize( this );

	/// <summary>Reads and validates a playback payload.</summary>
	public static MmdPlaybackData FromJson( string json )
	{
		var data = JsonSerializer.Deserialize<MmdPlaybackData>( json ) ?? throw new FormatException( "Empty MMD playback data." );
		var model = MmdModelReader.Read( data.Model );
		if ( data.Version != 1 || data.FrameCount <= 0 || !float.IsFinite( data.Fps ) || data.Fps <= 0 || data.Fps > 240
			|| (long)data.FrameCount * model.Bones.Count * 7 != data.Transforms.Length
			|| (long)data.FrameCount * data.SoftVertices.Length * 3 != data.SoftPositions.Length
			|| data.SoftVertices.Any( i => i < 0 || i >= model.Vertices.Count )
			|| data.Transforms.Any( value => !float.IsFinite( value ) ) || data.SoftPositions.Any( value => !float.IsFinite( value ) )
			|| data.Morphs.Values.Any( values => values.Length != data.FrameCount || values.Any( value => !float.IsFinite( value ) ) ) )
			throw new FormatException( "Invalid MMD playback data." );
		return data;
	}

	internal XForm TransformAt( int frame, int bone, int boneCount )
	{
		var offset = (frame * boneCount + bone) * 7;
		return new XForm( new Vector3( Transforms[ offset ], Transforms[ offset + 1 ], Transforms[ offset + 2 ] ),
			Quaternion.Normalize( new Quaternion( Transforms[ offset + 3 ], Transforms[ offset + 4 ], Transforms[ offset + 5 ], Transforms[ offset + 6 ] ) ) );
	}
}
