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
using System.Collections.Generic;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class TriangleInfoMap : Dictionary<int, TriangleInfo>
{
	public const int TRI_INFO_V0V1_CONVEX = 1;
	public const int TRI_INFO_V1V2_CONVEX = 2;
	public const int TRI_INFO_V2V0_CONVEX = 4;
	public const int TRI_INFO_V0V1_SWAP_NORMALB = 8;
	public const int TRI_INFO_V1V2_SWAP_NORMALB = 16;
	public const int TRI_INFO_V2V0_SWAP_NORMALB = 32;
	public float m_convexEpsilon;
	public float m_planarEpsilon;
	public float m_equalVertexThreshold;
	public float m_edgeDistanceThreshold;
	public float m_maxEdgeAngleThreshold; //ignore edges that connect triangles at an angle larger than this m_maxEdgeAngleThreshold
	public float m_zeroAreaThreshold;
	public TriangleInfoMap()
	{
		m_convexEpsilon = 0.00f;
		m_planarEpsilon = 0.0001f;
		m_equalVertexThreshold = 0.0001f * 0.0001f;
		m_edgeDistanceThreshold = 0.1f;
		m_zeroAreaThreshold = 0.0001f * 0.0001f;
		m_maxEdgeAngleThreshold = MathUtil.SIMD_2_PI;
	}
}