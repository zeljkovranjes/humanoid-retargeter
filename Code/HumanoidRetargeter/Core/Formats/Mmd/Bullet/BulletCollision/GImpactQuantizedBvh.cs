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

internal class GImpactQuantizedBvh
{
	protected QuantizedBvhTree m_box_tree = new QuantizedBvhTree();
	protected IPrimitiveManagerBase m_primitive_manager;
	static bool QuantizedNodeCollision(GImpactQuantizedBvh boxset0, GImpactQuantizedBvh boxset1, BT_BOX_BOX_TRANSFORM_CACHE trans_cache_1to0, int node0, int node1, bool complete_primitive_tests)
	{
		AABB box0;
		boxset0.GetNodeBound(node0, out box0);
		AABB box1;
		boxset1.GetNodeBound(node1, out box1);
		return box0.OverlappingTransCache(ref box1, ref trans_cache_1to0, complete_primitive_tests);
	//	box1.appy_transform_trans_cache(trans_cache_1to0);
	//	return box0.has_collision(box1);
	}

	//stackless refit
	protected void Refit()
	{
		int nodecount = GetNodeCount();
		while (nodecount-- != 0)
		{
			if (IsLeafNode(nodecount))
			{
				AABB leafbox;
				m_primitive_manager.GetPrimitiveBox(GetNodeData(nodecount), out leafbox);
				SetNodeBound(nodecount, ref leafbox);
			}
			else
			{
				//const GIM_BVH_TREE_NODE * nodepointer = get_node_pointer(nodecount);
				//get left bound
				AABB bound = new AABB();
				bound.Invalidate();
				AABB temp_box;
				int child_node = GetLeftNode(nodecount);
				if (child_node != 0)
				{
					GetNodeBound(child_node, out temp_box);
					bound.Merge(ref temp_box);
				}

				child_node = GetRightNode(nodecount);
				if (child_node != 0)
				{
					GetNodeBound(child_node, out temp_box);
					bound.Merge(ref temp_box);
				}

				SetNodeBound(nodecount, ref bound);
			}
		}
	}

	//! this constructor doesn't build the tree. you must call	buildSet
	public GImpactQuantizedBvh()
	{
	}

	//! this constructor doesn't build the tree. you must call	buildSet
	public GImpactQuantizedBvh(IPrimitiveManagerBase primitive_manager)
	{
		m_primitive_manager = primitive_manager;
	}

	public AABB GetGlobalBox()
	{
		AABB totalbox;
		GetNodeBound(0, out totalbox);
		return totalbox;
	}

	public void SetPrimitiveManager(IPrimitiveManagerBase primitive_manager)
	{
		m_primitive_manager = primitive_manager;
	}

	public IPrimitiveManagerBase GetPrimitiveManager()
	{
		return m_primitive_manager;
	}

	//! node manager prototype functions
	//! this attemps to refit the box set.
	public void Update()
	{
		Refit();
	}

	//! this rebuild the entire set
	public void BuildSet()
	{
		//obtain primitive boxes
		int listSize = m_primitive_manager.GetPrimitiveCount();
		GIM_BVH_DATA_ARRAY primitive_boxes = new GIM_BVH_DATA_ARRAY(listSize);
		// forces boxes to be allocated
		primitive_boxes.Resize(listSize);
		GIM_BVH_DATA[] rawArray = primitive_boxes.GetRawArray();
		for (int i = 0; i < listSize; i++)
		{
			m_primitive_manager.GetPrimitiveBox(i, out rawArray[i].m_bound);
			rawArray[i].m_data = i;
		}

		m_box_tree.BuildTree(primitive_boxes);
	}

	//! returns the indices of the primitives in the m_primitive_manager
	public bool BoxQuery(ref AABB box, ObjectArray<int> collided_results)
	{
		int curIndex = 0;
		int numNodes = GetNodeCount();
		//quantize box
		UShortVector3 quantizedMin;
		UShortVector3 quantizedMax;
		m_box_tree.QuantizePoint(out quantizedMin, ref box.m_min);
		m_box_tree.QuantizePoint(out quantizedMax, ref box.m_max);
		while (curIndex < numNodes)
		{
			//catch bugs in tree data
			bool aabbOverlap = m_box_tree.TestQuantizedBoxOverlap(curIndex, ref quantizedMin, ref quantizedMax);
			bool isLeafNode = IsLeafNode(curIndex);
			if (isLeafNode && aabbOverlap)
			{
				collided_results.Add(GetNodeData(curIndex));
			}

			if (aabbOverlap || isLeafNode)
			{
				//next subnode
				curIndex++;
			}
			else
			{
				//skip node
				curIndex += GetEscapeNodeIndex(curIndex);
			}
		}

		if (collided_results.Count > 0)
			return true;
		return false;
	}

	//! returns the indices of the primitives in the m_primitive_manager
	public bool BoxQueryTrans(ref AABB box, ref IndexedMatrix transform, ObjectArray<int> collided_results)
	{
		AABB transbox = box;
		transbox.ApplyTransform(ref transform);
		return BoxQuery(ref transbox, collided_results);
	}

	//! returns the indices of the primitives in the m_primitive_manager
	public bool RayQuery(ref IndexedVector3 ray_dir, ref IndexedVector3 ray_origin, ObjectArray<int> collided_results)
	{
		int curIndex = 0;
		int numNodes = GetNodeCount();
		while (curIndex < numNodes)
		{
			AABB bound;
			GetNodeBound(curIndex, out bound);
			//catch bugs in tree data
			bool aabbOverlap = bound.CollideRay(ref ray_origin, ref ray_dir);
			bool isLeafNode = IsLeafNode(curIndex);
			if (isLeafNode && aabbOverlap)
			{
				collided_results.Add(GetNodeData(curIndex));
			}

			if (aabbOverlap || isLeafNode)
			{
				//next subnode
				curIndex++;
			}
			else
			{
				//skip node
				curIndex += GetEscapeNodeIndex(curIndex);
			}
		}

		if (collided_results.Count > 0)
			return true;
		return false;
	}

	//! tells if this set has hierarcht
	public bool HasHierarchy()
	{
		return true;
	}

	//! tells if this set is a trimesh
	public bool IsTrimesh()
	{
		return m_primitive_manager.IsTrimesh();
	}

	//! node count
	public int GetNodeCount()
	{
		return m_box_tree.GetNodeCount();
	}

	//! tells if the node is a leaf
	public bool IsLeafNode(int nodeindex)
	{
		return m_box_tree.IsLeafNode(nodeindex);
	}

	public int GetNodeData(int nodeindex)
	{
		return m_box_tree.GetNodeData(nodeindex);
	}

	public void GetNodeBound(int nodeindex, out AABB bound)
	{
		m_box_tree.GetNodeBound(nodeindex, out bound);
	}

	public void SetNodeBound(int nodeindex, ref AABB bound)
	{
		m_box_tree.SetNodeBound(nodeindex, ref bound);
	}

	public int GetLeftNode(int nodeindex)
	{
		return m_box_tree.GetLeftNode(nodeindex);
	}

	public int GetRightNode(int nodeindex)
	{
		return m_box_tree.GetRightNode(nodeindex);
	}

	public int GetEscapeNodeIndex(int nodeindex)
	{
		return m_box_tree.GetEscapeNodeIndex(nodeindex);
	}

	public void GetNodeTriangle(int nodeindex, PrimitiveTriangle triangle)
	{
		m_primitive_manager.GetPrimitiveTriangle(GetNodeData(nodeindex), triangle);
	}

	//SIMD_FORCE_INLINE const BT_QUANTIZED_BVH_NODE * get_node_pointer(int index = 0) const
	//{
	//    return m_box_tree.get_node_pointer(index);
	//}
	public static float GetAverageTreeCollisionTime()
	{
		return 1.0f;
	}

	public static void FindQuantizedCollisionPairsRecursive(GImpactQuantizedBvh boxset0, GImpactQuantizedBvh boxset1, PairSet collision_pairs, ref BT_BOX_BOX_TRANSFORM_CACHE trans_cache_1to0, int node0, int node1, bool complete_primitive_tests)
	{
		if (QuantizedNodeCollision(boxset0, boxset1, trans_cache_1to0, node0, node1, complete_primitive_tests) == false)
			return; //avoid colliding internal nodes
		if (boxset0.IsLeafNode(node0))
		{
			if (boxset1.IsLeafNode(node1))
			{
				// collision result
				collision_pairs.PushPair(boxset0.GetNodeData(node0), boxset1.GetNodeData(node1));
				return;
			}
			else
			{
				//collide left recursive
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, node0, boxset1.GetLeftNode(node1), false);
				//collide right recursive
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, node0, boxset1.GetRightNode(node1), false);
			}
		}
		else
		{
			if (boxset1.IsLeafNode(node1))
			{
				//collide left recursive
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetLeftNode(node0), node1, false);
				//collide right recursive
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetRightNode(node0), node1, false);
			}
			else
			{
				//collide left0 left1
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetLeftNode(node0), boxset1.GetLeftNode(node1), false);
				//collide left0 right1
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetLeftNode(node0), boxset1.GetRightNode(node1), false);
				//collide right0 left1
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetRightNode(node0), boxset1.GetLeftNode(node1), false);
				//collide right0 right1
				FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, boxset0.GetRightNode(node0), boxset1.GetRightNode(node1), false);
			} // else if node1 is not a leaf
		} // else if node0 is not a leaf
	}

	public static void FindCollision(GImpactQuantizedBvh boxset0, ref IndexedMatrix trans0, GImpactQuantizedBvh boxset1, ref IndexedMatrix trans1, PairSet collision_pairs)
	{
		if (boxset0.GetNodeCount() == 0 || boxset1.GetNodeCount() == 0)
		{
			return;
		}

		BT_BOX_BOX_TRANSFORM_CACHE trans_cache_1to0 = new BT_BOX_BOX_TRANSFORM_CACHE();
		trans_cache_1to0.CalcFromHomogenic(ref trans0, ref trans1);
		FindQuantizedCollisionPairsRecursive(boxset0, boxset1, collision_pairs, ref trans_cache_1to0, 0, 0, true);
	}
}