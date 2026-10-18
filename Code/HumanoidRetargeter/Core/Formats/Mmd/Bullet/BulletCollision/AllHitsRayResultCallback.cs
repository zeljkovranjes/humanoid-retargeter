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

internal class AllHitsRayResultCallback : RayResultCallback
{
	public AllHitsRayResultCallback(IndexedVector3 rayFromWorld, IndexedVector3 rayToWorld) : this(ref rayFromWorld, ref rayToWorld)
	{
	}

	public AllHitsRayResultCallback(ref IndexedVector3 rayFromWorld, ref IndexedVector3 rayToWorld)
	{
		m_rayFromWorld = rayFromWorld;
		m_rayToWorld = rayToWorld;
	}

	public ObjectArray<CollisionObject> m_collisionObjects = new ObjectArray<CollisionObject>();
	IndexedVector3 m_rayFromWorld; //used to calculate hitPointWorld from hitFraction
	IndexedVector3 m_rayToWorld;
	public ObjectArray<IndexedVector3> m_hitNormalWorld = new ObjectArray<IndexedVector3>();
	public ObjectArray<IndexedVector3> m_hitPointWorld = new ObjectArray<IndexedVector3>();
	public ObjectArray<float> m_hitFractions = new ObjectArray<float>();
	public override float AddSingleResult(ref LocalRayResult rayResult, bool normalInWorldSpace)
	{
		m_collisionObject = rayResult.m_collisionObject;
		m_collisionObjects.Add(rayResult.m_collisionObject);
		IndexedVector3 hitNormalWorld;
		if (normalInWorldSpace)
		{
			hitNormalWorld = rayResult.m_hitNormalLocal;
		}
		else
		{
			hitNormalWorld = m_collisionObject.GetWorldTransform()._basis * rayResult.m_hitNormalLocal;
		}

		m_hitNormalWorld.Add(hitNormalWorld);
		IndexedVector3 hitPointWorld = MathUtil.Interpolate3(ref m_rayFromWorld, ref m_rayToWorld, rayResult.m_hitFraction);
		m_hitPointWorld.Add(hitPointWorld);
		m_hitFractions.Add(rayResult.m_hitFraction);
		return m_closestHitFraction;
	}
}