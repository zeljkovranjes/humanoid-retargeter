/*
 *
 * C# / XNA  port of Bullet (c) 2011 Mark Neale <xexuxjy@hotmail.com>
 *
This source file is part of GIMPACT Library.

For the latest info, see http://gimpact.sourceforge.net/

Copyright (c) 2007 Francisco Leon Najera. C.C. 80087371.
email: projectileman@yahoo.com


This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it freely,
subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/
// Adapted from BulletXNA 73b2e1b9: namespace isolation, no XNA rendering, one type per file.
#nullable disable
#pragma warning disable CS1591, CS0168, CS0219, CS0414, CS0660, CS0661, CS0659, CS0693, CS8981, CS0108, CS0114
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System.Diagnostics;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class GIM_TRIANGLE_CONTACT
{
	public const int MAX_TRI_CLIPPING = 16;
	public float m_penetration_depth;
	public int m_point_count;
	public IndexedVector4 m_separating_normal;
	public IndexedVector3[] m_points = new IndexedVector3[MAX_TRI_CLIPPING];
	public void CopyFrom(GIM_TRIANGLE_CONTACT other)
	{
		m_penetration_depth = other.m_penetration_depth;
		m_separating_normal = other.m_separating_normal;
		m_point_count = other.m_point_count;
		int i = m_point_count;
		while (i-- != 0)
		{
			m_points[i] = other.m_points[i];
		}
	}

	public GIM_TRIANGLE_CONTACT()
	{
	}

	public GIM_TRIANGLE_CONTACT(GIM_TRIANGLE_CONTACT other)
	{
		CopyFrom(other);
	}

	//! classify points that are closer
	int[] point_indices = new int[MAX_TRI_CLIPPING];
	public void MergePoints(ref IndexedVector4 plane, float margin, IndexedVector3[] points, int point_count)
	{
		m_point_count = 0;
		m_penetration_depth = -1000.0f;
		int _k;
		for (_k = 0; _k < point_count; _k++)
		{
			float _dist = -ClipPolygon.DistancePointPlane(ref plane, ref points[_k]) + margin;
			if (_dist >= 0.0f)
			{
				if (_dist > m_penetration_depth)
				{
					m_penetration_depth = _dist;
					point_indices[0] = _k;
					m_point_count = 1;
				}
				else if ((_dist + MathUtil.SIMD_EPSILON) >= m_penetration_depth)
				{
					point_indices[m_point_count] = _k;
					m_point_count++;
				}
			}
		}

		for (_k = 0; _k < m_point_count; _k++)
		{
			m_points[_k] = points[point_indices[_k]];
		}
	}
}