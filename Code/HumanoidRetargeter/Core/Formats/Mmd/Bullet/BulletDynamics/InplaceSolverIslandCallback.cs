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

internal class InplaceSolverIslandCallback : IIslandCallback
{
	public ContactSolverInfo m_solverInfo;
	public IConstraintSolver m_solver;
	public ObjectArray<TypedConstraint> m_sortedConstraints;
	public int m_numConstraints;
	public IDebugDraw m_debugDrawer;
	public IDispatcher m_dispatcher;
	public ObjectArray<CollisionObject> m_bodies;
	public PersistentManifoldArray m_manifolds;
	public ObjectArray<TypedConstraint> m_constraints;
	public InplaceSolverIslandCallback(ContactSolverInfo solverInfo, IConstraintSolver solver, ObjectArray<TypedConstraint> sortedConstraints, int numConstraints, IDebugDraw debugDrawer, IDispatcher dispatcher)
	{
		m_solverInfo = solverInfo;
		m_solver = solver;
		m_sortedConstraints = sortedConstraints;
		m_numConstraints = numConstraints;
		m_debugDrawer = debugDrawer;
		m_dispatcher = dispatcher;
		m_bodies = new ObjectArray<CollisionObject>();
		m_manifolds = new PersistentManifoldArray();
		m_constraints = new ObjectArray<TypedConstraint>();
	}

	//InplaceSolverIslandCallback operator=(InplaceSolverIslandCallback& other)
	//{
	//    Debug.Assert(false);
	//    //(void)other;
	//    return *this;
	//}
	public void Setup(ContactSolverInfo solverInfo, ObjectArray<TypedConstraint> sortedConstraints, int numConstraints, IDebugDraw debugDrawer)
	{
		{
		}

		m_solverInfo = solverInfo;
		m_sortedConstraints = sortedConstraints;
		m_numConstraints = numConstraints;
		m_debugDrawer = debugDrawer;
		m_bodies.Resize(0);
		m_manifolds.Resize(0);
		m_constraints.Resize(0);
	}

	public virtual void ProcessIsland(ObjectArray<CollisionObject> bodies, int numBodies, PersistentManifoldArray manifolds, int startManifold, int numManifolds, int islandId)
	{
		if (islandId < 0)
		{
			if (numManifolds + m_numConstraints > 0)
			{
				m_solver.SolveGroup(bodies, numBodies, manifolds, startManifold, numManifolds, m_sortedConstraints, 0, m_numConstraints, m_solverInfo, m_debugDrawer, m_dispatcher);
			}
		}
		else
		{
			//also add all non-contact constraints/joints for this island
			int startConstraint = 0;
			int numCurConstraints = 0;
			int i = 0;
			//find the first constraint for this island
			for (i = 0; i < m_numConstraints; i++)
			{
				if (DiscreteDynamicsWorld.GetConstraintIslandId(m_sortedConstraints[i]) == islandId)
				{
					startConstraint = i;
					break;
				}
			}

			//count the number of constraints in this island
			for (; i < m_numConstraints; i++)
			{
				if (DiscreteDynamicsWorld.GetConstraintIslandId(m_sortedConstraints[i]) == islandId)
				{
					numCurConstraints++;
				}
			}

			if (m_solverInfo.m_minimumSolverBatchSize <= 1)
			{
				if (numManifolds + numCurConstraints > 0)
				{
					m_solver.SolveGroup(bodies, numBodies, manifolds, startManifold, numManifolds, m_sortedConstraints, startConstraint, numCurConstraints, m_solverInfo, m_debugDrawer, m_dispatcher);
				}
			}
			else
			{
				for (i = 0; i < numBodies; i++)
				{
					m_bodies.Add(bodies[i]);
				}

				int lastManifold = startManifold + numManifolds;
				for (i = startManifold; i < lastManifold; i++)
				{
					m_manifolds.Add(manifolds[i]);
				}

				int lastConstraint = startConstraint + numCurConstraints;
				for (i = startConstraint; i < lastConstraint; i++)
				{
					m_constraints.Add(m_sortedConstraints[i]);
				}

				if ((m_constraints.Count + m_manifolds.Count) > m_solverInfo.m_minimumSolverBatchSize)
				{
					ProcessConstraints();
				}
				else
				{
				//printf("deferred\n");
				}
			}
		}
	}

	public void ProcessConstraints()
	{
		if (m_manifolds.Count + m_constraints.Count > 0)
		{
			m_solver.SolveGroup(m_bodies, m_bodies.Count, m_manifolds, 0, m_manifolds.Count, m_constraints, 0, m_constraints.Count, m_solverInfo, m_debugDrawer, m_dispatcher);
		}

		m_bodies.Clear();
		m_manifolds.Clear();
		m_constraints.Clear();
	}
}