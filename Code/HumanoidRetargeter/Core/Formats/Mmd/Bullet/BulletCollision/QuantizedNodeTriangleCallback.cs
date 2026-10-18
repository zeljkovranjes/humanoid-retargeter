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
using System.Collections.Generic;
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class QuantizedNodeTriangleCallback : IInternalTriangleIndexCallback
{
	public virtual bool graphics()
	{
		return false;
	}

	private IList<QuantizedBvhNode> m_triangleNodes;
	QuantizedBvh m_optimizedTree; // for quantization
	public QuantizedNodeTriangleCallback(ObjectArray<QuantizedBvhNode> triangleNodes, QuantizedBvh tree)
	{
		m_triangleNodes = triangleNodes;
		m_optimizedTree = tree;
	}

	public virtual void InternalProcessTriangleIndex(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		{
		}

		{
		}

		{
		}

		QuantizedBvhNode node = new QuantizedBvhNode();
		IndexedVector3 aabbMin, aabbMax;
		aabbMin = MathUtil.MAX_VECTOR;
		aabbMax = MathUtil.MIN_VECTOR;
		MathUtil.VectorMin(ref triangle[0], ref aabbMin);
		MathUtil.VectorMax(ref triangle[0], ref aabbMax);
		MathUtil.VectorMin(ref triangle[1], ref aabbMin);
		MathUtil.VectorMax(ref triangle[1], ref aabbMax);
		MathUtil.VectorMin(ref triangle[2], ref aabbMin);
		MathUtil.VectorMax(ref triangle[2], ref aabbMax);
		//PCK: add these checks for zero dimensions of aabb
		float MIN_AABB_DIMENSION = 0.002f;
		float MIN_AABB_HALF_DIMENSION = 0.001f;
		if (aabbMax.X - aabbMin.X < MIN_AABB_DIMENSION)
		{
			aabbMax.X = (aabbMax.X + MIN_AABB_HALF_DIMENSION);
			aabbMin.X = (aabbMin.X - MIN_AABB_HALF_DIMENSION);
		}

		if (aabbMax.Y - aabbMin.Y < MIN_AABB_DIMENSION)
		{
			aabbMax.Y = (aabbMax.Y + MIN_AABB_HALF_DIMENSION);
			aabbMin.Y = (aabbMin.Y - MIN_AABB_HALF_DIMENSION);
		}

		if (aabbMax.Z - aabbMin.Z < MIN_AABB_DIMENSION)
		{
			aabbMax.Z = (aabbMax.Z + MIN_AABB_HALF_DIMENSION);
			aabbMin.Z = (aabbMin.Z - MIN_AABB_HALF_DIMENSION);
		}

		m_optimizedTree.Quantize(out node.m_quantizedAabbMin, ref aabbMin, false);
		m_optimizedTree.Quantize(out node.m_quantizedAabbMax, ref aabbMax, true);
		node.m_escapeIndexOrTriangleIndex = (partId << (31 - MAX_NUM_PARTS_IN_BITS)) | triangleIndex;
		m_triangleNodes.Add(node);
	}

	public virtual void Cleanup()
	{
	}

	public const int MAX_NUM_PARTS_IN_BITS = 10;
}