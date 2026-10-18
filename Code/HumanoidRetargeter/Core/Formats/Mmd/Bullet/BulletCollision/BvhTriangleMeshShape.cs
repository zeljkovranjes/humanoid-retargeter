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

internal class BvhTriangleMeshShape : TriangleMeshShape
{
	public BvhTriangleMeshShape() : base(null)
	{
		m_bvh = null;
		m_ownsBvh = false;
		m_shapeType = BroadphaseNativeTypes.TRIANGLE_MESH_SHAPE_PROXYTYPE;
		m_triangleInfoMap = null;
	}

	public BvhTriangleMeshShape(StridingMeshInterface meshInterface, bool useQuantizedAabbCompression, bool buildBvh) : base(meshInterface)
	{
		m_bvh = null;
		m_ownsBvh = false;
		m_useQuantizedAabbCompression = useQuantizedAabbCompression;
		m_shapeType = BroadphaseNativeTypes.TRIANGLE_MESH_SHAPE_PROXYTYPE;
		//construct bvh from meshInterface
		//IndexedVector3 bvhAabbMin = IndexedVector3.Zero, bvhAabbMax = IndexedVector3.Zero;
		//if (meshInterface.hasPremadeAabb())
		//{
		//    meshInterface.getPremadeAabb(ref bvhAabbMin, ref bvhAabbMax);
		//}
		//else
		//{
		//    meshInterface.calculateAabbBruteForce(ref bvhAabbMin, ref bvhAabbMax);
		//}
		if (buildBvh)
		{
			BuildOptimizedBvh();
		}
	}

	private void BuildOptimizedBvh()
	{
		if (m_ownsBvh)
		{
			m_bvh.Cleanup();
			m_bvh = null;
		}

		//void* mem = btAlignedAlloc(sizeof(btOptimizedBvh),16);
		m_bvh = new OptimizedBvh();
		//rebuild the bvh...
		m_bvh.Build(m_meshInterface, m_useQuantizedAabbCompression, ref m_localAabbMin, ref m_localAabbMax);
		m_ownsBvh = true;
	}

	public BvhTriangleMeshShape(StridingMeshInterface meshInterface, bool useQuantizedAabbCompression, ref IndexedVector3 bvhAabbMin, ref IndexedVector3 bvhAabbMax, bool buildBvh) : base(meshInterface)
	{
		m_bvh = null;
		m_ownsBvh = false;
		m_useQuantizedAabbCompression = useQuantizedAabbCompression;
		m_shapeType = BroadphaseNativeTypes.TRIANGLE_MESH_SHAPE_PROXYTYPE;
		if (buildBvh)
		{
			m_bvh = new OptimizedBvh();
			m_bvh.Build(meshInterface, m_useQuantizedAabbCompression, ref bvhAabbMin, ref bvhAabbMax);
			m_ownsBvh = true;
		}
	}

	public bool GetOwnsBvh()
	{
		return m_ownsBvh;
	}

	public void PerformRaycast(ITriangleCallback callback, ref IndexedVector3 raySource, ref IndexedVector3 rayTarget)
	{
		if (m_bvh != null)
		{
			using (MyNodeOverlapCallback myNodeCallback = BulletGlobals.MyNodeOverlapCallbackPool.Get())
			{
				myNodeCallback.Initialize(callback, m_meshInterface);
				m_bvh.ReportRayOverlappingNodex(myNodeCallback, ref raySource, ref rayTarget);
			}
		}
	}

	public void PerformConvexCast(ITriangleCallback callback, ref IndexedVector3 boxSource, ref IndexedVector3 boxTarget, ref IndexedVector3 boxMin, ref IndexedVector3 boxMax)
	{
		if (m_bvh != null)
		{
			using (MyNodeOverlapCallback myNodeCallback = BulletGlobals.MyNodeOverlapCallbackPool.Get())
			{
				myNodeCallback.Initialize(callback, m_meshInterface);
				m_bvh.ReportBoxCastOverlappingNodex(myNodeCallback, ref boxSource, ref boxTarget, ref boxMin, ref boxMax);
			}
		}
	}

	//public override void processAllTriangles(ITriangleCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	public override void ProcessAllTriangles(ITriangleCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		if (m_bvh != null)
		{
			using (MyNodeOverlapCallback myNodeCallback = BulletGlobals.MyNodeOverlapCallbackPool.Get())
			{
				myNodeCallback.Initialize(callback, m_meshInterface);
				m_bvh.ReportAabbOverlappingNodex(myNodeCallback, ref aabbMin, ref aabbMax);
			}
		}
	}

	public void RefitTree(IndexedVector3 aabbMin, IndexedVector3 aabbMax)
	{
		RefitTree(ref aabbMin, ref aabbMax);
	}

	public void RefitTree(ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		if (m_bvh != null)
		{
			m_bvh.Refit(m_meshInterface, ref aabbMin, ref aabbMax);
			RecalcLocalAabb();
		}
	}

	public void PartialRefitTree(ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		if (m_bvh != null)
		{
			m_bvh.RefitPartial(m_meshInterface, ref aabbMin, ref aabbMax);
			MathUtil.VectorMin(ref aabbMin, ref m_localAabbMin);
			MathUtil.VectorMax(ref aabbMax, ref m_localAabbMax);
		}
	}

	//debugging
	public override String GetName()
	{
		return "BVHTRIANGLEMESH";
	}

	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		if ((GetLocalScaling() - scaling).LengthSquared() > MathUtil.SIMD_EPSILON)
		{
			base.SetLocalScaling(ref scaling);
			BuildOptimizedBvh();
		}
	}

	public OptimizedBvh GetOptimizedBvh()
	{
		return m_bvh;
	}

	public void SetOptimizedBvh(OptimizedBvh bvh, ref IndexedVector3 scaling)
	{
		{
		}

		{
		}

		m_bvh = bvh;
		m_ownsBvh = false;
		// update the scaling without rebuilding the bvh
		if ((GetLocalScaling() - scaling).LengthSquared() > MathUtil.SIMD_EPSILON)
		{
			base.SetLocalScaling(ref scaling);
		}
	}

	public bool UsesQuantizedAabbCompression()
	{
		return m_useQuantizedAabbCompression;
	}

	public void SetTriangleInfoMap(TriangleInfoMap triangleInfoMap)
	{
		m_triangleInfoMap = triangleInfoMap;
	}

	public TriangleInfoMap GetTriangleInfoMap()
	{
		return m_triangleInfoMap;
	}

	private OptimizedBvh m_bvh;
	private bool m_useQuantizedAabbCompression;
	private bool m_ownsBvh;
	private TriangleInfoMap m_triangleInfoMap;
}