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

internal class GImpactMeshShape : GImpactShapeInterface
{
	StridingMeshInterface m_meshInterface;
	protected ObjectArray<GImpactMeshShapePart> m_mesh_parts = new ObjectArray<GImpactMeshShapePart>();
	protected void BuildMeshParts(StridingMeshInterface meshInterface)
	{
		for (int i = 0; i < meshInterface.GetNumSubParts(); ++i)
		{
			GImpactMeshShapePart newpart = new GImpactMeshShapePart(meshInterface, i);
			m_mesh_parts.Add(newpart);
		}
	}

	//! use this function for perfofm refit in bounding boxes
	protected override void CalcLocalAABB()
	{
		m_localAABB.Invalidate();
		int i = m_mesh_parts.Count;
		while (i-- != 0)
		{
			m_mesh_parts[i].UpdateBound();
			m_localAABB.Merge(m_mesh_parts[i].GetLocalBox());
		}
	}

	public GImpactMeshShape(StridingMeshInterface meshInterface)
	{
		m_meshInterface = meshInterface;
		BuildMeshParts(meshInterface);
	}

	public override void Cleanup()
	{
		base.Cleanup();
		m_mesh_parts.Clear();
	}

	public StridingMeshInterface GetMeshInterface()
	{
		return m_meshInterface;
	}

	public int GetMeshPartCount()
	{
		return m_mesh_parts.Count;
	}

	public GImpactMeshShapePart GetMeshPart(int index)
	{
		return m_mesh_parts[index];
	}

	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		localScaling = scaling;
		int i = m_mesh_parts.Count;
		while (i-- != 0)
		{
			GImpactMeshShapePart part = m_mesh_parts[i];
			part.SetLocalScaling(ref scaling);
		}

		m_needs_update = true;
	}

	public override void SetMargin(float margin)
	{
		m_collisionMargin = margin;
		int i = m_mesh_parts.Count;
		while (i-- != 0)
		{
			GImpactMeshShapePart part = m_mesh_parts[i];
			part.SetMargin(margin);
		}

		m_needs_update = true;
	}

	//! Tells to this object that is needed to refit all the meshes
	public override void PostUpdate()
	{
		int i = m_mesh_parts.Count;
		while (i-- != 0)
		{
			GImpactMeshShapePart part = m_mesh_parts[i];
			part.PostUpdate();
		}

		m_needs_update = true;
	}

	public override void CalculateLocalInertia(float mass, out IndexedVector3 inertia)
	{
		inertia = IndexedVector3.Zero;
		int i = GetMeshPartCount();
		float partmass = mass / (float)i;
		while (i-- != 0)
		{
			IndexedVector3 partinertia = IndexedVector3.Zero;
			GetMeshPart(i).CalculateLocalInertia(partmass, ref partinertia);
			inertia += partinertia;
		}
	}

	//! Obtains the primitive manager
	public override IPrimitiveManagerBase GetPrimitiveManager()
	{
		{
		}

		return null;
	}

	//! Gets the number of children
	public override int GetNumChildShapes()
	{
		{
		}

		return 0;
	}

	//! if true, then its children must get transforms.
	public override bool ChildrenHasTransform()
	{
		{
		}

		return false;
	}

	//! Determines if this shape has triangles
	public override bool NeedsRetrieveTriangles()
	{
		{
		}

		return false;
	}

	//! Determines if this shape has tetrahedrons
	public override bool NeedsRetrieveTetrahedrons()
	{
		{
		}

		return false;
	}

	public override void GetBulletTriangle(int prim_index, TriangleShapeEx triangle)
	{
		{
		}
	}

	public override void GetBulletTetrahedron(int prim_index, TetrahedronShapeEx tetrahedron)
	{
		{
		}
	}

	//! call when reading child shapes
	public override void LockChildShapes()
	{
		{
		}
	}

	public override void UnlockChildShapes()
	{
		{
		}
	}

	//! Retrieves the bound from a child
	/*!
        */
	public override void GetChildAabb(int child_index, ref IndexedMatrix t, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		{
		}

		aabbMin = IndexedVector3.Zero;
		aabbMax = IndexedVector3.Zero;
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

	public override GIMPACT_SHAPE_TYPE GetGImpactShapeType()
	{
		return GIMPACT_SHAPE_TYPE.CONST_GIMPACT_TRIMESH_SHAPE;
	}

	public override String GetName()
	{
		return "GImpactMesh";
	}

	public void RayTest(IndexedVector3 rayFrom, ref IndexedVector3 rayTo, RayResultCallback resultCallback)
	{
	}

	//! Function for retrieve triangles.
	/*!
        It gives the triangles in local space
        */
	public override void ProcessAllTriangles(ITriangleCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		int i = m_mesh_parts.Count;
		while (i-- != 0)
		{
			m_mesh_parts[i].ProcessAllTriangles(callback, ref aabbMin, ref aabbMax);
		}
	}
}