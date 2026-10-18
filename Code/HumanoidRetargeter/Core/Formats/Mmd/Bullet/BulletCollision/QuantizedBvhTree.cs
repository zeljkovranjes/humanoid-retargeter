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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class QuantizedBvhTree
{
	protected int m_num_nodes;
	protected GIM_QUANTIZED_BVH_NODE_ARRAY m_node_array = new GIM_QUANTIZED_BVH_NODE_ARRAY();
	protected AABB m_global_bound;
	protected IndexedVector3 m_bvhQuantization;
	protected void CalcQuantization(GIM_BVH_DATA_ARRAY primitive_boxes)
	{
		CalcQuantization(primitive_boxes, 1.0f);
	}

	protected void CalcQuantization(GIM_BVH_DATA_ARRAY primitive_boxes, float boundMargin)
	{
		//calc globa box
		AABB global_bound = new AABB();
		global_bound.Invalidate();
		int count = primitive_boxes.Count;
		for (int i = 0; i < count; i++)
		{
			global_bound.Merge(ref primitive_boxes.GetRawArray()[i].m_bound);
		}

		GImpactQuantization.CalcQuantizationParameters(out m_global_bound.m_min, out m_global_bound.m_max, out m_bvhQuantization, ref global_bound.m_min, ref global_bound.m_max, boundMargin);
	}

	protected int SortAndCalcSplittingIndex(GIM_BVH_DATA_ARRAY primitive_boxes, int startIndex, int endIndex, int splitAxis)
	{
		int i;
		int splitIndex = startIndex;
		int numIndices = endIndex - startIndex;
		// average of centers
		float splitValue = 0.0f;
		IndexedVector3 means = IndexedVector3.Zero;
		for (i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (primitive_boxes[i].m_bound.m_max + primitive_boxes[i].m_bound.m_min);
			means += center;
		}

		means *= ((1.0f) / (float)numIndices);
		splitValue = means[splitAxis];
		//sort leafNodes so all values larger then splitValue comes first, and smaller values start from 'splitIndex'.
		for (i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (primitive_boxes[i].m_bound.m_max + primitive_boxes[i].m_bound.m_min);
			if (center[splitAxis] > splitValue)
			{
				//swap
				primitive_boxes.Swap(i, splitIndex);
				//swapLeafNodes(i,splitIndex);
				splitIndex++;
			}
		}

		//if the splitIndex causes unbalanced trees, fix this by using the center in between startIndex and endIndex
		//otherwise the tree-building might fail due to stack-overflows in certain cases.
		//unbalanced1 is unsafe: it can cause stack overflows
		//bool unbalanced1 = ((splitIndex==startIndex) || (splitIndex == (endIndex-1)));
		//unbalanced2 should work too: always use center (perfect balanced trees)
		//bool unbalanced2 = true;
		//this should be safe too:
		int rangeBalancedIndices = numIndices / 3;
		bool unbalanced = ((splitIndex <= (startIndex + rangeBalancedIndices)) || (splitIndex >= (endIndex - 1 - rangeBalancedIndices)));
		if (unbalanced)
		{
			splitIndex = startIndex + (numIndices >> 1);
		}

		{
		}

		return splitIndex;
	}

	protected int CalcSplittingAxis(GIM_BVH_DATA_ARRAY primitive_boxes, int startIndex, int endIndex)
	{
		int i;
		IndexedVector3 means = IndexedVector3.Zero;
		IndexedVector3 variance = IndexedVector3.Zero;
		int numIndices = endIndex - startIndex;
		for (i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (primitive_boxes[i].m_bound.m_max + primitive_boxes[i].m_bound.m_min);
			means += center;
		}

		means *= (1.0f / (float)numIndices);
		for (i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (primitive_boxes[i].m_bound.m_max + primitive_boxes[i].m_bound.m_min);
			IndexedVector3 diff2 = center - means;
			diff2 = diff2 * diff2;
			variance += diff2;
		}

		variance *= (1.0f) / ((float)numIndices - 1);
		return MathUtil.MaxAxis(ref variance);
	}

	protected void BuildSubTree(GIM_BVH_DATA_ARRAY primitive_boxes, int startIndex, int endIndex)
	{
		int curIndex = m_num_nodes;
		m_num_nodes++;
		{
		}

		if ((endIndex - startIndex) == 1)
		{
			//We have a leaf node
			SetNodeBound(curIndex, ref primitive_boxes.GetRawArray()[startIndex].m_bound);
			m_node_array[curIndex].SetDataIndex(primitive_boxes[startIndex].m_data);
			return;
		}

		//calculate Best Splitting Axis and where to split it. Sort the incoming 'leafNodes' array within range 'startIndex/endIndex'.
		//split axis
		int splitIndex = CalcSplittingAxis(primitive_boxes, startIndex, endIndex);
		splitIndex = SortAndCalcSplittingIndex(primitive_boxes, startIndex, endIndex, splitIndex //split axis
		);
		//calc this node bounding box
		AABB node_bound = new AABB();
		node_bound.Invalidate();
		for (int i = startIndex; i < endIndex; i++)
		{
			node_bound.Merge(ref primitive_boxes.GetRawArray()[i].m_bound);
		}

		SetNodeBound(curIndex, ref node_bound);
		//build left branch
		BuildSubTree(primitive_boxes, startIndex, splitIndex);
		//build right branch
		BuildSubTree(primitive_boxes, splitIndex, endIndex);
		m_node_array.GetRawArray()[curIndex].SetEscapeIndex(m_num_nodes - curIndex);
	}

	public QuantizedBvhTree()
	{
		m_num_nodes = 0;
	}

	//! prototype functions for box tree management
	//!@{
	public void BuildTree(GIM_BVH_DATA_ARRAY primitive_boxes)
	{
		CalcQuantization(primitive_boxes);
		// initialize node count to 0
		m_num_nodes = 0;
		// allocate nodes
		m_node_array.Resize(primitive_boxes.Count * 2);
		BuildSubTree(primitive_boxes, 0, primitive_boxes.Count);
		int ibreak = 0;
	}

	public void QuantizePoint(out UShortVector3 quantizedpoint, ref IndexedVector3 point)
	{
		GImpactQuantization.QuantizeClamp(out quantizedpoint, ref point, ref m_global_bound.m_min, ref m_global_bound.m_max, ref m_bvhQuantization);
	}

	public bool TestQuantizedBoxOverlap(int node_index, ref UShortVector3 quantizedMin, ref UShortVector3 quantizedMax)
	{
		return m_node_array[node_index].TestQuantizedBoxOverlapp(ref quantizedMin, ref quantizedMax);
	}

	public void ClearNodes()
	{
		m_node_array.Clear();
		m_num_nodes = 0;
	}

	//! node count
	public int GetNodeCount()
	{
		return m_num_nodes;
	}

	//! tells if the node is a leaf
	public bool IsLeafNode(int nodeindex)
	{
		return m_node_array[nodeindex].IsLeafNode();
	}

	public int GetNodeData(int nodeindex)
	{
		return m_node_array[nodeindex].GetDataIndex();
	}

	public void GetNodeBound(int nodeindex, out AABB bound)
	{
		bound.m_min = GImpactQuantization.Unquantize(ref m_node_array.GetRawArray()[nodeindex].m_quantizedAabbMin, ref m_global_bound.m_min, ref m_bvhQuantization);
		bound.m_max = GImpactQuantization.Unquantize(ref m_node_array.GetRawArray()[nodeindex].m_quantizedAabbMax, ref m_global_bound.m_min, ref m_bvhQuantization);
	}

	public void SetNodeBound(int nodeindex, ref AABB bound)
	{
		GImpactQuantization.QuantizeClamp(out m_node_array.GetRawArray()[nodeindex].m_quantizedAabbMin, ref bound.m_min, ref m_global_bound.m_min, ref m_global_bound.m_max, ref m_bvhQuantization);
		GImpactQuantization.QuantizeClamp(out m_node_array.GetRawArray()[nodeindex].m_quantizedAabbMax, ref bound.m_max, ref m_global_bound.m_min, ref m_global_bound.m_max, ref m_bvhQuantization);
	}

	public int GetLeftNode(int nodeindex)
	{
		return nodeindex + 1;
	}

	public int GetRightNode(int nodeindex)
	{
		if (m_node_array[nodeindex + 1].IsLeafNode())
			return nodeindex + 2;
		return nodeindex + 1 + m_node_array[nodeindex + 1].GetEscapeIndex();
	}

	public int GetEscapeNodeIndex(int nodeindex)
	{
		return m_node_array[nodeindex].GetEscapeIndex();
	}
//public const BT_QUANTIZED_BVH_NODE * get_node_pointer(int index = 0)
//{
//    return &m_node_array[index];
//}
//!@}
}