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
using System;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal struct BT_BOX_BOX_TRANSFORM_CACHE
{
	public IndexedVector3 m_T1to0; //!< Transforms translation of model1 to model 0
	public IndexedBasisMatrix m_R1to0; //!< Transforms Rotation of model1 to model 0, equal  to R0' * R1
	public IndexedBasisMatrix m_AR; //!< Absolute value of m_R1to0
	public void CalcAbsoluteMatrix()
	{
		//		static  IndexedVector3 vepsi(1e-6f,1e-6f,1e-6f);
		//		m_AR[0] = vepsi + m_R1to0[0].absolute();
		//		m_AR[1] = vepsi + m_R1to0[1].absolute();
		//		m_AR[2] = vepsi + m_R1to0[2].absolute();
		int i, j;
		for (i = 0; i < 3; i++)
		{
			for (j = 0; j < 3; j++)
			{
				m_AR[i, j] = 1e-6f + Math.Abs(m_R1to0[i, j]);
			}
		}
	}

	//! Calc the transformation relative  1 to 0. Inverts matrics by transposing
	public void CalcFromHomogenic(ref IndexedMatrix trans0, ref IndexedMatrix trans1)
	{
		IndexedMatrix temp_trans = trans0.Inverse();
		temp_trans = temp_trans * trans1;
		m_T1to0 = temp_trans._origin;
		m_R1to0 = temp_trans._basis;
		CalcAbsoluteMatrix();
	}

	//! Calcs the full invertion of the matrices. Useful for scaling matrices
	public void CalcFromFullInvert(ref IndexedMatrix trans0, ref IndexedMatrix trans1)
	{
		m_R1to0 = trans0._basis.Inverse();
		m_T1to0 = m_R1to0 * (-trans0._origin);
		m_T1to0 += m_R1to0 * trans1._origin;
		m_R1to0 *= trans1._basis;
		CalcAbsoluteMatrix();
	}

	public IndexedVector3 Transform(ref IndexedVector3 point)
	{
		return new IndexedVector3(m_R1to0._el0.Dot(ref point) + m_T1to0.X, m_R1to0._el1.Dot(ref point) + m_T1to0.Y, m_R1to0._el2.Dot(ref point) + m_T1to0.Z);
	}
}