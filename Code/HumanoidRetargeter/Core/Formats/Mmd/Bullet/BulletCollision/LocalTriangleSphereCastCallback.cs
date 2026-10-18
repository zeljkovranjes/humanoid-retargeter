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

internal class LocalTriangleSphereCastCallback : ITriangleCallback, IDisposable
{
	public IndexedMatrix m_ccdSphereFromTrans;
	public IndexedMatrix m_ccdSphereToTrans;
	public IndexedMatrix m_meshTransform;
	public float m_ccdSphereRadius;
	public float m_hitFraction;
	public virtual bool graphics()
	{
		return false;
	}

	public LocalTriangleSphereCastCallback()
	{
	} // for pool

	public LocalTriangleSphereCastCallback(ref IndexedMatrix from, ref IndexedMatrix to, float ccdSphereRadius, float hitFraction)
	{
		m_ccdSphereFromTrans = from;
		m_ccdSphereToTrans = to;
		m_ccdSphereRadius = ccdSphereRadius;
		m_hitFraction = hitFraction;
	}

	public void Initialize(ref IndexedMatrix from, ref IndexedMatrix to, float ccdSphereRadius, float hitFraction)
	{
		m_ccdSphereFromTrans = from;
		m_ccdSphereToTrans = to;
		m_ccdSphereRadius = ccdSphereRadius;
		m_hitFraction = hitFraction;
	}

	public virtual void Cleanup()
	{
	}

	public void ProcessTriangle(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		//do a swept sphere for now
		IndexedMatrix ident = IndexedMatrix.Identity;
		CastResult castResult = BulletGlobals.CastResultPool.Get();
		castResult.m_fraction = m_hitFraction;
		SphereShape pointShape = BulletGlobals.SphereShapePool.Get();
		pointShape.Initialize(m_ccdSphereRadius);
		using (TriangleShape triShape = BulletGlobals.TriangleShapePool.Get())
		{
			triShape.Initialize(ref triangle[0], ref triangle[1], ref triangle[2]);
			VoronoiSimplexSolver simplexSolver = BulletGlobals.VoronoiSimplexSolverPool.Get();
			SubSimplexConvexCast convexCaster = BulletGlobals.SubSimplexConvexCastPool.Get();
			convexCaster.Initialize(pointShape, triShape, simplexSolver);
			//GjkConvexCast	convexCaster(&pointShape,convexShape,&simplexSolver);
			//ContinuousConvexCollision convexCaster(&pointShape,convexShape,&simplexSolver,0);
			//local space?
			if (convexCaster.CalcTimeOfImpact(ref m_ccdSphereFromTrans, ref m_ccdSphereToTrans, ref ident, ref ident, castResult))
			{
				if (m_hitFraction > castResult.m_fraction)
				{
					m_hitFraction = castResult.m_fraction;
				}
			}

			BulletGlobals.SubSimplexConvexCastPool.Free(convexCaster);
			BulletGlobals.VoronoiSimplexSolverPool.Free(simplexSolver);
			BulletGlobals.SphereShapePool.Free(pointShape);
			castResult.Cleanup();
		}
	}

	public void Dispose()
	{
		BulletGlobals.LocalTriangleSphereCastCallbackPool.Free(this);
	}
};