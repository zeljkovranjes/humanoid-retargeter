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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet;

internal class ConvexSeparatingDistanceUtil
{
	private IndexedQuaternion m_ornA;
	private IndexedQuaternion m_ornB;
	private IndexedVector3 m_posA;
	private IndexedVector3 m_posB;
	private IndexedVector3 m_separatingNormal;
	private float m_boundingRadiusA;
	private float m_boundingRadiusB;
	private float m_separatingDistance;
	public ConvexSeparatingDistanceUtil(float boundingRadiusA, float boundingRadiusB)
	{
		m_boundingRadiusA = boundingRadiusA;
		m_boundingRadiusB = boundingRadiusB;
		m_separatingDistance = 0f;
	}

	public float GetConservativeSeparatingDistance()
	{
		return m_separatingDistance;
	}

	public void UpdateSeparatingDistance(ref IndexedMatrix transA, ref IndexedMatrix transB)
	{
		IndexedVector3 toPosA = transA._origin;
		IndexedVector3 toPosB = transB._origin;
		IndexedQuaternion toOrnA = transA.GetRotation();
		IndexedQuaternion toOrnB = transB.GetRotation();
		if (m_separatingDistance > 0.0f)
		{
			IndexedVector3 linVelA;
			IndexedVector3 angVelA;
			IndexedVector3 linVelB;
			IndexedVector3 angVelB;
			TransformUtil.CalculateVelocityQuaternion(ref m_posA, ref toPosA, ref m_ornA, ref toOrnA, 1f, out linVelA, out angVelA);
			TransformUtil.CalculateVelocityQuaternion(ref m_posB, ref toPosB, ref m_ornB, ref toOrnB, 1f, out linVelB, out angVelB);
			float maxAngularProjectedVelocity = angVelA.Length() * m_boundingRadiusA + angVelB.Length() * m_boundingRadiusB;
			IndexedVector3 relLinVel = (linVelB - linVelA);
			float relLinVelocLength = IndexedVector3.Dot((linVelB - linVelA), m_separatingNormal);
			if (relLinVelocLength < 0f)
			{
				relLinVelocLength = 0f;
			}

			float projectedMotion = maxAngularProjectedVelocity + relLinVelocLength;
			m_separatingDistance -= projectedMotion;
		}

		m_posA = toPosA;
		m_posB = toPosB;
		m_ornA = toOrnA;
		m_ornB = toOrnB;
	}

	void InitSeparatingDistance(ref IndexedVector3 separatingVector, float separatingDistance, ref IndexedMatrix transA, ref IndexedMatrix transB)
	{
		m_separatingNormal = separatingVector;
		m_separatingDistance = separatingDistance;
		IndexedVector3 toPosA = transA._origin;
		IndexedVector3 toPosB = transB._origin;
		IndexedQuaternion toOrnA = transA.GetRotation();
		IndexedQuaternion toOrnB = transB.GetRotation();
		m_posA = toPosA;
		m_posB = toPosB;
		m_ornA = toOrnA;
		m_ornB = toOrnB;
	}
}