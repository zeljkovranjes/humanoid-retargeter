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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class SimpleBroadphaseProxy : BroadphaseProxy
{
	public SimpleBroadphaseProxy(int position)
	{
		m_position = position;
	}

	public SimpleBroadphaseProxy(int position, ref IndexedVector3 minpt, ref IndexedVector3 maxpt, BroadphaseNativeTypes shapeType, Object userPtr, CollisionFilterGroups collisionFilterGroup, CollisionFilterGroups collisionFilterMask, Object multiSapProxy) : base(ref minpt, ref maxpt, userPtr, collisionFilterGroup, collisionFilterMask, multiSapProxy)
	{
		// bit of a cheat/hack - store the position in the array as we can't find it for freehandle otherwise.
		m_position = position;
	}

	public int GetNextFree()
	{
		return m_nextFree;
	}

	public int GetPosition()
	{
		return m_position;
	}

	public void SetNextFree(int nextFree)
	{
		m_nextFree = nextFree;
	}

	private int m_nextFree;
	private int m_position;
}