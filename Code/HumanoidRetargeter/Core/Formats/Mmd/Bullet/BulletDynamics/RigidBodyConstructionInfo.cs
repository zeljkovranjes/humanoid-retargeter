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

internal class RigidBodyConstructionInfo
{
	public float m_mass;
	public IMotionState m_motionState;
	public IndexedMatrix m_startWorldTransform;
	public CollisionShape m_collisionShape;
	public IndexedVector3 m_localInertia;
	public float m_linearDamping;
	public float m_angularDamping;
	public float m_friction;
	public float m_restitution;
	public float m_linearSleepingThreshold;
	public float m_angularSleepingThreshold;
	//Additional damping can help avoiding lowpass jitter motion, help stability for ragdolls etc.
	//Such damping is undesirable, so once the overall simulation quality of the rigid body dynamics system has improved, this should become obsolete
	public bool m_additionalDamping;
	public float m_additionalDampingFactor;
	public float m_additionalLinearDampingThresholdSqr;
	public float m_additionalAngularDampingThresholdSqr;
	public float m_additionalAngularDampingFactor;
	public RigidBodyConstructionInfo(float mass, IMotionState motionState, CollisionShape collisionShape) : this(mass, motionState, collisionShape, new IndexedVector3(0))
	{
	}

	public RigidBodyConstructionInfo(float mass, IMotionState motionState, CollisionShape collisionShape, IndexedVector3 localInertia)
	{
		m_mass = mass;
		m_motionState = motionState;
		m_collisionShape = collisionShape;
		m_localInertia = localInertia;
		m_linearDamping = 0f;
		m_angularDamping = 0f;
		m_friction = 0.5f;
		m_restitution = 0f;
		m_linearSleepingThreshold = 0.8f;
		m_angularSleepingThreshold = 1f;
		m_additionalDamping = false;
		m_additionalDampingFactor = 0.005f;
		m_additionalLinearDampingThresholdSqr = 0.01f;
		m_additionalAngularDampingThresholdSqr = 0.01f;
		m_additionalAngularDampingFactor = 0.01f;
		m_startWorldTransform = IndexedMatrix.Identity;
	}
}