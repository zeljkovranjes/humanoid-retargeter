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

internal class CollisionDispatcher : IDispatcher
{
	public CollisionDispatcher(ICollisionConfiguration collisionConfiguration)
	{
		m_collisionConfiguration = collisionConfiguration;
		m_dispatcherFlags = DispatcherFlags.CD_USE_RELATIVE_CONTACT_BREAKING_THRESHOLD;
		SetNearCallback(new DefaultNearCallback());
		const int maxTypes = (int)BroadphaseNativeTypes.MAX_BROADPHASE_COLLISION_TYPES;
		m_doubleDispatch = new CollisionAlgorithmCreateFunc[maxTypes, maxTypes];
		for (int i = 0; i < maxTypes; i++)
		{
			for (int j = 0; j < maxTypes; j++)
			{
				m_doubleDispatch[i, j] = m_collisionConfiguration.GetCollisionAlgorithmCreateFunc((BroadphaseNativeTypes)i, (BroadphaseNativeTypes)j);
				{
				}
			}
		}
	}

	public DispatcherFlags GetDispatcherFlags()
	{
		return m_dispatcherFlags;
	}

	public void SetDispatcherFlags(DispatcherFlags flags)
	{
		m_dispatcherFlags = flags;
	}

	public virtual void Cleanup()
	{
	}

	public virtual PersistentManifold GetNewManifold(CollisionObject b0, CollisionObject b1)
	{
		gNumManifold++;
		CollisionObject body0 = b0;
		CollisionObject body1 = b1;
		//optional relative contact breaking threshold, turned on by default (use setDispatcherFlags to switch off feature for improved performance)
		float contactBreakingThreshold = ((m_dispatcherFlags & DispatcherFlags.CD_USE_RELATIVE_CONTACT_BREAKING_THRESHOLD) > 0) ? Math.Min(body0.GetCollisionShape().GetContactBreakingThreshold(BulletGlobals.gContactBreakingThreshold), body1.GetCollisionShape().GetContactBreakingThreshold(BulletGlobals.gContactBreakingThreshold)) : BulletGlobals.gContactBreakingThreshold;
		float contactProcessingThreshold = Math.Min(body0.GetContactProcessingThreshold(), body1.GetContactProcessingThreshold());
		// nothing in our pool so create a new one and return it.
		// need a way to flush the pool ideally
		PersistentManifold manifold = BulletGlobals.PersistentManifoldPool.Get();
		manifold.Initialise(body0, body1, 0, contactBreakingThreshold, contactProcessingThreshold);
		manifold.m_index1a = m_manifoldsPtr.Count;
		m_manifoldsPtr.Add(manifold);
		return manifold;
	}

	public virtual void ReleaseManifold(PersistentManifold manifold)
	{
		gNumManifold--;
		ClearManifold(manifold);
		int findIndex = manifold.m_index1a;
		//m_manifoldsPtr.Remove(manifold);
		m_manifoldsPtr.RemoveAtQuick(findIndex);
		m_manifoldsPtr[findIndex].m_index1a = findIndex;
		//PersistentManifold swapTemp = m_manifoldsPtr[findIndex];
		//m_manifoldsPtr[findIndex] = m_manifoldsPtr[m_manifoldsPtr.Count - 1];
		//m_manifoldsPtr[m_manifoldsPtr.Count - 1] = swapTemp;
		//m_manifoldsPtr[findIndex].m_index1a = findIndex;
		//m_manifoldsPtr.RemoveAt(m_manifoldsPtr.Count - 1);
		// and return it to free list.
		BulletGlobals.PersistentManifoldPool.Free(manifold);
	}

	public virtual void ClearManifold(PersistentManifold manifold)
	{
		manifold.ClearManifold();
	}

	public CollisionAlgorithm FindAlgorithm(CollisionObject body0, CollisionObject body1)
	{
		return FindAlgorithm(body0, body1, null);
	}

	public CollisionAlgorithm FindAlgorithm(CollisionObject body0, CollisionObject body1, PersistentManifold sharedManifold)
	{
		CollisionAlgorithmConstructionInfo ci = new CollisionAlgorithmConstructionInfo(this, -1);
		ci.SetManifold(sharedManifold);
		int index1 = (int)body0.GetCollisionShape().GetShapeType();
		int index2 = (int)body1.GetCollisionShape().GetShapeType();
		return m_doubleDispatch[index1, index2].CreateCollisionAlgorithm(ci, body0, body1);
	}

	public virtual bool NeedsCollision(CollisionObject body0, CollisionObject body1)
	{
		{
		}

		{
		}

		bool needsCollision = true;
		if ((!body0.IsActive()) && (!body1.IsActive()))
		{
			needsCollision = false;
		}
		else if (!body0.CheckCollideWith(body1))
		{
			needsCollision = false;
		}

		return needsCollision;
	}

	public virtual bool NeedsResponse(CollisionObject body0, CollisionObject body1)
	{
		//here you can do filtering
		bool hasResponse = (body0.HasContactResponse() && body1.HasContactResponse());
		//no response between two static/kinematic bodies:
		hasResponse = hasResponse && ((!body0.IsStaticOrKinematicObject()) || (!body1.IsStaticOrKinematicObject()));
		return hasResponse;
	}

	public virtual void DispatchAllCollisionPairs(IOverlappingPairCache pairCache, DispatcherInfo dispatchInfo, IDispatcher dispatcher)
	{
		m_collisionCallback.Initialize(dispatchInfo, this);
		pairCache.ProcessAllOverlappingPairs(m_collisionCallback, dispatcher);
		m_collisionCallback.cleanup();
	}

	public void SetNearCallback(INearCallback nearCallback)
	{
		m_nearCallback = nearCallback;
	}

	public INearCallback GetNearCallback()
	{
		return m_nearCallback;
	}

	public void RegisterCollisionCreateFunc(int proxyType0, int proxyType1, CollisionAlgorithmCreateFunc createFunc)
	{
		m_doubleDispatch[proxyType0, proxyType1] = createFunc;
	}

	public int GetNumManifolds()
	{
		return m_manifoldsPtr.Count;
	}

	public PersistentManifold GetManifoldByIndexInternal(int index)
	{
		return m_manifoldsPtr[index];
	}

	//by default, Bullet will use this near callback
	//public static void  defaultNearCallback(BroadphasePair collisionPair, CollisionDispatcher dispatcher, DispatcherInfo dispatchInfo);
	public virtual Object AllocateCollisionAlgorithm(int size)
	{
		return null;
	}

	public virtual void FreeCollisionAlgorithm(CollisionAlgorithm collisionAlgorithm)
	{
		if (collisionAlgorithm != null)
		{
			collisionAlgorithm.Cleanup();
		}
	}

	public ICollisionConfiguration GetCollisionConfiguration()
	{
		return m_collisionConfiguration;
	}

	public void SetCollisionConfiguration(ICollisionConfiguration config)
	{
		m_collisionConfiguration = config;
	}

	public virtual PersistentManifoldArray GetInternalManifoldPointer()
	{
		return m_manifoldsPtr;
	}

	public ManifoldResult GetNewManifoldResult(CollisionObject o1, CollisionObject o2)
	{
		ManifoldResult manifoldResult = null;
		manifoldResult = BulletGlobals.ManifoldResultPool.Get();
		manifoldResult.Initialise(o1, o2);
		return manifoldResult;
	}

	public void FreeManifoldResult(ManifoldResult result)
	{
		BulletGlobals.ManifoldResultPool.Free(result);
	}

	private PersistentManifoldArray m_manifoldsPtr = new PersistentManifoldArray();
	private DispatcherFlags m_dispatcherFlags;
	//private bool m_useIslands;
	//private bool m_staticWarningReported;
	private ManifoldResult m_defaultManifoldResult;
	private INearCallback m_nearCallback;
	private ICollisionConfiguration m_collisionConfiguration;
	private CollisionPairCallback m_collisionCallback = new CollisionPairCallback(null, null);
	public static int gNumManifold = 0;
	//btPoolAllocator*	m_collisionAlgorithmPoolAllocator;
	//btPoolAllocator*	m_persistentManifoldPoolAllocator;
	CollisionAlgorithmCreateFunc[, ] m_doubleDispatch;
}