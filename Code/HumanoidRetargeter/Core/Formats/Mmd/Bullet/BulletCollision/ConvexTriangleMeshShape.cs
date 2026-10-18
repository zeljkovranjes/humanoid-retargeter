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

internal class ConvexTriangleMeshShape : PolyhedralConvexAabbCachingShape
{
	public ConvexTriangleMeshShape(StridingMeshInterface meshInterface, bool calcAabb)
	{
		m_shapeType = BroadphaseNativeTypes.CONVEX_TRIANGLEMESH_SHAPE_PROXYTYPE;
		m_stridingMesh = meshInterface;
		if (calcAabb)
		{
			RecalcLocalAabb();
		}
	}

	public override void Cleanup()
	{
		base.Cleanup();
	}

	public StridingMeshInterface GetMeshInterface()
	{
		return m_stridingMesh;
	}

	public override IndexedVector3 LocalGetSupportingVertex(ref IndexedVector3 vec)
	{
		IndexedVector3 supVertex = LocalGetSupportingVertexWithoutMargin(ref vec);
		if (GetMargin() != 0f)
		{
			IndexedVector3 vecnorm = vec;
			if (vecnorm.LengthSquared() < (MathUtil.SIMD_EPSILON * MathUtil.SIMD_EPSILON))
			{
				vecnorm = new IndexedVector3(-1f);
			}

			vecnorm.Normalize();
			supVertex += GetMargin() * vecnorm;
		}

		return supVertex;
	}

	public override IndexedVector3 LocalGetSupportingVertexWithoutMargin(ref IndexedVector3 vec0)
	{
		IndexedVector3 supVec = IndexedVector3.Zero;
		IndexedVector3 vec = vec0;
		float lenSqr = vec.LengthSquared();
		if (lenSqr < 0.0001f)
		{
			vec = new IndexedVector3(1, 0, 0);
		}
		else
		{
			float rlen = (1.0f) / (float)Math.Sqrt(lenSqr);
			vec *= rlen;
		//vec.Normalize();
		}

		LocalSupportVertexCallback supportCallback = new LocalSupportVertexCallback(ref vec);
		IndexedVector3 aabbMax = new IndexedVector3(float.MaxValue);
		IndexedVector3 aabbMin = -aabbMax;
		m_stridingMesh.InternalProcessAllTriangles(supportCallback, ref aabbMin, ref aabbMax);
		supVec = supportCallback.GetSupportVertexLocal();
		return supVec;
	}

	public override void BatchedUnitVectorGetSupportingVertexWithoutMargin(IndexedVector3[] vectors, IndexedVector4[] supportVerticesOut, int numVectors)
	{
		for (int j = 0; j < numVectors; j++)
		{
			IndexedVector3 vec = vectors[j];
			LocalSupportVertexCallback supportCallback = new LocalSupportVertexCallback(ref vec);
			IndexedVector3 aabbMax = MathUtil.MAX_VECTOR;
			IndexedVector3 aabbMin = MathUtil.MIN_VECTOR;
			m_stridingMesh.InternalProcessAllTriangles(supportCallback, ref aabbMin, ref aabbMax);
			supportVerticesOut[j] = new IndexedVector4(supportCallback.GetSupportVertexLocal(), 0);
		}
	}

	//debugging
	public override String GetName()
	{
		return "ConvexTrimesh";
	}

	public override int GetNumVertices()
	{
		return 0;
	}

	public override int GetNumEdges()
	{
		return 0;
	}

	public override void GetEdge(int i, out IndexedVector3 pa, out IndexedVector3 pb)
	{
		{
		}

		pa = IndexedVector3.Zero;
		pb = IndexedVector3.Zero;
	}

	public override void GetVertex(int i, out IndexedVector3 vtx)
	{
		{
		}

		vtx = IndexedVector3.Zero;
	}

	public override int GetNumPlanes()
	{
		return 0;
	}

	public override void GetPlane(out IndexedVector3 planeNormal, out IndexedVector3 planeSupport, int i)
	{
		{
		}

		planeNormal = IndexedVector3.Zero;
		planeSupport = IndexedVector3.Zero;
	}

	public override bool IsInside(ref IndexedVector3 pt, float tolerance)
	{
		{
		}

		return false;
	}

	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		m_stridingMesh.SetScaling(ref scaling);
		RecalcLocalAabb();
	}

	public override IndexedVector3 GetLocalScaling()
	{
		return m_stridingMesh.GetScaling();
	}

	public void CalculatePrincipalAxisTransform(ref IndexedMatrix principal, out IndexedVector3 inertia, float volume)
	{
		CenterCallback centerCallback = new CenterCallback();
		IndexedVector3 aabbMax = MathUtil.MAX_VECTOR;
		IndexedVector3 aabbMin = MathUtil.MIN_VECTOR;
		m_stridingMesh.InternalProcessAllTriangles(centerCallback, ref aabbMin, ref aabbMax);
		IndexedVector3 center = centerCallback.GetCenter();
		principal._origin = center;
		volume = centerCallback.GetVolume();
		InertiaCallback inertiaCallback = new InertiaCallback(ref center);
		m_stridingMesh.InternalProcessAllTriangles(inertiaCallback, ref aabbMax, ref aabbMax);
		IndexedBasisMatrix i = inertiaCallback.GetInertia();
		i.Diagonalize(out principal, 0.00001f, 20);
		//i.diagonalize(principal.getBasis(), 0.00001f, 20);
		inertia = new IndexedVector3(i[0, 0], i[1, 1], i[2, 2]);
		inertia /= volume;
	}

	private StridingMeshInterface m_stridingMesh;
}