using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

internal static partial class MmdPhysics
{
	private static void ApplyImpulses( MmdModel model, RigidBody[] bodies, float[] weights, float frameFraction )
	{
		for ( var i = 0; i < weights.Length; i++ )
		{
			if ( weights[ i ] == 0 )
				continue;
			foreach ( var impulse in model.Morphs[ i ].Impulses )
			{
				var body = bodies[ impulse.Body ];
				if ( model.RigidBodies[ impulse.Body ].Mode == 0 )
					continue;
				var velocity = Vector( impulse.Velocity * (weights[ i ] * frameFraction) );
				var torque = Vector( impulse.Torque * (weights[ i ] * frameFraction) );
				if ( impulse.Local )
				{
					var basis = body.GetWorldTransform()._basis;
					velocity = basis * velocity;
					torque = basis * torque;
				}
				body.ApplyCentralImpulse( ref velocity );
				body.ApplyTorqueImpulse( ref torque );
			}
		}
	}
}
