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

internal class BasicProfileIterator : IProfileIterator
{
	public BasicProfileIterator(CProfileNode start)
	{
		m_currentParent = start;
		m_currentChild = start.Get_Child();
	}

	public void First()
	{
		m_currentChild = m_currentParent.Get_Child();
	}

	public void Next()
	{
		m_currentChild = m_currentChild.Get_Sibling();
	}

	public bool Is_Done()
	{
		return m_currentChild == null;
	}

	public bool Is_Root()
	{
		return m_currentParent.Get_Parent() == null;
	}

	public void Enter_Child(int index)
	{
		m_currentChild = m_currentParent.Get_Child();
		while ((m_currentChild != null) && (index != 0))
		{
			index--;
			m_currentChild = m_currentChild.Get_Sibling();
		}

		if (m_currentChild != null)
		{
			m_currentParent = m_currentChild;
			m_currentChild = m_currentParent.Get_Child();
		}
	}

	public void Enter_Largest_Child()
	{
		throw new NotImplementedException();
	}

	public void Enter_Parent()
	{
		if (m_currentParent.Get_Parent() != null)
		{
			m_currentParent = m_currentParent.Get_Parent();
		}

		m_currentChild = m_currentParent.Get_Child();
	}

	public string Get_Current_Name()
	{
		return m_currentChild.Get_Name();
	}

	public int Get_Current_Total_Calls()
	{
		return m_currentChild.Get_Total_Calls();
	}

	public float Get_Current_Total_Time()
	{
		return m_currentChild.Get_Total_Time();
	}

	public string Get_Current_Parent_Name()
	{
		return m_currentParent.Get_Name();
	}

	public int Get_Current_Parent_Total_Calls()
	{
		return m_currentParent.Get_Total_Calls();
	}

	public float Get_Current_Parent_Total_Time()
	{
		return m_currentParent.Get_Total_Time();
	}

	private CProfileNode m_currentParent;
	private CProfileNode m_currentChild;
}