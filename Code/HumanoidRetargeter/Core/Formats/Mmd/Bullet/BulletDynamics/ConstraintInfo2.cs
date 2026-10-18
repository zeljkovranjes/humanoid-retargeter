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
using System.IO;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal class ConstraintInfo2
{
	// little dodgy...
	public const int maxConstraints = 6;
	// Workaround for the idea of having multiple solver constraints and row count.
	// This should be populated with a list of the SolverConstraint for a give Constraint. - MAN
	public SolverConstraint[] m_solverConstraints = new SolverConstraint[maxConstraints];
	public int m_numRows = 0;
	// integrator parameters: frames per second (1/stepsize), default error
	// reduction parameter (0..1).
	public float fps;
	public float erp;
	// findex vector for variables. see the LCP solver interface for a
	// description of what this does. this is set to -1 on entry.
	// note that the returned indexes are relative to the first index of
	// the constraint.
	public int findex;
	public int m_numIterations;
	//damping of the velocity
	public float m_damping;
	public void Reset()
	{
		for (int i = 0; i < maxConstraints; ++i)
		{
			m_solverConstraints[i] = null;
		}

		m_numRows = 0;
		fps = 0f;
		erp = 0;
		findex = 0;
		m_numIterations = 0;
		m_damping = 0f;
	}

	~ConstraintInfo2()
	{
		int ibreak = 0;
	}
}