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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal abstract class TriangleConvexcastCallback : ITriangleCallback
{
	public TriangleConvexcastCallback()
	{
	} // for pool

	public TriangleConvexcastCallback(ConvexShape convexShape, ref IndexedMatrix convexShapeFrom, ref IndexedMatrix convexShapeTo, ref IndexedMatrix triangleToWorld, float triangleCollisionMargin)
	{
		m_convexShape = convexShape;
		m_convexShapeFrom = convexShapeFrom;
		m_convexShapeTo = convexShapeTo;
		m_triangleToWorld = triangleToWorld;
		m_triangleCollisionMargin = triangleCollisionMargin;
	}

	public virtual void Initialize(ConvexShape convexShape, ref IndexedMatrix convexShapeFrom, ref IndexedMatrix convexShapeTo, ref IndexedMatrix triangleToWorld, float triangleCollisionMargin)
	{
		m_convexShape = convexShape;
		m_convexShapeFrom = convexShapeFrom;
		m_convexShapeTo = convexShapeTo;
		m_triangleToWorld = triangleToWorld;
		m_triangleCollisionMargin = triangleCollisionMargin;
	}

	public virtual bool graphics()
	{
		return false;
	}

	public virtual void ProcessTriangle(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		TriangleShape triangleShape = new TriangleShape(ref triangle[0], ref triangle[1], ref triangle[2]);
		triangleShape.SetMargin(m_triangleCollisionMargin);
		VoronoiSimplexSolver simplexSolver = BulletGlobals.VoronoiSimplexSolverPool.Get();
		GjkEpaPenetrationDepthSolver gjkEpaPenetrationSolver = new GjkEpaPenetrationDepthSolver();
		//#define  USE_SUBSIMPLEX_CONVEX_CAST 1
		//if you reenable USE_SUBSIMPLEX_CONVEX_CAST see commented ref code below
		//btGjkConvexCast	convexCaster(m_convexShape,&triangleShape,&simplexSolver);
		ContinuousConvexCollision convexCaster = BulletGlobals.ContinuousConvexCollisionPool.Get();
		convexCaster.Initialize(m_convexShape, triangleShape, simplexSolver, gjkEpaPenetrationSolver);
		CastResult castResult = BulletGlobals.CastResultPool.Get();
		castResult.m_fraction = 1f;
		if (convexCaster.CalcTimeOfImpact(ref m_convexShapeFrom, ref m_convexShapeTo, ref m_triangleToWorld, ref m_triangleToWorld, castResult))
		{
			//add hit
			if (castResult.m_normal.LengthSquared() > 0.0001f)
			{
				if (castResult.m_fraction < m_hitFraction)
				{
					/* btContinuousConvexCast's normal is already in world space */
					/*
                        #ifdef USE_SUBSIMPLEX_CONVEX_CAST
				                        //rotate normal into worldspace
				                        castResult.m_normal = m_convexShapeFrom.getBasis() * castResult.m_normal;
                        #endif //USE_SUBSIMPLEX_CONVEX_CAST
                        */
					castResult.m_normal.Normalize();
					ReportHit(ref castResult.m_normal, ref castResult.m_hitPoint, castResult.m_fraction, partId, triangleIndex);
				}
			}
		}

		BulletGlobals.ContinuousConvexCollisionPool.Free(convexCaster);
		BulletGlobals.VoronoiSimplexSolverPool.Free(simplexSolver);
		castResult.Cleanup();
	}

	public virtual void Cleanup()
	{
	}

	public abstract float ReportHit(ref IndexedVector3 hitNormalLocal, ref IndexedVector3 hitPointLocal, float hitFraction, int partId, int triangleIndex);
	public ConvexShape m_convexShape;
	public IndexedMatrix m_convexShapeFrom;
	public IndexedMatrix m_convexShapeTo;
	public IndexedMatrix m_triangleToWorld;
	public float m_hitFraction;
	public float m_triangleCollisionMargin;
	public float m_allowedPenetration;
}