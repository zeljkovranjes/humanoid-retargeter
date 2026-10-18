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

internal class BridgeTriangleConvexcastCallback : TriangleConvexcastCallback, IDisposable
{
	public BridgeTriangleConvexcastCallback()
	{
	} // for pool

	public BridgeTriangleConvexcastCallback(ConvexShape castShape, ref IndexedMatrix from, ref IndexedMatrix to, ConvexResultCallback resultCallback, CollisionObject collisionObject, ConcaveShape triangleMesh, ref IndexedMatrix triangleToWorld) : base(castShape, ref from, ref to, ref triangleToWorld, triangleMesh.GetMargin())
	{
		m_resultCallback = resultCallback;
		m_collisionObject = collisionObject;
		m_triangleMesh = triangleMesh;
	}

	public void Initialize(ConvexShape castShape, ref IndexedMatrix from, ref IndexedMatrix to, ConvexResultCallback resultCallback, CollisionObject collisionObject, ConcaveShape triangleMesh, ref IndexedMatrix triangleToWorld)
	{
		base.Initialize(castShape, ref from, ref to, ref triangleToWorld, triangleMesh.GetMargin());
		m_resultCallback = resultCallback;
		m_collisionObject = collisionObject;
		m_triangleMesh = triangleMesh;
	}

	public override float ReportHit(ref IndexedVector3 hitNormalLocal, ref IndexedVector3 hitPointLocal, float hitFraction, int partId, int triangleIndex)
	{
		LocalShapeInfo shapeInfo = new LocalShapeInfo();
		shapeInfo.m_shapePart = partId;
		shapeInfo.m_triangleIndex = triangleIndex;
		if (hitFraction <= m_resultCallback.m_closestHitFraction)
		{
			LocalConvexResult convexResult = new LocalConvexResult(m_collisionObject, ref shapeInfo, ref hitNormalLocal, ref hitPointLocal, hitFraction);
			bool normalInWorldSpace = m_triangleMesh is TriangleMeshShape;
			return m_resultCallback.AddSingleResult(ref convexResult, normalInWorldSpace);
		}

		return hitFraction;
	}

	public void Dispose()
	{
		Cleanup();
		BulletGlobals.BridgeTriangleConvexcastCallbackPool.Free(this);
	}

	ConvexResultCallback m_resultCallback;
	CollisionObject m_collisionObject;
	ConcaveShape m_triangleMesh;
};