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

internal class TriangleShapeEx : TriangleShape
{
	public TriangleShapeEx() : base(IndexedVector3.Zero, IndexedVector3.Zero, IndexedVector3.Zero)
	{
	}

	public TriangleShapeEx(ref IndexedVector3 p0, ref IndexedVector3 p1, ref IndexedVector3 p2) : base(ref p0, ref p1, ref p2)
	{
	}

	public TriangleShapeEx(TriangleShapeEx other) : base(ref other.m_vertices1[0], ref other.m_vertices1[1], ref other.m_vertices1[2])
	{
	}

	public virtual void GetAabb(ref IndexedMatrix t, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		IndexedVector3 tv0 = t * m_vertices1[0];
		IndexedVector3 tv1 = t * m_vertices1[1];
		IndexedVector3 tv2 = t * m_vertices1[2];
		AABB trianglebox = new AABB(ref tv0, ref tv1, ref tv2, m_collisionMargin);
		aabbMin = trianglebox.m_min;
		aabbMax = trianglebox.m_max;
	}

	public void ApplyTransform(ref IndexedMatrix t)
	{
		IndexedVector3.Transform(m_vertices1, ref t, m_vertices1);
	}

	public void BuildTriPlane(out IndexedVector4 plane)
	{
		IndexedVector3 normal = IndexedVector3.Cross(m_vertices1[1] - m_vertices1[0], m_vertices1[2] - m_vertices1[0]);
		normal.Normalize();
		plane = new IndexedVector4(normal, IndexedVector3.Dot(m_vertices1[0], normal));
	}

	public bool OverlapTestConservative(TriangleShapeEx other)
	{
		float total_margin = GetMargin() + other.GetMargin();
		IndexedVector4 plane0;
		BuildTriPlane(out plane0);
		IndexedVector4 plane1;
		other.BuildTriPlane(out plane1);
		// classify points on other triangle
		float dis0 = ClipPolygon.DistancePointPlane(ref plane0, ref other.m_vertices1[0]) - total_margin;
		float dis1 = ClipPolygon.DistancePointPlane(ref plane0, ref other.m_vertices1[1]) - total_margin;
		float dis2 = ClipPolygon.DistancePointPlane(ref plane0, ref other.m_vertices1[2]) - total_margin;
		if (dis0 > 0.0f && dis1 > 0.0f && dis2 > 0.0f)
			return false;
		// classify points on this triangle
		dis0 = ClipPolygon.DistancePointPlane(ref plane1, ref m_vertices1[0]) - total_margin;
		dis1 = ClipPolygon.DistancePointPlane(ref plane1, ref m_vertices1[1]) - total_margin;
		dis2 = ClipPolygon.DistancePointPlane(ref plane1, ref m_vertices1[2]) - total_margin;
		if (dis0 > 0.0f && dis1 > 0.0f && dis2 > 0.0f)
			return false;
		return true;
	}
}