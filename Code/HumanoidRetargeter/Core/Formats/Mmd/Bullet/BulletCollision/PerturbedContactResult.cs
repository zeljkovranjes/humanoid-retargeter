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

internal class PerturbedContactResult : ManifoldResult
{
	public ManifoldResult m_originalManifoldResult;
	public IndexedMatrix m_transformA;
	public IndexedMatrix m_transformB;
	public IndexedMatrix m_unPerturbedTransform;
	public bool m_perturbA;
	public IDebugDraw m_debugDrawer;
	public PerturbedContactResult(ManifoldResult originalResult, ref IndexedMatrix transformA, ref IndexedMatrix transformB, ref IndexedMatrix unPerturbedTransform, bool perturbA, IDebugDraw debugDrawer)
	{
		m_originalManifoldResult = originalResult;
		m_transformA = transformA;
		m_transformB = transformB;
		m_perturbA = perturbA;
		m_unPerturbedTransform = unPerturbedTransform;
		m_debugDrawer = debugDrawer;
	}

	public override void AddContactPoint(ref IndexedVector3 normalOnBInWorld, ref IndexedVector3 pointInWorld, float orgDepth)
	{
		IndexedVector3 endPt, startPt;
		float newDepth;
		IndexedVector3 newNormal = new IndexedVector3(0, 1, 0);
		if (m_perturbA)
		{
			IndexedVector3 endPtOrg = pointInWorld + normalOnBInWorld * orgDepth;
			endPt = (m_unPerturbedTransform * m_transformA.Inverse()) * (endPtOrg);
			newDepth = IndexedVector3.Dot((endPt - pointInWorld), normalOnBInWorld);
			startPt = endPt + normalOnBInWorld * newDepth;
		}
		else
		{
			endPt = pointInWorld + normalOnBInWorld * orgDepth;
			startPt = (m_unPerturbedTransform * m_transformB.Inverse()) * (pointInWorld);
			newDepth = IndexedVector3.Dot((endPt - startPt), normalOnBInWorld);
		}

		//#define DEBUG_CONTACTS 1
		m_debugDrawer.DrawLine(startPt, endPt, new IndexedVector3(1, 0, 0));
		m_debugDrawer.DrawSphere(startPt, 0.5f, new IndexedVector3(0, 1, 0));
		m_debugDrawer.DrawSphere(endPt, 0.5f, new IndexedVector3(0, 0, 1));
		m_originalManifoldResult.AddContactPoint(ref normalOnBInWorld, ref startPt, newDepth);
	}
}