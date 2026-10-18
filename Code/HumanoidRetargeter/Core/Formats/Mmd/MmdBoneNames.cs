using System.Text;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

/// <summary>Stable ASCII bone identities for MMD models compiled by ModelDoc.</summary>
public static class MmdBoneNames
{
	/// <summary>Encodes the complete name so Unicode and helper names remain distinct.</summary>
	public static string Encode( string name )
		=> "mmd_" + Convert.ToHexString( Encoding.UTF8.GetBytes( name ) ).ToLowerInvariant();

	/// <summary>Copies a model skeleton with compiler-safe names and unchanged bind transforms.</summary>
	public static SkeletonModel EncodeSkeleton( SkeletonModel skeleton )
		=> SkeletonModel.Create( skeleton.Bones.Select( bone => new BoneDefinition(
			Encode( bone.Name ), bone.ParentIndex < 0 ? null : Encode( skeleton[ bone.ParentIndex ].Name ),
			bone.RestLocal ) ).ToArray() );

	internal static int Find( SkeletonModel skeleton, string name )
	{
		var index = skeleton.IndexOf( name );
		return index >= 0 ? index : skeleton.IndexOf( Encode( name ) );
	}
}
