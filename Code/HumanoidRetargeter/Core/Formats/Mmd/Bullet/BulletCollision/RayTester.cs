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

internal class RayTester : DefaultCollide
{
	public CollisionObject m_collisionObject;
	public CompoundShape m_compoundShape;
	public IndexedMatrix m_colObjWorldTransform;
	public IndexedMatrix m_rayFromTrans;
	public IndexedMatrix m_rayToTrans;
	public RayResultCallback m_resultCallback;
	public RayTester(CollisionObject collisionObject, CompoundShape compoundShape, ref IndexedMatrix colObjWorldTransform, ref IndexedMatrix rayFromTrans, ref IndexedMatrix rayToTrans, RayResultCallback resultCallback)
	{
		m_collisionObject = collisionObject;
		m_compoundShape = compoundShape;
		m_colObjWorldTransform = colObjWorldTransform;
		m_rayFromTrans = rayFromTrans;
		m_rayToTrans = rayToTrans;
		m_resultCallback = resultCallback;
	}

	public void Process(int i)
	{
		CollisionShape childCollisionShape = m_compoundShape.GetChildShape(i);
		IndexedMatrix childTrans = m_compoundShape.GetChildTransform(i);
		IndexedMatrix childWorldTrans = m_colObjWorldTransform * childTrans;
		// replace collision shape so that callback can determine the triangle
		CollisionShape saveCollisionShape = m_collisionObject.GetCollisionShape();
		m_collisionObject.InternalSetTemporaryCollisionShape(childCollisionShape);
		LocalInfoAdder2 my_cb = new LocalInfoAdder2(i, m_resultCallback);
		CollisionWorld.RayTestSingle(ref m_rayFromTrans, ref m_rayToTrans, m_collisionObject, childCollisionShape, ref childWorldTrans, my_cb);
		// restore
		m_collisionObject.InternalSetTemporaryCollisionShape(saveCollisionShape);
	}

	public override void Process(DbvtNode leaf)
	{
		Process(leaf.dataAsInt);
	}

	public virtual void Cleanup()
	{
	}
}