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

//------- IndexedVector4 ----------
//public static IndexedVector4 PlaneFlip(IndexedVector4 &IndexedVector4){return IndexedVector4(-IndexedVector4.normal,-IndexedVector4.dist);}
//inline int operator==( IndexedVector4 &a, IndexedVector4 &b ) { return (a.normal==b.normal && a.dist==b.dist); }
//inline int coplanar( IndexedVector4 &a, IndexedVector4 &b ) { return (a==b || a==PlaneFlip(b)); }
//--------- Utility Functions ------
//IndexedVector3  PlaneLineIntersection(IndexedVector4 IndexedVector4, IndexedVector3 p0, IndexedVector3 p1);
//IndexedVector3  PlaneProject(IndexedVector4 IndexedVector4, IndexedVector3 point);
//IndexedVector3  ThreePlaneIntersection(IndexedVector4 p0,IndexedVector4 p1, IndexedVector4 p2);
//float   DistanceBetweenLines(ref IndexedVector3 ustart, ref IndexedVector3 udir, ref IndexedVector3 vstart, ref IndexedVector3 vdir, IndexedVector3 *upoint=NULL, IndexedVector3 *vpoint=NULL);
//IndexedVector3  TriNormal(ref IndexedVector3 v0, ref IndexedVector3 v1, ref IndexedVector3 v2);
//IndexedVector3  NormalOf(IndexedVector3 *vert, int n);
[Flags]
internal enum PlaneIntersectType
{
	COPLANAR = 0, //(0)
	UNDER = 1, //(1)
	OVER = 2, //(2)
	SPLIT = 3 //(OVER|UNDER)
}