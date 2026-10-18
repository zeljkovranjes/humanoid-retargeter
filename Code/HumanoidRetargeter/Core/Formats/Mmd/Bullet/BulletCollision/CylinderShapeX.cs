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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class CylinderShapeX : CylinderShape
{
	public CylinderShapeX(IndexedVector3 halfExtents) : this(ref halfExtents)
	{
	}

	public CylinderShapeX(ref IndexedVector3 halfExtents) : base(ref halfExtents)
	{
		m_upAxis = 0;
	}

	public override IndexedVector3 LocalGetSupportingVertexWithoutMargin(ref IndexedVector3 vec)
	{
		return CylinderLocalSupportX(GetHalfExtentsWithoutMargin(), vec);
	}

	public override void BatchedUnitVectorGetSupportingVertexWithoutMargin(IndexedVector3[] vectors, IndexedVector4[] supportVerticesOut, int numVectors)
	{
		IndexedVector3 halfExtents = GetHalfExtentsWithoutMargin();
		for (int i = 0; i < numVectors; i++)
		{
			supportVerticesOut[i] = new IndexedVector4(CylinderLocalSupportX(halfExtents, vectors[i]), 0);
		}
	}

	//debugging
	public override String GetName()
	{
		return "CylinderX";
	}

	public override float GetRadius()
	{
		return GetHalfExtentsWithMargin().Y;
	}
}