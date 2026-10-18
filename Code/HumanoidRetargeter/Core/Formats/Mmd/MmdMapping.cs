#nullable enable annotations

using HumanoidRetargeter.Core.Mapping;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

internal static class MmdMapping
{
	internal static MappingResult? TryMap( SkeletonModel skeleton )
	{
		// Restrict this vocabulary to MMD rigs, leaving the existing preset library stable.
		if ( MmdBoneNames.Find( skeleton, "下半身" ) < 0 || MmdBoneNames.Find( skeleton, "上半身" ) < 0 )
			return null;
		var aliases = new Dictionary<BoneRole, string[]>
		{
			[ BoneRole.Hips ] = new[] { "下半身" },
			[ BoneRole.Spine0 ] = new[] { "上半身" },
			[ BoneRole.Spine1 ] = new[] { "上半身2", "上半身２" },
			[ BoneRole.Spine2 ] = new[] { "上半身3", "上半身３" },
			[ BoneRole.Neck ] = new[] { "首" },
			[ BoneRole.Head ] = new[] { "頭" },
		};
		foreach ( var (prefix, side) in new[] { ("左", "L"), ("右", "R") } )
		{
			foreach ( var (role, name) in new[]
			{
				("Clavicle", "肩"), ("UpperArm", "腕"), ("LowerArm", "ひじ"), ("Hand", "手首"),
				("UpperLeg", "足"), ("LowerLeg", "ひざ"), ("Foot", "足首"), ("Toe", "つま先"),
			} )
				aliases[ Enum.Parse<BoneRole>( role + side ) ] = new[] { prefix + name };
			foreach ( var (finger, name) in new[] { ("Index", "人指"), ("Middle", "中指"), ("Ring", "薬指"), ("Pinky", "小指") } )
			{
				var segments = new[] { "Prox", "Mid", "Dist" };
				for ( var i = 0; i < segments.Length; i++ )
					aliases[ Enum.Parse<BoneRole>( finger + segments[ i ] + side ) ] = new[]
					{
						prefix + name + (i + 1), prefix + name + (char)('１' + i),
					};
			}
			aliases[ Enum.Parse<BoneRole>( "ThumbMeta" + side ) ] = new[] { prefix + "親指0", prefix + "親指０" };
			aliases[ Enum.Parse<BoneRole>( "ThumbProx" + side ) ] = new[] { prefix + "親指1", prefix + "親指１" };
			aliases[ Enum.Parse<BoneRole>( "ThumbDist" + side ) ] = new[] { prefix + "親指2", prefix + "親指２" };
		}
		var names = aliases.ToDictionary( entry => entry.Key,
			entry => entry.Value.Concat( entry.Value.Select( MmdBoneNames.Encode ) ).ToArray() );
		return ProfileDetector.Apply( new Profile( "mmd", Array.Empty<string>(), names ), skeleton );
	}
}
