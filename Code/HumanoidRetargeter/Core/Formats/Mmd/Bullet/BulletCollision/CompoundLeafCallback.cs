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
using System.Collections.Generic;
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class CompoundLeafCallback : DefaultCollide, IDisposable
{
	public CollisionObject m_compoundColObj;
	public CollisionObject m_otherObj;
	public IDispatcher m_dispatcher;
	public DispatcherInfo m_dispatchInfo;
	public ManifoldResult m_resultOut;
	public IList<CollisionAlgorithm> m_childCollisionAlgorithms;
	public PersistentManifold m_sharedManifold;
	public CompoundCollisionAlgorithm m_parent;
	public CompoundLeafCallback()
	{
	} // for pool

	public CompoundLeafCallback(CollisionObject compoundObj, CollisionObject otherObj, IDispatcher dispatcher, DispatcherInfo dispatchInfo, ManifoldResult resultOut, CompoundCollisionAlgorithm parent, IList<CollisionAlgorithm> childCollisionAlgorithms, PersistentManifold sharedManifold)
	{
		m_compoundColObj = compoundObj;
		m_otherObj = otherObj;
		m_dispatcher = dispatcher;
		m_dispatchInfo = dispatchInfo;
		m_resultOut = resultOut;
		m_childCollisionAlgorithms = childCollisionAlgorithms;
		m_sharedManifold = sharedManifold;
		m_parent = parent;
	}

	public void Initialize(CollisionObject compoundObj, CollisionObject otherObj, IDispatcher dispatcher, DispatcherInfo dispatchInfo, ManifoldResult resultOut, CompoundCollisionAlgorithm parent, IList<CollisionAlgorithm> childCollisionAlgorithms, PersistentManifold sharedManifold)
	{
		m_compoundColObj = compoundObj;
		m_otherObj = otherObj;
		m_dispatcher = dispatcher;
		m_dispatchInfo = dispatchInfo;
		m_resultOut = resultOut;
		m_childCollisionAlgorithms = childCollisionAlgorithms;
		m_sharedManifold = sharedManifold;
		m_parent = parent;
	}

	public void ProcessChildShape(CollisionShape childShape, int index)
	{
		{
		}

		CompoundShape compoundShape = (CompoundShape)(m_compoundColObj.GetCollisionShape());
		{
		}

		//backup
		IndexedMatrix orgTrans = m_compoundColObj.GetWorldTransform();
		IndexedMatrix orgInterpolationTrans = m_compoundColObj.GetInterpolationWorldTransform();
		IndexedMatrix childTrans = compoundShape.GetChildTransform(index);
		IndexedMatrix newChildWorldTrans = orgTrans * childTrans;
		//perform an AABB check first
		IndexedVector3 aabbMin0;
		IndexedVector3 aabbMax0;
		IndexedVector3 aabbMin1;
		IndexedVector3 aabbMax1;
		childShape.GetAabb(ref newChildWorldTrans, out aabbMin0, out aabbMax0);
		m_otherObj.GetCollisionShape().GetAabb(m_otherObj.GetWorldTransform(), out aabbMin1, out aabbMax1);
		if (AabbUtil2.TestAabbAgainstAabb2(ref aabbMin0, ref aabbMax0, ref aabbMin1, ref aabbMax1))
		{
			m_compoundColObj.SetWorldTransform(ref newChildWorldTrans);
			m_compoundColObj.SetInterpolationWorldTransform(ref newChildWorldTrans);
			//the contactpoint is still projected back using the original inverted worldtrans
			CollisionShape tmpShape = m_compoundColObj.GetCollisionShape();
			m_compoundColObj.InternalSetTemporaryCollisionShape(childShape);
			if (m_childCollisionAlgorithms[index] == null)
			{
				m_childCollisionAlgorithms[index] = m_dispatcher.FindAlgorithm(m_compoundColObj, m_otherObj, m_sharedManifold);
				if (m_childCollisionAlgorithms[index] == m_parent)
				{
					int ibreak = 0;
				}
			}

			if (m_resultOut.GetBody0Internal() == m_compoundColObj)
			{
				m_resultOut.SetShapeIdentifiersA(-1, index);
			}
			else
			{
				m_resultOut.SetShapeIdentifiersB(-1, index);
			}

			m_childCollisionAlgorithms[index].ProcessCollision(m_compoundColObj, m_otherObj, m_dispatchInfo, m_resultOut);
			if (m_dispatchInfo.getDebugDraw() != null && (((m_dispatchInfo.getDebugDraw().GetDebugMode() & DebugDrawModes.DBG_DrawAabb)) != 0))
			{
				IndexedVector3 worldAabbMin = IndexedVector3.Zero, worldAabbMax = IndexedVector3.Zero;
				m_dispatchInfo.getDebugDraw().DrawAabb(aabbMin0, aabbMax0, new IndexedVector3(1, 1, 1));
				m_dispatchInfo.getDebugDraw().DrawAabb(aabbMin1, aabbMax1, new IndexedVector3(1, 1, 1));
			}

			//revert back transform
			m_compoundColObj.InternalSetTemporaryCollisionShape(tmpShape);
			m_compoundColObj.SetWorldTransform(ref orgTrans);
			m_compoundColObj.SetInterpolationWorldTransform(ref orgInterpolationTrans);
		}
	}

	public override void Process(DbvtNode leaf)
	{
		int index = leaf.dataAsInt;
		CompoundShape compoundShape = (CompoundShape)(m_compoundColObj.GetCollisionShape());
		CollisionShape childShape = compoundShape.GetChildShape(index);
		if (m_dispatchInfo.getDebugDraw() != null && (((m_dispatchInfo.getDebugDraw().GetDebugMode() & DebugDrawModes.DBG_DrawAabb)) != 0))
		{
			IndexedVector3 worldAabbMin;
			IndexedVector3 worldAabbMax;
			IndexedMatrix orgTrans = m_compoundColObj.GetWorldTransform();
			AabbUtil2.TransformAabb(leaf.volume.Mins(), leaf.volume.Maxs(), 0f, ref orgTrans, out worldAabbMin, out worldAabbMax);
			m_dispatchInfo.getDebugDraw().DrawAabb(worldAabbMin, worldAabbMax, new IndexedVector3(1, 0, 0));
		}

		ProcessChildShape(childShape, index);
	}

	public void Dispose()
	{
		BulletGlobals.CompoundLeafCallbackPool.Free(this);
	}
}