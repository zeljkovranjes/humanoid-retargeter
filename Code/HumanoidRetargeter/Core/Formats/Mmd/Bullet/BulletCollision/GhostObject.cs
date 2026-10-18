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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class GhostObject : CollisionObject
{
	public GhostObject()
	{
		SetInternalType(CollisionObjectTypes.CO_GHOST_OBJECT);
	}

	public override void Cleanup()
	{
		{
		}
	}

	public void ConvexSweepTest(ConvexShape castShape, ref IndexedMatrix convexFromWorld, ref IndexedMatrix convexToWorld, ConvexResultCallback resultCallback, float allowedCcdPenetration)
	{
		IndexedMatrix convexFromTrans = convexFromWorld;
		IndexedMatrix convexToTrans = convexToWorld;
		IndexedVector3 castShapeAabbMin;
		IndexedVector3 castShapeAabbMax;
		/* Compute AABB that encompasses angular movement */
		IndexedVector3 linVel, angVel;
		TransformUtil.CalculateVelocity(ref convexFromTrans, ref convexToTrans, 1.0f, out linVel, out angVel);
		// FIXME MAN check this - should be a get/set rotation call, basis copy like this may break with scale?
		IndexedMatrix R = IndexedMatrix.Identity;
		R.SetRotation(convexFromTrans.GetRotation());
		castShape.CalculateTemporalAabb(ref R, ref linVel, ref angVel, 1.0f, out castShapeAabbMin, out castShapeAabbMax);
		// do a ray-shape query using convexCaster (CCD)
		for (int i = 0; i < m_overlappingObjects.Count; i++)
		{
			CollisionObject collisionObject = m_overlappingObjects[i];
			//only perform raycast if filterMask matches
			if (resultCallback.NeedsCollision(collisionObject.GetBroadphaseHandle()))
			{
				//RigidcollisionObject* collisionObject = ctrl->GetRigidcollisionObject();
				IndexedVector3 collisionObjectAabbMin;
				IndexedVector3 collisionObjectAabbMax;
				IndexedMatrix t = collisionObject.GetWorldTransform();
				collisionObject.GetCollisionShape().GetAabb(ref t, out collisionObjectAabbMin, out collisionObjectAabbMax);
				AabbUtil2.AabbExpand(ref collisionObjectAabbMin, ref collisionObjectAabbMax, ref castShapeAabbMin, ref castShapeAabbMax);
				float hitLambda = 1f; //could use resultCallback.m_closestHitFraction, but needs testing
				IndexedVector3 hitNormal;
				if (AabbUtil2.RayAabb(convexFromWorld._origin, convexToWorld._origin, ref collisionObjectAabbMin, ref collisionObjectAabbMax, ref hitLambda, out hitNormal))
				{
					IndexedMatrix wt = collisionObject.GetWorldTransform();
					CollisionWorld.ObjectQuerySingle(castShape, ref convexFromTrans, ref convexToTrans, collisionObject, collisionObject.GetCollisionShape(), ref wt, resultCallback, allowedCcdPenetration);
				}
			}
		}
	}

	public void RayTest(ref IndexedVector3 rayFromWorld, ref IndexedVector3 rayToWorld, RayResultCallback resultCallback)
	{
		int ibreak = 0;
	}

	public virtual void AddOverlappingObjectInternal(BroadphaseProxy otherProxy, BroadphaseProxy thisProxy)
	{
		CollisionObject otherObject = otherProxy.m_clientObject as CollisionObject;
		{
		}

		if (!m_overlappingObjects.Contains(otherObject))
		{
			//not found
			m_overlappingObjects.Add(otherObject);
		}
	}

	public virtual void RemoveOverlappingObjectInternal(BroadphaseProxy otherProxy, IDispatcher dispatcher, BroadphaseProxy thisProxy)
	{
		CollisionObject otherObject = otherProxy.m_clientObject as CollisionObject;
		{
		}

		{
			m_overlappingObjects.RemoveQuick(otherObject);
		}
	}

	public int GetNumOverlappingObjects()
	{
		return m_overlappingObjects.Count;
	}

	public CollisionObject GetOverlappingObject(int index)
	{
		return m_overlappingObjects[index];
	}

	public ObjectArray<CollisionObject> GetOverlappingPairs()
	{
		return m_overlappingObjects;
	}

	//
	// internal cast
	//
	public static GhostObject Upcast(CollisionObject colObj)
	{
		if (colObj.GetInternalType() == CollisionObjectTypes.CO_GHOST_OBJECT)
		{
			return (GhostObject)colObj;
		}

		return null;
	}

	protected ObjectArray<CollisionObject> m_overlappingObjects = new ObjectArray<CollisionObject>();
}