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

internal class SingleRayCallback : BroadphaseRayCallback, IDisposable
{
	public SingleRayCallback()
	{
	}

	public SingleRayCallback(ref IndexedVector3 rayFromWorld, ref IndexedVector3 rayToWorld, CollisionWorld world, RayResultCallback resultCallback)
	{
		Initialize(ref rayFromWorld, ref rayToWorld, world, resultCallback);
	}

	public void Initialize(ref IndexedVector3 rayFromWorld, ref IndexedVector3 rayToWorld, CollisionWorld world, RayResultCallback resultCallback)
	{
		m_rayFromWorld = rayFromWorld;
		m_rayToWorld = rayToWorld;
		m_world = world;
		m_resultCallback = resultCallback;
		m_rayFromTrans = IndexedMatrix.CreateTranslation(m_rayFromWorld);
		m_rayToTrans = IndexedMatrix.CreateTranslation(m_rayToWorld);
		IndexedVector3 rayDir = (rayToWorld - rayFromWorld);
		rayDir.Normalize();
		m_rayDirectionInverse.X = MathUtil.FuzzyZero(rayDir.X) ? float.MaxValue : 1f / rayDir.X;
		m_rayDirectionInverse.Y = MathUtil.FuzzyZero(rayDir.Y) ? float.MaxValue : 1f / rayDir.Y;
		m_rayDirectionInverse.Z = MathUtil.FuzzyZero(rayDir.Z) ? float.MaxValue : 1f / rayDir.Z;
		m_signs[0] = m_rayDirectionInverse.X < 0.0f;
		m_signs[1] = m_rayDirectionInverse.Y < 0.0f;
		m_signs[2] = m_rayDirectionInverse.Z < 0.0f;
		m_lambda_max = rayDir.Dot(m_rayToWorld - m_rayFromWorld);
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
			//IndexedVector3 collisionObjectAabbMin,collisionObjectAabbMax;
			//#if 0
			//#ifdef RECALCULATE_AABB
			//            IndexedVector3 collisionObjectAabbMin,collisionObjectAabbMax;
			//            collisionObject.getCollisionShape().getAabb(collisionObject.getWorldTransform(),collisionObjectAabbMin,collisionObjectAabbMax);
			//#else
			//getBroadphase().getAabb(collisionObject.getBroadphaseHandle(),collisionObjectAabbMin,collisionObjectAabbMax);
			IndexedVector3 collisionObjectAabbMin = collisionObject.GetBroadphaseHandle().m_aabbMin;
			IndexedVector3 collisionObjectAabbMax = collisionObject.GetBroadphaseHandle().m_aabbMax;
			//#endif
			//#endif
			//float hitLambda = m_resultCallback.m_closestHitFraction;
			//culling already done by broadphase
			//if (btRayAabb(m_rayFromWorld,m_rayToWorld,collisionObjectAabbMin,collisionObjectAabbMax,hitLambda,m_hitNormal))
			{
				IndexedMatrix trans = collisionObject.GetWorldTransform();
				CollisionWorld.RayTestSingle(ref m_rayFromTrans, ref m_rayToTrans, collisionObject, collisionObject.GetCollisionShape(), ref trans, m_resultCallback);
			}
		}

		return true;
	}

	public override void Cleanup()
	{
		base.Cleanup();
	}

	public void Dispose()
	{
		BulletGlobals.SingleRayCallbackPool.Free(this);
	}

	public IndexedVector3 m_rayFromWorld;
	public IndexedVector3 m_rayToWorld;
	public IndexedMatrix m_rayFromTrans;
	public IndexedMatrix m_rayToTrans;
	public IndexedVector3 m_hitNormal;
	public CollisionWorld m_world;
	public RayResultCallback m_resultCallback;
}