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

internal class PrimitiveTriangle
{
	public IndexedVector3[] m_vertices = new IndexedVector3[3];
	public IndexedVector4 m_plane;
	public float m_margin;
	GIM_TRIANGLE_CONTACT m_contacts1 = new GIM_TRIANGLE_CONTACT();
	//float m_dummy;
	public PrimitiveTriangle()
	{
		m_margin = 0.01f;
	}

	public void BuildTriPlane()
	{
		IndexedVector3 normal = IndexedVector3.Cross(m_vertices[1] - m_vertices[0], m_vertices[2] - m_vertices[0]);
		normal.Normalize();
		m_plane = new IndexedVector4(normal, IndexedVector3.Dot(m_vertices[0], normal));
	}

	//! Test if triangles could collide
	public bool OverlapTestConservative(PrimitiveTriangle other)
	{
		float total_margin = m_margin + other.m_margin;
		// classify points on other triangle
		float dis0 = ClipPolygon.DistancePointPlane(ref m_plane, ref other.m_vertices[0]) - total_margin;
		float dis1 = ClipPolygon.DistancePointPlane(ref m_plane, ref other.m_vertices[1]) - total_margin;
		float dis2 = ClipPolygon.DistancePointPlane(ref m_plane, ref other.m_vertices[2]) - total_margin;
		if (dis0 > 0.0f && dis1 > 0.0f && dis2 > 0.0f)
			return false;
		// classify points on this triangle
		dis0 = ClipPolygon.DistancePointPlane(ref other.m_plane, ref m_vertices[0]) - total_margin;
		dis1 = ClipPolygon.DistancePointPlane(ref other.m_plane, ref m_vertices[1]) - total_margin;
		dis2 = ClipPolygon.DistancePointPlane(ref other.m_plane, ref m_vertices[2]) - total_margin;
		if (dis0 > 0.0f && dis1 > 0.0f && dis2 > 0.0f)
			return false;
		return true;
	}

	//! Calcs the plane which is paralele to the edge and perpendicular to the triangle plane
	/*!
        \pre this triangle must have its plane calculated.
        */
	public void GetEdgePlane(int edge_index, out IndexedVector4 plane)
	{
		IndexedVector3 e0 = m_vertices[edge_index];
		IndexedVector3 e1 = m_vertices[(edge_index + 1) % 3];
		IndexedVector3 planeNormal = new IndexedVector3(m_plane.X, m_plane.Y, m_plane.Z);
		GeometeryOperations.bt_edge_plane(ref e0, ref e1, ref planeNormal, out plane);
	}

	public void ApplyTransform(ref IndexedMatrix t)
	{
		IndexedVector3.Transform(m_vertices, ref t, m_vertices);
	}

	//! Clips the triangle against this
	/*!
        \pre clipped_points must have MAX_TRI_CLIPPING size, and this triangle must have its plane calculated.
        \return the number of clipped points
        */
	//public static ObjectArray<IndexedVector3> temp_points = new ObjectArray<IndexedVector3>(GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING);
	//public static ObjectArray<IndexedVector3> temp_points1 = new ObjectArray<IndexedVector3>(GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING);
	public static IndexedVector3[] temp_points = new IndexedVector3[GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING];
	public static IndexedVector3[] temp_points1 = new IndexedVector3[GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING];
	public int ClipTriangle(PrimitiveTriangle other, IndexedVector3[] clipped_points)
	{
		// edge 0
		//ObjectArray<IndexedVector3> temp_points = new ObjectArray<IndexedVector3>(GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING);
		IndexedVector4 edgeplane;
		GetEdgePlane(0, out edgeplane);
		int clipped_count = ClipPolygon.PlaneClipTriangle(ref edgeplane, ref other.m_vertices[0], ref other.m_vertices[1], ref other.m_vertices[2], temp_points);
		if (clipped_count == 0)
		{
			return 0;
		}

		//ObjectArray<IndexedVector3> temp_points1 = new ObjectArray<IndexedVector3>(GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING);
		// edge 1
		GetEdgePlane(1, out edgeplane);
		clipped_count = ClipPolygon.PlaneClipPolygon(ref edgeplane, temp_points, clipped_count, temp_points1);
		if (clipped_count == 0)
		{
			return 0;
		}

		// edge 2
		GetEdgePlane(2, out edgeplane);
		{
		}

		clipped_count = ClipPolygon.PlaneClipPolygon(ref edgeplane, temp_points1, clipped_count, clipped_points);
		return clipped_count;
	}

	//! Find collision using the clipping method
	/*!
        \pre this triangle and other must have their triangles calculated
        */
	static IndexedVector3[] clipped_points = new IndexedVector3[(GIM_TRIANGLE_CONTACT.MAX_TRI_CLIPPING)];
	public bool FindTriangleCollisionClipMethod(PrimitiveTriangle other, GIM_TRIANGLE_CONTACT contacts)
	{
		float margin = m_margin + other.m_margin;
		int clipped_count;
		//create planes
		// plane v vs U points
		m_contacts1.m_separating_normal = m_plane;
		clipped_count = ClipTriangle(other, clipped_points);
		if (clipped_count == 0)
		{
			return false; //Reject
		}

		//find most deep interval face1
		m_contacts1.MergePoints(ref m_contacts1.m_separating_normal, margin, clipped_points, clipped_count);
		if (m_contacts1.m_point_count == 0)
		{
			return false; // too far
		}

		//Normal pointing to this triangle
		m_contacts1.m_separating_normal *= -1.0f;
		//Clip tri1 by tri2 edges
		GIM_TRIANGLE_CONTACT contacts2 = new GIM_TRIANGLE_CONTACT();
		contacts2.m_separating_normal = other.m_plane;
		clipped_count = other.ClipTriangle(this, clipped_points);
		if (clipped_count == 0)
		{
			return false; //Reject
		}

		//find most deep interval face1
		contacts2.MergePoints(ref contacts2.m_separating_normal, margin, clipped_points, clipped_count);
		if (contacts2.m_point_count == 0)
		{
			return false; // too far
		}

		////check most dir for contacts
		if (contacts2.m_penetration_depth < m_contacts1.m_penetration_depth)
		{
			contacts.CopyFrom(contacts2);
		}
		else
		{
			contacts.CopyFrom(m_contacts1);
		}

		return true;
	}
}