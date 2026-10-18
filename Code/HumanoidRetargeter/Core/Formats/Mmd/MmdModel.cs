using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdModel
{
	// Conventional MMD scale: 1 unit = 0.08 m. Reflect Z to use right-handed Y-up space.
	internal const float UnitScaleCm = 8f;
	internal List<MmdBone> Bones = new();
	internal List<MmdVertex> Vertices = new();
	internal List<MmdMaterial> Materials = new();
	internal List<MmdMorph> Morphs = new();
	internal List<MmdRigidBody> RigidBodies = new();
	internal List<MmdJoint> Joints = new();
	internal List<MmdSoftBody> SoftBodies = new();
	internal int[] Indices = Array.Empty<int>();
	internal List<string> Notes = new();
	internal static Vector3 Position( Vector3 value ) => new Vector3( value.X, value.Y, -value.Z ) * UnitScaleCm;
	internal static Quaternion Rotation( Quaternion value ) => new( -value.X, -value.Y, value.Z, value.W );

	internal SkeletonModel CreateSkeleton()
	{
		var definitions = new List<BoneDefinition>();
		foreach ( var bone in Bones )
		{
			var parent = bone.Parent < 0 ? null : Bones[ bone.Parent ];
			definitions.Add( new BoneDefinition( bone.Name, parent?.Name,
				new XForm( Position( bone.Position - (parent?.Position ?? Vector3.Zero) ), Quaternion.Identity ) ) );
		}
		try
		{
			return SkeletonModel.Create( definitions );
		}
		catch ( ArgumentException error )
		{
			throw new FormatException( "Invalid MMD bone hierarchy: " + error.Message, error );
		}
	}

	internal void Validate()
	{
		foreach ( var morph in Morphs )
			foreach ( var impulse in morph.Impulses )
				if ( impulse.Body >= RigidBodies.Count )
					throw new FormatException( "Invalid PMX impulse rigid-body index." );
		if ( Bones.Count == 0 )
			throw new FormatException( "MMD model has no skeleton." );
		// MMD addresses bones by index; helper bones may legitimately share a name.
		var reserved = Bones.Select( bone => bone.Name ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var used = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in Bones )
		{
			var original = string.IsNullOrWhiteSpace( bone.Name ) ? "mmd_bone" : bone.Name;
			var name = original;
			var suffix = 2;
			while ( used.Contains( name ) || (name != original && reserved.Contains( name )) )
				name = original + "_" + suffix++;
			used.Add( name );
			bone.Name = name;
		}
		foreach ( var bone in Bones )
		{
			CheckBone( bone.Parent );
			CheckBone( bone.AppendParent );
			CheckBone( bone.IkTarget );
			foreach ( var link in bone.Links )
				CheckBone( link.Bone, false );
		}
		foreach ( var vertex in Vertices )
		{
			var sum = 0f;
			for ( var i = 0; i < 4; i++ )
			{
				if ( vertex.Weights[ i ] < 0 )
					throw new FormatException( "Negative MMD skin weight." );
				if ( vertex.Weights[ i ] > 0 )
					CheckBone( vertex.Bones[ i ], false );
				sum += vertex.Weights[ i ];
			}
			if ( sum <= 0 )
				throw new FormatException( "MMD vertex has no skin weights." );
			for ( var i = 0; i < 4; i++ )
				vertex.Weights[ i ] /= sum;
		}
		foreach ( var index in Indices )
		{
			if ( index < 0 || index >= Vertices.Count )
				throw new FormatException( "MMD triangle references an invalid vertex." );
		}
		if ( Indices.Length % 3 != 0 || Materials.Sum( m => (long)m.IndexCount ) != Indices.Length )
			throw new FormatException( "MMD material ranges do not match the triangle list." );
		CreateSkeleton();
	}

	private void CheckBone( int index, bool optional = true )
	{
		if ( index < (optional ? -1 : 0) || index >= Bones.Count )
			throw new FormatException( "MMD references an invalid bone index." );
	}
}
