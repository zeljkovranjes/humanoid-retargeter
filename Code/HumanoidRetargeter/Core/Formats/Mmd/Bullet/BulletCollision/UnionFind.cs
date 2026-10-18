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

internal class UnionFind
{
	public UnionFind()
	{
		m_elements = new ObjectArray<Element>();
	}

	public virtual void Cleanup()
	{
		Free();
	}

	//this is a special operation, destroying the content of btUnionFind.
	//it sorts the elements, based on island id, in order to make it easy to iterate over islands
	public void sortIslands()
	{
		//first store the original body index, and islandId
		int numElements = m_elements.Count;
		Element[] raw = m_elements.GetRawArray();
		for (int i = 0; i < numElements; i++)
		{
			raw[i].m_id = Find(i);
		}

		//m_elements.Sort();
		m_elements.QuickSort(m_sortPredicate);
	}

	public void Reset(int N)
	{
		Allocate(N);
		Element[] raw = m_elements.GetRawArray();
		for (int i = 0; i < N; i++)
		{
			raw[i].m_id = i;
			raw[i].m_sz = 1;
		}
	}

	public int GetNumElements()
	{
		return m_elements.Count;
	}

	public bool IsRoot(int x)
	{
		return (x == m_elements[x].m_id);
	}

	public Element GetElement(int index)
	{
		return m_elements[index];
	}

	public void SetElementSize(int index, int size)
	{
		m_elements.GetRawArray()[index].m_sz = size;
	}

	public void Allocate(int N)
	{
		m_elements.Resize(N, true);
	}

	public void Free()
	{
		m_elements.Clear();
	}

	public bool Find(int p, int q)
	{
		return (Find(p) == Find(q));
	}

	public void Unite(int p, int q)
	{
		int i = Find(p), j = Find(q);
		if (i == j)
		{
			return;
		}

		m_elements.GetRawArray()[i].m_id = j;
		m_elements.GetRawArray()[j].m_sz += m_elements[i].m_sz;
	}

	public int Find(int x)
	{
		//btAssert(x < m_N);
		//btAssert(x >= 0);
		Element[] rawElements = m_elements.GetRawArray();
		while (x != rawElements[x].m_id)
		{
			//not really a reason not to use path compression, and it flattens the trees/improves find performance dramatically
			//m_elements[x].m_id = m_elements[m_elements[x].m_id].m_id;
			Element elementPtr = rawElements[rawElements[x].m_id];
			rawElements[x].m_id = elementPtr.m_id;
			x = elementPtr.m_id;
		//btAssert(x < m_N);
		//btAssert(x >= 0);
		}

		return x;
	}

	private ObjectArray<Element> m_elements;
	private UnionFindElementSortPredicate m_sortPredicate = new UnionFindElementSortPredicate();
}