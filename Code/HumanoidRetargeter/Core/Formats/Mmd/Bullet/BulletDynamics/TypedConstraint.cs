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

internal class TypedConstraint : TypedObject
{
	private static RigidBody s_fixed;
	public const float DEFAULT_DEBUGDRAW_SIZE = 0.3f;
	public String m_debugName;
	private int m_userConstraintType;
	// warning - these are unioned in the c++ version
	private int m_userConstraintId;
	private Object m_userConstraintPtr;
	private float m_breakingImpulseThreshold;
	private bool m_isEnabled;
	private bool m_needsFeedback;
	private int m_overrideNumSolverIterations;
	protected TypedConstraintType m_constraintType;
	protected RigidBody m_rbA;
	protected RigidBody m_rbB;
	protected float m_appliedImpulse;
	protected float m_dbgDrawSize;
	//public TypedConstraint(TypedConstraintType type)
	//{
	//    m_userConstraintType = -1;
	//    m_userConstraintId = -1;
	//    m_constraintType = type;
	//    m_rbA = s_fixed;
	//    m_rbB = s_fixed;
	//    m_appliedImpulse = 0f;
	//    m_dbgDrawSize = DEFAULT_DEBUGDRAW_SIZE;
	//    {
	//        s_fixed.setMassProps(0f,IndexedVector3.Zero);
	//    }
	//}
	public TypedConstraint() : base(-1)
	{
	}

	public TypedConstraint(TypedConstraintType type, RigidBody rbA) : base((int)type)
	{
		m_userConstraintType = -1;
		m_userConstraintId = -1;
		m_constraintType = type;
		m_rbA = rbA;
		m_rbB = GetFixedBody();
		m_appliedImpulse = 0f;
		m_breakingImpulseThreshold = MathUtil.SIMD_INFINITY;
		m_isEnabled = true;
		m_dbgDrawSize = DEFAULT_DEBUGDRAW_SIZE;
		{
			s_fixed.SetMassProps(0f, IndexedVector3.Zero);
		}
	}

	public TypedConstraint(TypedConstraintType type, RigidBody rbA, RigidBody rbB) : base((int)type)
	{
		m_userConstraintType = -1;
		m_userConstraintId = -1;
		m_constraintType = type;
		m_rbA = rbA;
		m_rbB = rbB;
		m_appliedImpulse = 0f;
		m_breakingImpulseThreshold = MathUtil.SIMD_INFINITY;
		m_isEnabled = true;
		m_dbgDrawSize = DEFAULT_DEBUGDRAW_SIZE;
		{
			GetFixedBody().SetMassProps(0f, IndexedVector3.Zero);
		}
	}

	public virtual void SetupSolverConstraint(IList<SolverConstraint> ca, int solverBodyA, int solverBodyB, float timeStep)
	{
	}

	public virtual void GetInfo1(ConstraintInfo1 info)
	{
	}

	public virtual void GetInfo2(ConstraintInfo2 info)
	{
	}

	public void InternalSetAppliedImpulse(float appliedImpulse)
	{
		m_appliedImpulse = appliedImpulse;
	}

	public float InternalGetAppliedImpulse()
	{
		return m_appliedImpulse;
	}

	public int GetOverrideNumSolverIterations()
	{
		return m_overrideNumSolverIterations;
	}

	public void SetOverrideNumSolverIterations(int overideNumIterations)
	{
		m_overrideNumSolverIterations = overideNumIterations;
	}

	protected float GetMotorFactor(float pos, float lowLim, float uppLim, float vel, float timeFact)
	{
		if (lowLim > uppLim)
		{
			return 1f;
		}
		else if (MathUtil.CompareFloat(lowLim, uppLim))
		{
			return 0f;
		}

		float lim_fact = 1.0f;
		float delta_max = vel / timeFact;
		if (delta_max < 0.0f)
		{
			if ((pos >= lowLim) && (pos < (lowLim - delta_max)))
			{
				lim_fact = (lowLim - pos) / delta_max;
			}
			else if (pos < lowLim)
			{
				lim_fact = 0.0f;
			}
			else
			{
				lim_fact = 1f;
			}
		}
		else if (delta_max > 0.0f)
		{
			if ((pos <= uppLim) && (pos > (uppLim - delta_max)))
			{
				lim_fact = (uppLim - pos) / delta_max;
			}
			else if (pos > uppLim)
			{
				lim_fact = 0.0f;
			}
			else
			{
				lim_fact = 1.0f;
			}
		}
		else
		{
			lim_fact = 0.0f;
		}

		return lim_fact;
	}

	public static RigidBody GetFixedBody()
	{
		//static btRigidBody s_fixed(0, 0,0);
		//s_fixed.setMassProps(float(0.),btVector3(float(0.),float(0.),float(0.)));
		if (s_fixed == null)
		{
			s_fixed = new RigidBody(0f, null, null, IndexedVector3.Zero);
		}

		IndexedVector3 inertia = IndexedVector3.Zero;
		s_fixed.SetMassProps(0f, ref inertia);
		return s_fixed;
	}

	public RigidBody GetRigidBodyA()
	{
		return m_rbA;
	}

	public RigidBody GetRigidBodyB()
	{
		return m_rbB;
	}

	public int GetUserConstraintType()
	{
		return m_userConstraintType;
	}

	public void SetUserConstraintType(int userConstraintType)
	{
		m_userConstraintType = userConstraintType;
	}

	public void SetUserConstraintId(int uid)
	{
		m_userConstraintId = uid;
	}

	public int GetUserConstraintId()
	{
		return m_userConstraintId;
	}

	public int GetUid()
	{
		return m_userConstraintId;
	}

	public bool NeedsFeedback()
	{
		return m_needsFeedback;
	}

	public void EnableFeedback(bool needsFeedback)
	{
		m_needsFeedback = needsFeedback;
	}

	public float GetAppliedImpulse()
	{
		return m_appliedImpulse;
	}

	public TypedConstraintType GetConstraintType()
	{
		return m_constraintType;
	}

	public void SetDbgDrawSize(float dbgDrawSize)
	{
		m_dbgDrawSize = dbgDrawSize;
	}

	public float GetDbgDrawSize()
	{
		return m_dbgDrawSize;
	}

	// returns angle in range [-SIMD_2_PI, SIMD_2_PI], closest to one of the limits
	// all arguments should be normalized angles (i.e. in range [-SIMD_PI, SIMD_PI])
	public float AdjustAngleToLimits(float angleInRadians, float angleLowerLimitInRadians, float angleUpperLimitInRadians)
	{
		if (angleLowerLimitInRadians >= angleUpperLimitInRadians)
		{
			return angleInRadians;
		}
		else if (angleInRadians < angleLowerLimitInRadians)
		{
			float diffLo = Math.Abs(MathUtil.NormalizeAngle(angleLowerLimitInRadians - angleInRadians)); // this is positive
			float diffHi = Math.Abs(MathUtil.NormalizeAngle(angleUpperLimitInRadians - angleInRadians));
			return (diffLo < diffHi) ? angleInRadians : (angleInRadians + MathUtil.SIMD_2_PI);
		}
		else if (angleInRadians > angleUpperLimitInRadians)
		{
			float diffHi = Math.Abs(MathUtil.NormalizeAngle(angleInRadians - angleUpperLimitInRadians)); // this is positive
			float diffLo = Math.Abs(MathUtil.NormalizeAngle(angleInRadians - angleLowerLimitInRadians));
			return (diffLo < diffHi) ? (angleInRadians - MathUtil.SIMD_2_PI) : angleInRadians;
		}
		else
		{
			return angleInRadians;
		}
	}

	public virtual void Cleanup()
	{
	}

	public float GetBreakingImpulseThreshold()
	{
		return m_breakingImpulseThreshold;
	}

	public void SetBreakingImpulseThreshold(float threshold)
	{
		m_breakingImpulseThreshold = threshold;
	}

	public bool IsEnabled()
	{
		return m_isEnabled;
	}

	public void SetEnabled(bool enabled)
	{
		m_isEnabled = enabled;
	}

	public virtual void SolveConstraintObsolete(RigidBody bodyA, RigidBody bodyB, float timeStep)
	{
	}

	public virtual void BuildJacobian()
	{
	}
}