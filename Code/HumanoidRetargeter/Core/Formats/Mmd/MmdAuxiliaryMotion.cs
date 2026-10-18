using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Core.Mapping;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static class MmdAuxiliaryMotion
{
	internal static void Apply( SourceScene scene, TargetRig target, List<XForm[]> frames, int take )
	{
		if ( target.HelpersAreConstraintDriven )
			return;
		var source = scene.Skeleton;
		var rig = target.Skeleton;
		var matches = new Dictionary<int, int>();
		var scales = new List<float>();
		var encodedSource = source.Bones.ToDictionary( bone => MmdBoneNames.Encode( bone.Name ), bone => bone.Index,
			StringComparer.OrdinalIgnoreCase );
		for ( var t = 0; t < rig.Count; t++ )
		{
			var s = source.IndexOf( rig[ t ].Name );
			if ( s < 0 && encodedSource.TryGetValue( rig[ t ].Name, out var encoded ) )
				s = encoded;
			if ( s < 0 )
				continue;
			matches[ t ] = s;
			var sourceLength = source[ s ].RestLocal.Pos.Length();
			if ( sourceLength > 0.1f && rig[ t ].RestLocal.Pos.Length() > 0.1f )
				scales.Add( rig[ t ].RestLocal.Pos.Length() / sourceLength );
		}
		if ( scales.Count == 0 )
			return;
		scales.Sort();
		var scale = scales[ scales.Count / 2 ];
		var excluded = new HashSet<int>();
		if ( target.BoneForRole( BoneRole.Hips ) is { } hips )
		{
			for ( var parent = hips; parent >= 0; parent = rig[ parent ].ParentIndex )
				excluded.Add( parent );
		}
		foreach ( var (t, s) in matches )
		{
			if ( target.RoleOf( t ) is not null || excluded.Contains( t ) )
				continue;
			var sp = source[ s ].ParentIndex;
			var tp = rig[ t ].ParentIndex;
			if ( sp >= 0 && (tp < 0 || !matches.TryGetValue( tp, out var mappedParent ) || mappedParent != sp) )
				continue;
			var sourceRest = source[ s ].RestLocal;
			var targetRest = rig[ t ].RestLocal;
			if ( MathF.Abs( sourceRest.Pos.Length() * scale - targetRest.Pos.Length() ) > MathF.Max( 0.1f, targetRest.Pos.Length() * 0.05f ) )
				continue;
			for ( var frame = 0; frame < frames.Count; frame++ )
			{
				var value = scene.Clips[ take ].Frames[ frame ][ s ];
				var delta = Quaternion.Inverse( sourceRest.Rot ) * value.Rot;
				var solved = frames[ frame ][ t ];
				frames[ frame ][ t ] = new XForm( solved.Pos + (value.Pos - sourceRest.Pos) * scale,
					Quaternion.Normalize( solved.Rot * delta ) );
			}
		}
	}
}
