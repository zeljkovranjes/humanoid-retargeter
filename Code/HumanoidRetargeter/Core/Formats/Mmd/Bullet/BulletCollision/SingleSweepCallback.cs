/*
 * C# / XNA  port of Bullet (c) 2011 Mark Neale <xexuxjy@hotmail.com>
 *
 * Bullet Continuous Collision Detection and Physics Library
 * Copyright (c) 2003-2008 Erwin Coumans  http://www.bulletphysics.com/
 *
 * This software is provided 'as-is', without any express or implied warranty.
 * In no event will the authors be held liable for any damages arising from
 * the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would be
 *    appreciated but is not required.
 * 2. Altered source versions must be plainly marked as such, and must not be
 *    misrepresented as being the original software.
 * 3. This notice may not be removed or altered from any source distribution.
 */
// Adapted from BulletXNA 73b2e1b9: namespace isolation, no XNA rendering, one type per file.
#nullable disable
#pragma warning disable CS1591, CS0168, CS0219, CS0414, CS0660, CS0661, CS0659, CS0693, CS8981, CS0108, CS0114
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class SingleSweepCallback : BroadphaseRayCallback
{
	IndexedMatrix m_convexFromTrans;
	IndexedMatrix m_convexToTrans;
	IndexedVector3 m_hitNormal;
	CollisionWorld m_world;
	ConvexResultCallback m_resultCallback;
	float m_allowedCcdPenetration;
	ConvexShape m_castShape;
	public SingleSweepCallback()
	{
	} // for pool

	public SingleSweepCallback(ConvexShape castShape, ref IndexedMatrix convexFromTrans, ref IndexedMatrix convexToTrans, CollisionWorld world, ConvexResultCallback resultCallback, float allowedPenetration)
	{
		Initialize(castShape, ref convexFromTrans, ref convexToTrans, world, resultCallback, allowedPenetration);
	}

	public void Initialize(ConvexShape castShape, ref IndexedMatrix convexFromTrans, ref IndexedMatrix convexToTrans, CollisionWorld world, ConvexResultCallback resultCallback, float allowedPenetration)
	{
		m_convexFromTrans = convexFromTrans;
		m_convexToTrans = convexToTrans;
		m_world = world;
		m_resultCallback = resultCallback;
		m_allowedCcdPenetration = allowedPenetration;
		m_castShape = castShape;
		IndexedVector3 unnormalizedRayDir = (m_convexToTrans._origin - m_convexFromTrans._origin);
		IndexedVector3 rayDir = unnormalizedRayDir;
		rayDir.Normalize();
		m_rayDirectionInverse.X = MathUtil.CompareFloat(rayDir.X, 0.0f) ? float.MaxValue : 1f / rayDir.X;
		m_rayDirectionInverse.Y = MathUtil.CompareFloat(rayDir.Y, 0.0f) ? float.MaxValue : 1f / rayDir.Y;
		m_rayDirectionInverse.Z = MathUtil.CompareFloat(rayDir.Z, 0.0f) ? float.MaxValue : 1f / rayDir.Z;
		m_signs[0] = m_rayDirectionInverse.X < 0.0;
		m_signs[1] = m_rayDirectionInverse.Y < 0.0;
		m_signs[2] = m_rayDirectionInverse.Z < 0.0;
		m_lambda_max = rayDir.Dot(ref unnormalizedRayDir);
	}

	public override bool Process(BroadphaseProxy proxy)
	{
		if (MathUtil.FuzzyZero(m_resultCallback.m_closestHitFraction))
		{
			return false;
		}

		CollisionObject collisionObject = proxy.m_clientObject as CollisionObject;
		//only perform raycast if filterMask matches
		if (m_resultCallback.NeedsCollision(collisionObject.GetBroadphaseHandle()))
		{
			//RigidcollisionObject* collisionObject = ctrl.GetRigidcollisionObject();
			IndexedMatrix temp = collisionObject.GetWorldTransform();
			CollisionWorld.ObjectQuerySingle(m_castShape, ref m_convexFromTrans, ref m_convexToTrans, collisionObject, collisionObject.GetCollisionShape(), ref temp, m_resultCallback, m_allowedCcdPenetration);
		}

		return true;
	}
}