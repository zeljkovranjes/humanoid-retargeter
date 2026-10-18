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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal class TranslationalLimitMotor
{
	public IndexedVector3 m_lowerLimit; //!< the constraint lower limits
	public IndexedVector3 m_upperLimit; //!< the constraint upper limits
	public IndexedVector3 m_accumulatedImpulse;
	public float m_limitSoftness; //!< Softness for linear limit
	public float m_damping; //!< Damping for linear limit
	public float m_restitution; //! Bounce parameter for linear limit
	public IndexedVector3 m_normalCFM; //!< Constraint force mixing factor
	public IndexedVector3 m_stopERP; //!< Error tolerance factor when joint is at limit
	public IndexedVector3 m_stopCFM; //!< Constraint force mixing factor when joint is at limit
	public bool[] m_enableMotor = new bool[3];
	public IndexedVector3 m_targetVelocity; //!< target motor velocity
	public IndexedVector3 m_maxMotorForce; //!< max force on motor
	public IndexedVector3 m_currentLimitError; //!  How much is violated this limit
	public IndexedVector3 m_currentLinearDiff; //!  Current relative offset of constraint frames
	public int[] m_currentLimit = new int[3]; //!< 0=free, 1=at lower limit, 2=at upper limit
	public TranslationalLimitMotor()
	{
		m_lowerLimit = IndexedVector3.Zero;
		m_upperLimit = IndexedVector3.Zero;
		m_accumulatedImpulse = IndexedVector3.Zero;
		m_normalCFM = IndexedVector3.Zero;
		m_stopERP = new IndexedVector3(0.2f, 0.2f, 0.2f);
		m_stopCFM = IndexedVector3.Zero;
		m_limitSoftness = 0.7f;
		m_damping = 1f;
		m_restitution = 0.5f;
		for (int i = 0; i < 3; i++)
		{
			m_enableMotor[i] = false;
		}

		m_targetVelocity = IndexedVector3.Zero;
		m_maxMotorForce = IndexedVector3.Zero;
	}

	public TranslationalLimitMotor(TranslationalLimitMotor other)
	{
		m_lowerLimit = other.m_lowerLimit;
		m_upperLimit = other.m_upperLimit;
		m_accumulatedImpulse = other.m_accumulatedImpulse;
		m_limitSoftness = other.m_limitSoftness;
		m_damping = other.m_damping;
		m_restitution = other.m_restitution;
		m_normalCFM = other.m_normalCFM;
		m_stopERP = other.m_stopERP;
		m_stopCFM = other.m_stopCFM;
		for (int i = 0; i < 3; i++)
		{
			m_enableMotor[i] = other.m_enableMotor[i];
		}

		m_targetVelocity = other.m_targetVelocity;
		m_maxMotorForce = other.m_maxMotorForce;
	}

	//! Test limit
	/*!
		- free means upper < lower,
		- locked means upper == lower
		- limited means upper > lower
		- limitIndex: first 3 are linear, next 3 are angular
		*/
	public bool IsLimited(int limitIndex)
	{
		return m_upperLimit[limitIndex] >= m_lowerLimit[limitIndex];
	}

	public bool NeedApplyForce(int limitIndex)
	{
		if (m_currentLimit[limitIndex] == 0 && m_enableMotor[limitIndex] == false)
		{
			return false;
		}

		return true;
	}

	public int TestLimitValue(int limitIndex, float test_value)
	{
		float loLimit = m_lowerLimit[limitIndex];
		float hiLimit = m_upperLimit[limitIndex];
		if (loLimit > hiLimit)
		{
			m_currentLimit[limitIndex] = 0; //Free from violation
			m_currentLimitError[limitIndex] = 0f;
			return 0;
		}

		if (test_value < loLimit)
		{
			m_currentLimit[limitIndex] = 2; //low limit violation
			m_currentLimitError[limitIndex] = test_value - loLimit;
			return 2;
		}
		else if (test_value > hiLimit)
		{
			m_currentLimit[limitIndex] = 1; //High limit violation
			m_currentLimitError[limitIndex] = test_value - hiLimit;
			return 1;
		};
		m_currentLimit[limitIndex] = 0; //Free from violation
		m_currentLimitError[limitIndex] = 0f;
		return 0;
	}

	public float SolveLinearAxis(float timeStep, float jacDiagABInv, RigidBody body1, ref IndexedVector3 pointInA, RigidBody body2, ref IndexedVector3 pointInB, int limit_index, ref IndexedVector3 axis_normal_on_a, ref IndexedVector3 anchorPos)
	{
		//    IndexedVector3 rel_pos1 = pointInA - body1.getCenterOfMassPosition();
		//    IndexedVector3 rel_pos2 = pointInB - body2.getCenterOfMassPosition();
		IndexedVector3 rel_pos1 = anchorPos - body1.GetCenterOfMassPosition();
		IndexedVector3 rel_pos2 = anchorPos - body2.GetCenterOfMassPosition();
		IndexedVector3 vel1 = IndexedVector3.Zero;
		body1.InternalGetVelocityInLocalPointObsolete(ref rel_pos1, ref vel1);
		IndexedVector3 vel2 = IndexedVector3.Zero;
		;
		body2.InternalGetVelocityInLocalPointObsolete(ref rel_pos2, ref vel2);
		IndexedVector3 vel = vel1 - vel2;
		float rel_vel = IndexedVector3.Dot(axis_normal_on_a, vel);
		//positional error (zeroth order error)
		float depth = -IndexedVector3.Dot((pointInA - pointInB), axis_normal_on_a);
		float lo = float.MinValue;
		float hi = float.MaxValue;
		float minLimit = m_lowerLimit[limit_index];
		float maxLimit = m_upperLimit[limit_index];
		//handle the limits
		if (minLimit < maxLimit)
		{
			{
				if (depth > maxLimit)
				{
					depth -= maxLimit;
					lo = 0f;
				}
				else
				{
					if (depth < minLimit)
					{
						depth -= minLimit;
						hi = 0f;
					}
					else
					{
						return 0.0f;
					}
				}
			}
		}

		float normalImpulse = m_limitSoftness * (m_restitution * depth / timeStep - m_damping * rel_vel) * jacDiagABInv;
		float oldNormalImpulse = m_accumulatedImpulse[limit_index];
		float sum = oldNormalImpulse + normalImpulse;
		m_accumulatedImpulse[limit_index] = (sum > hi ? 0f : sum < lo ? 0f : sum);
		normalImpulse = m_accumulatedImpulse[limit_index] - oldNormalImpulse;
		IndexedVector3 impulse_vector = axis_normal_on_a * normalImpulse;
		//body1.applyImpulse( impulse_vector, rel_pos1);
		//body2.applyImpulse(-impulse_vector, rel_pos2);
		IndexedVector3 ftorqueAxis1 = IndexedVector3.Cross(rel_pos1, axis_normal_on_a);
		IndexedVector3 ftorqueAxis2 = IndexedVector3.Cross(rel_pos2, axis_normal_on_a);
		body1.InternalApplyImpulse(axis_normal_on_a * body1.GetInvMass(), body1.GetInvInertiaTensorWorld() * ftorqueAxis1, normalImpulse, "Generic6DoF body1");
		body2.InternalApplyImpulse(axis_normal_on_a * body2.GetInvMass(), body2.GetInvInertiaTensorWorld() * ftorqueAxis2, -normalImpulse, "Generic6DoF body2");
		return normalImpulse;
	}
}