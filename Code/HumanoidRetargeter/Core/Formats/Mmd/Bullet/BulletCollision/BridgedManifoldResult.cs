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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class BridgedManifoldResult : ManifoldResult
{
	ContactResultCallback m_resultCallback;
	public BridgedManifoldResult(CollisionObject obj0, CollisionObject obj1, ContactResultCallback resultCallback) : base(obj0, obj1)
	{
		m_resultCallback = resultCallback;
	}

	public override void AddContactPoint(ref IndexedVector3 normalOnBInWorld, ref IndexedVector3 pointInWorld, float depth)
	{
		bool isSwapped = m_manifoldPtr.GetBody0() != m_body0;
		IndexedVector3 pointA = pointInWorld + normalOnBInWorld * depth;
		IndexedVector3 localA;
		IndexedVector3 localB;
		if (isSwapped)
		{
			MathUtil.InverseTransform(ref m_rootTransB, ref pointA, out localA);
			MathUtil.InverseTransform(ref m_rootTransA, ref pointInWorld, out localB);
		}
		else
		{
			MathUtil.InverseTransform(ref m_rootTransA, ref pointA, out localA);
			MathUtil.InverseTransform(ref m_rootTransB, ref pointInWorld, out localB);
		}

		ManifoldPoint newPt = BulletGlobals.ManifoldPointPool.Get();
		newPt.Initialise(ref localA, ref localB, ref normalOnBInWorld, depth);
		newPt.m_positionWorldOnA = pointA;
		newPt.m_positionWorldOnB = pointInWorld;
		//BP mod, store contact triangles.
		if (isSwapped)
		{
			newPt.m_partId0 = m_partId1;
			newPt.m_partId1 = m_partId0;
			newPt.m_index0 = m_index1;
			newPt.m_index1 = m_index0;
		}
		else
		{
			newPt.m_partId0 = m_partId0;
			newPt.m_partId1 = m_partId1;
			newPt.m_index0 = m_index0;
			newPt.m_index1 = m_index1;
		}

		//experimental feature info, for per-triangle material etc.
		CollisionObject obj0 = isSwapped ? m_body1 : m_body0;
		CollisionObject obj1 = isSwapped ? m_body0 : m_body1;
		m_resultCallback.AddSingleResult(ref newPt, obj0, newPt.m_partId0, newPt.m_index0, obj1, newPt.m_partId1, newPt.m_index1);
	}
}