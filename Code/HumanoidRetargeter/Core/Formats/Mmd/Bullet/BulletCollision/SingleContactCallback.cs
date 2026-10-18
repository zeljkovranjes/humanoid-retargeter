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

internal class SingleContactCallback : IBroadphaseAabbCallback
{
	CollisionObject m_collisionObject;
	CollisionWorld m_world;
	ContactResultCallback m_resultCallback;
	public SingleContactCallback(CollisionObject collisionObject, CollisionWorld world, ContactResultCallback resultCallback)
	{
		m_collisionObject = collisionObject;
		m_world = world;
		m_resultCallback = resultCallback;
	}

	public virtual void Cleanup()
	{
	}

	public virtual bool Process(BroadphaseProxy proxy)
	{
		if (proxy.m_clientObject == m_collisionObject)
		{
			return true;
		}

		CollisionObject collisionObject = proxy.m_clientObject as CollisionObject;
		//only perform raycast if filterMask matches
		if (m_resultCallback.NeedsCollision(collisionObject.GetBroadphaseHandle()))
		{
			CollisionAlgorithm algorithm = m_world.GetDispatcher().FindAlgorithm(m_collisionObject, collisionObject);
			if (algorithm != null)
			{
				BridgedManifoldResult contactPointResult = new BridgedManifoldResult(m_collisionObject, collisionObject, m_resultCallback);
				//discrete collision detection query
				algorithm.ProcessCollision(m_collisionObject, collisionObject, m_world.GetDispatchInfo(), contactPointResult);
				m_world.GetDispatcher().FreeCollisionAlgorithm(algorithm);
			}
		}

		return true;
	}
}