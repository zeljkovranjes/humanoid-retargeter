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

internal abstract class DynamicsWorld : CollisionWorld
{
	public DynamicsWorld(IDispatcher dispatcher, IBroadphaseInterface broadphase, ICollisionConfiguration collisionConfiguration) : base(dispatcher, broadphase, collisionConfiguration)
	{
		m_internalTickCallback = null;
		m_worldUserInfo = null;
		m_solverInfo = new ContactSolverInfo();
	}

	public abstract int StepSimulation(float timeStep, int maxSubSteps);
	public abstract int StepSimulation(float timeStep, int maxSubSteps, float fixedTimeStep);
	public override void DebugDrawWorld()
	{
		base.DebugDrawWorld();
	}

	public virtual void AddConstraint(TypedConstraint constraint)
	{
		AddConstraint(constraint, false);
	}

	public virtual void AddConstraint(TypedConstraint constraint, bool disableCollisionsBetweenLinkedBodies)
	{
	//(void)constraint;
	//(void)disableCollisionsBetweenLinkedBodies;
	}

	public virtual void RemoveConstraint(TypedConstraint constraint)
	{
	//(void)constraint;
	}

	public abstract void AddAction(IActionInterface action);
	public abstract void RemoveAction(IActionInterface action);
	//once a rigidbody is added to the dynamics world, it will get this gravity assigned
	//existing rigidbodies in the world get gravity assigned too, during this method
	public abstract void SetGravity(ref IndexedVector3 gravity);
	public abstract void SetGravity(IndexedVector3 gravity);
	public abstract IndexedVector3 GetGravity();
	public abstract void SynchronizeMotionStates();
	//public abstract  void addRigidBody(RigidBody body,short mask1,short mask2);
	public abstract void AddRigidBody(RigidBody body);
	public abstract void AddRigidBody(RigidBody body, CollisionFilterGroups group, CollisionFilterGroups mask);
	public abstract void RemoveRigidBody(RigidBody body);
	public abstract void SetConstraintSolver(IConstraintSolver solver);
	public abstract IConstraintSolver GetConstraintSolver();
	public virtual int GetNumConstraints()
	{
		return 0;
	}

	public virtual TypedConstraint GetConstraint(int index)
	{
		return null;
	}

	public abstract DynamicsWorldType GetWorldType();
	public abstract void ClearForces();
	public void SetInternalTickCallback(IInternalTickCallback cb, Object worldUserInfo, bool isPreTick)
	{
		if (isPreTick)
		{
			m_internalPreTickCallback = cb;
		}
		else
		{
			m_internalTickCallback = cb;
		}

		m_worldUserInfo = worldUserInfo;
	}

	public void SetWorldUserInfo(Object worldUserInfo)
	{
		m_worldUserInfo = worldUserInfo;
	}

	public Object GetWorldUserInfo()
	{
		return m_worldUserInfo;
	}

	public ContactSolverInfo GetSolverInfo()
	{
		return m_solverInfo;
	}

	public virtual void AddVehicle(IActionInterface vehicle)
	{
	}

	public virtual void RemoveVehicle(IActionInterface vehicle)
	{
	}

	public virtual void AddCharacter(IActionInterface character)
	{
	}

	public virtual void RemoveCharacter(IActionInterface character)
	{
	}

	protected IInternalTickCallback m_internalTickCallback;
	protected IInternalTickCallback m_internalPreTickCallback;
	protected Object m_worldUserInfo;
	protected ContactSolverInfo m_solverInfo;
}