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

internal class HullResult
{
	public HullResult()
	{
		mPolygons = true;
		mNumOutputVertices = 0;
		mNumFaces = 0;
		mNumIndices = 0;
	}

	public bool mPolygons; // true if indices represents polygons, false indices are triangles
	public int mNumOutputVertices; // number of vertices in the output hull
	public IList<IndexedVector3> m_OutputVertices = new List<IndexedVector3>(); // array of vertices
	public int mNumFaces; // the number of faces produced
	public int mNumIndices; // the total number of indices
	public IList<int> m_Indices = new List<int>(); // pointer to indices.
// If triangles, then indices are array indexes into the vertex list.
// If polygons, indices are in the form (number of points in face) (p1, p2, p3, ..) etc..
}