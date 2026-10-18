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
using System;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal struct GIM_CONTACT
{
	public const int NORMAL_CONTACT_AVERAGE = 1;
	public const float CONTACT_DIFF_EPSILON = 0.00001f;
	public IndexedVector3 m_point;
	public IndexedVector3 m_normal;
	public float m_depth; //Positive value indicates interpenetration
	//public float m_distance;//Padding not for use
	public int m_feature1; //Face number
	public int m_feature2; //Face number
	public GIM_CONTACT(ref GIM_CONTACT contact)
	{
		m_point = contact.m_point;
		m_normal = contact.m_normal;
		m_depth = contact.m_depth;
		m_feature1 = contact.m_feature1;
		m_feature2 = contact.m_feature2;
	}

	public GIM_CONTACT(ref IndexedVector3 point, ref IndexedVector3 normal, float depth, int feature1, int feature2)
	{
		m_point = point;
		m_normal = normal;
		m_depth = depth;
		m_feature1 = feature1;
		m_feature2 = feature2;
	}

	//! Calcs key for coord classification
	public uint CalcKeyContact()
	{
		int[] _coords = new int[]
		{
			(int)(m_point.X * 1000.0f + 1.0f),
			(int)(m_point.Y * 1333.0f),
			(int)(m_point.Z * 2133.0f + 3.0f)
		};
		uint _hash = 0;
		uint _uitmp = (uint)_coords[0];
		_hash = _uitmp;
		_uitmp = (uint)_coords[1];
		_hash += (_uitmp) << 4;
		_uitmp = (uint)_coords[2];
		_hash += (_uitmp) << 8;
		return _hash;
	}

	public void InterpolateNormals(IndexedVector3[] normals, int normal_count)
	{
		IndexedVector3 vec_sum = m_normal;
		for (int i = 0; i < normal_count; i++)
		{
			vec_sum += normals[i];
		}

		float vec_sum_len = vec_sum.LengthSquared();
		if (vec_sum_len < CONTACT_DIFF_EPSILON)
			return;
		//GIM_INV_SQRT(vec_sum_len,vec_sum_len); // 1/sqrt(vec_sum_len)
		m_normal = vec_sum / (float)Math.Sqrt(vec_sum_len);
	}
}