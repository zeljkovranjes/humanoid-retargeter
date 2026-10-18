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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal abstract class PolyhedralConvexShape : ConvexInternalShape
{
	public override IndexedVector3 LocalGetSupportingVertexWithoutMargin(ref IndexedVector3 vec)
	{
		return IndexedVector3.Zero;
	}

	public override void Cleanup()
	{
		base.Cleanup();
	}

	public virtual bool InitializePolyhedralFeatures()
	{
		return true;
	}

	public ConvexPolyhedron GetConvexPolyhedron()
	{
		return m_polyhedron;
	}

	public override void BatchedUnitVectorGetSupportingVertexWithoutMargin(IndexedVector3[] vectors, IndexedVector4[] supportVerticesOut, int numVectors)
	{
		int i;
		IndexedVector3 vtx;
		float newDot = 0f;
		for (i = 0; i < numVectors; i++)
		{
			IndexedVector4 temp = supportVerticesOut[i];
			temp.W = -MathUtil.BT_LARGE_FLOAT;
			supportVerticesOut[i] = temp;
		}

		for (int j = 0; j < numVectors; j++)
		{
			IndexedVector3 vec = vectors[j];
			for (i = 0; i < GetNumVertices(); i++)
			{
				GetVertex(i, out vtx);
				newDot = IndexedVector3.Dot(vec, vtx);
				if (newDot > supportVerticesOut[j].W)
				{
					supportVerticesOut[j] = new IndexedVector4(vtx, newDot);
				}
			}
		}
	}

	public override void CalculateLocalInertia(float mass, out IndexedVector3 inertia)
	{
		//not yet, return box inertia
		float margin = GetMargin();
		IndexedMatrix ident = IndexedMatrix.Identity;
		IndexedVector3 aabbMin = IndexedVector3.Zero, aabbMax = IndexedVector3.Zero;
		GetAabb(ref ident, out aabbMin, out aabbMax);
		IndexedVector3 halfExtents = (aabbMax - aabbMin) * 0.5f;
		float lx = 2.0f * (halfExtents.X + margin);
		float ly = 2.0f * (halfExtents.Y + margin);
		float lz = 2.0f * (halfExtents.Z + margin);
		float x2 = lx * lx;
		float y2 = ly * ly;
		float z2 = lz * lz;
		float scaledmass = mass * 0.08333333f;
		inertia = scaledmass * (new IndexedVector3(y2 + z2, x2 + z2, x2 + y2));
	}

	public abstract int GetNumVertices();
	public abstract int GetNumEdges();
	public abstract void GetEdge(int i, out IndexedVector3 pa, out IndexedVector3 pb);
	public abstract void GetVertex(int i, out IndexedVector3 vtx);
	public abstract int GetNumPlanes();
	public abstract void GetPlane(out IndexedVector3 planeNormal, out IndexedVector3 planeSupport, int i);
	//	virtual int getIndex(int i) const = 0 ;
	public abstract bool IsInside(ref IndexedVector3 pt, float tolerance);
	protected ConvexPolyhedron m_polyhedron;
}