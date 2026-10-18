using System.Numerics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal static partial class MmdPhysics
{
	// Upstream BulletXNA shares scratch pools. Serialize imports so separate conversions
	// cannot race over the same pooled collision and constraint objects.
	private static readonly object SimulationLock = new();

	internal static void Bake( MmdModel model, SkeletonModel skeleton, Clip clip )
	{
		if ( model.RigidBodies.Count == 0 || clip.Frames.Count < 2 )
			return;
		lock ( SimulationLock )
			Simulate( model, skeleton, clip );
	}

	private static void Simulate( MmdModel model, SkeletonModel skeleton, Clip clip )
	{
		var configuration = new DefaultCollisionConfiguration();
		var dispatcher = new CollisionDispatcher( configuration );
		var broadphase = new DbvtBroadphase();
		var solver = new SequentialImpulseConstraintSolver();
		solver.SetRandSeed( 0 );
		var world = new DiscreteDynamicsWorld( dispatcher, broadphase, solver, configuration );
		world.SetGravity( new IndexedVector3( 0, -98, 0 ) );
		var bodies = new RigidBody[ model.RigidBodies.Count ];
		var offsets = new XForm[ bodies.Length ];
		var boneIndices = new int[ bodies.Length ];
		var firstPose = new Pose( clip.Frames[ 0 ] ).ToWorld( skeleton );
		var morphs = new MmdMorphWeights( model );
		try
		{
			for ( var i = 0; i < bodies.Length; i++ )
			{
				var definition = model.RigidBodies[ i ];
				boneIndices[ i ] = definition.Bone < 0 ? -1 : skeleton.IndexOf( model.Bones[ definition.Bone ].Name );
				var rest = new XForm( definition.Position, Euler( definition.Rotation ) );
				var boneRest = definition.Bone < 0 ? XForm.Identity
					: new XForm( model.Bones[ definition.Bone ].Position, Quaternion.Identity );
				offsets[ i ] = XForm.ToLocal( boneRest, rest );
				var initial = boneIndices[ i ] < 0 ? rest : XForm.Compose( ToMmd( firstPose[ boneIndices[ i ] ] ), offsets[ i ] );
				bodies[ i ] = CreateBody( definition, initial );
				world.AddRigidBody( bodies[ i ], (CollisionFilterGroups)(1 << definition.Group),
					(CollisionFilterGroups)(~definition.ExcludedGroups & 0xffff) );
			}
			foreach ( var joint in model.Joints )
				AddJoint( world, model, bodies, joint );
			var plane = new StaticPlaneShape( new IndexedVector3( 0, 1, 0 ), 0 );
			world.AddRigidBody( new RigidBody( new RigidBodyConstructionInfo( 0, null, plane ) ) );

			var previous = firstPose;
			for ( var frame = 1; frame < clip.Frames.Count; frame++ )
			{
				var animated = new Pose( clip.Frames[ frame ] ).ToWorld( skeleton );
				var substeps = Math.Max( 1, (int)MathF.Ceiling( 120f / clip.Fps ) );
				var dt = 1f / (clip.Fps * substeps);
				for ( var step = 1; step <= substeps; step++ )
				{
					for ( var i = 0; i < bodies.Length; i++ )
					{
						var bone = boneIndices[ i ];
						var mode = model.RigidBodies[ i ].Mode;
						if ( bone < 0 || mode == 1 )
							continue;
						var fraction = step / (float)substeps;
						var pose = new XForm( Vector3.Lerp( previous[ bone ].Pos, animated[ bone ].Pos, fraction ),
							Quaternion.Slerp( previous[ bone ].Rot, animated[ bone ].Rot, fraction ) );
						var target = Matrix( XForm.Compose( ToMmd( pose ), offsets[ i ] ) );
						if ( mode == 2 )
							target._basis = bodies[ i ].GetWorldTransform()._basis;
						bodies[ i ].SetWorldTransform( target );
						bodies[ i ].GetMotionState()?.SetWorldTransform( ref target );
					}
					morphs.Sample( clip.MorphWeights, frame - 1 + step / (float)substeps );
					ApplyImpulses( model, bodies, morphs.Values, dt * 30f );
					world.StepSimulation( dt, 0, dt );
				}

				var driven = new Dictionary<int, XForm>();
				for ( var i = 0; i < bodies.Length; i++ )
				{
					var bone = boneIndices[ i ];
					var mode = model.RigidBodies[ i ].Mode;
					if ( bone < 0 || mode == 0 )
						continue;
					var body = Transform( bodies[ i ].GetWorldTransform() );
					var raw = XForm.Compose( body, offsets[ i ].Inverse() );
					var result = new XForm( MmdModel.Position( raw.Pos ), MmdModel.Rotation( raw.Rot ) );
					if ( mode == 2 )
						result.Pos = animated[ bone ].Pos;
					if ( !float.IsFinite( result.Pos.LengthSquared() ) || !float.IsFinite( result.Rot.LengthSquared() ) )
						throw new FormatException( "MMD physics became unstable. Check rigid-body sizes and joint settings." );
					driven[ bone ] = result;
				}
				var outputWorld = new XForm[ skeleton.Count ];
				for ( var bone = 0; bone < skeleton.Count; bone++ )
				{
					var parent = skeleton[ bone ].ParentIndex;
					var parentWorld = parent < 0 ? XForm.Identity : outputWorld[ parent ];
					if ( driven.TryGetValue( bone, out var result ) )
						clip.Frames[ frame ][ bone ] = XForm.ToLocal( parentWorld, result );
					outputWorld[ bone ] = XForm.Compose( parentWorld, clip.Frames[ frame ][ bone ] );
				}
				previous = animated;
			}
		}
		finally
		{
			world.Cleanup();
			solver.Cleanup();
			broadphase.Cleanup();
			dispatcher.Cleanup();
			configuration.Cleanup();
		}
	}

	private static RigidBody CreateBody( MmdRigidBody definition, XForm initial )
	{
		CollisionShape shape = definition.Shape switch
		{
			0 => new SphereShape( definition.Size.X ),
			1 => new BoxShape( Vector( definition.Size ) ),
			2 => new CapsuleShape( definition.Size.X, definition.Size.Y ),
			_ => throw new FormatException( "Unsupported MMD rigid shape." ),
		};
		var mass = definition.Mode == 0 ? 0 : MathF.Max( definition.Mass, 0.001f );
		shape.CalculateLocalInertia( mass, out var inertia );
		var state = new DefaultMotionState( Matrix( initial ), IndexedMatrix.Identity );
		var body = new RigidBody( new RigidBodyConstructionInfo( mass, state, shape, inertia )
		{
			m_linearDamping = Math.Clamp( definition.LinearDamping, 0, 1 ),
			m_angularDamping = Math.Clamp( definition.AngularDamping, 0, 1 ),
			m_friction = MathF.Max( definition.Friction, 0 ),
			m_restitution = Math.Clamp( definition.Restitution, 0, 1 ),
		} );
		if ( definition.Mode == 0 )
			body.SetCollisionFlags( CollisionFlags.CF_KINEMATIC_OBJECT );
		body.SetActivationState( ActivationState.DISABLE_DEACTIVATION );
		return body;
	}

	private static void AddJoint( DiscreteDynamicsWorld world, MmdModel model, RigidBody[] bodies, MmdJoint definition )
	{
		var jointWorld = new XForm( definition.Position, Euler( definition.Rotation ) );
		XForm BodyRest( int index ) => new( model.RigidBodies[ index ].Position, Euler( model.RigidBodies[ index ].Rotation ) );
		var joint = new Generic6DofSpringConstraint( bodies[ definition.BodyA ], bodies[ definition.BodyB ],
			Matrix( XForm.ToLocal( BodyRest( definition.BodyA ), jointWorld ) ),
			Matrix( XForm.ToLocal( BodyRest( definition.BodyB ), jointWorld ) ), true );
		joint.SetLinearLowerLimit( Vector( definition.LinearMin ) );
		joint.SetLinearUpperLimit( Vector( definition.LinearMax ) );
		joint.SetAngularLowerLimit( Vector( definition.AngularMin ) );
		joint.SetAngularUpperLimit( Vector( definition.AngularMax ) );
		for ( var axis = 0; axis < 6; axis++ )
		{
			var value = axis < 3 ? Vector( definition.LinearSpring )[ axis ] : Vector( definition.AngularSpring )[ axis - 3 ];
			joint.EnableSpring( axis, value > 0 );
			joint.SetStiffness( axis, MathF.Max( value, 0 ) );
		}
		joint.SetEquilibriumPoint();
		world.AddConstraint( joint, true );
	}

	private static XForm ToMmd( XForm transform ) => new( MmdModel.Position( transform.Pos ) / 64f, MmdModel.Rotation( transform.Rot ) );
	private static IndexedVector3 Vector( Vector3 value ) => new( value.X, value.Y, value.Z );
	private static Quaternion Euler( Vector3 angles ) => Quaternion.CreateFromAxisAngle( Vector3.UnitZ, angles.Z )
		* Quaternion.CreateFromAxisAngle( Vector3.UnitY, angles.Y ) * Quaternion.CreateFromAxisAngle( Vector3.UnitX, angles.X );
	private static IndexedMatrix Matrix( XForm transform ) => new(
		new IndexedBasisMatrix( new IndexedQuaternion( transform.Rot.X, transform.Rot.Y, transform.Rot.Z, transform.Rot.W ) ), Vector( transform.Pos ) );
	private static XForm Transform( IndexedMatrix matrix )
	{
		var rotation = matrix.GetRotation();
		return new XForm( new Vector3( matrix._origin.X, matrix._origin.Y, matrix._origin.Z ),
			Quaternion.Normalize( new Quaternion( rotation.X, rotation.Y, rotation.Z, rotation.W ) ) );
	}
}
