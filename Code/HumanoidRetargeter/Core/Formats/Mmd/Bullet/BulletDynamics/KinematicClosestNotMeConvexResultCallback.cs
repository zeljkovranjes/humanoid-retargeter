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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal class KinematicClosestNotMeConvexResultCallback : ClosestConvexResultCallback
{
	public KinematicClosestNotMeConvexResultCallback(CollisionObject me, IndexedVector3 up, float minSlopeDot) : base(IndexedVector3.Zero, IndexedVector3.Zero)
	{
		m_me = me;
		m_up = up;
		m_minSlopeDot = minSlopeDot;
	}

	public override float AddSingleResult(ref LocalConvexResult convexResult, bool normalInWorldSpace)
	{
		if (convexResult.m_hitCollisionObject == m_me)
		{
			return 1.0f;
		}

		IndexedVector3 hitNormalWorld;
		if (normalInWorldSpace)
		{
			hitNormalWorld = convexResult.m_hitNormalLocal;
		}
		else
		{
			hitNormalWorld = convexResult.m_hitCollisionObject.GetWorldTransform()._basis * convexResult.m_hitNormalLocal;
		}

		float dotUp = IndexedVector3.Dot(m_up, hitNormalWorld);
		if (dotUp < m_minSlopeDot)
		{
			return 1.0f;
		}

		return base.AddSingleResult(ref convexResult, normalInWorldSpace);
	}

	protected CollisionObject m_me;
	protected IndexedVector3 m_up;
	protected float m_minSlopeDot;
}