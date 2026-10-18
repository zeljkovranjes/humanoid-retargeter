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

internal abstract class GImpactShapeInterface : ConcaveShape
{
	protected AABB m_localAABB;
	protected bool m_needs_update;
	protected IndexedVector3 localScaling;
	protected GImpactQuantizedBvh m_box_set = new GImpactQuantizedBvh(); // optionally boxset
	//! declare Quantized trees, (you can change to float based trees)
	//typedef btGImpactQuantizedBvh btGImpactBoxSet;
	//! Base class for gimpact shapes
	//class btGImpactShapeInterface : public ConcaveShape
	//{
	//protected:
	//    btAABB m_localAABB;
	//    bool m_needs_update;
	//    btVector3  localScaling;
	//    btGImpactBoxSet m_box_set;// optionally boxset
	//! use this function for perfofm refit in bounding boxes
	//! use this function for perfofm refit in bounding boxes
	protected virtual void CalcLocalAABB()
	{
		LockChildShapes();
		if (m_box_set.GetNodeCount() == 0)
		{
			m_box_set.BuildSet();
		}
		else
		{
			m_box_set.Update();
		}

		UnlockChildShapes();
		m_localAABB = m_box_set.GetGlobalBox();
	}

	public GImpactShapeInterface()
	{
		m_shapeType = BroadphaseNativeTypes.GIMPACT_SHAPE_PROXYTYPE;
		m_localAABB.Invalidate();
		m_needs_update = true;
		localScaling = IndexedVector3.One;
	}

	//! performs refit operation
	/*!
        Updates the entire Box set of this shape.
        \pre postUpdate() must be called for attemps to calculating the box set, else this function
            will does nothing.
        \post if m_needs_update == true, then it calls calcLocalAABB();
        */
	public void UpdateBound()
	{
		if (!m_needs_update)
			return;
		CalcLocalAABB();
		m_needs_update = false;
	}

	//! If the Bounding box is not updated, then this class attemps to calculate it.
	/*!
        \post Calls updateBound() for update the box set.
        */
	public override void GetAabb(ref IndexedMatrix t, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		AABB transformedbox = m_localAABB;
		transformedbox.ApplyTransform(ref t);
		aabbMin = transformedbox.m_min;
		aabbMax = transformedbox.m_max;
	}

	//! Tells to this object that is needed to refit the box set
	public virtual void PostUpdate()
	{
		m_needs_update = true;
	}

	//! Obtains the local box, which is the global calculated box of the total of subshapes
	public AABB GetLocalBox()
	{
		return m_localAABB;
	}

	/*!
        \post You must call updateBound() for update the box set.
        */
	public override void SetLocalScaling(ref IndexedVector3 scaling)
	{
		localScaling = scaling;
		PostUpdate();
	}

	public override IndexedVector3 GetLocalScaling()
	{
		return localScaling;
	}

	public override void SetMargin(float margin)
	{
		m_collisionMargin = margin;
		int i = GetNumChildShapes();
		while (i-- != 0)
		{
			CollisionShape child = GetChildShape(i);
			child.SetMargin(margin);
		}

		m_needs_update = true;
	}

	//! Subshape member functions
	//!@{
	//! Base method for determinig which kind of GIMPACT shape we get
	public abstract GIMPACT_SHAPE_TYPE GetGImpactShapeType();
	//! gets boxset
	public GImpactQuantizedBvh GetBoxSet()
	{
		return m_box_set;
	}

	//! Determines if this class has a hierarchy structure for sorting its primitives
	public bool HasBoxSet()
	{
		if (m_box_set.GetNodeCount() == 0)
			return false;
		return true;
	}

	//! Obtains the primitive manager
	public abstract IPrimitiveManagerBase GetPrimitiveManager();
	//! Gets the number of children
	public abstract int GetNumChildShapes();
	//! if true, then its children must get transforms.
	public abstract bool ChildrenHasTransform();
	//! Determines if this shape has triangles
	public abstract bool NeedsRetrieveTriangles();
	//! Determines if this shape has tetrahedrons
	public abstract bool NeedsRetrieveTetrahedrons();
	public abstract void GetBulletTriangle(int prim_index, TriangleShapeEx triangle);
	public abstract void GetBulletTetrahedron(int prim_index, TetrahedronShapeEx tetrahedron);
	//! call when reading child shapes
	public virtual void LockChildShapes()
	{
	}

	public virtual void UnlockChildShapes()
	{
	}

	//! if this trimesh
	public void GetPrimitiveTriangle(int index, PrimitiveTriangle triangle)
	{
		GetPrimitiveManager().GetPrimitiveTriangle(index, triangle);
	}

	//! Retrieves the bound from a child
	/*!
        */
	public virtual void GetChildAabb(int child_index, ref IndexedMatrix t, out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		AABB child_aabb;
		GetPrimitiveManager().GetPrimitiveBox(child_index, out child_aabb);
		child_aabb.ApplyTransform(ref t);
		aabbMin = child_aabb.m_min;
		aabbMax = child_aabb.m_max;
	}

	//! Gets the child
	public abstract CollisionShape GetChildShape(int index);
	//! Gets the children transform
	public abstract IndexedMatrix GetChildTransform(int index);
	//! Sets the children transform
	/*!
        \post You must call updateBound() for update the box set.
        */
	public abstract void SetChildTransform(int index, ref IndexedMatrix transform);
	//!@}
	//! virtual method for ray collision
	public virtual void RayTest(ref IndexedVector3 rayFrom, ref IndexedVector3 rayTo, RayResultCallback resultCallback)
	{
	}

	//! Function for retrieve triangles.
	/*!
        It gives the triangles in local space
        */
	public override void ProcessAllTriangles(ITriangleCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
	}
//!@}
}