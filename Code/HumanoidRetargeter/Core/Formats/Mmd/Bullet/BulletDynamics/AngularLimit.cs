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

internal class AngularLimit
{
	private float m_center;
	private float m_halfRange;
	private float m_softness;
	private float m_biasFactor;
	private float m_relaxationFactor;
	private float m_correction;
	private float m_sign;
	private bool m_solveLimit;
	public AngularLimit()
	{
		m_center = 0.0f;
		m_halfRange = -1.0f;
		m_softness = 0.9f;
		m_biasFactor = 0.3f;
		m_relaxationFactor = 1.0f;
		m_correction = 0.0f;
		m_sign = 0.0f;
		m_solveLimit = false;
	}

	public void Set(float low, float high)
	{
		Set(low, high, 0.9f, 0.3f, 1.0f);
	}

	public void Set(float low, float high, float _softness, float _biasFactor, float _relaxationFactor)
	{
		m_halfRange = (high - low) / 2.0f;
		m_center = MathUtil.NormalizeAngle(low + m_halfRange);
		m_softness = _softness;
		m_biasFactor = _biasFactor;
		m_relaxationFactor = _relaxationFactor;
	}

	public void Test(float angle)
	{
		m_correction = 0.0f;
		m_sign = 0.0f;
		m_solveLimit = false;
		if (m_halfRange >= 0.0f)
		{
			float deviation = MathUtil.NormalizeAngle(angle - m_center);
			if (deviation < -m_halfRange)
			{
				m_solveLimit = true;
				m_correction = -(deviation + m_halfRange);
				m_sign = +1.0f;
			}
			else if (deviation > m_halfRange)
			{
				m_solveLimit = true;
				m_correction = m_halfRange - deviation;
				m_sign = -1.0f;
			}
		}
	}

	public float GetSoftness()
	{
		return m_softness;
	}

	public float GetBiasFactor()
	{
		return m_biasFactor;
	}

	public float GetRelaxationFactor()
	{
		return m_relaxationFactor;
	}

	public float GetCorrection()
	{
		return m_correction;
	}

	public float GetSign()
	{
		return m_sign;
	}

	public float GetHalfRange()
	{
		return m_halfRange;
	}

	public bool IsLimit()
	{
		return m_solveLimit;
	}

	public void Fit(ref float angle)
	{
		if (m_halfRange > 0.0f)
		{
			float relativeAngle = MathUtil.NormalizeAngle(angle - m_center);
			if (!MathUtil.CompareFloat(relativeAngle, m_halfRange))
			{
				if (relativeAngle > 0.0f)
				{
					angle = GetHigh();
				}
				else
				{
					angle = GetLow();
				}
			}
		}
	}

	public float GetError()
	{
		return m_correction * m_sign;
	}

	public float GetLow()
	{
		return MathUtil.NormalizeAngle(m_center - m_halfRange);
	}

	public float GetHigh()
	{
		return MathUtil.NormalizeAngle(m_center + m_halfRange);
	}
}