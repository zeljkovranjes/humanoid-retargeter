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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal class ClosestNotMeConvexResultCallback : ClosestConvexResultCallback, IDisposable
{
	public CollisionObject m_me;
	public float m_allowedPenetration;
	public IOverlappingPairCache m_pairCache;
	public IDispatcher m_dispatcher;
	public ClosestNotMeConvexResultCallback()
	{
	} // for pool

	public ClosestNotMeConvexResultCallback(CollisionObject me, IndexedVector3 fromA, IndexedVector3 toA, IOverlappingPairCache pairCache, IDispatcher dispatcher) : this(me, ref fromA, ref toA, pairCache, dispatcher)
	{
	}

	public virtual void Initialize(CollisionObject me, ref IndexedVector3 fromA, ref IndexedVector3 toA, IOverlappingPairCache pairCache, IDispatcher dispatcher)
	{
		base.Initialize(ref fromA, ref toA);
		m_allowedPenetration = 0.0f;
		m_me = me;
		m_pairCache = pairCache;
		m_dispatcher = dispatcher;
	}

	public virtual void Initialize(CollisionObject me, IndexedVector3 fromA, IndexedVector3 toA, IOverlappingPairCache pairCache, IDispatcher dispatcher)
	{
		base.Initialize(ref fromA, ref toA);
		m_allowedPenetration = 0.0f;
		m_me = me;
		m_pairCache = pairCache;
		m_dispatcher = dispatcher;
	}

	public ClosestNotMeConvexResultCallback(CollisionObject me, ref IndexedVector3 fromA, ref IndexedVector3 toA, IOverlappingPairCache pairCache, IDispatcher dispatcher) : base(ref fromA, ref toA)
	{
		m_allowedPenetration = 0.0f;
		m_me = me;
		m_pairCache = pairCache;
		m_dispatcher = dispatcher;
	}

	public override float AddSingleResult(ref LocalConvexResult convexResult, bool normalInWorldSpace)
	{
		if (convexResult.m_hitCollisionObject == m_me)
			return 1.0f;
		//ignore result if there is no contact response
		if (!convexResult.m_hitCollisionObject.HasContactResponse())
			return 1.0f;
		IndexedVector3 linVelA, linVelB;
		linVelA = m_convexToWorld - m_convexFromWorld;
		linVelB = IndexedVector3.Zero; //toB._origin-fromB._origin;
		IndexedVector3 relativeVelocity = (linVelA - linVelB);
		//don't report time of impact for motion away from the contact normal (or causes minor penetration)
		if (IndexedVector3.Dot(convexResult.m_hitNormalLocal, relativeVelocity) >= -m_allowedPenetration)
			return 1f;
		return base.AddSingleResult(ref convexResult, normalInWorldSpace);
	}

	public override bool NeedsCollision(BroadphaseProxy proxy0)
	{
		//don't collide with itself
		if (proxy0.m_clientObject == m_me)
			return false;
		if (!base.NeedsCollision(proxy0))
			return false;
		CollisionObject otherObj = proxy0.m_clientObject as CollisionObject;
		//call needsResponse, see http://code.google.com/p/bullet/issues/detail?id=179
		if (m_dispatcher.NeedsResponse(m_me, otherObj))
		{
			return true;
		}

		return false;
	}

	public void Dispose()
	{
		BulletGlobals.ClosestNotMeConvexResultCallbackPool.Free(this);
	}
}