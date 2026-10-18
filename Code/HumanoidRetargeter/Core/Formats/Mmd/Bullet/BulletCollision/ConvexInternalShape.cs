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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal abstract class ConvexInternalShape : ConvexShape
{
	public ConvexInternalShape()
	{
		m_localScaling = new IndexedVector3(1);
		m_collisionMargin = CollisionMargin.CONVEX_DISTANCE_MARGIN;
	}

	public override void Cleanup()
	{
		base.Cleanup();
	}

	public override IndexedVector3 LocalGetSupportingVertex(ref IndexedVector3 vec)
	{
		IndexedVector3 supVertex = LocalGetSupportingVertexWithoutMargin(ref vec);
		if (GetMargin() != 0f)
		{
			IndexedVector3 vecnorm = vec;
			if (vecnorm.LengthSquared() < (MathUtil.SIMD_EPSILON * MathUtil.SIMD_EPSILON))
			{
				vecnorm = new IndexedVector3(-1);
			}

			vecnorm.Normalize();
			supVertex += GetMargin() * vecnorm;
		}

		return supVertex;
	}

	public IndexedVector3 GetImplicitShapeDimensions()
	{
		return m_implicitShapeDimensions;
	}

	public void SetSafeMargin(float minDimension)
	{
		SetSafeMargin(minDimension, 0.1f);
	}

	public void SetSafeMargin(float minDimension, float defaultMarginMultiplier)
	{
		float safeMargin = defaultMarginMultiplier * minDimension;
		if (safeMargin < GetMargin())
		{
			SetMargin(safeMargin);
		}
	}

	public void SetSafeMargin(ref IndexedVector3 halfExtents)
	{
		SetSafeMargin(ref halfExtents, 0.1f);
	}

	public void SetSafeMargin(ref IndexedVector3 halfExtents, float defaultMarginMultiplier)
	{
		//see http://code.google.com/p/bullet/issues/detail?id=349
		//this margin check could could be added to other collision shapes too,
		//or add some assert/warning somewhere
		float minDimension = halfExtents[halfExtents.MinAxis()];
		SetSafeMargin(minDimension, defaultMarginMultiplier);
	}

	public override void GetAabb(ref IndexedMatrix t, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		GetAabbSlow(ref t, out aabbMin, out aabbMax);
	}

	public override void GetAabbSlow(ref IndexedMatrix trans, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		float margin = GetMargin();
		aabbMin = IndexedVector3.Zero;
		aabbMax = IndexedVector3.Zero;
		for (int i = 0; i < 3; i++)
		{
			IndexedVector3 vec = new IndexedVector3();
			vec[i] = 1f;
			IndexedVector3 temp = vec * trans._basis;
			IndexedVector3 sv = LocalGetSupportingVertex(ref temp);
			IndexedVector3 tmp = trans * sv;
			aabbMax[i] = tmp[i] + margin;
			vec[i] = -1f;
			temp = vec * trans._basis;
			sv = LocalGetSupportingVertex(ref temp);
			tmp = trans * sv;
			aabbMin[i] = tmp[i] - margin;
		}
	}

	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		m_localScaling = scaling.Absolute();
	}

	public override IndexedVector3 GetLocalScaling()
	{
		return m_localScaling;
	}

	public IndexedVector3 GetLocalScalingNV()
	{
		return m_localScaling;
	}

	public override void SetMargin(float margin)
	{
		m_collisionMargin = margin;
	}

	public override float GetMargin()
	{
		return m_collisionMargin;
	}

	public float GetMarginNV()
	{
		return m_collisionMargin;
	}

	public override int GetNumPreferredPenetrationDirections()
	{
		return 0;
	}

	public override void GetPreferredPenetrationDirection(int index, out IndexedVector3 penetrationVector)
	{
		{
		}

		penetrationVector = IndexedVector3.Zero;
	}

	protected IndexedVector3 m_localScaling;
	protected IndexedVector3 m_implicitShapeDimensions;
	protected float m_collisionMargin;
	protected float m_padding;
}