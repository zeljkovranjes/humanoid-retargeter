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

internal struct AABB
{
	public IndexedVector3 m_min;
	public IndexedVector3 m_max;
	public AABB(ref IndexedVector3 V1, ref IndexedVector3 V2, ref IndexedVector3 V3)
	{
		m_min = new IndexedVector3();
		m_max = new IndexedVector3();
		m_min.X = BoxCollision.BT_MIN3(V1.X, V2.X, V3.X);
		m_min.Y = BoxCollision.BT_MIN3(V1.Y, V2.Y, V3.Y);
		m_min.Z = BoxCollision.BT_MIN3(V1.Z, V2.Z, V3.Z);
		m_max.X = BoxCollision.BT_MAX3(V1.X, V2.X, V3.X);
		m_max.Y = BoxCollision.BT_MAX3(V1.Y, V2.Y, V3.Y);
		m_max.Z = BoxCollision.BT_MAX3(V1.Z, V2.Z, V3.Z);
	}

	public AABB(ref IndexedVector3 V1, ref IndexedVector3 V2, ref IndexedVector3 V3, float margin)
	{
		m_min = new IndexedVector3();
		m_max = new IndexedVector3();
		m_min.X = BoxCollision.BT_MIN3(V1.X, V2.X, V3.X);
		m_min.Y = BoxCollision.BT_MIN3(V1.Y, V2.Y, V3.Y);
		m_min.Z = BoxCollision.BT_MIN3(V1.Z, V2.Z, V3.Z);
		m_max.X = BoxCollision.BT_MAX3(V1.X, V2.X, V3.X);
		m_max.Y = BoxCollision.BT_MAX3(V1.Y, V2.Y, V3.Y);
		m_max.Z = BoxCollision.BT_MAX3(V1.Z, V2.Z, V3.Z);
		m_min.X -= margin;
		m_min.Y -= margin;
		m_min.Z -= margin;
		m_max.X += margin;
		m_max.Y += margin;
		m_max.Z += margin;
	}

	public AABB(ref AABB other)
	{
		m_max = other.m_max;
		m_min = other.m_min;
	}

	public AABB(ref AABB other, float margin)
	{
		m_max = other.m_max;
		m_min = other.m_min;
		m_min.X -= margin;
		m_min.Y -= margin;
		m_min.Z -= margin;
		m_max.X += margin;
		m_max.Y += margin;
		m_max.Z += margin;
	}

	public void Invalidate()
	{
		m_min.X = MathUtil.SIMD_INFINITY;
		m_min.Y = MathUtil.SIMD_INFINITY;
		m_min.Z = MathUtil.SIMD_INFINITY;
		m_max.X = -MathUtil.SIMD_INFINITY;
		m_max.Y = -MathUtil.SIMD_INFINITY;
		m_max.Z = -MathUtil.SIMD_INFINITY;
	}

	public void IncrementMargin(float margin)
	{
		m_min.X -= margin;
		m_min.Y -= margin;
		m_min.Z -= margin;
		m_max.X += margin;
		m_max.Y += margin;
		m_max.Z += margin;
	}

	public void CopyWithMargin(ref AABB other, float margin)
	{
		m_min.X = other.m_min.X - margin;
		m_min.Y = other.m_min.Y - margin;
		m_min.Z = other.m_min.Z - margin;
		m_max.X = other.m_max.X + margin;
		m_max.Y = other.m_max.Y + margin;
		m_max.Z = other.m_max.Z + margin;
	}

	public void CalcFromTriangle(ref IndexedVector3 V1, ref IndexedVector3 V2, ref IndexedVector3 V3)
	{
		m_min.X = BoxCollision.BT_MIN3(V1.X, V2.X, V3.X);
		m_min.Y = BoxCollision.BT_MIN3(V1.Y, V2.Y, V3.Y);
		m_min.Z = BoxCollision.BT_MIN3(V1.Z, V2.Z, V3.Z);
		m_max.X = BoxCollision.BT_MAX3(V1.X, V2.X, V3.X);
		m_max.Y = BoxCollision.BT_MAX3(V1.Y, V2.Y, V3.Y);
		m_max.Z = BoxCollision.BT_MAX3(V1.Z, V2.Z, V3.Z);
	}

	public void CalcFromTriangleMargin(ref IndexedVector3 V1, ref IndexedVector3 V2, ref IndexedVector3 V3, float margin)
	{
		m_min.X = BoxCollision.BT_MIN3(V1.X, V2.X, V3.X);
		m_min.Y = BoxCollision.BT_MIN3(V1.Y, V2.Y, V3.Y);
		m_min.Z = BoxCollision.BT_MIN3(V1.Z, V2.Z, V3.Z);
		m_max.X = BoxCollision.BT_MAX3(V1.X, V2.X, V3.X);
		m_max.Y = BoxCollision.BT_MAX3(V1.Y, V2.Y, V3.Y);
		m_max.Z = BoxCollision.BT_MAX3(V1.Z, V2.Z, V3.Z);
		m_min.X -= margin;
		m_min.Y -= margin;
		m_min.Z -= margin;
		m_max.X += margin;
		m_max.Y += margin;
		m_max.Z += margin;
	}

	//! Apply a transform to an AABB
	public void ApplyTransform(ref IndexedMatrix trans)
	{
		IndexedVector3 center = (m_max + m_min) * 0.5f;
		IndexedVector3 extends = m_max - center;
		// Compute new center
		center = trans * center;
		IndexedVector3 textends = new IndexedVector3(extends.Dot(trans._basis.GetRow(0).Absolute()), extends.Dot(trans._basis.GetRow(1).Absolute()), extends.Dot(trans._basis.GetRow(2).Absolute()));
		m_min = center - textends;
		m_max = center + textends;
	}

	//! Apply a transform to an AABB
	public void ApplyTransformTransCache(ref BT_BOX_BOX_TRANSFORM_CACHE trans)
	{
		IndexedVector3 center = (m_max + m_min) * 0.5f;
		IndexedVector3 extends = m_max - center;
		// Compute new center
		center = trans.Transform(ref center);
		IndexedBasisMatrix absMatrix = trans.m_R1to0.Absolute();
		IndexedVector3 textends = new IndexedVector3(extends.Dot(trans.m_R1to0.GetRow(0).Absolute()), extends.Dot(trans.m_R1to0.GetRow(1).Absolute()), extends.Dot(trans.m_R1to0.GetRow(2).Absolute()));
		m_min = center - textends;
		m_max = center + textends;
	}

	//! Merges a Box
	public void Merge(AABB box)
	{
		Merge(ref box);
	}

	public void Merge(ref AABB box)
	{
		m_min.X = BoxCollision.BT_MIN(m_min.X, box.m_min.X);
		m_min.Y = BoxCollision.BT_MIN(m_min.Y, box.m_min.Y);
		m_min.Z = BoxCollision.BT_MIN(m_min.Z, box.m_min.Z);
		m_max.X = BoxCollision.BT_MAX(m_max.X, box.m_max.X);
		m_max.Y = BoxCollision.BT_MAX(m_max.Y, box.m_max.Y);
		m_max.Z = BoxCollision.BT_MAX(m_max.Z, box.m_max.Z);
	}

	//! Merges a point
	public void MergePoint(ref IndexedVector3 point)
	{
		m_min.X = BoxCollision.BT_MIN(m_min.X, point.X);
		m_min.Y = BoxCollision.BT_MIN(m_min.Y, point.Y);
		m_min.Z = BoxCollision.BT_MIN(m_min.Z, point.Z);
		m_max.X = BoxCollision.BT_MAX(m_max.X, point.X);
		m_max.Y = BoxCollision.BT_MAX(m_max.Y, point.Y);
		m_max.Z = BoxCollision.BT_MAX(m_max.Z, point.Z);
	}

	//! Gets the extend and center
	public void GetCenterExtend(out IndexedVector3 center, out IndexedVector3 extend)
	{
		center = new IndexedVector3((m_max + m_min) * 0.5f);
		extend = new IndexedVector3(m_max - center);
	}

	//! Finds the intersecting box between this box and the other.
	public void FindIntersection(ref AABB other, ref AABB intersection)
	{
		intersection.m_min.X = BoxCollision.BT_MAX(other.m_min.X, m_min.X);
		intersection.m_min.Y = BoxCollision.BT_MAX(other.m_min.Y, m_min.Y);
		intersection.m_min.Z = BoxCollision.BT_MAX(other.m_min.Z, m_min.Z);
		intersection.m_max.X = BoxCollision.BT_MIN(other.m_max.X, m_max.X);
		intersection.m_max.Y = BoxCollision.BT_MIN(other.m_max.Y, m_max.Y);
		intersection.m_max.Z = BoxCollision.BT_MIN(other.m_max.Z, m_max.Z);
	}

	public bool HasCollision(ref AABB other)
	{
		if (m_min.X > other.m_max.X || m_max.X < other.m_min.X || m_min.Y > other.m_max.Y || m_max.Y < other.m_min.Y || m_min.Z > other.m_max.Z || m_max.Z < other.m_min.Z)
		{
			return false;
		}

		return true;
	}

	/*! \brief Finds the Ray intersection parameter.
        \param aabb Aligned box
        \param vorigin A vec3f with the origin of the ray
        \param vdir A vec3f with the direction of the ray
        */
	public bool CollideRay(ref IndexedVector3 vorigin, ref IndexedVector3 vdir)
	{
		IndexedVector3 extents, center;
		GetCenterExtend(out center, out extents);
		float Dx = vorigin.X - center.X;
		if (BoxCollision.BT_GREATER(Dx, extents.X) && Dx * vdir.X >= 0.0f)
			return false;
		float Dy = vorigin.Y - center.Y;
		if (BoxCollision.BT_GREATER(Dy, extents.Y) && Dy * vdir.Y >= 0.0f)
			return false;
		float Dz = vorigin.Z - center.Z;
		if (BoxCollision.BT_GREATER(Dz, extents.Z) && Dz * vdir.Z >= 0.0f)
			return false;
		float f = vdir.Y * Dz - vdir.Z * Dy;
		if (Math.Abs(f) > extents.Y * Math.Abs(vdir.Z) + extents.Z * Math.Abs(vdir.Y))
			return false;
		f = vdir.Z * Dx - vdir.X * Dz;
		if (Math.Abs(f) > extents.X * Math.Abs(vdir.Z) + extents.Z * Math.Abs(vdir.X))
			return false;
		f = vdir.X * Dy - vdir.Y * Dx;
		if (Math.Abs(f) > extents.X * Math.Abs(vdir.Y) + extents.Y * Math.Abs(vdir.X))
			return false;
		return true;
	}

	public void ProjectionInterval(ref IndexedVector4 direction, out float vmin, out float vmax)
	{
		IndexedVector3 temp = new IndexedVector3(direction.X, direction.Y, direction.Z);
		ProjectionInterval(ref temp, out vmin, out vmax);
	}

	public void ProjectionInterval(ref IndexedVector3 direction, out float vmin, out float vmax)
	{
		IndexedVector3 center = (m_max + m_min) * 0.5f;
		IndexedVector3 extend = m_max - center;
		float _fOrigin = direction.Dot(ref center);
		float _fMaximumExtent = extend.Dot(direction.Absolute());
		vmin = _fOrigin - _fMaximumExtent;
		vmax = _fOrigin + _fMaximumExtent;
	}

	// seems silly to duplicate this stuff when it's in xna plane, but a cleanup will wait
	// till it's 'working'
	public BT_PLANE_INTERSECTION_TYPE PlaneClassify(ref IndexedVector4 plane)
	{
		float _fmin, _fmax;
		ProjectionInterval(ref plane, out _fmin, out _fmax);
		if (plane.W > _fmax + BoxCollision.BOX_PLANE_EPSILON)
		{
			return BT_PLANE_INTERSECTION_TYPE.BT_CONST_BACK_PLANE; // 0
		}

		if (plane.W + BoxCollision.BOX_PLANE_EPSILON >= _fmin)
		{
			return BT_PLANE_INTERSECTION_TYPE.BT_CONST_COLLIDE_PLANE; //1
		}

		return BT_PLANE_INTERSECTION_TYPE.BT_CONST_FRONT_PLANE; //2
	}

	public bool OverlappingTransConservative(ref AABB box, ref IndexedMatrix trans1_to_0)
	{
		AABB tbox = box;
		tbox.ApplyTransform(ref trans1_to_0);
		return HasCollision(ref tbox);
	}

	public bool OverlappingTransConservative2(ref AABB box, BT_BOX_BOX_TRANSFORM_CACHE trans1_to_0)
	{
		AABB tbox = box;
		tbox.ApplyTransformTransCache(ref trans1_to_0);
		return HasCollision(ref tbox);
	}

	//! transcache is the transformation cache from box to this AABB
	public bool OverlappingTransCache(ref AABB box, ref BT_BOX_BOX_TRANSFORM_CACHE transcache, bool fulltest)
	{
		//Taken from OPCODE
		IndexedVector3 ea, eb; //extends
		IndexedVector3 ca, cb; //extends
		GetCenterExtend(out ca, out ea);
		box.GetCenterExtend(out cb, out eb);
		IndexedVector3 T = new IndexedVector3(0, 0, 0);
		float t, t2;
		int i;
		// Class I : A's basis vectors
		for (i = 0; i < 3; i++)
		{
			T[i] = transcache.m_R1to0[i].Dot(ref cb) + transcache.m_T1to0[i] - ca[i];
			t = transcache.m_AR[i].Dot(ref eb) + ea[i];
			if (BoxCollision.BT_GREATER(T[i], t))
			{
				return false;
			}
		}

		// Class II : B's basis vectors
		for (i = 0; i < 3; i++)
		{
			t = Mat3DotCol(ref transcache.m_R1to0, ref T, i);
			t2 = Mat3DotCol(ref transcache.m_AR, ref ea, i) + eb[i];
			if (BoxCollision.BT_GREATER(t, t2))
			{
				return false;
			}
		}

		// Class III : 9 cross products
		if (fulltest)
		{
			// check to see if these need to be restored back or are read-only
			float[, ] m_R1to0 = MathUtil.BasisMatrixToFloatArray(ref transcache.m_R1to0);
			float[, ] m_AR = MathUtil.BasisMatrixToFloatArray(ref transcache.m_AR);
			int j, m, n, o, p, q, r;
			for (i = 0; i < 3; i++)
			{
				m = (i + 1) % 3;
				n = (i + 2) % 3;
				o = i == 0 ? 1 : 0;
				p = i == 2 ? 1 : 2;
				for (j = 0; j < 3; j++)
				{
					q = j == 2 ? 1 : 2;
					r = j == 0 ? 1 : 0;
					t = T[n] * m_R1to0[m, j] - T[m] * m_R1to0[n, j];
					t2 = ea[o] * m_AR[p, j] + ea[p] * m_AR[o, j] + eb[r] * m_AR[i, q] + eb[q] * m_AR[i, r];
					if (BoxCollision.BT_GREATER(t, t2))
						return false;
				}
			}
		}

		return true;
	}

	public static float Mat3DotCol(ref IndexedBasisMatrix mat, ref IndexedVector3 vec3, int colindex)
	{
		return vec3.X * mat[0, colindex] + vec3.Y * mat[1, colindex] + vec3.Z * mat[2, colindex];
	}

	public static float Mat3DotCol(IndexedBasisMatrix mat, ref IndexedVector3 vec3, int colindex)
	{
		return vec3.X * mat[0, colindex] + vec3.Y * mat[1, colindex] + vec3.Z * mat[2, colindex];
	}

	//! Simple test for planes.
	public bool CollidePlane(ref IndexedVector4 plane)
	{
		BT_PLANE_INTERSECTION_TYPE classify = PlaneClassify(ref plane);
		return (classify == BT_PLANE_INTERSECTION_TYPE.BT_CONST_COLLIDE_PLANE);
	}

	//! test for a triangle, with edges
	public bool CollideTriangleExact(ref IndexedVector3 p1, ref IndexedVector3 p2, ref IndexedVector3 p3, ref IndexedVector4 triangle_plane)
	{
		if (!CollidePlane(ref triangle_plane))
		{
			return false;
		}

		IndexedVector3 center, extends;
		GetCenterExtend(out center, out extends);
		IndexedVector3 v1 = (p1 - center);
		IndexedVector3 v2 = (p2 - center);
		IndexedVector3 v3 = (p3 - center);
		//First axis
		IndexedVector3 diff = (v2 - v1);
		IndexedVector3 abs_diff = diff.Absolute();
		//Test With X axis
		BoxCollision.TEST_CROSS_EDGE_BOX_X_AXIS_MCR(ref diff, ref abs_diff, ref v1, ref v3, ref extends);
		//Test With Y axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Y_AXIS_MCR(ref diff, ref abs_diff, ref v1, ref v3, ref extends);
		//Test With Z axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Z_AXIS_MCR(ref diff, ref abs_diff, ref v1, ref v3, ref extends);
		diff = v3 - v2;
		abs_diff = diff.Absolute();
		//Test With X axis
		BoxCollision.TEST_CROSS_EDGE_BOX_X_AXIS_MCR(ref diff, ref abs_diff, ref v2, ref v1, ref extends);
		//Test With Y axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Y_AXIS_MCR(ref diff, ref abs_diff, ref v2, ref v1, ref extends);
		//Test With Z axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Z_AXIS_MCR(ref diff, ref abs_diff, ref v2, ref v1, ref extends);
		diff = v1 - v3;
		abs_diff = diff.Absolute();
		//Test With X axis
		BoxCollision.TEST_CROSS_EDGE_BOX_X_AXIS_MCR(ref diff, ref abs_diff, ref v3, ref v2, ref extends);
		//Test With Y axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Y_AXIS_MCR(ref diff, ref abs_diff, ref v3, ref v2, ref extends);
		//Test With Z axis
		BoxCollision.TEST_CROSS_EDGE_BOX_Z_AXIS_MCR(ref diff, ref abs_diff, ref v3, ref v2, ref extends);
		return true;
	}
}