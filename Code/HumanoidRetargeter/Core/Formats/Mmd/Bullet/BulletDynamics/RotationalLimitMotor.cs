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

internal class RotationalLimitMotor
{
	//! limit_parameters
	//!@{
	public float m_loLimit; //!< joint limit
	public float m_hiLimit; //!< joint limit
	public float m_targetVelocity; //!< target motor velocity
	public float m_maxMotorForce; //!< max force on motor
	public float m_maxLimitForce; //!< max force on limit
	public float m_damping; //!< Damping.
	public float m_limitSoftness; //! Relaxation factor
	public float m_normalCFM; //!< Constraint force mixing factor
	public float m_stopERP; //!< Error tolerance factor when joint is at limit
	public float m_stopCFM; //!< Constraint force mixing factor when joint is at limit
	public float m_bounce; //!< restitution factor
	public bool m_enableMotor;
	//!@}
	//! temp_variables
	//!@{
	public float m_currentLimitError; //!  How much is violated this limit
	public float m_currentPosition; //!  current value of angle
	public int m_currentLimit; //!< 0=free, 1=at lo limit, 2=at hi limit
	public float m_accumulatedImpulse;
	//!@}
	public RotationalLimitMotor()
	{
		m_accumulatedImpulse = 0f;
		m_targetVelocity = 0;
		m_maxMotorForce = 0.1f;
		m_maxLimitForce = 300.0f;
		m_loLimit = 1.0f;
		m_hiLimit = -1.0f;
		m_normalCFM = 0.0f;
		m_stopERP = 0.2f;
		m_stopCFM = 0.0f;
		m_bounce = 0.0f;
		m_damping = 1.0f;
		m_limitSoftness = 0.5f;
		m_currentLimit = 0;
		m_currentLimitError = 0;
		m_enableMotor = false;
	}

	public RotationalLimitMotor(RotationalLimitMotor limot)
	{
		m_targetVelocity = limot.m_targetVelocity;
		m_maxMotorForce = limot.m_maxMotorForce;
		m_limitSoftness = limot.m_limitSoftness;
		m_loLimit = limot.m_loLimit;
		m_hiLimit = limot.m_hiLimit;
		m_normalCFM = limot.m_normalCFM;
		m_stopERP = limot.m_stopERP;
		m_stopCFM = limot.m_stopCFM;
		m_bounce = limot.m_bounce;
		m_currentLimit = limot.m_currentLimit;
		m_currentLimitError = limot.m_currentLimitError;
		m_enableMotor = limot.m_enableMotor;
	}

	//! Is limited
	public bool IsLimited()
	{
		if (m_loLimit > m_hiLimit)
			return false;
		return true;
	}

	//! Need apply correction
	public bool NeedApplyTorques()
	{
		if (m_currentLimit == 0 && m_enableMotor == false)
			return false;
		return true;
	}

	//! calculates  error
	/*!
		calculates m_currentLimit and m_currentLimitError.
		*/
	public int TestLimitValue(float test_value)
	{
		if (m_loLimit > m_hiLimit)
		{
			m_currentLimit = 0; //Free from violation
			return 0;
		}

		if (test_value < m_loLimit)
		{
			m_currentLimit = 1; //low limit violation
			m_currentLimitError = test_value - m_loLimit;
			if (m_currentLimitError > MathUtil.SIMD_PI)
			{
				m_currentLimitError -= MathUtil.SIMD_2_PI;
			}
			else if (m_currentLimitError < -MathUtil.SIMD_PI)
			{
				m_currentLimitError += MathUtil.SIMD_2_PI;
			}

			return 1;
		}
		else if (test_value > m_hiLimit)
		{
			m_currentLimit = 2; //High limit violation
			m_currentLimitError = test_value - m_hiLimit;
			if (m_currentLimitError > MathUtil.SIMD_PI)
			{
				m_currentLimitError -= MathUtil.SIMD_2_PI;
			}
			else if (m_currentLimitError < -MathUtil.SIMD_PI)
			{
				m_currentLimitError += MathUtil.SIMD_2_PI;
			}

			return 2;
		};
		m_currentLimit = 0; //Free from violation
		return 0;
	}

	//! apply the correction impulses for two bodies
	public float SolveAngularLimits(float timeStep, ref IndexedVector3 axis, float jacDiagABInv, RigidBody body0, RigidBody body1)
	{
		if (NeedApplyTorques() == false)
		{
			return 0.0f;
		}

		float target_velocity = m_targetVelocity;
		float maxMotorForce = m_maxMotorForce;
		//current error correction
		if (m_currentLimit != 0)
		{
			target_velocity = -m_stopERP * m_currentLimitError / (timeStep);
			maxMotorForce = m_maxLimitForce;
		}

		maxMotorForce *= timeStep;
		// current velocity difference
		IndexedVector3 angVelA = IndexedVector3.Zero;
		body0.InternalGetAngularVelocity(ref angVelA);
		IndexedVector3 angVelB = IndexedVector3.Zero;
		body1.InternalGetAngularVelocity(ref angVelB);
		IndexedVector3 vel_diff = angVelA - angVelB;
		float rel_vel = IndexedVector3.Dot(axis, vel_diff);
		// correction velocity
		float motor_relvel = m_limitSoftness * (target_velocity - m_damping * rel_vel);
		if (motor_relvel < MathUtil.SIMD_EPSILON && motor_relvel > -MathUtil.SIMD_EPSILON)
		{
			return 0.0f; //no need for applying force
		}

		// correction impulse
		float unclippedMotorImpulse = (1 + m_bounce) * motor_relvel * jacDiagABInv;
		// clip correction impulse
		float clippedMotorImpulse;
		if (unclippedMotorImpulse > 0.0f)
		{
			clippedMotorImpulse = unclippedMotorImpulse > maxMotorForce ? maxMotorForce : unclippedMotorImpulse;
		}
		else
		{
			clippedMotorImpulse = unclippedMotorImpulse < -maxMotorForce ? -maxMotorForce : unclippedMotorImpulse;
		}

		// sort with accumulated impulses
		float lo = float.MinValue;
		float hi = float.MaxValue;
		float oldaccumImpulse = m_accumulatedImpulse;
		float sum = oldaccumImpulse + clippedMotorImpulse;
		m_accumulatedImpulse = sum > hi ? 0f : sum < lo ? 0f : sum;
		clippedMotorImpulse = m_accumulatedImpulse - oldaccumImpulse;
		IndexedVector3 motorImp = clippedMotorImpulse * axis;
		//body0.applyTorqueImpulse(motorImp);
		//body1.applyTorqueImpulse(-motorImp);
		body0.InternalApplyImpulse(IndexedVector3.Zero, body0.GetInvInertiaTensorWorld() * axis, clippedMotorImpulse, "Generic6DoF body0");
		body1.InternalApplyImpulse(IndexedVector3.Zero, body1.GetInvInertiaTensorWorld() * axis, -clippedMotorImpulse, "Generic6DoF body1");
		return clippedMotorImpulse;
	}
}