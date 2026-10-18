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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class InertiaCallback : IInternalTriangleIndexCallback
{
	public virtual bool graphics()
	{
		return false;
	}

	public InertiaCallback(ref IndexedVector3 center)
	{
		m_sum = new IndexedBasisMatrix();
		m_center = center;
	}

	public virtual void InternalProcessTriangleIndex(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		IndexedBasisMatrix i = new IndexedBasisMatrix();
		IndexedVector3 a = triangle[0] - m_center;
		IndexedVector3 b = triangle[1] - m_center;
		IndexedVector3 c = triangle[2] - m_center;
		float volNeg = -Math.Abs(a.Triple(ref b, ref c)) * (1.0f / 6.0f);
		for (int j = 0; j < 3; j++)
		{
			for (int k = 0; k <= j; k++)
			{
				i[j, k] = i[k, j] = volNeg * (0.1f * (a[j] * a[k] + b[j] * b[k] + c[j] * c[k]) + 0.05f * (a[j] * b[k] + a[k] * b[j] + a[j] * c[k] + a[k] * c[j] + b[j] * c[k] + b[k] * c[j]));
			}
		}

		float i00 = -i._el0.X;
		float i11 = -i._el1.Y;
		float i22 = -i._el2.Z;
		i[0, 0] = i11 + i22;
		i[1, 1] = i22 + i00;
		i[2, 2] = i00 + i11;
		m_sum._el0 += i._el0;
		m_sum._el1 += i._el1;
		m_sum._el2 += i._el2;
	}

	public IndexedBasisMatrix GetInertia()
	{
		return m_sum;
	}

	public void Cleanup()
	{
	}

	IndexedBasisMatrix m_sum;
	IndexedVector3 m_center;
}