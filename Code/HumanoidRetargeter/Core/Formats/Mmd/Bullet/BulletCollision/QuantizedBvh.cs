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

internal class QuantizedBvh
{
	//Note: currently we have 16 bytes per quantized node
	// MAN - cheat here rather then using size-of , calc a fixed number?
	public const int MAX_SUBTREE_SIZE_IN_BYTES = 2048 / 16;
	// 10 gives the potential for 1024 parts, with at most 2^21 (2097152) (minus one
	// actually) triangles each (since the sign bit is reserved
	public const int MAX_NUM_PARTS_IN_BITS = 10;
	public static int gStackDepth = 0;
	public static int gMaxStackDepth = 0;
	protected IndexedVector3 m_bvhAabbMin;
	protected IndexedVector3 m_bvhAabbMax;
	protected IndexedVector3 m_bvhQuantization;
	protected int m_bulletVersion; //for serialization versioning. It could also be used to detect endianess.
	protected int m_curNodeIndex;
	//quantization data
	protected bool m_useQuantization;
	protected ObjectArray<OptimizedBvhNode> m_leafNodes = new ObjectArray<OptimizedBvhNode>();
	protected ObjectArray<OptimizedBvhNode> m_contiguousNodes = new ObjectArray<OptimizedBvhNode>();
	protected ObjectArray<QuantizedBvhNode> m_quantizedLeafNodes = new ObjectArray<QuantizedBvhNode>();
	protected ObjectArray<QuantizedBvhNode> m_quantizedContiguousNodes = new ObjectArray<QuantizedBvhNode>();
	protected TraversalMode m_traversalMode;
	protected ObjectArray<BvhSubtreeInfo> m_SubtreeHeaders = new ObjectArray<BvhSubtreeInfo>();
	//This is only used for serialization so we don't have to add serialization directly to btAlignedObjectArray
	protected int m_subtreeHeaderCount;
	public int m_maxIterations;
	public QuantizedBvh()
	{
		m_bulletVersion = BulletGlobals.BT_BULLET_VERSION;
		m_useQuantization = false;
		//m_traversalMode = TraversalMode.TRAVERSAL_STACKLESS_CACHE_FRIENDLY;
		m_traversalMode = TraversalMode.TRAVERSAL_STACKLESS;
		//m_traversalMode = TraversalMode.TRAVERSAL_RECURSIVE;
		m_subtreeHeaderCount = 0; //PCK: add this line
		m_bvhAabbMin = new IndexedVector3(-MathUtil.SIMD_INFINITY);
		m_bvhAabbMax = new IndexedVector3(MathUtil.SIMD_INFINITY);
	}

	public virtual void Cleanup()
	{
	}

	protected void SetInternalNodeAabbMin(int nodeIndex, ref IndexedVector3 aabbMin)
	{
		if (m_useQuantization)
		{
			QuantizedBvhNode bvh = m_quantizedContiguousNodes[nodeIndex];
			Quantize(out bvh.m_quantizedAabbMin, ref aabbMin, false);
			m_quantizedContiguousNodes[nodeIndex] = bvh;
		}
		else
		{
			OptimizedBvhNode bvh = m_contiguousNodes[nodeIndex];
			bvh.m_aabbMinOrg = aabbMin;
			m_contiguousNodes[nodeIndex] = bvh;
		}
	}

	public void SetInternalNodeAabbMax(int nodeIndex, ref IndexedVector3 aabbMax)
	{
		if (m_useQuantization)
		{
			QuantizedBvhNode bvh = m_quantizedContiguousNodes[nodeIndex];
			Quantize(out bvh.m_quantizedAabbMax, ref aabbMax, true);
			m_quantizedContiguousNodes[nodeIndex] = bvh;
		}
		else
		{
			OptimizedBvhNode bvh = m_contiguousNodes[nodeIndex];
			bvh.m_aabbMaxOrg = aabbMax;
			m_contiguousNodes[nodeIndex] = bvh;
		}
	}

	public IndexedVector3 GetAabbMin(int nodeIndex)
	{
		if (m_useQuantization)
		{
			IndexedVector3 output;
			UnQuantize(ref m_quantizedLeafNodes[nodeIndex].m_quantizedAabbMin, out output);
			return output;
		}

		//non-quantized
		return m_leafNodes[nodeIndex].m_aabbMinOrg;
	}

	public IndexedVector3 GetAabbMax(int nodeIndex)
	{
		if (m_useQuantization)
		{
			IndexedVector3 output;
			UnQuantize(ref m_quantizedLeafNodes[nodeIndex].m_quantizedAabbMax, out output);
			return output;
		}

		//non-quantized
		return m_leafNodes[nodeIndex].m_aabbMaxOrg;
	}

	public void SetInternalNodeEscapeIndex(int nodeIndex, int escapeIndex)
	{
		if (m_useQuantization)
		{
			m_quantizedContiguousNodes[nodeIndex].m_escapeIndexOrTriangleIndex = -escapeIndex;
		}
		else
		{
			m_contiguousNodes[nodeIndex].m_escapeIndex = escapeIndex;
		}
	}

	public void MergeInternalNodeAabb(int nodeIndex, IndexedVector3 newAabbMin, IndexedVector3 newAabbMax)
	{
		MergeInternalNodeAabb(nodeIndex, ref newAabbMin, ref newAabbMax);
	}

	public void MergeInternalNodeAabb(int nodeIndex, ref IndexedVector3 newAabbMin, ref IndexedVector3 newAabbMax)
	{
		if (m_useQuantization)
		{
			UShortVector3 quantizedAabbMin;
			UShortVector3 quantizedAabbMax;
			Quantize(out quantizedAabbMin, ref newAabbMin, false);
			Quantize(out quantizedAabbMax, ref newAabbMax, true);
			QuantizedBvhNode node = m_quantizedContiguousNodes[nodeIndex];
			node.m_quantizedAabbMin.Min(ref quantizedAabbMin);
			node.m_quantizedAabbMax.Max(ref quantizedAabbMax);
			m_quantizedContiguousNodes[nodeIndex] = node;
		}
		else
		{
			OptimizedBvhNode node = m_contiguousNodes[nodeIndex];
			//non-quantized
			MathUtil.VectorMin(ref newAabbMin, ref node.m_aabbMinOrg);
			MathUtil.VectorMax(ref newAabbMax, ref node.m_aabbMaxOrg);
			m_contiguousNodes[nodeIndex] = node;
		}
	}

	public void SwapLeafNodes(int firstIndex, int secondIndex)
	{
		if (m_useQuantization)
		{
			QuantizedBvhNode tmp = m_quantizedLeafNodes[firstIndex];
			m_quantizedLeafNodes[firstIndex] = m_quantizedLeafNodes[secondIndex];
			m_quantizedLeafNodes[secondIndex] = tmp;
		}
		else
		{
			OptimizedBvhNode tmp = m_leafNodes[firstIndex];
			m_leafNodes[firstIndex] = m_leafNodes[secondIndex];
			m_leafNodes[secondIndex] = tmp;
		}
	}

	public void AssignInternalNodeFromLeafNode(int internalNode, int leafNodeIndex)
	{
		if (m_useQuantization)
		{
			m_quantizedContiguousNodes[internalNode] = m_quantizedLeafNodes[leafNodeIndex];
		}
		else
		{
			m_contiguousNodes[internalNode] = m_leafNodes[leafNodeIndex];
		}
	}

	protected void BuildTree(int startIndex, int endIndex)
	{
		int splitAxis, splitIndex, i;
		int numIndices = endIndex - startIndex;
		int curIndex = m_curNodeIndex;
		{
		}

		if (numIndices == 1)
		{
			AssignInternalNodeFromLeafNode(m_curNodeIndex, startIndex);
			m_curNodeIndex++;
			return;
		}

		//calculate Best Splitting Axis and where to split it. Sort the incoming 'leafNodes' array within range 'startIndex/endIndex'.
		splitAxis = CalcSplittingAxis(startIndex, endIndex);
		splitIndex = SortAndCalcSplittingIndex(startIndex, endIndex, splitAxis);
		int internalNodeIndex = m_curNodeIndex;
		//set the min aabb to 'inf' or a max value, and set the max aabb to a -inf/minimum value.
		//the aabb will be expanded during buildTree/mergeInternalNodeAabb with actual node values
		SetInternalNodeAabbMin(m_curNodeIndex, ref m_bvhAabbMax); //can't use IndexedVector3(SIMD_INFINITY,SIMD_INFINITY,SIMD_INFINITY)) because of quantization
		SetInternalNodeAabbMax(m_curNodeIndex, ref m_bvhAabbMin); //can't use IndexedVector3(-SIMD_INFINITY,-SIMD_INFINITY,-SIMD_INFINITY)) because of quantization
		for (i = startIndex; i < endIndex; i++)
		{
			MergeInternalNodeAabb(m_curNodeIndex, GetAabbMin(i), GetAabbMax(i));
		}

		int lastIndex = m_curNodeIndex;
		m_curNodeIndex++;
		//internalNode->m_escapeIndex;
		int leftChildNodexIndex = m_curNodeIndex;
		m_quantizedContiguousNodes[lastIndex].m_leftChildIndex = leftChildNodexIndex;
		//build left child tree
		BuildTree(startIndex, splitIndex);
		int rightChildNodexIndex = m_curNodeIndex;
		m_quantizedContiguousNodes[lastIndex].m_rightChildIndex = rightChildNodexIndex;
		//build right child tree
		BuildTree(splitIndex, endIndex);
		int escapeIndex = m_curNodeIndex - curIndex;
		if (m_useQuantization)
		{
			//escapeIndex is the number of nodes of this subtree
			//const int sizeQuantizedNode =sizeof(btQuantizedBvhNode);
			//const int treeSizeInBytes = escapeIndex * sizeQuantizedNode;
			// MAN - Cheat and use the number of nodes rather then size.
			if (escapeIndex > MAX_SUBTREE_SIZE_IN_BYTES)
			{
				UpdateSubtreeHeaders(leftChildNodexIndex, rightChildNodexIndex);
			}
		}
		else
		{
		}

		SetInternalNodeEscapeIndex(internalNodeIndex, escapeIndex);
	}

	protected int CalcSplittingAxis(int startIndex, int endIndex)
	{
		IndexedVector3 means = IndexedVector3.Zero;
		IndexedVector3 variance = IndexedVector3.Zero;
		int numIndices = endIndex - startIndex;
		for (int i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (GetAabbMax(i) + GetAabbMin(i));
			means += center;
		}

		means *= (1f / numIndices);
		for (int i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (GetAabbMax(i) + GetAabbMin(i));
			IndexedVector3 diff2 = center - means;
			diff2 = diff2 * diff2;
			variance += diff2;
		}

		variance *= (1f / ((float)(numIndices - 1)));
		return MathUtil.MaxAxis(ref variance);
	}

	protected int SortAndCalcSplittingIndex(int startIndex, int endIndex, int splitAxis)
	{
		int splitIndex = startIndex;
		int numIndices = endIndex - startIndex;
		float splitValue;
		IndexedVector3 means = IndexedVector3.Zero;
		for (int i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (GetAabbMax(i) + GetAabbMin(i));
			means += center;
		}

		means *= (1f / (float)numIndices);
		splitValue = means[splitAxis];
		//sort leafNodes so all values larger then splitValue comes first, and smaller values start from 'splitIndex'.
		for (int i = startIndex; i < endIndex; i++)
		{
			IndexedVector3 center = 0.5f * (GetAabbMax(i) + GetAabbMin(i));
			if (center[splitAxis] > splitValue)
			{
				//swap
				SwapLeafNodes(i, splitIndex);
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

		bool unbal = (splitIndex == startIndex) || (splitIndex == (endIndex));
		{
		}

		return splitIndex;
	}

	protected void WalkStacklessTree(INodeOverlapCallback nodeCallback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		{
		}

		int escapeIndex, curIndex = 0;
		OptimizedBvhNode rootNode = m_contiguousNodes[curIndex];
		int walkIterations = 0;
		bool isLeafNode;
		//PCK: unsigned instead of bool
		bool aabbOverlap;
		while (curIndex < m_curNodeIndex)
		{
			{
			}

			walkIterations++;
			aabbOverlap = AabbUtil2.TestAabbAgainstAabb2(ref aabbMin, ref aabbMax, ref rootNode.m_aabbMinOrg, ref rootNode.m_aabbMaxOrg);
			isLeafNode = rootNode.m_escapeIndex == -1;
			//PCK: unsigned instead of bool
			if (isLeafNode && (aabbOverlap))
			{
				nodeCallback.ProcessNode(rootNode.m_subPart, rootNode.m_triangleIndex);
			}

			//PCK: unsigned instead of bool
			if ((aabbOverlap) || isLeafNode)
			{
				curIndex++;
			}
			else
			{
				escapeIndex = rootNode.m_escapeIndex;
				curIndex += escapeIndex;
			}

			rootNode = m_contiguousNodes[curIndex];
		}

		if (m_maxIterations < walkIterations)
		{
			m_maxIterations = walkIterations;
		}
	}

	protected void WalkStacklessQuantizedTreeAgainstRay(INodeOverlapCallback nodeCallback, ref IndexedVector3 raySource, ref IndexedVector3 rayTarget, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax, int startNodeIndex, int endNodeIndex)
	{
		{
		}

		int curIndex = startNodeIndex;
		int walkIterations = 0;
		int subTreeSize = endNodeIndex - startNodeIndex;
		//(void)subTreeSize;
		QuantizedBvhNode rootNode = m_quantizedContiguousNodes[curIndex];
		int escapeIndex = 0;
		bool isLeafNode = false;
		//PCK: unsigned instead of bool
		bool boxBoxOverlap = false;
		bool rayBoxOverlap = false;
		float lambda_max = 1.0f;
		IndexedVector3 rayDirection = (rayTarget - raySource);
		rayDirection.Normalize();
		lambda_max = IndexedVector3.Dot(rayDirection, rayTarget - raySource);
		rayDirection.X = MathUtil.FuzzyZero(rayDirection.X) ? MathUtil.BT_LARGE_FLOAT : 1f / rayDirection.X;
		rayDirection.Y = MathUtil.FuzzyZero(rayDirection.Y) ? MathUtil.BT_LARGE_FLOAT : 1f / rayDirection.Y;
		rayDirection.Z = MathUtil.FuzzyZero(rayDirection.Z) ? MathUtil.BT_LARGE_FLOAT : 1f / rayDirection.Z;
		//bool[] sign = new bool[] { rayDirection.X < 0.0f, rayDirection.Y < 0.0f, rayDirection.Z < 0.0f };
		bool sign0 = rayDirection.X < 0.0f;
		bool sign1 = rayDirection.Y < 0.0f;
		bool sign2 = rayDirection.Z < 0.0f;
		/* Quick pruning by quantized box */
		IndexedVector3 rayAabbMin = raySource;
		IndexedVector3 rayAabbMax = raySource;
		MathUtil.VectorMin(ref rayTarget, ref rayAabbMin);
		MathUtil.VectorMax(ref rayTarget, ref rayAabbMax);
		/* Add box cast extents to bounding box */
		rayAabbMin += aabbMin;
		rayAabbMax += aabbMax;
		UShortVector3 quantizedQueryAabbMin;
		UShortVector3 quantizedQueryAabbMax;
		QuantizeWithClamp(out quantizedQueryAabbMin, ref rayAabbMin, false);
		QuantizeWithClamp(out quantizedQueryAabbMax, ref rayAabbMax, true);
		while (curIndex < endNodeIndex)
		{
			{
			}

			walkIterations++;
			//PCK: unsigned instead of bool
			// only interested if this is closer than any previous hit
			float param = 1.0f;
			rayBoxOverlap = false;
			boxBoxOverlap = AabbUtil2.TestQuantizedAabbAgainstQuantizedAabb(ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, ref rootNode.m_quantizedAabbMin, ref rootNode.m_quantizedAabbMax);
			isLeafNode = rootNode.IsLeafNode();
			if (boxBoxOverlap)
			{
				//IndexedVector3[] bounds = new IndexedVector3[2];
				//UnQuantize(ref rootNode.m_quantizedAabbMin, out bounds[0]);
				//UnQuantize(ref rootNode.m_quantizedAabbMax, out bounds[1]);
				//bounds[0] -= aabbMax;
				//bounds[1] -= aabbMin;
				IndexedVector3 minBound;
				IndexedVector3 maxBound;
				UnQuantize(ref rootNode.m_quantizedAabbMin, out minBound);
				UnQuantize(ref rootNode.m_quantizedAabbMax, out maxBound);
				/* Add box cast extents */
				minBound -= aabbMax;
				maxBound -= aabbMin;
				IndexedVector3 normal;
				BulletGlobals.StartProfile("btRayAabb2");
				rayBoxOverlap = AabbUtil2.RayAabb2Alt(ref raySource, ref rayDirection, sign0, sign1, sign2, ref minBound, ref maxBound, out param, 0.0f, lambda_max);
				BulletGlobals.StopProfile();
			}

			if (isLeafNode && rayBoxOverlap)
			{
				nodeCallback.ProcessNode(rootNode.GetPartId(), rootNode.GetTriangleIndex());
			}

			//PCK: unsigned instead of bool
			if ((rayBoxOverlap) || isLeafNode)
			{
				curIndex++;
			}
			else
			{
				escapeIndex = rootNode.GetEscapeIndex();
				curIndex += escapeIndex;
			}

			rootNode = m_quantizedContiguousNodes[curIndex];
		}

		if (m_maxIterations < walkIterations)
		{
			m_maxIterations = walkIterations;
		}
	}

	protected void WalkStacklessQuantizedTree(INodeOverlapCallback nodeCallback, ref UShortVector3 quantizedQueryAabbMin, ref UShortVector3 quantizedQueryAabbMax, int startNodeIndex, int endNodeIndex)
	{
		{
		}

		int curIndex = startNodeIndex;
		int walkIterations = 0;
		int subTreeSize = endNodeIndex - startNodeIndex;
		//(void)subTreeSize;
		QuantizedBvhNode rootNode = m_quantizedContiguousNodes[curIndex];
		int escapeIndex = 0;
		bool isLeafNode;
		//PCK: unsigned instead of bool
		bool aabbOverlap = false;
		while (curIndex < endNodeIndex)
		{
			{
			}

			walkIterations++;
			//PCK: unsigned instead of bool
			aabbOverlap = AabbUtil2.TestQuantizedAabbAgainstQuantizedAabb(ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, ref rootNode.m_quantizedAabbMin, ref rootNode.m_quantizedAabbMax);
			isLeafNode = rootNode.IsLeafNode();
			if (isLeafNode && aabbOverlap)
			{
				nodeCallback.ProcessNode(rootNode.GetPartId(), rootNode.GetTriangleIndex());
			}

			//PCK: unsigned instead of bool
			if ((aabbOverlap) || isLeafNode)
			{
				curIndex++;
			}
			else
			{
				escapeIndex = rootNode.GetEscapeIndex();
				curIndex += escapeIndex;
			}

			rootNode = m_quantizedContiguousNodes[curIndex];
		}

		if (m_maxIterations < walkIterations)
		{
			m_maxIterations = walkIterations;
		}
	}

	protected void WalkStacklessTreeAgainstRay(INodeOverlapCallback nodeCallback, ref IndexedVector3 raySource, ref IndexedVector3 rayTarget, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax, int startNodeIndex, int endNodeIndex)
	{
		{
		}

		int escapeIndex, curIndex = 0;
		OptimizedBvhNode rootNode = m_contiguousNodes[curIndex];
		int walkIterations = 0;
		bool isLeafNode;
		bool aabbOverlap = false;
		bool rayBoxOverlap = false;
		float lambda_max = 1.0f;
		/* Quick pruning by quantized box */
		IndexedVector3 rayAabbMin = raySource;
		IndexedVector3 rayAabbMax = raySource;
		MathUtil.VectorMin(ref rayTarget, ref rayAabbMin);
		MathUtil.VectorMax(ref rayTarget, ref rayAabbMax);
		/* Add box cast extents to bounding box */
		rayAabbMin += aabbMin;
		rayAabbMax += aabbMax;
		IndexedVector3 rayDir = (rayTarget - raySource);
		rayDir.Normalize();
		lambda_max = IndexedVector3.Dot(rayDir, rayTarget - raySource);
		IndexedVector3 rayDirectionInverse = new IndexedVector3(MathUtil.FuzzyZero(rayDir.X) ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.X, MathUtil.FuzzyZero(rayDir.Y) ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.Y, MathUtil.FuzzyZero(rayDir.Z) ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.Z);
		//bool[] sign = new bool[] { rayDirectionInverse.X < 0.0f, rayDirectionInverse.Y < 0.0f, rayDirectionInverse.Z < 0.0f };
		bool sign0 = rayDirectionInverse.X < 0.0f;
		bool sign1 = rayDirectionInverse.Y < 0.0f;
		bool sign2 = rayDirectionInverse.Z < 0.0f;
		//IndexedVector3[] bounds = new IndexedVector3[2];
		IndexedVector3 minBound;
		IndexedVector3 maxBound;
		while (curIndex < m_curNodeIndex)
		{
			float param = 1.0f;
			{
			}

			walkIterations++;
			minBound = rootNode.m_aabbMinOrg;
			maxBound = rootNode.m_aabbMaxOrg;
			/* Add box cast extents */
			minBound -= aabbMax;
			maxBound -= aabbMin;
			aabbOverlap = AabbUtil2.TestAabbAgainstAabb2(ref rayAabbMin, ref rayAabbMax, ref rootNode.m_aabbMinOrg, ref rootNode.m_aabbMaxOrg);
			//perhaps profile if it is worth doing the aabbOverlap test first
			rayBoxOverlap = aabbOverlap ? AabbUtil2.RayAabb2Alt(ref raySource, ref rayDirectionInverse, sign0, sign1, sign2, ref minBound, ref maxBound, out param, 0.0f, lambda_max) : false;
			isLeafNode = rootNode.m_escapeIndex == -1;
			//PCK: unsigned instead of bool
			if (isLeafNode && (rayBoxOverlap))
			{
				nodeCallback.ProcessNode(rootNode.m_subPart, rootNode.m_triangleIndex);
			}

			//PCK: unsigned instead of bool
			if ((rayBoxOverlap) || isLeafNode)
			{
				curIndex++;
			}
			else
			{
				escapeIndex = rootNode.m_escapeIndex;
				curIndex += escapeIndex;
			}

			rootNode = m_contiguousNodes[curIndex];
		}

		if (m_maxIterations < walkIterations)
		{
			m_maxIterations = walkIterations;
		}
	}

	protected void WalkStacklessQuantizedTreeCacheFriendly(INodeOverlapCallback nodeCallback, ref UShortVector3 quantizedQueryAabbMin, ref UShortVector3 quantizedQueryAabbMax)
	{
		{
		}

		for (int i = 0; i < m_SubtreeHeaders.Count; i++)
		{
			BvhSubtreeInfo subtree = m_SubtreeHeaders[i];
			//PCK: unsigned instead of bool
			bool overlap = AabbUtil2.TestQuantizedAabbAgainstQuantizedAabb(ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, ref subtree.m_quantizedAabbMin, ref subtree.m_quantizedAabbMax);
			if (overlap)
			{
				WalkStacklessQuantizedTree(nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, subtree.m_rootNodeIndex, subtree.m_rootNodeIndex + subtree.m_subtreeSize);
			}
		}
	}

	protected void WalkRecursiveQuantizedTreeAgainstQueryAabb(ref QuantizedBvhNode currentNode, INodeOverlapCallback nodeCallback, ref UShortVector3 quantizedQueryAabbMin, ref UShortVector3 quantizedQueryAabbMax)
	{
		{
		}

		bool isLeafNode;
		//PCK: unsigned instead of bool
		bool aabbOverlap = false;
		//PCK: unsigned instead of bool
		aabbOverlap = AabbUtil2.TestQuantizedAabbAgainstQuantizedAabb(ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, ref currentNode.m_quantizedAabbMin, ref currentNode.m_quantizedAabbMax);
		isLeafNode = currentNode.IsLeafNode();
		//PCK: unsigned instead of bool
		if (aabbOverlap)
		{
			if (isLeafNode)
			{
				nodeCallback.ProcessNode(currentNode.GetPartId(), currentNode.GetTriangleIndex());
			}
			else
			{
				//process left and right children
				//QuantizedBvhNode leftChildNode = currentNode + 1;
				// Not sure bout thie replacement... but avoids pointer arithmetic
				// this is broken ...
				//int nodeIndex = currentNode.GetTriangleIndex() + 1;
				if (currentNode.m_leftChildIndex > -1 && currentNode.m_leftChildIndex < m_quantizedContiguousNodes.Count)
				{
					QuantizedBvhNode leftChildNode = m_quantizedContiguousNodes[currentNode.m_leftChildIndex];
					WalkRecursiveQuantizedTreeAgainstQueryAabb(ref leftChildNode, nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax);
				}

				if (currentNode.m_rightChildIndex > -1 && currentNode.m_rightChildIndex < m_quantizedContiguousNodes.Count)
				{
					//int newIndex = leftChildNode.IsLeafNode() ? leftChildNode.GetTriangleIndex() + 1 : leftChildNode.GetTriangleIndex() + leftChildNode.GetEscapeIndex();
					QuantizedBvhNode rightChildNode = m_quantizedContiguousNodes[currentNode.m_rightChildIndex];
					WalkRecursiveQuantizedTreeAgainstQueryAabb(ref rightChildNode, nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax);
				}
			}
		}
	}

	protected void WalkRecursiveQuantizedTreeAgainstQuantizedTree(QuantizedBvhNode treeNodeA, QuantizedBvhNode treeNodeB, INodeOverlapCallback nodeCallback)
	{
	// MAN - No implementation??
	}

	protected void UpdateSubtreeHeaders(int leftChildNodexIndex, int rightChildNodexIndex)
	{
		{
		}

		QuantizedBvhNode leftChildNode = m_quantizedContiguousNodes[leftChildNodexIndex];
		int leftSubTreeSize = leftChildNode.IsLeafNode() ? 1 : leftChildNode.GetEscapeIndex();
		//int leftSubTreeSizeInBytes = leftSubTreeSize * static_cast<int>(sizeof(btQuantizedBvhNode));
		QuantizedBvhNode rightChildNode = m_quantizedContiguousNodes[rightChildNodexIndex];
		int rightSubTreeSize = rightChildNode.IsLeafNode() ? 1 : rightChildNode.GetEscapeIndex();
		//int rightSubTreeSizeInBytes = rightSubTreeSize * static_cast<int>(sizeof(btQuantizedBvhNode));
		if (leftChildNode.GetEscapeIndex() <= MAX_SUBTREE_SIZE_IN_BYTES)
		{
			BvhSubtreeInfo subtree = new BvhSubtreeInfo();
			m_SubtreeHeaders.Add(subtree);
			subtree.SetAabbFromQuantizeNode(leftChildNode);
			subtree.m_rootNodeIndex = leftChildNodexIndex;
			subtree.m_subtreeSize = leftSubTreeSize;
		}

		if (rightChildNode.GetEscapeIndex() <= MAX_SUBTREE_SIZE_IN_BYTES)
		{
			BvhSubtreeInfo subtree = new BvhSubtreeInfo();
			m_SubtreeHeaders.Add(subtree);
			subtree.SetAabbFromQuantizeNode(rightChildNode);
			subtree.m_rootNodeIndex = rightChildNodexIndex;
			subtree.m_subtreeSize = rightSubTreeSize;
		}

		//PCK: update the copy of the size
		m_subtreeHeaderCount = m_SubtreeHeaders.Count;
	}

	public void SetQuantizationValues(ref IndexedVector3 bvhAabbMin, ref IndexedVector3 bvhAabbMax)
	{
		SetQuantizationValues(ref bvhAabbMin, ref bvhAabbMax, 1.0f);
	}

	public void SetQuantizationValues(ref IndexedVector3 bvhAabbMin, ref IndexedVector3 bvhAabbMax, float quantizationMargin)
	{
		//enlarge the AABB to avoid division by zero when initializing the quantization values
		IndexedVector3 clampValue = new IndexedVector3(quantizationMargin);
		m_bvhAabbMin = bvhAabbMin - clampValue;
		m_bvhAabbMax = bvhAabbMax + clampValue;
		IndexedVector3 aabbSize = m_bvhAabbMax - m_bvhAabbMin;
		m_bvhQuantization = new IndexedVector3(65533.000f) / aabbSize;
		m_useQuantization = true;
	}

	public ObjectArray<QuantizedBvhNode> GetLeafNodeArray()
	{
		return m_quantizedLeafNodes;
	}

	public void BuildInternal()
	{
		m_useQuantization = true;
		int numLeafNodes = 0;
		if (m_useQuantization)
		{
			//now we have an array of leafnodes in m_leafNodes
			numLeafNodes = m_quantizedLeafNodes.Count;
			m_quantizedContiguousNodes.Capacity = 2 * numLeafNodes;
		}

		m_curNodeIndex = 0;
		BuildTree(0, numLeafNodes);
		for (int i = 0; i < m_quantizedContiguousNodes.Count; ++i)
		{
			QuantizedBvhNode bvhn = m_quantizedContiguousNodes[i];
			{
			}
		}

		if (m_useQuantization && m_SubtreeHeaders.Count == 0)
		{
			//BvhSubtrreeInfo subtree = m_SubtreeHeaders.expand();
			BvhSubtreeInfo subtree = new BvhSubtreeInfo();
			m_SubtreeHeaders.Add(subtree);
			subtree.SetAabbFromQuantizeNode(m_quantizedContiguousNodes[0]);
			subtree.m_rootNodeIndex = 0;
			subtree.m_subtreeSize = m_quantizedContiguousNodes[0].IsLeafNode() ? 1 : m_quantizedContiguousNodes[0].GetEscapeIndex();
		}

		//PCK: update the copy of the size
		m_subtreeHeaderCount = m_SubtreeHeaders.Count;
		//PCK: clear m_quantizedLeafNodes and m_leafNodes, they are temporary
		m_quantizedLeafNodes.Clear();
		m_leafNodes.Clear();
	}

	public void ReportAabbOverlappingNodex(INodeOverlapCallback nodeCallback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		//either choose recursive traversal (walkTree) or stackless (walkStacklessTree)
		if (m_useQuantization)
		{
			UShortVector3 quantizedQueryAabbMin;
			UShortVector3 quantizedQueryAabbMax;
			QuantizeWithClamp(out quantizedQueryAabbMin, ref aabbMin, false);
			QuantizeWithClamp(out quantizedQueryAabbMax, ref aabbMax, true);
			switch (m_traversalMode)
			{
				case TraversalMode.TRAVERSAL_STACKLESS:
					if (m_useQuantization)
					{
						WalkStacklessQuantizedTree(nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax, 0, m_curNodeIndex);
					}
					else
					{
						WalkStacklessTree(nodeCallback, ref aabbMin, ref aabbMax);
					}

					break;
				case TraversalMode.TRAVERSAL_STACKLESS_CACHE_FRIENDLY:
					WalkStacklessQuantizedTreeCacheFriendly(nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax);
					break;
				case TraversalMode.TRAVERSAL_RECURSIVE:
				{
					QuantizedBvhNode rootNode = m_quantizedContiguousNodes[0];
					WalkRecursiveQuantizedTreeAgainstQueryAabb(ref rootNode, nodeCallback, ref quantizedQueryAabbMin, ref quantizedQueryAabbMax);
				}

					break;
				default:
				{
				}

					break;
			}
		}
		else
		{
			WalkStacklessTree(nodeCallback, ref aabbMin, ref aabbMax);
		}
	}

	public void ReportRayOverlappingNodex(INodeOverlapCallback nodeCallback, ref IndexedVector3 raySource, ref IndexedVector3 rayTarget)
	{
		IndexedVector3 abMin = IndexedVector3.Zero, abMax = IndexedVector3.Zero;
		ReportBoxCastOverlappingNodex(nodeCallback, ref raySource, ref rayTarget, ref abMin, ref abMax);
	}

	public void ReportBoxCastOverlappingNodex(INodeOverlapCallback nodeCallback, ref IndexedVector3 raySource, ref IndexedVector3 rayTarget, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		//always use stackless
		if (m_useQuantization)
		{
			WalkStacklessQuantizedTreeAgainstRay(nodeCallback, ref raySource, ref rayTarget, ref aabbMin, ref aabbMax, 0, m_curNodeIndex);
		}
		else
		{
			WalkStacklessTreeAgainstRay(nodeCallback, ref raySource, ref rayTarget, ref aabbMin, ref aabbMax, 0, m_curNodeIndex);
		}
	/*
            {
                //recursive traversal
                IndexedVector3 qaabbMin = raySource;
                IndexedVector3 qaabbMax = raySource;
                qaabbMin.setMin(rayTarget);
                qaabbMax.setMax(rayTarget);
                qaabbMin += aabbMin;
                qaabbMax += aabbMax;
                reportAabbOverlappingNodex(nodeCallback,qaabbMin,qaabbMax);
            }
            */
	}

	public void Quantize(out UShortVector3 result, ref IndexedVector3 point, bool isMax)
	{
		{
		}

		{
		}

		{
		}

		{
		}

		{
		}

		{
		}

		{
		}

		IndexedVector3 v = (point - m_bvhAabbMin) * m_bvhQuantization;
		result = new UShortVector3();
		if (isMax)
		{
			result.X = (ushort)(((ushort)(v.X + 1f) | 1));
			result.Y = (ushort)(((ushort)(v.Y + 1f) | 1));
			result.Z = (ushort)(((ushort)(v.Z + 1f) | 1));
		}
		else
		{
			result.X = (ushort)(((ushort)(v.X) & 0xfffe));
			result.Y = (ushort)(((ushort)(v.Y) & 0xfffe));
			result.Z = (ushort)(((ushort)(v.Z) & 0xfffe));
		}
	}

	//public void quantizeWithClamp(ref UShortVector3 result, ref UShortVector3 point2, bool isMax)
	//{
	//    Debug.Assert(m_useQuantization);
	//    IndexedVector3 clampedPoint = new IndexedVector3(point2.X, point2.Y, point2.Z);
	//    MathUtil.vectorMax(ref m_bvhAabbMin, ref clampedPoint);
	//    MathUtil.vectorMin(ref m_bvhAabbMax, ref clampedPoint);
	//    quantize(ref result, ref clampedPoint, isMax);
	//}
	public void QuantizeWithClamp(out UShortVector3 result, ref IndexedVector3 point2, bool isMax)
	{
		{
		}

		IndexedVector3 clampedPoint = point2;
		MathUtil.VectorMax(ref m_bvhAabbMin, ref clampedPoint);
		MathUtil.VectorMin(ref m_bvhAabbMax, ref clampedPoint);
		Quantize(out result, ref clampedPoint, isMax);
	}

	//public void UnQuantize(UShortVector3 vecIn,out IndexedVector3 vecOut)
	//{
	//    UnQuantize(ref vecIn,out vecOut);
	//}
	public void UnQuantize(ref UShortVector3 vecIn, out IndexedVector3 vecOut)
	{
		vecOut = new IndexedVector3(((float)vecIn.X) / m_bvhQuantization.X, ((float)vecIn.Y) / m_bvhQuantization.Y, ((float)vecIn.Z) / m_bvhQuantization.Z);
		vecOut += m_bvhAabbMin;
	}

	void SetTraversalMode(TraversalMode traversalMode)
	{
		m_traversalMode = traversalMode;
	}

	public ObjectArray<QuantizedBvhNode> GetQuantizedNodeArray()
	{
		return m_quantizedContiguousNodes;
	}

	public ObjectArray<BvhSubtreeInfo> GetSubtreeInfoArray()
	{
		return m_SubtreeHeaders;
	}

	public bool IsQuantized()
	{
		return m_useQuantization;
	}
}