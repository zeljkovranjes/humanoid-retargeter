using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdPoseEvaluator
{
	private readonly MmdModel _model;
	private readonly SkeletonModel _skeleton;
	private readonly int[] _indices;
	private readonly XForm[] _world;
	private readonly int[] _order;

	internal MmdPoseEvaluator( MmdModel model, SkeletonModel skeleton )
	{
		_model = model;
		_skeleton = skeleton;
		_indices = model.Bones.Select( bone => skeleton.IndexOf( bone.Name ) ).ToArray();
		_world = new XForm[ skeleton.Count ];
		_order = Enumerable.Range( 0, model.Bones.Count ).OrderBy( i => model.Bones[ i ].Layer ).ToArray();
	}

	internal void Apply( XForm[] pose, MmdMotion motion, float frame )
	{
		var state = new byte[ _model.Bones.Count ];
		foreach ( var index in _order )
			Append( index, pose, state );
		foreach ( var index in _order )
		{
			var bone = _model.Bones[ index ];
			if ( bone.IkTarget < 0 || bone.Links.Count == 0 )
				continue;
			var enabled = true;
			if ( motion.IkStates.TryGetValue( bone.Name, out var states ) )
			{
				foreach ( var key in states )
				{
					if ( key.Frame > frame )
						break;
					enabled = key.Enabled;
				}
			}
			if ( enabled )
				SolveIk( index, pose );
		}
	}

	private void Append( int index, XForm[] pose, byte[] state )
	{
		if ( state[ index ] == 2 )
			return;
		if ( state[ index ] == 1 )
			throw new FormatException( "PMX append-transform cycle." );
		state[ index ] = 1;
		var bone = _model.Bones[ index ];
		if ( bone.AppendParent >= 0 )
		{
			Append( bone.AppendParent, pose, state );
			var source = _indices[ bone.AppendParent ];
			var target = _indices[ index ];
			if ( (bone.Flags & 0x100) != 0 )
				pose[ target ].Rot = Quaternion.Normalize( pose[ target ].Rot * Quaternion.Slerp( Quaternion.Identity, pose[ source ].Rot, bone.AppendWeight ) );
			if ( (bone.Flags & 0x200) != 0 )
				pose[ target ].Pos += (pose[ source ].Pos - _skeleton[ source ].RestLocal.Pos) * bone.AppendWeight;
		}
		state[ index ] = 2;
	}

	private void UpdateWorld( XForm[] pose )
	{
		for ( var i = 0; i < pose.Length; i++ )
		{
			var parent = _skeleton[ i ].ParentIndex;
			_world[ i ] = parent < 0 ? pose[ i ] : XForm.Compose( _world[ parent ], pose[ i ] );
		}
	}

	private void SolveIk( int index, XForm[] pose )
	{
		var bone = _model.Bones[ index ];
		UpdateWorld( pose );
		var goal = _world[ _indices[ index ] ].Pos;
		var effector = _indices[ bone.IkTarget ];
		var maxAngle = Math.Clamp( bone.AngleLimit, 0f, MathF.PI );
		// A bounded CCD solve protects editor responsiveness on hostile loop counts.
		for ( var iteration = 0; iteration < Math.Clamp( bone.Iterations, 0, 128 ); iteration++ )
		{
			foreach ( var link in bone.Links )
			{
				UpdateWorld( pose );
				if ( Vector3.DistanceSquared( goal, _world[ effector ].Pos ) < 0.0001f )
					return;
				var joint = _indices[ link.Bone ];
				var inverse = Quaternion.Conjugate( _world[ joint ].Rot );
				var from = Vector3.Transform( _world[ effector ].Pos - _world[ joint ].Pos, inverse );
				var to = Vector3.Transform( goal - _world[ joint ].Pos, inverse );
				if ( from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f )
					continue;
				from = Vector3.Normalize( from );
				to = Vector3.Normalize( to );
				var angle = MathF.Min( MathF.Acos( Math.Clamp( Vector3.Dot( from, to ), -1f, 1f ) ), maxAngle );
				var axis = Vector3.Cross( from, to );
				if ( angle < 1e-6f )
					continue;
				if ( axis.LengthSquared() < 1e-10f )
					axis = Vector3.Cross( from, MathF.Abs( from.X ) < 0.9f ? Vector3.UnitX : Vector3.UnitZ );
				var rotation = Quaternion.Normalize( pose[ joint ].Rot * Quaternion.CreateFromAxisAngle( Vector3.Normalize( axis ), angle ) );
				if ( link.Limited )
					rotation = Limit( rotation, link );
				pose[ joint ].Rot = rotation;
			}
		}
	}

	private static Quaternion Limit( Quaternion value, MmdIkLink link )
	{
		// Limits are authored in MMD's original handedness, in XYZ Euler order.
		var q = MmdModel.Rotation( value );
		var angles = new Vector3(
			MathF.Atan2( 2f * (q.W * q.X + q.Y * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y) ),
			MathF.Asin( Math.Clamp( 2f * (q.W * q.Y - q.Z * q.X), -1f, 1f ) ),
			MathF.Atan2( 2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z) ) );
		// A straight knee has no preferred bend direction; reflect into its allowed half-plane.
		if ( link.Maximum.X <= 0 && angles.X > 0 )
			angles.X = -angles.X;
		angles = Vector3.Clamp( angles, link.Minimum, link.Maximum );
		var rotation = Quaternion.CreateFromAxisAngle( Vector3.UnitZ, angles.Z )
			* Quaternion.CreateFromAxisAngle( Vector3.UnitY, angles.Y )
			* Quaternion.CreateFromAxisAngle( Vector3.UnitX, angles.X );
		return MmdModel.Rotation( Quaternion.Normalize( rotation ) );
	}
}
