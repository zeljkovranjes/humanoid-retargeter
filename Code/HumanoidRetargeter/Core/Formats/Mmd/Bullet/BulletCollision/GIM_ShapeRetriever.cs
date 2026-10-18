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
// Adapted from BulletXNA 73b2e1b9: namespace isolation, no XNA rendering, one type per file.
#nullable disable
#pragma warning disable CS1591, CS0168, CS0219, CS0414, CS0660, CS0661, CS0659, CS0693, CS8981, CS0108, CS0114
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class GIM_ShapeRetriever : IDisposable
{
	public GImpactShapeInterface m_gim_shape;
	public TriangleShapeEx m_trishape = new TriangleShapeEx();
	public TetrahedronShapeEx m_tetrashape = new TetrahedronShapeEx();
	public class ChildShapeRetriever
	{
		public GIM_ShapeRetriever m_parent;
		public virtual CollisionShape GetChildShape(int index)
		{
			return m_parent.m_gim_shape.GetChildShape(index);
		}

		public virtual void Cleanup()
		{
		}
	}

	public class TriangleShapeRetriever : ChildShapeRetriever
	{
		public override CollisionShape GetChildShape(int index)
		{
			m_parent.m_gim_shape.GetBulletTriangle(index, m_parent.m_trishape);
			return m_parent.m_trishape;
		}

		public override void Cleanup()
		{
		}
	}

	public class TetraShapeRetriever : ChildShapeRetriever
	{
		public override CollisionShape GetChildShape(int index)
		{
			m_parent.m_gim_shape.GetBulletTetrahedron(index, m_parent.m_tetrashape);
			return m_parent.m_tetrashape;
		}
	};
	public ChildShapeRetriever m_child_retriever = new ChildShapeRetriever();
	public TriangleShapeRetriever m_tri_retriever = new TriangleShapeRetriever();
	public TetraShapeRetriever m_tetra_retriever = new TetraShapeRetriever();
	public ChildShapeRetriever m_current_retriever;
	public GIM_ShapeRetriever()
	{
	} // for pool

	public GIM_ShapeRetriever(GImpactShapeInterface gim_shape)
	{
		m_gim_shape = gim_shape;
		//select retriever
		if (m_gim_shape.NeedsRetrieveTriangles())
		{
			m_current_retriever = m_tri_retriever;
		}
		else if (m_gim_shape.NeedsRetrieveTetrahedrons())
		{
			m_current_retriever = m_tetra_retriever;
		}
		else
		{
			m_current_retriever = m_child_retriever;
		}

		m_current_retriever.m_parent = this;
	}

	public void Initialize(GImpactShapeInterface gim_shape)
	{
		m_gim_shape = gim_shape;
		//select retriever
		if (m_gim_shape.NeedsRetrieveTriangles())
		{
			m_current_retriever = m_tri_retriever;
		}
		else if (m_gim_shape.NeedsRetrieveTetrahedrons())
		{
			m_current_retriever = m_tetra_retriever;
		}
		else
		{
			m_current_retriever = m_child_retriever;
		}

		m_current_retriever.m_parent = this;
	}

	public CollisionShape GetChildShape(int index)
	{
		return m_current_retriever.GetChildShape(index);
	}

	public void Dispose()
	{
		BulletGlobals.GIM_ShapeRetrieverPool.Free(this);
	}
}