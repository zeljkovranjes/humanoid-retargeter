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

internal class MyNodeOverlapCallback : INodeOverlapCallback, IDisposable
{
	public StridingMeshInterface m_meshInterface;
	public ITriangleCallback m_callback;
	IndexedVector3[] m_triangle = new IndexedVector3[3];
	public MyNodeOverlapCallback()
	{
	} // for pool

	public MyNodeOverlapCallback(ITriangleCallback callback, StridingMeshInterface meshInterface)
	{
		m_meshInterface = meshInterface;
		m_callback = callback;
	}

	public void Initialize(ITriangleCallback callback, StridingMeshInterface meshInterface)
	{
		m_meshInterface = meshInterface;
		m_callback = callback;
	}

	public virtual void ProcessNode(int nodeSubPart, int nodeTriangleIndex)
	{
		//m_triangle.Clear();
		Object vertexBase;
		int numVerts;
		PHY_ScalarType type;
		int stride;
		Object indexBase;
		int indexStride;
		int numfaces;
		PHY_ScalarType indicesType;
		m_meshInterface.GetLockedReadOnlyVertexIndexBase(out vertexBase, out numVerts, out type, out stride, out indexBase, out indexStride, out numfaces, out indicesType, nodeSubPart);
		//unsigned int* gfxbase = (unsigned int*)(indexbase+nodeTriangleIndex*indexstride);
		// force index stride to be 1 regardless.
		indexStride = 3;
		int indexIndex = nodeTriangleIndex * indexStride;
		{
		}

		IndexedVector3 meshScaling = m_meshInterface.GetScaling();
		int[] indexRaw = ((ObjectArray<int>)indexBase).GetRawArray();
		if (vertexBase is ObjectArray<IndexedVector3>)
		{
			IndexedVector3[] vertexBaseRaw = ((ObjectArray<IndexedVector3>)vertexBase).GetRawArray();
			for (int j = 2; j >= 0; j--)
			{
				m_triangle[j] = vertexBaseRaw[indexRaw[indexIndex + j]];
				m_triangle[j] *= meshScaling;
			}
		}
		else if (vertexBase is ObjectArray<float>)
		{
			float[] floats = ((ObjectArray<float>)vertexBase).GetRawArray();
			for (int j = 2; j >= 0; j--)
			{
				int offset = indexRaw[indexIndex + j] * 3;
				m_triangle[j] = new IndexedVector3(floats[offset] * meshScaling.X, floats[offset + 1] * meshScaling.Y, floats[offset + 2] * meshScaling.Z);
			}
		}
		else
		{
			{
			}
		}

		//FIXME - Debug here and on quantized Bvh walking
		/* Perform ray vs. triangle collision here */
		m_callback.ProcessTriangle(m_triangle, nodeSubPart, nodeTriangleIndex);
		m_meshInterface.UnLockReadOnlyVertexBase(nodeSubPart);
	}

	public virtual void Cleanup()
	{
	}

	public void Dispose()
	{
		Cleanup();
		BulletGlobals.MyNodeOverlapCallbackPool.Free(this);
	}
}