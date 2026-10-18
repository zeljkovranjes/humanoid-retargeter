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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class BroadphasePair : IComparable
{
	public BroadphasePair()
	{
		m_pProxy0 = null;
		m_pProxy1 = null;
		m_algorithm = null;
		m_internalInfo1 = null;
	}

	public BroadphasePair(ref BroadphasePair other)
	{
		m_pProxy0 = other.m_pProxy0;
		m_pProxy1 = other.m_pProxy1;
		m_algorithm = other.m_algorithm;
		m_internalInfo1 = other.m_internalInfo1;
	}

	public BroadphasePair(BroadphaseProxy proxy0, BroadphaseProxy proxy1)
	{
		//keep them sorted, so the std::set operations work
		if (proxy0.GetUid() < proxy1.GetUid())
		{
			m_pProxy0 = proxy0;
			m_pProxy1 = proxy1;
		}
		else
		{
			m_pProxy0 = proxy1;
			m_pProxy1 = proxy0;
		}

		m_algorithm = null;
		m_internalInfo1 = null;
	}

	public override bool Equals(object obj)
	{
		if (this == obj)
		{
			return true;
		}
		else
		{
			if (obj is BroadphasePair)
			{
				BroadphasePair that = (BroadphasePair)obj;
				return m_pProxy0 == that.m_pProxy0 && m_pProxy1 == that.m_pProxy1;
			}
		}

		return false;
	}

	public static bool IsLessThen(BroadphasePair a, BroadphasePair b)
	{
		int uidA0 = a.m_pProxy0 != null ? a.m_pProxy0.GetUid() : -1;
		int uidB0 = b.m_pProxy0 != null ? b.m_pProxy0.GetUid() : -1;
		if (uidA0 > uidB0)
		{
			return true;
		}

		int uidA1 = a.m_pProxy1 != null ? a.m_pProxy1.GetUid() : -1;
		int uidB1 = b.m_pProxy1 != null ? b.m_pProxy1.GetUid() : -1;
		int colAlgIdA = a.m_algorithm != null ? a.m_algorithm.colAgorithmId : 0;
		int colAlgIdB = b.m_algorithm != null ? b.m_algorithm.colAgorithmId : 0;
		return (a.m_pProxy0 == b.m_pProxy0 && uidA1 > uidB1) || (a.m_pProxy0 == b.m_pProxy0 && a.m_pProxy1 == b.m_pProxy1) && colAlgIdA > colAlgIdB;
	}

	public int CompareTo(object obj)
	{
		return (IsLessThen(this, (BroadphasePair)obj) ? -1 : 1);
	}

	public int m_index; // if we maintain this then it saves a lot on searches...
	public BroadphaseProxy m_pProxy0;
	public BroadphaseProxy m_pProxy1;
	public CollisionAlgorithm m_algorithm;
	//don't use this data, it will be removed in future version.
	public Object m_internalInfo1;
	public int m_internalTmpValue;
}