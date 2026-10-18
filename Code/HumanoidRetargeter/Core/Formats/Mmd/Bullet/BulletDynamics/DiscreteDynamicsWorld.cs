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

internal class DiscreteDynamicsWorld : DynamicsWorld
{
	public DiscreteDynamicsWorld(IDispatcher dispatcher, IBroadphaseInterface pairCache, IConstraintSolver constraintSolver, ICollisionConfiguration collisionConfiguration) : base(dispatcher, pairCache, collisionConfiguration)
	{
		m_ownsIslandManager = true;
		m_constraints = new ObjectArray<TypedConstraint>();
		m_sortedConstraints = new ObjectArray<TypedConstraint>();
		m_islandSortPredicate = new SortConstraintOnIslandPredicate();
		m_islandQuickSortPredicate = new QuickSortConstraintOnIslandPredicate();
		m_actions = new List<IActionInterface>();
		m_nonStaticRigidBodies = new ObjectArray<RigidBody>();
		m_islandManager = new SimulationIslandManager();
		m_constraintSolver = constraintSolver;
		IndexedVector3 gravity = new IndexedVector3(0, -10, 0);
		SetGravity(ref gravity);
		m_localTime = 0f;
		m_profileTimings = 0;
		m_synchronizeAllMotionStates = false;
		if (m_constraintSolver == null)
		{
			m_constraintSolver = new SequentialImpulseConstraintSolver();
			m_ownsConstraintSolver = true;
		}
		else
		{
			m_ownsConstraintSolver = false;
		}
	}

	public override void Cleanup()
	{
		base.Cleanup();
		//only delete it when we created it
		if (m_ownsIslandManager)
		{
			m_islandManager.Cleanup();
			m_islandManager = null;
			m_ownsIslandManager = false;
		}

		if (m_ownsConstraintSolver)
		{
			m_constraintSolver.Cleanup();
			m_constraintSolver = null;
			m_ownsConstraintSolver = false;
		}
	}

	public override int StepSimulation(float timeStep, int maxSubSteps)
	{
		return StepSimulation(timeStep, maxSubSteps, s_fixedTimeStep);
	}

	public override int StepSimulation(float timeStep, int maxSubSteps, float fixedTimeStep)
	{
		StartProfiling(timeStep);
		BulletGlobals.StartProfile("stepSimulation");
		int numSimulationSubSteps = 0;
		if (maxSubSteps != 0)
		{
			//fixed timestep with interpolation
			m_localTime += timeStep;
			if (m_localTime >= fixedTimeStep)
			{
				numSimulationSubSteps = (int)(m_localTime / fixedTimeStep);
				m_localTime -= numSimulationSubSteps * fixedTimeStep;
			}
		}
		else
		{
			//variable timestep
			fixedTimeStep = timeStep;
			m_localTime = timeStep;
			if (MathUtil.FuzzyZero(timeStep))
			{
				numSimulationSubSteps = 0;
				maxSubSteps = 0;
			}
			else
			{
				numSimulationSubSteps = 1;
				maxSubSteps = 1;
			}
		}

		//process some debugging flags
		if (GetDebugDrawer() != null)
		{
			IDebugDraw debugDrawer = GetDebugDrawer();
			BulletGlobals.gDisableDeactivation = ((debugDrawer.GetDebugMode() & DebugDrawModes.DBG_NoDeactivation) != 0);
		}

		if (numSimulationSubSteps != 0)
		{
			//clamp the number of substeps, to prevent simulation grinding spiralling down to a halt
			int clampedSimulationSteps = (numSimulationSubSteps > maxSubSteps) ? maxSubSteps : numSimulationSubSteps;
			SaveKinematicState(fixedTimeStep * clampedSimulationSteps);
			ApplyGravity();
			for (int i = 0; i < clampedSimulationSteps; i++)
			{
				InternalSingleStepSimulation(fixedTimeStep);
				SynchronizeMotionStates();
			}
		}
		else
		{
			SynchronizeMotionStates();
		}

		ClearForces();
		if (m_profileManager != null)
		{
			m_profileManager.Increment_Frame_Counter();
		}

		return numSimulationSubSteps;
	}

	public override void SynchronizeMotionStates()
	{
		if (m_synchronizeAllMotionStates)
		{
			//iterate  over all collision objects
			int length = m_collisionObjects.Count;
			for (int i = 0; i < length; ++i)
			{
				RigidBody body = RigidBody.Upcast(m_collisionObjects[i]);
				if (body != null)
				{
					SynchronizeSingleMotionState(body);
				}
			}
		}
		else
		{
			//iterate over all active rigid bodies
			int length = m_nonStaticRigidBodies.Count;
			for (int i = 0; i < length; ++i)
			{
				RigidBody body = m_nonStaticRigidBodies[i];
				if (body.IsActive())
				{
					SynchronizeSingleMotionState(body);
				}
			}
		}
	}

	public void SynchronizeSingleMotionState(RigidBody body)
	{
		{
		}

		if (body.GetMotionState() != null && !body.IsStaticOrKinematicObject())
		{
			//we need to call the update at least once, even for sleeping objects
			//otherwise the 'graphics' transform never updates properly
			//if (body.getActivationState() != ISLAND_SLEEPING)
			{
				IndexedMatrix interpolatedTransform;
				TransformUtil.IntegrateTransform(body.GetInterpolationWorldTransform(), body.SetInterpolationLinearVelocity(), body.GetInterpolationAngularVelocity(), m_localTime * body.GetHitFraction(), out interpolatedTransform);
				body.GetMotionState().SetWorldTransform(ref interpolatedTransform);
			}
		}
	}

	public override void AddConstraint(TypedConstraint constraint, bool disableCollisionsBetweenLinkedBodies)
	{
		//if (constraint is ConeTwistConstraint)
		//if (constraint is HingeConstraint)
		//{
		//    return;
		//}
		m_constraints.Add(constraint);
		if (disableCollisionsBetweenLinkedBodies)
		{
			constraint.GetRigidBodyA().AddConstraintRef(constraint);
			constraint.GetRigidBodyB().AddConstraintRef(constraint);
		}
	}

	public override void RemoveConstraint(TypedConstraint constraint)
	{
		m_constraints.Remove(constraint);
		constraint.GetRigidBodyA().RemoveConstraintRef(constraint);
		constraint.GetRigidBodyB().RemoveConstraintRef(constraint);
	}

	public override void AddAction(IActionInterface action)
	{
		m_actions.Add(action);
	}

	public override void RemoveAction(IActionInterface action)
	{
		m_actions.Remove(action);
	}

	public SimulationIslandManager GetSimulationIslandManager()
	{
		return m_islandManager;
	}

	public CollisionWorld GetCollisionWorld()
	{
		return this;
	}

	public override void SetGravity(IndexedVector3 gravity)
	{
		SetGravity(ref gravity);
	}

	public override void SetGravity(ref IndexedVector3 gravity)
	{
		m_gravity = gravity;
		int length = m_nonStaticRigidBodies.Count;
		for (int i = 0; i < length; ++i)
		{
			RigidBody body = m_nonStaticRigidBodies[i];
			if (body.IsActive() && ((body.GetFlags() & RigidBodyFlags.BT_DISABLE_WORLD_GRAVITY) == 0))
			{
				body.SetGravity(ref gravity);
			}
		}
	}

	public override IndexedVector3 GetGravity()
	{
		return m_gravity;
	}

	public override void AddCollisionObject(CollisionObject collisionObject)
	{
		AddCollisionObject(collisionObject, CollisionFilterGroups.StaticFilter, CollisionFilterGroups.AllFilter ^ CollisionFilterGroups.StaticFilter);
	}

	public override void AddCollisionObject(CollisionObject collisionObject, CollisionFilterGroups collisionFilterGroup, CollisionFilterGroups collisionFilterMask)
	{
		base.AddCollisionObject(collisionObject, collisionFilterGroup, collisionFilterMask);
	}

	public override void RemoveCollisionObject(CollisionObject collisionObject)
	{
		RigidBody body = RigidBody.Upcast(collisionObject);
		if (body != null)
		{
			RemoveRigidBody(body);
		}
		else
		{
			base.RemoveCollisionObject(collisionObject);
		}
	}

	public void SetSynchronizeAllMotionStates(bool synchronizeAll)
	{
		m_synchronizeAllMotionStates = synchronizeAll;
	}

	public bool GetSynchronizeAllMotionStates()
	{
		return m_synchronizeAllMotionStates;
	}

	public override void AddRigidBody(RigidBody body)
	{
		if (!body.IsStaticOrKinematicObject() && 0 == (body.GetFlags() & RigidBodyFlags.BT_DISABLE_WORLD_GRAVITY))
		{
			body.SetGravity(ref m_gravity);
		}

		if (body.GetCollisionShape() != null)
		{
			if (!body.IsStaticObject())
			{
				m_nonStaticRigidBodies.Add(body);
			}
			else
			{
				body.SetActivationState(ActivationState.ISLAND_SLEEPING);
			}

			bool isDynamic = !(body.IsStaticObject() || body.IsKinematicObject());
			CollisionFilterGroups collisionFilterGroup = isDynamic ? CollisionFilterGroups.DefaultFilter : CollisionFilterGroups.StaticFilter;
			CollisionFilterGroups collisionFilterMask = isDynamic ? CollisionFilterGroups.AllFilter : (CollisionFilterGroups.AllFilter ^ CollisionFilterGroups.StaticFilter);
			AddCollisionObject(body, collisionFilterGroup, collisionFilterMask);
		}
	}

	public override void AddRigidBody(RigidBody body, CollisionFilterGroups group, CollisionFilterGroups mask)
	{
		if (!body.IsStaticOrKinematicObject() && 0 == (body.GetFlags() & RigidBodyFlags.BT_DISABLE_WORLD_GRAVITY))
		{
			body.SetGravity(ref m_gravity);
		}

		if (body.GetCollisionShape() != null)
		{
			if (!body.IsStaticObject())
			{
				if (!m_nonStaticRigidBodies.Contains(body))
				{
					m_nonStaticRigidBodies.Add(body);
				}
			}
			else
			{
				body.SetActivationState(ActivationState.ISLAND_SLEEPING);
			}

			AddCollisionObject(body, group, mask);
		}
	}

	public override void RemoveRigidBody(RigidBody body)
	{
		m_nonStaticRigidBodies.Remove(body);
		base.RemoveCollisionObject(body);
	}

	public override void DebugDrawWorld()
	{
		BulletGlobals.StartProfile("debugDrawWorld");
		base.DebugDrawWorld();
		if (GetDebugDrawer() != null)
		{
			DebugDrawModes mode = GetDebugDrawer().GetDebugMode();
			if ((mode & (DebugDrawModes.DBG_DrawConstraints | DebugDrawModes.DBG_DrawConstraintLimits)) != 0)
			{
				for (int i = GetNumConstraints() - 1; i >= 0; i--)
				{
					TypedConstraint constraint = GetConstraint(i);
					DrawHelper.DebugDrawConstraint(constraint, GetDebugDrawer());
				}
			}

			if (mode != 0)
			{
				int actionsCount = m_actions.Count;
				for (int i = 0; i < actionsCount; ++i)
				{
					m_actions[i].DebugDraw(m_debugDrawer);
				}
			}
		}

		BulletGlobals.StopProfile();
	}

	public override void SetConstraintSolver(IConstraintSolver solver)
	{
		//if (m_ownsConstraintSolver)
		//{
		//    btAlignedFree( m_constraintSolver);
		//}
		m_ownsConstraintSolver = false;
		m_constraintSolver = solver;
	}

	public override IConstraintSolver GetConstraintSolver()
	{
		return m_constraintSolver;
	}

	public override int GetNumConstraints()
	{
		return m_constraints.Count;
	}

	public override TypedConstraint GetConstraint(int index)
	{
		return m_constraints[index];
	}

	public override DynamicsWorldType GetWorldType()
	{
		return DynamicsWorldType.BT_DISCRETE_DYNAMICS_WORLD;
	}

	public override void ClearForces()
	{
		int length = m_nonStaticRigidBodies.Count;
		for (int i = 0; i < length; ++i)
		{
			m_nonStaticRigidBodies[i].ClearForces();
		}
	}

	public virtual void ApplyGravity()
	{
		int length = m_nonStaticRigidBodies.Count;
		for (int i = 0; i < length; ++i)
		{
			RigidBody body = m_nonStaticRigidBodies[i];
			if (body != null && body.IsActive())
			{
				body.ApplyGravity();
			}
		}
	}

	public virtual void SetNumTasks(int numTasks)
	{
	//(void) numTasks;
	}

	public virtual void UpdateVehicles(float timeStep)
	{
		UpdateActions(timeStep);
	}

	public override void AddVehicle(IActionInterface vehicle)
	{
		AddAction(vehicle);
	}

	public override void RemoveVehicle(IActionInterface vehicle)
	{
		RemoveAction(vehicle);
	}

	public override void AddCharacter(IActionInterface character)
	{
		AddAction(character);
	}

	public override void RemoveCharacter(IActionInterface character)
	{
		RemoveAction(character);
	}

	protected virtual void PredictUnconstraintMotion(float timeStep)
	{
		BulletGlobals.StartProfile("predictUnconstraintMotion");
		int length = m_nonStaticRigidBodies.Count;
		//for (int i = 0; i < length;i++)
		for (int i = 0; i < m_nonStaticRigidBodies.Count; i++)
		{
			RigidBody body = m_nonStaticRigidBodies[i];
			if (!body.IsStaticOrKinematicObject())
			{
				body.IntegrateVelocities(timeStep);
				//dampingF
				body.ApplyDamping(timeStep);
				IndexedMatrix temp;
				body.PredictIntegratedTransform(timeStep, out temp);
				body.SetInterpolationWorldTransform(ref temp);
			}
		}

		BulletGlobals.StopProfile();
	}

	protected virtual void IntegrateTransforms(float timeStep)
	{
		BulletGlobals.StartProfile("integrateTransforms");
		IndexedMatrix predictedTrans;
		int length = m_nonStaticRigidBodies.Count;
		for (int i = 0; i < length; ++i)
		{
			RigidBody body = m_nonStaticRigidBodies[i];
			if (body != null)
			{
				body.SetHitFraction(1f);
				if (body.IsActive() && (!body.IsStaticOrKinematicObject()))
				{
					body.PredictIntegratedTransform(timeStep, out predictedTrans);
					float squareMotion = (predictedTrans._origin - body.GetWorldTransform()._origin).LengthSquared();
					//if (body.GetCcdSquareMotionThreshold() != 0 && body.GetCcdSquareMotionThreshold() < squareMotion)
					if (GetDispatchInfo().m_useContinuous && body.GetCcdSquareMotionThreshold() != 0.0f && body.GetCcdSquareMotionThreshold() < squareMotion)
					{
						BulletGlobals.StartProfile("CCD motion clamping");
						if (body.GetCollisionShape().IsConvex())
						{
							gNumClampedCcdMotions++;
							using (ClosestNotMeConvexResultCallback sweepResults = BulletGlobals.ClosestNotMeConvexResultCallbackPool.Get())
							{
								sweepResults.Initialize(body, body.GetWorldTransform()._origin, predictedTrans._origin, GetBroadphase().GetOverlappingPairCache(), GetDispatcher());
								//btConvexShape* convexShape = static_cast<btConvexShape*>(body.GetCollisionShape());
								SphereShape tmpSphere = BulletGlobals.SphereShapePool.Get();
								tmpSphere.Initialize(body.GetCcdSweptSphereRadius()); //btConvexShape* convexShape = static_cast<btConvexShape*>(body.GetCollisionShape());
								sweepResults.m_allowedPenetration = GetDispatchInfo().GetAllowedCcdPenetration();
								sweepResults.m_collisionFilterGroup = body.GetBroadphaseProxy().m_collisionFilterGroup;
								sweepResults.m_collisionFilterMask = body.GetBroadphaseProxy().m_collisionFilterMask;
								IndexedMatrix modifiedPredictedTrans = predictedTrans;
								modifiedPredictedTrans._basis = body.GetWorldTransform()._basis;
								modifiedPredictedTrans._origin = predictedTrans._origin;
								ConvexSweepTest(tmpSphere, body.GetWorldTransform(), modifiedPredictedTrans, sweepResults, 0f);
								if (sweepResults.HasHit() && (sweepResults.m_closestHitFraction < 1.0f))
								{
									//printf("clamped integration to hit fraction = %f\n",fraction);
									body.SetHitFraction(sweepResults.m_closestHitFraction);
									body.PredictIntegratedTransform(timeStep * body.GetHitFraction(), out predictedTrans);
									body.SetHitFraction(0.0f);
									body.ProceedToTransform(ref predictedTrans);
									//response  between two dynamic objects without friction, assuming 0 penetration depth
									float appliedImpulse = 0.0f;
									float depth = 0.0f;
									appliedImpulse = ContactConstraint.ResolveSingleCollision(body, sweepResults.m_hitCollisionObject, ref sweepResults.m_hitPointWorld, ref sweepResults.m_hitNormalWorld, GetSolverInfo(), depth);
									continue;
								}

								BulletGlobals.SphereShapePool.Free(tmpSphere);
							}
						}

						BulletGlobals.StopProfile();
					}

					body.ProceedToTransform(ref predictedTrans);
				}
			}
		}
	}

	protected virtual void CalculateSimulationIslands()
	{
		BulletGlobals.StartProfile("calculateSimulationIslands");
		GetSimulationIslandManager().UpdateActivationState(GetCollisionWorld(), GetCollisionWorld().GetDispatcher());
		{
			int length = m_constraints.Count;
			for (int i = 0; i < length; ++i)
			{
				TypedConstraint constraint = m_constraints[i];
				RigidBody colObj0 = constraint.GetRigidBodyA();
				RigidBody colObj1 = constraint.GetRigidBodyB();
				if (((colObj0 != null) && (!colObj0.IsStaticOrKinematicObject())) && ((colObj1 != null) && (!colObj1.IsStaticOrKinematicObject())))
				{
					if (colObj0.IsActive() || colObj1.IsActive())
					{
						GetSimulationIslandManager().GetUnionFind().Unite((colObj0).GetIslandTag(), (colObj1).GetIslandTag());
					}
				}
			}
		}

		//Store the island id in each body
		GetSimulationIslandManager().StoreIslandActivationState(GetCollisionWorld());
		BulletGlobals.StopProfile();
	}

	protected virtual void SolveConstraints(ContactSolverInfo solverInfo)
	{
		//sorted version of all btTypedConstraint, based on islandId
		m_sortedConstraints.Resize(m_constraints.Count);
		int numConstraints = GetNumConstraints();
		for (int i = 0; i < numConstraints; i++)
		{
			m_sortedConstraints[i] = m_constraints[i];
		}

		if (numConstraints > 1)
		{
			//sortedConstraints.quickSort(btSortConstraintOnIslandPredicate());
			// If this sort is removed then the constraint gets twitchy...
			//m_sortedConstraints.Sort(m_islandSortPredicate);
			m_sortedConstraints.QuickSort(m_islandQuickSortPredicate);
		}

		//	btAssert(0);
		if (m_solverIslandCallback == null)
		{
			m_solverIslandCallback = new InplaceSolverIslandCallback(solverInfo, m_constraintSolver, m_sortedConstraints, GetNumConstraints(), m_debugDrawer, m_dispatcher1);
		}
		else
		{
			m_solverIslandCallback.Setup(solverInfo, m_sortedConstraints, numConstraints, m_debugDrawer);
		}

		m_constraintSolver.PrepareSolve(GetCollisionWorld().GetNumCollisionObjects(), GetCollisionWorld().GetDispatcher().GetNumManifolds());
		m_islandManager.BuildAndProcessIslands(GetCollisionWorld().GetDispatcher(), GetCollisionWorld(), m_solverIslandCallback);
		m_solverIslandCallback.ProcessConstraints();
		m_constraintSolver.AllSolved(solverInfo, m_debugDrawer);
	}

	protected void UpdateActivationState(float timeStep)
	{
		BulletGlobals.StartProfile("updateActivationState");
		int length = m_nonStaticRigidBodies.Count;
		for (int i = 0; i < length; ++i)
		{
			RigidBody body = m_nonStaticRigidBodies[i];
			if (body != null)
			{
				body.UpdateDeactivation(timeStep);
				if (body.WantsSleeping())
				{
					if (body.IsStaticOrKinematicObject())
					{
						body.SetActivationState(ActivationState.ISLAND_SLEEPING);
					}
					else
					{
						if (body.GetActivationState() == ActivationState.ACTIVE_TAG)
						{
							body.SetActivationState(ActivationState.WANTS_DEACTIVATION);
						}

						if (body.GetActivationState() == ActivationState.ISLAND_SLEEPING)
						{
							IndexedVector3 zero = IndexedVector3.Zero;
							body.SetAngularVelocity(ref zero);
							body.SetLinearVelocity(ref zero);
						}
					}
				}
				else
				{
					if (body.GetActivationState() != ActivationState.DISABLE_DEACTIVATION)
						body.SetActivationState(ActivationState.ACTIVE_TAG);
				}
			}
		}

		BulletGlobals.StopProfile();
	}

	protected void UpdateActions(float timeStep)
	{
		BulletGlobals.StartProfile("updateActions");
		int length = m_actions.Count;
		for (int i = 0; i < length; ++i)
		{
			m_actions[i].UpdateAction(this, timeStep);
		}

		BulletGlobals.StopProfile();
	}

	protected void StartProfiling(float timeStep)
	{
		BulletGlobals.ResetProfile();
	}

	protected virtual void InternalSingleStepSimulation(float timeStep)
	{
		BulletGlobals.StartProfile("internalSingleStepSimulation");
		if (null != m_internalPreTickCallback)
		{
			m_internalPreTickCallback.InternalTickCallback(this, timeStep);
		}

		PredictUnconstraintMotion(timeStep);
		DispatcherInfo dispatchInfo = GetDispatchInfo();
		dispatchInfo.SetTimeStep(timeStep);
		dispatchInfo.SetStepCount(0);
		dispatchInfo.SetDebugDraw(GetDebugDrawer());
		PerformDiscreteCollisionDetection();
		CalculateSimulationIslands();
		GetSolverInfo().m_timeStep = timeStep;
		SolveConstraints(GetSolverInfo());
		IntegrateTransforms(timeStep);
		UpdateActions(timeStep);
		UpdateActivationState(timeStep);
		if (m_internalTickCallback != null)
		{
			m_internalTickCallback.InternalTickCallback(this, timeStep);
		}

		BulletGlobals.StopProfile();
	}

	protected virtual void SaveKinematicState(float timeStep)
	{
		int length = m_collisionObjects.Count;
		for (int i = 0; i < length; ++i)
		{
			RigidBody body = RigidBody.Upcast(m_collisionObjects[i]);
			if (body != null && body.GetActivationState() != ActivationState.ISLAND_SLEEPING)
			{
				if (body.IsKinematicObject())
				{
					//to calculate velocities next frame
					body.SaveKinematicState(timeStep);
				}
			}
		}
	}

	public static int GetConstraintIslandId(TypedConstraint lhs)
	{
		int islandId;
		CollisionObject rcolObj0 = lhs.GetRigidBodyA();
		CollisionObject rcolObj1 = lhs.GetRigidBodyB();
		islandId = rcolObj0.GetIslandTag() >= 0 ? rcolObj0.GetIslandTag() : rcolObj1.GetIslandTag();
		return islandId;
	}

	protected IConstraintSolver m_constraintSolver;
	protected InplaceSolverIslandCallback m_solverIslandCallback;
	protected SimulationIslandManager m_islandManager;
	protected ObjectArray<TypedConstraint> m_constraints;
	protected ObjectArray<TypedConstraint> m_sortedConstraints;
	protected SortConstraintOnIslandPredicate m_islandSortPredicate;
	protected QuickSortConstraintOnIslandPredicate m_islandQuickSortPredicate;
	protected ObjectArray<RigidBody> m_nonStaticRigidBodies;
	protected IndexedVector3 m_gravity;
	//for variable timesteps
	protected float m_localTime;
	//for variable timesteps
	protected bool m_ownsIslandManager;
	protected bool m_ownsConstraintSolver;
	protected bool m_synchronizeAllMotionStates;
	protected IList<IActionInterface> m_actions;
	protected int m_profileTimings;
	protected int gNumClampedCcdMotions;
	protected const float s_fixedTimeStep = 1f / 60f;
//public const float s_fixedTimeStep = 0.016666668f;
//protected float s_fixedTimeStep2 = 1 / 60;
//protected float s_fixedTimeStep3 = (float)(1.0 / 60.0);
//protected float s_fixedTimeStep4 = 0.016666668f;
}