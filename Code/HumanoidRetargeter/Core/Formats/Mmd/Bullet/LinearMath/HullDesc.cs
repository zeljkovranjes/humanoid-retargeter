/*
 *
 * * C# / XNA  port of Bullet (c) 2011 Mark Neale <xexuxjy@hotmail.com>

Stan Melax Convex Hull Computation
Copyright (c) 2003-2006 Stan Melax http://www.melax.com/

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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

internal class HullDesc
{
	public HullDesc()
	{
		mFlags = HullFlag.QF_DEFAULT;
		mVcount = 0;
		//mVertices       = 0;
		//mVertexStride   = sizeof(btVector3);
		mNormalEpsilon = 0.001f;
		mMaxVertices = 4096; // maximum number of points to be considered for a convex hull.
		mMaxFaces = 4096;
	}

	public HullDesc(HullFlag flag, int vcount, IList<IndexedVector3> vertices)
	{
		mFlags = flag;
		mVcount = vcount;
		mVertices = vertices;
		//mVertexStride   = stride;
		mNormalEpsilon = 0.001f;
		mMaxVertices = 4096;
	}

	public bool HasHullFlag(HullFlag flag)
	{
		return ((mFlags & flag) != 0);
	}

	void SetHullFlag(HullFlag flag)
	{
		mFlags |= flag;
	}

	void ClearHullFlag(HullFlag flag)
	{
		mFlags &= ~flag;
	}

	public HullFlag mFlags; // flags to use when generating the convex hull.
	public int mVcount; // number of vertices in the input point cloud
	public IList<IndexedVector3> mVertices = new List<IndexedVector3>(); // the array of vertices.
	public int mVertexStride; // the stride of each vertex, in bytes.
	public float mNormalEpsilon; // the epsilon for removing duplicates.  This is a normalized value, if normalized bit is on.
	public int mMaxVertices; // maximum number of vertices to be considered for the hull!
	public int mMaxFaces;
}