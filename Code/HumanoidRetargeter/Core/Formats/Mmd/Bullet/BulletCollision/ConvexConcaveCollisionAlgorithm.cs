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

internal class ConvexConcaveCollisionAlgorithm : ActivatingCollisionAlgorithm
{
	public ConvexConcaveCollisionAlgorithm()
	{
	} // for pool

	public ConvexConcaveCollisionAlgorithm(CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, bool isSwapped) : base(ci, body0, body1)
	{
		m_isSwapped = isSwapped;
		m_convexTriangleCallback = new ConvexTriangleCallback(m_dispatcher, body0, body1, isSwapped);
	}

	public void Inititialize(CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, bool isSwapped)
	{
		base.Initialize(ci, body0, body1);
		m_isSwapped = isSwapped;
		if (m_convexTriangleCallback == null)
		{
			m_convexTriangleCallback = new ConvexTriangleCallback(m_dispatcher, body0, body1, isSwapped);
		}
		else
		{
			m_convexTriangleCallback.Initialize(m_dispatcher, body0, body1, isSwapped);
		}
	}

	public override void Cleanup()
	{
		// empty on purpose...
		base.Cleanup();
		if (m_convexTriangleCallback != null)
		{
			m_convexTriangleCallback.Cleanup();
		// pool means we don't clear this
		//m_convexTriangleCallback = null;
		}

		BulletGlobals.ConvexConcaveCollisionAlgorithmPool.Free(this);
	}

	//public override void processCollision (CollisionObject body0,CollisionObject body1,DispatcherInfo dispatchInfo,ManifoldResult resultOut)
	//{
	//    CollisionObject convexBody = m_isSwapped ? body1 : body0;
	//    CollisionObject triBody = m_isSwapped ? body0 : body1;
	//    if (triBody.getCollisionShape().isConcave())
	//    {
	//        CollisionObject triOb = triBody;
	//        ConcaveShape concaveShape = (ConcaveShape)(triOb.getCollisionShape());
	//        if (convexBody.getCollisionShape().isConvex())
	//        {
	//            float collisionMarginTriangle = concaveShape.getMargin();
	//            resultOut.setPersistentManifold(m_convexTriangleCallback.m_manifoldPtr);
	//            m_convexTriangleCallback.setTimeStepAndCounters(collisionMarginTriangle, dispatchInfo, resultOut);
	//            //Disable persistency. previously, some older algorithm calculated all contacts in one go, so you can clear it here.
	//            //m_dispatcher.clearManifold(m_convexTriangleCallback.m_manifoldPtr);
	//            m_convexTriangleCallback.m_manifoldPtr.setBodies(convexBody, triBody);
	//            IndexedVector3 min = m_convexTriangleCallback.getAabbMin();
	//            IndexedVector3 max = m_convexTriangleCallback.getAabbMax();
	//            concaveShape.processAllTriangles(m_convexTriangleCallback, ref min,ref max );
	//            resultOut.refreshContactPoints();
	//        }
	//    }
	//}
	//public override float calculateTimeOfImpact(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	//{
	//    CollisionObject convexbody = m_isSwapped ? body1 : body0;
	//    CollisionObject triBody = m_isSwapped ? body0 : body1;
	//    //quick approximation using raycast, todo: hook up to the continuous collision detection (one of the btConvexCast)
	//    //only perform CCD above a certain threshold, this prevents blocking on the long run
	//    //because object in a blocked ccd state (hitfraction<1) get their linear velocity halved each frame...
	//    float squareMot0 = (convexbody.getInterpolationWorldTransform()._origin - convexbody.getWorldTransform()._origin).LengthSquared();
	//    if (squareMot0 < convexbody.getCcdSquareMotionThreshold())
	//    {
	//        return 1f;
	//    }
	//    //const IndexedVector3& from = convexbody.m_worldTransform._origin;
	//    //IndexedVector3 to = convexbody.m_interpolationWorldTransform._origin;
	//    //todo: only do if the motion exceeds the 'radius'
	//    //IndexedMatrix triInv = IndexedMatrix.Invert(triBody.getWorldTransform());
	//    //IndexedMatrix convexFromLocal = MathUtil.bulletMatrixMultiply(triInv , convexbody.getWorldTransform());
	//    //IndexedMatrix convexToLocal = MathUtil.bulletMatrixMultiply(triInv , convexbody.getInterpolationWorldTransform());
	//    IndexedMatrix triInv = IndexedMatrix.Invert(triBody.getWorldTransform());
	//    IndexedMatrix convexFromLocal = MathUtil.inverseTimes(triBody.getWorldTransform(), convexbody.getWorldTransform());
	//    IndexedMatrix convexToLocal = MathUtil.inverseTimes(triBody.getWorldTransform(), convexbody.getInterpolationWorldTransform());
	//    if (triBody.getCollisionShape().isConcave())
	//    {
	//        IndexedVector3 rayAabbMin = convexFromLocal._origin;
	//        MathUtil.vectorMin(convexToLocal._origin, ref rayAabbMin);
	//        IndexedVector3 rayAabbMax = convexFromLocal._origin;
	//        MathUtil.vectorMax(convexToLocal._origin,ref rayAabbMax);
	//        float ccdRadius0 = convexbody.getCcdSweptSphereRadius();
	//        rayAabbMin -= new IndexedVector3(ccdRadius0,ccdRadius0,ccdRadius0);
	//        rayAabbMax += new IndexedVector3(ccdRadius0,ccdRadius0,ccdRadius0);
	//        float curHitFraction = 1.0f; //is this available?
	//        LocalTriangleSphereCastCallback raycastCallback = new LocalTriangleSphereCastCallback(ref convexFromLocal, ref convexToLocal,
	//            convexbody.getCcdSweptSphereRadius(),curHitFraction);
	//        raycastCallback.m_hitFraction = convexbody.getHitFraction();
	//        CollisionObject concavebody = triBody;
	//        ConcaveShape triangleMesh = (ConcaveShape) concavebody.getCollisionShape();
	//        if (triangleMesh != null)
	//        {
	//            triangleMesh.processAllTriangles(raycastCallback,ref rayAabbMin,ref rayAabbMax);
	//        }
	//        if (raycastCallback.m_hitFraction < convexbody.getHitFraction())
	//        {
	//            convexbody.setHitFraction( raycastCallback.m_hitFraction);
	//            float result = raycastCallback.m_hitFraction;
	//            raycastCallback.cleanup();
	//            return result;
	//        }
	//        raycastCallback.cleanup();
	//    }
	//    return 1f;
	//}
	public override void ProcessCollision(CollisionObject bodyA, CollisionObject bodyB, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		//fixme
		CollisionObject convexBody = m_isSwapped ? bodyB : bodyA;
		CollisionObject triBody = m_isSwapped ? bodyA : bodyB;
		if (triBody.GetCollisionShape().IsConcave())
		{
			CollisionObject triOb = triBody;
			ConcaveShape concaveShape = triOb.GetCollisionShape() as ConcaveShape;
			if (convexBody.GetCollisionShape().IsConvex())
			{
				float collisionMarginTriangle = concaveShape.GetMargin();
				resultOut.SetPersistentManifold(m_convexTriangleCallback.m_manifoldPtr);
				m_convexTriangleCallback.SetTimeStepAndCounters(collisionMarginTriangle, dispatchInfo, resultOut);
				//Disable persistency. previously, some older algorithm calculated all contacts in one go, so you can clear it here.
				//m_dispatcher->clearManifold(m_btConvexTriangleCallback.m_manifoldPtr);
				m_convexTriangleCallback.m_manifoldPtr.SetBodies(convexBody, triBody);
				IndexedVector3 min = m_convexTriangleCallback.GetAabbMin();
				IndexedVector3 max = m_convexTriangleCallback.GetAabbMax();
				concaveShape.ProcessAllTriangles(m_convexTriangleCallback, ref min, ref max);
				resultOut.RefreshContactPoints();
			}
		}
	}

	public override float CalculateTimeOfImpact(CollisionObject bodyA, CollisionObject bodyB, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		CollisionObject convexbody = m_isSwapped ? bodyB : bodyA;
		CollisionObject triBody = m_isSwapped ? bodyA : bodyB;
		//quick approximation using raycast, todo: hook up to the continuous collision detection (one of the btConvexCast)
		//only perform CCD above a certain threshold, this prevents blocking on the long run
		//because object in a blocked ccd state (hitfraction<1) get their linear velocity halved each frame...
		float squareMot0 = (convexbody.GetInterpolationWorldTransform()._origin - convexbody.GetWorldTransform()._origin).LengthSquared();
		if (squareMot0 < convexbody.GetCcdSquareMotionThreshold())
		{
			return 1;
		}

		//IndexedMatrix triInv = MathHelper.InvertMatrix(triBody.getWorldTransform());
		IndexedMatrix triInv = triBody.GetWorldTransform().Inverse();
		IndexedMatrix convexFromLocal = triInv * convexbody.GetWorldTransform();
		IndexedMatrix convexToLocal = triInv * convexbody.GetInterpolationWorldTransform();
		if (triBody.GetCollisionShape().IsConcave())
		{
			IndexedVector3 rayAabbMin = convexFromLocal._origin;
			MathUtil.VectorMin(convexToLocal._origin, ref rayAabbMin);
			IndexedVector3 rayAabbMax = convexFromLocal._origin;
			MathUtil.VectorMax(convexToLocal._origin, ref rayAabbMax);
			IndexedVector3 ccdRadius0 = new IndexedVector3(convexbody.GetCcdSweptSphereRadius());
			rayAabbMin -= ccdRadius0;
			rayAabbMax += ccdRadius0;
			float curHitFraction = 1f; //is this available?
			using (LocalTriangleSphereCastCallback raycastCallback = BulletGlobals.LocalTriangleSphereCastCallbackPool.Get())
			{
				raycastCallback.Initialize(ref convexFromLocal, ref convexToLocal, convexbody.GetCcdSweptSphereRadius(), curHitFraction);
				raycastCallback.m_hitFraction = convexbody.GetHitFraction();
				CollisionObject concavebody = triBody;
				ConcaveShape triangleMesh = concavebody.GetCollisionShape() as ConcaveShape;
				if (triangleMesh != null)
				{
					triangleMesh.ProcessAllTriangles(raycastCallback, ref rayAabbMin, ref rayAabbMax);
				}

				if (raycastCallback.m_hitFraction < convexbody.GetHitFraction())
				{
					convexbody.SetHitFraction(raycastCallback.m_hitFraction);
					return raycastCallback.m_hitFraction;
				}
			}
		}

		return 1;
	}

	public override void GetAllContactManifolds(PersistentManifoldArray manifoldArray)
	{
		if (m_convexTriangleCallback.m_manifoldPtr != null)
		{
			manifoldArray.Add(m_convexTriangleCallback.m_manifoldPtr);
		}
	}

	public void ClearCache()
	{
		m_convexTriangleCallback.ClearCache();
	}

	private bool m_isSwapped;
	private ConvexTriangleCallback m_convexTriangleCallback;
}