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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System.Diagnostics;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class GjkEpaSolver2MinkowskiDiff : IDisposable
{
	public GjkEpaSolver2MinkowskiDiff()
	{
	}

	public void EnableMargin(bool enable)
	{
		m_enableMargin = enable;
	}

	public IndexedVector3 Support0(ref IndexedVector3 d)
	{
		if (m_enableMargin)
		{
			return m_shapes[0].LocalGetSupportVertexNonVirtual(ref d);
		}

		return m_shapes[0].LocalGetSupportVertexWithoutMarginNonVirtual(ref d);
	}

	public IndexedVector3 Support1(ref IndexedVector3 d)
	{
		IndexedVector3 dcopy = m_toshape1 * d;
		IndexedVector3 temp = m_enableMargin ? m_shapes[1].LocalGetSupportVertexNonVirtual(ref dcopy) : m_shapes[1].LocalGetSupportVertexWithoutMarginNonVirtual(ref dcopy);
		return m_toshape0 * temp;
	}

	public IndexedVector3 Support(ref IndexedVector3 d)
	{
		IndexedVector3 minusD = -d;
		IndexedVector3 temp = Support1(ref minusD);
		return (Support0(ref d) - temp);
	}

	public IndexedVector3 Support(ref IndexedVector3 d, uint index)
	{
		if (index > 0)
			return (Support1(ref d));
		else
			return (Support0(ref d));
	}

	public virtual void Dispose()
	{
		BulletGlobals.GjkEpaSolver2MinkowskiDiffPool.Free(this);
	}

	public bool m_enableMargin;
	public ConvexShape[] m_shapes = new ConvexShape[2];
	public IndexedBasisMatrix m_toshape1 = IndexedBasisMatrix.Identity;
	public IndexedMatrix m_toshape0 = IndexedMatrix.Identity;
}