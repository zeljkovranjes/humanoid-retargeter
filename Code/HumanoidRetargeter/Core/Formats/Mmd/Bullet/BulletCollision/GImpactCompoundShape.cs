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

internal class GImpactCompoundShape : GImpactShapeInterface
{
	protected CompoundPrimitiveManager m_primitive_manager;
	protected ObjectArray<IndexedMatrix> m_childTransforms = new ObjectArray<IndexedMatrix>();
	//protected ObjectArray<CollisionShape>	m_childShapes = new ObjectArray<CollisionShape>();
	// List here as collisionshape is abstract
	protected List<CollisionShape> m_childShapes = new List<CollisionShape>();
	public GImpactCompoundShape() : this(true)
	{
	}

	public GImpactCompoundShape(bool children_has_transform)
	{
		m_primitive_manager.m_compoundShape = this;
		m_box_set.SetPrimitiveManager(m_primitive_manager);
	}

	public override void Cleanup()
	{
	}

	//! if true, then its children must get transforms.
	public override bool ChildrenHasTransform()
	{
		if (m_childTransforms.Count == 0)
			return false;
		return true;
	}

	//! Obtains the primitive manager
	public override IPrimitiveManagerBase GetPrimitiveManager()
	{
		return m_primitive_manager;
	}

	//! Obtains the compopund primitive manager
	public CompoundPrimitiveManager GetCompoundPrimitiveManager()
	{
		return m_primitive_manager;
	}

	//! Gets the number of children
	public override int GetNumChildShapes()
	{
		return m_childShapes.Count;
	}

	//! Use this method for adding children. Only Convex shapes are allowed.
	public void AddChildShape(ref IndexedMatrix localTransform, CollisionShape shape)
	{
		{
		}

		m_childTransforms.Add(localTransform);
		m_childShapes.Add(shape);
	}

	//! Use this method for adding children. Only Convex shapes are allowed.
	public void AddChildShape(CollisionShape shape)
	{
		{
		}

		m_childShapes.Add(shape);
	}

	//! Gets the children
	public override CollisionShape GetChildShape(int index)
	{
		return m_childShapes[index];
	}

	//! Retrieves the bound from a child
	/*!
        */
	public override void GetChildAabb(int child_index, ref IndexedMatrix t, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		if (ChildrenHasTransform())
		{
			IndexedMatrix temp = t * m_childTransforms[child_index];
			m_childShapes[child_index].GetAabb(ref temp, out aabbMin, out aabbMax);
		}
		else
		{
			m_childShapes[child_index].GetAabb(ref t, out aabbMin, out aabbMax);
		}
	}

	//! Gets the children transform
	public override IndexedMatrix GetChildTransform(int index)
	{
		{
		}

		return m_childTransforms[index];
	}

	//! Sets the children transform
	/*!
        \post You must call updateBound() for update the box set.
        */
	public override void SetChildTransform(int index, ref IndexedMatrix transform)
	{
		{
		}

		m_childTransforms[index] = transform;
		PostUpdate();
	}

	//! Determines if this shape has triangles
	public override bool NeedsRetrieveTriangles()
	{
		return false;
	}

	//! Determines if this shape has tetrahedrons
	public override bool NeedsRetrieveTetrahedrons()
	{
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

	//! Calculates the exact inertia tensor for this shape
	public virtual void CalculateLocalInertia(float mass, ref IndexedVector3 inertia)
	{
		LockChildShapes();
		inertia = IndexedVector3.Zero;
		int i = GetNumChildShapes();
		float shapemass = mass / ((float)i);
		while (i-- != 0)
		{
			IndexedVector3 temp_inertia;
			m_childShapes[i].CalculateLocalInertia(shapemass, out temp_inertia);
			if (ChildrenHasTransform())
			{
				inertia = GImpactMassUtil.GimInertiaAddTransformed(ref inertia, ref temp_inertia, ref m_childTransforms.GetRawArray()[i]);
			}
			else
			{
				IndexedMatrix identity = IndexedMatrix.Identity;
				inertia = GImpactMassUtil.GimInertiaAddTransformed(ref inertia, ref temp_inertia, ref identity);
			}
		}
	}

	public override String GetName()
	{
		return "GImpactCompound";
	}

	public override GIMPACT_SHAPE_TYPE GetGImpactShapeType()
	{
		return GIMPACT_SHAPE_TYPE.CONST_GIMPACT_COMPOUND_SHAPE;
	}
}