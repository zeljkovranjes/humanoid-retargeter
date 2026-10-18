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
/*! \file btGImpactShape.h
\author Francisco Len N�jera
*/
/*
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
using System.Collections.Generic;
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class GImpactMeshShapePart : GImpactShapeInterface
{
	//! Trimesh primitive manager
	/*!
        Manages the info from btStridingMeshInterface object and controls the Lock/Unlock mechanism
        */
	public class TrimeshPrimitiveManager : IPrimitiveManagerBase
	{
		public float m_margin;
		public StridingMeshInterface m_meshInterface;
		public IndexedVector3 m_scale;
		public int m_part;
		public int m_lock_count;
		public object vertexbase;
		public int numverts;
		public PHY_ScalarType type;
		public int stride;
		public object indexbase;
		public int indexstride;
		public int numfaces;
		public PHY_ScalarType indicestype;
		public int[] indicesHolder = new int[3];
		public TrimeshPrimitiveManager()
		{
			m_margin = 0.01f;
			m_scale = IndexedVector3.One;
			m_lock_count = 0;
			vertexbase = null;
			numverts = 0;
			stride = 0;
			indexbase = null;
			indexstride = 0;
			numfaces = 0;
		}

		public TrimeshPrimitiveManager(TrimeshPrimitiveManager manager)
		{
			m_meshInterface = manager.m_meshInterface;
			m_part = manager.m_part;
			m_margin = manager.m_margin;
			m_scale = manager.m_scale;
			m_lock_count = 0;
			vertexbase = 0;
			numverts = 0;
			stride = 0;
			indexbase = 0;
			indexstride = 0;
			numfaces = 0;
		}

		public TrimeshPrimitiveManager(StridingMeshInterface meshInterface, int part)
		{
			m_meshInterface = meshInterface;
			m_part = part;
			m_scale = m_meshInterface.GetScaling();
			m_margin = 0.1f;
			m_lock_count = 0;
			vertexbase = 0;
			numverts = 0;
			stride = 0;
			indexbase = 0;
			indexstride = 0;
			numfaces = 0;
		}

		public virtual void Cleanup()
		{
		}

		public void Lock()
		{
			if (m_lock_count > 0)
			{
				m_lock_count++;
				return;
			}

			m_meshInterface.GetLockedReadOnlyVertexIndexBase(out vertexbase, out numverts, out type, out stride, out indexbase, out indexstride, out numfaces, out indicestype, m_part);
			m_lock_count = 1;
		}

		public void Unlock()
		{
			if (m_lock_count == 0)
				return;
			if (m_lock_count > 1)
			{
				--m_lock_count;
				return;
			}

			m_meshInterface.UnLockReadOnlyVertexBase(m_part);
			vertexbase = null;
			m_lock_count = 0;
		}

		public virtual bool IsTrimesh()
		{
			return true;
		}

		public virtual int GetPrimitiveCount()
		{
			return (int)numfaces;
		}

		public int GetVertexCount()
		{
			return numverts;
		}

		public void GetIndices(int face_index, out int i0, out int i1, out int i2)
		{
			if (indicestype == PHY_ScalarType.PHY_SHORT)
			{
				ObjectArray<ushort> ushortArray = indexbase as ObjectArray<ushort>;
				if (ushortArray != null)
				{
					ushort[] temp = ushortArray.GetRawArray();
					int index = face_index * indexstride;
					i0 = temp[index];
					i1 = temp[index + 1];
					i2 = temp[index + 2];
				}
				else
				{
					i0 = 0;
					i1 = 1;
					i2 = 2;
				}
			}
			else
			{
				ObjectArray<int> intArray = indexbase as ObjectArray<int>;
				if (intArray != null)
				{
					int[] temp = intArray.GetRawArray();
					int index = face_index * indexstride;
					i0 = temp[index];
					i1 = temp[index + 1];
					i2 = temp[index + 2];
				}
				else
				{
					i0 = 0;
					i1 = 1;
					i2 = 2;
				}
			}
		}

		public void GetVertex(int vertex_index, out IndexedVector3 vertex)
		{
			ObjectArray<IndexedVector3> svertices = vertexbase as ObjectArray<IndexedVector3>;
			vertex = IndexedVector3.Zero;
			if (svertices != null)
			{
				int index = vertex_index * stride;
				vertex = svertices[index] * m_scale.X;
			}
			else
			{
				ObjectArray<float> fvertices = vertexbase as ObjectArray<float>;
				if (svertices != null)
				{
					float[] temp = fvertices.GetRawArray();
					int index = vertex_index * stride;
					vertex.X = temp[index] * m_scale.X;
					vertex.Y = temp[index + 1] * m_scale.Y;
					vertex.Z = temp[index + 2] * m_scale.Z;
				}
			}
		}

		public virtual void GetPrimitiveBox(int prim_index, out AABB primbox)
		{
			PrimitiveTriangle triangle = new PrimitiveTriangle();
			GetPrimitiveTriangle(prim_index, triangle);
			primbox = new AABB();
			primbox.CalcFromTriangleMargin(ref triangle.m_vertices[0], ref triangle.m_vertices[1], ref triangle.m_vertices[2], triangle.m_margin);
		}

		public virtual void GetPrimitiveTriangle(int prim_index, PrimitiveTriangle triangle)
		{
			GetIndices(prim_index, out indicesHolder[0], out indicesHolder[1], out indicesHolder[2]);
			GetVertex(indicesHolder[0], out triangle.m_vertices[0]);
			GetVertex(indicesHolder[1], out triangle.m_vertices[1]);
			GetVertex(indicesHolder[2], out triangle.m_vertices[2]);
			triangle.m_margin = m_margin;
		}

		public void GetBulletTriangle(int prim_index, TriangleShapeEx triangle)
		{
			GetIndices(prim_index, out indicesHolder[0], out indicesHolder[1], out indicesHolder[2]);
			GetVertex(indicesHolder[0], out triangle.m_vertices1[0]);
			GetVertex(indicesHolder[1], out triangle.m_vertices1[1]);
			GetVertex(indicesHolder[2], out triangle.m_vertices1[2]);
			triangle.SetMargin(m_margin);
		}
	};
	protected TrimeshPrimitiveManager m_primitive_manager = new TrimeshPrimitiveManager();
	public GImpactMeshShapePart()
	{
		m_box_set.SetPrimitiveManager(m_primitive_manager);
	}

	public GImpactMeshShapePart(StridingMeshInterface meshInterface, int part)
	{
		m_primitive_manager.m_meshInterface = meshInterface;
		m_primitive_manager.m_part = part;
		m_box_set.SetPrimitiveManager(m_primitive_manager);
	}

	public override void Cleanup()
	{
		base.Cleanup();
	}

	//! if true, then its children must get transforms.
	public override bool ChildrenHasTransform()
	{
		return false;
	}

	//! call when reading child shapes
	public override void LockChildShapes()
	{
		TrimeshPrimitiveManager dummymanager = m_box_set.GetPrimitiveManager() as TrimeshPrimitiveManager;
		dummymanager.Lock();
	}

	public override void UnlockChildShapes()
	{
		TrimeshPrimitiveManager dummymanager = m_box_set.GetPrimitiveManager() as TrimeshPrimitiveManager;
		dummymanager.Unlock();
	}

	//! Gets the number of children
	public override int GetNumChildShapes()
	{
		return m_primitive_manager.GetPrimitiveCount();
	}

	//! Gets the children
	public override CollisionShape GetChildShape(int index)
	{
		{
		}

		return null;
	}

	//! Gets the children transform
	public override IndexedMatrix GetChildTransform(int index)
	{
		{
		}

		return IndexedMatrix.Identity;
	}

	//! Sets the children transform
	/*!
        \post You must call updateBound() for update the box set.
        */
	public override void SetChildTransform(int index, ref IndexedMatrix transform)
	{
		{
		}
	}

	//! Obtains the primitive manager
	public override IPrimitiveManagerBase GetPrimitiveManager()
	{
		return m_primitive_manager;
	}

	public TrimeshPrimitiveManager GetTrimeshPrimitiveManager()
	{
		return m_primitive_manager;
	}

	public virtual void CalculateLocalInertia(float mass, ref IndexedVector3 inertia)
	{
		LockChildShapes();
		inertia = IndexedVector3.Zero;
		int i = GetVertexCount();
		float pointmass = mass / ((float)i);
		while (i-- != 0)
		{
			IndexedVector3 pointintertia;
			GetVertex(i, out pointintertia);
			pointintertia = GImpactMassUtil.GimGetPointInertia(ref pointintertia, pointmass);
			inertia += pointintertia;
		}

		UnlockChildShapes();
	}

	public override String GetName()
	{
		return "GImpactMeshShapePart";
	}

	public override GIMPACT_SHAPE_TYPE GetGImpactShapeType()
	{
		return GIMPACT_SHAPE_TYPE.CONST_GIMPACT_TRIMESH_SHAPE_PART;
	}

	//! Determines if this shape has triangles
	public override bool NeedsRetrieveTriangles()
	{
		return true;
	}

	//! Determines if this shape has tetrahedrons
	public override bool NeedsRetrieveTetrahedrons()
	{
		return false;
	}

	public override void GetBulletTriangle(int prim_index, TriangleShapeEx triangle)
	{
		m_primitive_manager.GetBulletTriangle(prim_index, triangle);
	}

	public override void GetBulletTetrahedron(int prim_index, TetrahedronShapeEx tetrahedron)
	{
		{
		}
	}

	public int GetVertexCount()
	{
		return m_primitive_manager.GetVertexCount();
	}

	public void GetVertex(int vertex_index, out IndexedVector3 vertex)
	{
		m_primitive_manager.GetVertex(vertex_index, out vertex);
	}

	public override void SetMargin(float margin)
	{
		m_primitive_manager.m_margin = margin;
		PostUpdate();
	}

	public override float GetMargin()
	{
		return m_primitive_manager.m_margin;
	}

	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		m_primitive_manager.m_scale = scaling;
		PostUpdate();
	}

	public override IndexedVector3 GetLocalScaling()
	{
		return m_primitive_manager.m_scale;
	}

	public int GetPart()
	{
		return m_primitive_manager.m_part;
	}

	public override void ProcessAllTriangles(ITriangleCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		LockChildShapes();
		AABB box = new AABB();
		box.m_min = aabbMin;
		box.m_max = aabbMax;
		ObjectArray<int> collided = new ObjectArray<int>();
		m_box_set.BoxQuery(ref box, collided);
		if (collided.Count == 0)
		{
			UnlockChildShapes();
			return;
		}

		int part = GetPart();
		PrimitiveTriangle triangle = new PrimitiveTriangle();
		int i = collided.Count;
		while (i-- != 0)
		{
			GetPrimitiveTriangle(collided[i], triangle);
			callback.ProcessTriangle(triangle.m_vertices, part, collided[i]);
		}

		UnlockChildShapes();
	}
}