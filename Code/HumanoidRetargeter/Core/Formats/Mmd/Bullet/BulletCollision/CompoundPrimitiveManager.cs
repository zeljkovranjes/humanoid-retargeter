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

internal class CompoundPrimitiveManager : IPrimitiveManagerBase
{
	//virtual ~CompoundPrimitiveManager() {}
	public virtual void Cleanup()
	{
	}

	public GImpactCompoundShape m_compoundShape;
	public CompoundPrimitiveManager(CompoundPrimitiveManager compound)
	{
		m_compoundShape = compound.m_compoundShape;
	}

	public CompoundPrimitiveManager(GImpactCompoundShape compoundShape)
	{
		m_compoundShape = compoundShape;
	}

	public CompoundPrimitiveManager()
	{
		m_compoundShape = null;
	}

	public virtual bool IsTrimesh()
	{
		return false;
	}

	public virtual int GetPrimitiveCount()
	{
		return m_compoundShape.GetNumChildShapes();
	}

	public virtual void GetPrimitiveBox(int prim_index, out AABB primbox)
	{
		IndexedMatrix prim_trans;
		if (m_compoundShape.ChildrenHasTransform())
		{
			prim_trans = m_compoundShape.GetChildTransform(prim_index);
		}
		else
		{
			prim_trans = IndexedMatrix.Identity;
		}

		CollisionShape shape = m_compoundShape.GetChildShape(prim_index);
		shape.GetAabb(prim_trans, out primbox.m_min, out primbox.m_max);
	}

	public virtual void GetPrimitiveTriangle(int prim_index, PrimitiveTriangle triangle)
	{
		{
		}
	}
}