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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal class DefaultVehicleRaycaster : IVehicleRaycaster
{
	private DynamicsWorld m_dynamicsWorld;
	private struct DataCopy
	{
		IndexedVector3 m_from;
		IndexedVector3 m_to;
		VehicleRaycasterResult m_result;
		public DataCopy(IndexedVector3 from, IndexedVector3 to, VehicleRaycasterResult result)
		{
			m_from = from;
			m_to = to;
			m_result = result;
		}
	}

	public DefaultVehicleRaycaster(DynamicsWorld world)
	{
		m_dynamicsWorld = world;
	}

	public virtual Object CastRay(ref IndexedVector3 from, ref IndexedVector3 to, ref VehicleRaycasterResult result)
	{
		//	RayResultCallback& resultCallback;
		ClosestRayResultCallback rayCallback = new ClosestRayResultCallback(ref from, ref to);
		m_dynamicsWorld.RayTest(ref from, ref to, rayCallback);
		if (rayCallback.HasHit())
		{
			RigidBody body = RigidBody.Upcast(rayCallback.m_collisionObject);
			if (body != null && body.HasContactResponse())
			{
				result.m_hitPointInWorld = rayCallback.m_hitPointWorld;
				result.m_hitNormalInWorld = rayCallback.m_hitNormalWorld;
				result.m_hitNormalInWorld.Normalize();
				result.m_distFraction = rayCallback.m_closestHitFraction;
				return body;
			}
		}
		else
		{
			int ibreak = 0;
			ClosestRayResultCallback rayCallback2 = new ClosestRayResultCallback(ref from, ref to);
			m_dynamicsWorld.RayTest(ref from, ref to, rayCallback2);
		}

		rayCallback.Cleanup();
		return null;
	}
}