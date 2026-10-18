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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class ConvexTriangleCallback : ITriangleCallback
{
	private CollisionObject m_convexBody;
	private CollisionObject m_triBody;
	private IndexedVector3 m_aabbMin;
	private IndexedVector3 m_aabbMax;
	private ManifoldResult m_resultOut;
	private IDispatcher m_dispatcher;
	private DispatcherInfo m_dispatchInfoPtr;
	private float m_collisionMarginTriangle;
	public int m_triangleCount;
	public PersistentManifold m_manifoldPtr;
	public virtual bool graphics()
	{
		return false;
	}

	public ConvexTriangleCallback(IDispatcher dispatcher, CollisionObject body0, CollisionObject body1, bool isSwapped)
	{
		m_dispatcher = dispatcher;
		m_convexBody = isSwapped ? body1 : body0;
		m_triBody = isSwapped ? body0 : body1;
		m_manifoldPtr = m_dispatcher.GetNewManifold(m_convexBody, m_triBody);
		ClearCache();
	}

	public void Initialize(IDispatcher dispatcher, CollisionObject body0, CollisionObject body1, bool isSwapped)
	{
		m_dispatcher = dispatcher;
		m_convexBody = isSwapped ? body1 : body0;
		m_triBody = isSwapped ? body0 : body1;
		m_manifoldPtr = m_dispatcher.GetNewManifold(m_convexBody, m_triBody);
		ClearCache();
	}

	public virtual void Cleanup()
	{
		ClearCache();
		m_dispatcher.ReleaseManifold(m_manifoldPtr);
	}

	public void SetTimeStepAndCounters(float collisionMarginTriangle, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		m_dispatchInfoPtr = dispatchInfo;
		m_collisionMarginTriangle = collisionMarginTriangle;
		m_resultOut = resultOut;
		//recalc aabbs
		//IndexedMatrix convexInTriangleSpace = MathUtil.bulletMatrixMultiply(IndexedMatrix.Invert(m_triBody.getWorldTransform()) , m_convexBody.getWorldTransform());
		IndexedMatrix convexInTriangleSpace = m_triBody.GetWorldTransform().Inverse() * m_convexBody.GetWorldTransform();
		CollisionShape convexShape = m_convexBody.GetCollisionShape();
		convexShape.GetAabb(ref convexInTriangleSpace, out m_aabbMin, out m_aabbMax);
		float extraMargin = collisionMarginTriangle;
		IndexedVector3 extra = new IndexedVector3(extraMargin);
		m_aabbMax += extra;
		m_aabbMin -= extra;
	}

	public virtual void ProcessTriangle(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		if (!AabbUtil2.TestTriangleAgainstAabb2(triangle, ref m_aabbMin, ref m_aabbMax))
		{
			return;
		}

		//aabb filter is already applied!
		CollisionAlgorithmConstructionInfo ci = new CollisionAlgorithmConstructionInfo();
		ci.SetDispatcher(m_dispatcher);
		CollisionObject ob = m_triBody as CollisionObject;
		if (m_convexBody.GetCollisionShape().IsConvex())
		{
			using (TriangleShape tm = BulletGlobals.TriangleShapePool.Get())
			{
				tm.Initialize(ref triangle[0], ref triangle[1], ref triangle[2]);
				tm.SetMargin(m_collisionMarginTriangle);
				CollisionShape tmpShape = ob.GetCollisionShape();
				ob.InternalSetTemporaryCollisionShape(tm);
				CollisionAlgorithm colAlgo = ci.GetDispatcher().FindAlgorithm(m_convexBody, m_triBody, m_manifoldPtr);
				//		btConvexConvexAlgorithm cvxcvxalgo(m_manifoldPtr,ci,m_convexBody,m_triBody);
				if (m_resultOut.GetBody0Internal() == m_triBody)
				{
					m_resultOut.SetShapeIdentifiersA(partId, triangleIndex);
				}
				else
				{
					m_resultOut.SetShapeIdentifiersB(partId, triangleIndex);
				}

				colAlgo.ProcessCollision(m_convexBody, m_triBody, m_dispatchInfoPtr, m_resultOut);
				ci.GetDispatcher().FreeCollisionAlgorithm(colAlgo);
				colAlgo = null;
				ob.InternalSetTemporaryCollisionShape(tmpShape);
			}
		}
	}

	public void ClearCache()
	{
		m_dispatcher.ClearManifold(m_manifoldPtr);
	}

	public IndexedVector3 GetAabbMin()
	{
		return m_aabbMin;
	}

	public IndexedVector3 GetAabbMax()
	{
		return m_aabbMax;
	}
}