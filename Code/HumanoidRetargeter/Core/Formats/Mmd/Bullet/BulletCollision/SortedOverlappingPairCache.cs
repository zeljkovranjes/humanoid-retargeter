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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class SortedOverlappingPairCache : IOverlappingPairCache
{
	public SortedOverlappingPairCache()
	{
		m_blockedForChanges = false;
		m_hasDeferredRemoval = true;
		m_overlapFilterCallback = null;
		m_ghostPairCallback = null;
		m_overlappingPairArray = new ObjectArray<BroadphasePair>(2);
	}

	//virtual ~btSortedOverlappingPairCache();
	public virtual void Cleanup()
	{
	}

	public virtual void ProcessAllOverlappingPairs(IOverlapCallback callback, IDispatcher dispatcher)
	{
		for (int i = 0; i < m_overlappingPairArray.Count;)
		{
			BroadphasePair pair = m_overlappingPairArray[i];
			if (callback.ProcessOverlap(pair))
			{
				CleanOverlappingPair(pair, dispatcher);
				pair.m_pProxy0 = null;
				pair.m_pProxy1 = null;
				m_overlappingPairArray.RemoveAtQuick(i);
				OverlappingPairCacheGlobals.gOverlappingPairs--;
			}
			else
			{
				i++;
			}
		}
	}

	public Object RemoveOverlappingPair(BroadphaseProxy proxy0, BroadphaseProxy proxy1, IDispatcher dispatcher)
	{
		if (!HasDeferredRemoval())
		{
			BroadphasePair findPair = new BroadphasePair(proxy0, proxy1);
			int findIndex = m_overlappingPairArray.IndexOf(findPair);
			if (findIndex >= 0 && findIndex < m_overlappingPairArray.Count)
			{
				OverlappingPairCacheGlobals.gOverlappingPairs--;
				BroadphasePair pair = m_overlappingPairArray[findIndex];
				Object userData = pair.m_internalInfo1;
				CleanOverlappingPair(pair, dispatcher);
				if (m_ghostPairCallback != null)
				{
					m_ghostPairCallback.RemoveOverlappingPair(proxy0, proxy1, dispatcher);
				}

				//BroadphasePair temp = m_overlappingPairArray[findIndex];
				//m_overlappingPairArray[findIndex] = m_overlappingPairArray[m_overlappingPairArray.Count-1];
				//m_overlappingPairArray[m_overlappingPairArray.Count-1] = temp;
				m_overlappingPairArray.RemoveAtQuick(findIndex);
				return userData;
			}
		}

		return null;
	}

	public void CleanOverlappingPair(BroadphasePair pair, IDispatcher dispatcher)
	{
		if (pair.m_algorithm != null)
		{
			{
				dispatcher.FreeCollisionAlgorithm(pair.m_algorithm);
				pair.m_algorithm = null;
				OverlappingPairCacheGlobals.gRemovePairs--;
			}
		}
	}

	public BroadphasePair AddOverlappingPair(BroadphaseProxy proxy0, BroadphaseProxy proxy1)
	{
		{
		}

		if (!NeedsBroadphaseCollision(proxy0, proxy1))
		{
			return null;
		}

		BroadphasePair pair = new BroadphasePair(proxy0, proxy1);
		m_overlappingPairArray.Add(pair);
		OverlappingPairCacheGlobals.gOverlappingPairs++;
		OverlappingPairCacheGlobals.gAddedPairs++;
		if (m_ghostPairCallback != null)
		{
			m_ghostPairCallback.AddOverlappingPair(proxy0, proxy1);
		}

		return pair;
	}

	public BroadphasePair FindPair(BroadphaseProxy proxy0, BroadphaseProxy proxy1)
	{
		if (!NeedsBroadphaseCollision(proxy0, proxy1))
		{
			return null;
		}

		BroadphasePair tmpPair = new BroadphasePair(proxy0, proxy1);
		int index = m_overlappingPairArray.IndexOf(tmpPair);
		if (index != -1)
		{
			return m_overlappingPairArray[index];
		}

		return null;
	}

	public void CleanProxyFromPairs(BroadphaseProxy proxy, IDispatcher dispatcher)
	{
		CleanPairCallback cleanPairs = new CleanPairCallback(proxy, this, dispatcher);
		ProcessAllOverlappingPairs(cleanPairs, dispatcher);
	}

	public void RemoveOverlappingPairsContainingProxy(BroadphaseProxy proxy, IDispatcher dispatcher)
	{
		RemovePairCallback removeCallback = new RemovePairCallback(proxy);
		ProcessAllOverlappingPairs(removeCallback, dispatcher);
	}

	public bool NeedsBroadphaseCollision(BroadphaseProxy proxy0, BroadphaseProxy proxy1)
	{
		if (m_overlapFilterCallback != null)
		{
			return m_overlapFilterCallback.NeedBroadphaseCollision(proxy0, proxy1);
		}

		bool collides = (proxy0.m_collisionFilterGroup & proxy1.m_collisionFilterMask) != 0;
		collides = collides && ((proxy1.m_collisionFilterGroup & proxy0.m_collisionFilterMask) != 0);
		return collides;
	}

	public ObjectArray<BroadphasePair> GetOverlappingPairArray()
	{
		return m_overlappingPairArray;
	}

	public int GetNumOverlappingPairs()
	{
		return m_overlappingPairArray.Count;
	}

	public IOverlapFilterCallback GetOverlapFilterCallback()
	{
		return m_overlapFilterCallback;
	}

	public void SetOverlapFilterCallback(IOverlapFilterCallback callback)
	{
		m_overlapFilterCallback = callback;
	}

	public virtual bool HasDeferredRemoval()
	{
		return m_hasDeferredRemoval;
	}

	public virtual void SetInternalGhostPairCallback(IOverlappingPairCallback ghostPairCallback)
	{
		m_ghostPairCallback = ghostPairCallback;
	}

	public virtual void SortOverlappingPairs(IDispatcher dispatcher)
	{
	//should already be sorted
	}

	//avoid brute-force finding all the time
	protected ObjectArray<BroadphasePair> m_overlappingPairArray;
	//during the dispatch, check that user doesn't destroy/create proxy
	protected bool m_blockedForChanges;
	protected bool m_hasDeferredRemoval;
	//if set, use the callback instead of the built in filter in needBroadphaseCollision
	protected IOverlapFilterCallback m_overlapFilterCallback;
	protected IOverlappingPairCallback m_ghostPairCallback;
}