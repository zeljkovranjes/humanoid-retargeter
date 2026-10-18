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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet;

internal class CProfileNode
{
	public CProfileNode(String name, CProfileNode parent, BasicProfileManager profileManager)
	{
		m_name = name;
		m_parent = parent;
		m_profileManager = profileManager;
		Reset();
	}

	public void Cleanup()
	{
		m_child = null;
		m_sibling = null;
	}

	public CProfileNode Get_Sub_Node(String name)
	{
		// Try to find this sub node
		CProfileNode child = m_child;
		while (child != null)
		{
			if (child.m_name == name)
			{
				return child;
			}

			child = child.m_sibling;
		}

		// We didn't find it, so add it
		CProfileNode node = new CProfileNode(name, this, m_profileManager);
		node.m_sibling = m_child;
		m_child = node;
		return node;
	}

	public CProfileNode Get_Parent()
	{
		return m_parent;
	}

	public CProfileNode Get_Sibling()
	{
		return m_sibling;
	}

	public CProfileNode Get_Child()
	{
		return m_child;
	}

	public void CleanupMemory()
	{
	}

	public void Reset()
	{
		m_totalCalls = 0;
		m_totalTime = 0.0f;
		if (m_child != null)
		{
			m_child.Reset();
		}

		if (m_sibling != null)
		{
			m_sibling.Reset();
		}
	}

	public void Call()
	{
		m_totalCalls++;
		if (m_recursionCounter++ == 0)
		{
			m_startTime = m_profileManager.m_stopwatch.ElapsedTicks;
		}
	}

	public bool Return()
	{
		if (--m_recursionCounter == 0 && m_totalCalls != 0)
		{
			long time = m_profileManager.m_stopwatch.ElapsedTicks;
			time -= m_startTime;
			m_totalTime += (float)time / m_profileManager.Profile_Get_Tick_Rate();
		}

		return (m_recursionCounter == 0);
	}

	public String Get_Name()
	{
		return m_name;
	}

	public int Get_Total_Calls()
	{
		return m_totalCalls;
	}

	public float Get_Total_Time()
	{
		return m_totalTime;
	}

	protected String m_name;
	protected int m_totalCalls;
	protected float m_totalTime;
	protected long m_startTime;
	protected int m_recursionCounter;
	protected CProfileNode m_parent;
	protected CProfileNode m_child;
	protected CProfileNode m_sibling;
	protected BasicProfileManager m_profileManager;
}