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

internal class BoxCollision
{
	//#define BT_SWAP_NUMBERS(a,b){ \
	//    a = a+b; \
	//    b = a-b; \
	//    a = a-b; \
	//}\
	//#define BoxCollision.BT_MAX(a,b) (a<b?b:a)
	//#define BoxCollision.BT_MIN(a,b) (a>b?b:a)
	//#define BT_GREATER(x, y)	Math.Abs(x) > (y)
	//#define BoxCollision.BT_MAX3(a,b,c) BoxCollision.BT_MAX(a,BT_MAX(b,c))
	//#define BoxCollision.BT_MIN3(a,b,c) BoxCollision.BT_MIN(a,BT_MIN(b,c))
	public static bool BT_GREATER(float x, float y)
	{
		return Math.Abs(x) > y;
	}

	public static float BT_MAX(float a, float b)
	{
		return Math.Max(a, b);
	}

	public static float BT_MIN(float a, float b)
	{
		return Math.Min(a, b);
	}

	public static float BT_MAX3(float a, float b, float c)
	{
		return Math.Max(a, Math.Max(b, c));
	}

	public static float BT_MIN3(float a, float b, float c)
	{
		return Math.Min(a, Math.Min(b, c));
	}

	// bool test_cross_edge_box(
	//	 ref IndexedVector3 edge,
	//	 ref IndexedVector3 absolute_edge,
	//	 ref IndexedVector3 pointa,
	//	 ref IndexedVector3 pointb,  ref IndexedVector3 extend,
	//	int dir_index0,
	//	int dir_index1
	//	int component_index0,
	//	int component_index1)
	//{
	//	// dir coords are -z and y
	//
	//	 float dir0 = -edge[dir_index0];
	//	 float dir1 = edge[dir_index1];
	//	float pmin = pointa[component_index0]*dir0 + pointa[component_index1]*dir1;
	//	float pmax = pointb[component_index0]*dir0 + pointb[component_index1]*dir1;
	//	//find minmax
	//	if(pmin>pmax)
	//	{
	//		BT_SWAP_NUMBERS(pmin,pmax);
	//	}
	//	//find extends
	//	 float rad = extend[component_index0] * absolute_edge[dir_index0] +
	//					extend[component_index1] * absolute_edge[dir_index1];
	//
	//	if(pmin>rad || -rad>pmax) return false;
	//	return true;
	//}
	//
	// bool test_cross_edge_box_X_axis(
	//	 ref IndexedVector3 edge,
	//	 ref IndexedVector3 absolute_edge,
	//	 ref IndexedVector3 pointa,
	//	 ref IndexedVector3 pointb, ref IndexedVector3 extend)
	//{
	//
	//	return test_cross_edge_box(edge,absolute_edge,pointa,pointb,extend,2,1,1,2);
	//}
	//
	//
	// bool test_cross_edge_box_Y_axis(
	//	 ref IndexedVector3 edge,
	//	 ref IndexedVector3 absolute_edge,
	//	 ref IndexedVector3 pointa,
	//	 ref IndexedVector3 pointb, ref IndexedVector3 extend)
	//{
	//
	//	return test_cross_edge_box(edge,absolute_edge,pointa,pointb,extend,0,2,2,0);
	//}
	//
	// bool test_cross_edge_box_Z_axis(
	//	 ref IndexedVector3 edge,
	//	 ref IndexedVector3 absolute_edge,
	//	 ref IndexedVector3 pointa,
	//	 ref IndexedVector3 pointb, ref IndexedVector3 extend)
	//{
	//
	//	return test_cross_edge_box(edge,absolute_edge,pointa,pointb,extend,1,0,0,1);
	//}
	public static bool TEST_CROSS_EDGE_BOX_MCR(ref IndexedVector3 edge, ref IndexedVector3 absolute_edge, ref IndexedVector3 pointa, ref IndexedVector3 pointb, ref IndexedVector3 _extend, int i_dir_0, int i_dir_1, int i_comp_0, int i_comp_1)
	{
		float dir0 = -edge[i_dir_0];
		float dir1 = edge[i_dir_1];
		float pmin = pointa[i_comp_0] * dir0 + pointa[i_comp_1] * dir1;
		float pmax = pointb[i_comp_0] * dir0 + pointb[i_comp_1] * dir1;
		if (pmin > pmax)
		{
			pmin = pmin + pmax;
			pmax = pmin - pmax;
			pmin = pmin - pmax;
		}

		float abs_dir0 = absolute_edge[i_dir_0];
		float abs_dir1 = absolute_edge[i_dir_1];
		float rad = _extend[i_comp_0] * abs_dir0 + _extend[i_comp_1] * abs_dir1;
		if (pmin > rad || -rad > pmax)
		{
			return false;
		}

		return true;
	}

	//    float dir0 = -MathUtil.VectorComponent(ref edge, i_dir_0);
	//    float dir1 = MathUtil.VectorComponent(ref edge, i_dir_1);
	//    float pmin = MathUtil.VectorComponent(ref pointa, i_comp_0) * dir0 + MathUtil.VectorComponent(ref pointa, i_comp_1) * dir1;
	//    float pmax = MathUtil.VectorComponent(ref pointb, i_comp_0) * dir0 + MathUtil.VectorComponent(ref pointb, i_comp_1) * dir1;
	//    if (pmin > pmax)
	//    {
	//        pmin = pmin + pmax;
	//        pmax = pmin - pmax;
	//        pmin = pmin - pmax;
	//    }
	//    float abs_dir0 = MathUtil.VectorComponent(ref absolute_edge, i_dir_0);
	//    float abs_dir1 = MathUtil.VectorComponent(ref absolute_edge, i_dir_1);
	//    float rad = MathUtil.VectorComponent(ref _extend, i_comp_0) * abs_dir0 + MathUtil.VectorComponent(ref _extend, i_comp_1) * abs_dir1;
	//    if (pmin > rad || -rad > pmax)
	//    {
	//        return false;
	//    }
	//    return true;
	//}
	public static bool TEST_CROSS_EDGE_BOX_X_AXIS_MCR(ref IndexedVector3 edge, ref IndexedVector3 absolute_edge, ref IndexedVector3 pointa, ref IndexedVector3 pointb, ref IndexedVector3 _extend)
	{
		return TEST_CROSS_EDGE_BOX_MCR(ref edge, ref absolute_edge, ref pointa, ref pointb, ref _extend, 2, 1, 1, 2);
	}

	public static bool TEST_CROSS_EDGE_BOX_Y_AXIS_MCR(ref IndexedVector3 edge, ref IndexedVector3 absolute_edge, ref IndexedVector3 pointa, ref IndexedVector3 pointb, ref IndexedVector3 _extend)
	{
		return TEST_CROSS_EDGE_BOX_MCR(ref edge, ref absolute_edge, ref pointa, ref pointb, ref _extend, 0, 2, 2, 0);
	}

	public static bool TEST_CROSS_EDGE_BOX_Z_AXIS_MCR(ref IndexedVector3 edge, ref IndexedVector3 absolute_edge, ref IndexedVector3 pointa, ref IndexedVector3 pointb, ref IndexedVector3 _extend)
	{
		return TEST_CROSS_EDGE_BOX_MCR(ref edge, ref absolute_edge, ref pointa, ref pointb, ref _extend, 1, 0, 0, 1);
	}

	//! Returns the dot product between a vec3f and the col of a matrix
	// float bt_mat3_dot_col(
	// btMatrix3x3 & mat,  ref IndexedVector3 vec3, int colindex)
	//{
	//    return vec3[0]*mat[0][colindex] + vec3[1]*mat[1][colindex] + vec3[2]*mat[2][colindex];
	//}
	public const float BOX_PLANE_EPSILON = 0.000001f;
	//! Compairison of transformation objects
	bool CompareTransformsEqual(ref IndexedMatrix t1, ref IndexedMatrix t2)
	{
		return t1.Equals(t2);
	}
}