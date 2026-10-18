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
using System.Collections.Generic;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System.Diagnostics;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal struct DbvtAabbMm
{
	public IndexedVector3 Center()
	{
		return (_max + _min) / 2f;
	}

	public IndexedVector3 Extent()
	{
		return (_max - _min) / 2f;
	}

	public IndexedVector3 Mins()
	{
		return _min;
	} // should be ref?

	public IndexedVector3 Maxs()
	{
		return _max;
	} // should be ref?

	public IndexedVector3 Lengths()
	{
		return new IndexedVector3();
	}

	public static float Proximity(ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		IndexedVector3 d = (a._min + a._max) - (b._min + b._max);
		return (Math.Abs(d.X) + Math.Abs(d.Y) + Math.Abs(d.Z));
	}

	public static DbvtAabbMm Merge(ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		DbvtAabbMm res = new DbvtAabbMm();
		Merge(ref a, ref b, ref res);
		return (res);
	}

	public static void Merge(ref DbvtAabbMm a, ref DbvtAabbMm b, ref DbvtAabbMm r)
	{
		//r = a;
		//SetMin(ref r._min, ref b._min);
		//SetMax(ref r._max, ref b._max);
		MathUtil.VectorMin(ref a._min, ref b._min, out r._min);
		MathUtil.VectorMax(ref a._max, ref b._max, out r._max);
	}

	public static int Select(ref DbvtAabbMm o, ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		return (Proximity(ref o, ref a) < Proximity(ref o, ref b) ? 0 : 1);
	}

	public static bool NotEqual(ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		return ((a._min.X != b._min.X) || (a._min.Y != b._min.Y) || (a._min.Z != b._min.Z) || (a._max.X != b._max.X) || (a._max.Y != b._max.Y) || (a._max.Z != b._max.Z));
	}

	public static DbvtAabbMm FromCE(ref IndexedVector3 c, ref IndexedVector3 e)
	{
		DbvtAabbMm box;
		box._min = c - e;
		box._max = c + e;
		return (box);
	}

	public static DbvtAabbMm FromCR(ref IndexedVector3 c, float r)
	{
		IndexedVector3 temp = new IndexedVector3(r);
		return (FromCE(ref c, ref temp));
	}

	public static DbvtAabbMm FromMM(ref IndexedVector3 mi, ref IndexedVector3 mx)
	{
		DbvtAabbMm box;
		box._min = mi;
		box._max = mx;
		return (box);
	}

	public static DbvtAabbMm FromPoints(ObjectArray<IndexedVector3> points)
	{
		DbvtAabbMm box;
		box._min = box._max = points[0];
		for (int i = 1; i < points.Count; ++i)
		{
			IndexedVector3 temp = points[i];
			//SetMin(ref box._min, ref temp);
			//SetMax(ref box._max, ref temp);
			MathUtil.VectorMin(ref temp, ref box._min);
			MathUtil.VectorMax(ref temp, ref box._max);
		}

		return (box);
	}

	public static DbvtAabbMm FromPoints(ObjectArray<ObjectArray<IndexedVector3>> points)
	{
		return new DbvtAabbMm();
	}

	public void Expand(IndexedVector3 e)
	{
		_min -= e;
		_max += e;
	}

	public void SignedExpand(IndexedVector3 e)
	{
		if (e.X > 0)
			_max.X = _max.X + e.X;
		else
			_min.X = _min.X + e.X;
		if (e.Y > 0)
			_max.Y = _max.Y + e.Y;
		else
			_min.Y = _min.Y + e.Y;
		if (e.Z > 0)
			_max.Z = _max.Z + e.Z;
		else
			_min.Z = _min.Z + e.Z;
	}

	public bool Contain(ref DbvtAabbMm a)
	{
		return ((_min.X <= a._min.X) && (_min.Y <= a._min.Y) && (_min.Z <= a._min.Z) && (_max.X >= a._max.X) && (_max.Y >= a._max.Y) && (_max.Z >= a._max.Z));
	}

	public static bool Intersect(ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		return ((a._min.X <= b._max.X) && (a._max.X >= b._min.X) && (a._min.Y <= b._max.Y) && (a._max.Y >= b._min.Y) && (a._min.Z <= b._max.Z) && (a._max.Z >= b._min.Z));
	}

	public static bool Intersect(DbvtAabbMm a, ref IndexedVector3 b)
	{
		return ((b.X >= a._min.X) && (b.Y >= a._min.Y) && (b.Z >= a._min.Z) && (b.X <= a._max.X) && (b.Y <= a._max.Y) && (b.Z <= a._max.Z));
	}

	public int Classify(ref IndexedVector3 n, float o, int s)
	{
		IndexedVector3 pi, px;
		switch (s)
		{
			case (0 + 0 + 0):
			{
				px = new IndexedVector3(_min.X, _min.Y, _min.Z);
				pi = new IndexedVector3(_max.X, _max.Y, _max.Z);
				break;
			}

			case (1 + 0 + 0):
			{
				px = new IndexedVector3(_max.X, _min.Y, _min.Z);
				pi = new IndexedVector3(_min.X, _max.Y, _max.Z);
				break;
			}

			case (0 + 2 + 0):
			{
				px = new IndexedVector3(_min.X, _max.Y, _min.Z);
				pi = new IndexedVector3(_max.X, _min.Y, _max.Z);
				break;
			}

			case (1 + 2 + 0):
			{
				px = new IndexedVector3(_max.X, _max.Y, _min.Z);
				pi = new IndexedVector3(_min.X, _min.Y, _max.Z);
				break;
			}

			case (0 + 0 + 4):
			{
				px = new IndexedVector3(_min.X, _min.Y, _max.Z);
				pi = new IndexedVector3(_max.X, _max.Y, _min.Z);
				break;
			}

			case (1 + 0 + 4):
			{
				px = new IndexedVector3(_max.X, _min.Y, _max.Z);
				pi = new IndexedVector3(_min.X, _max.Y, _min.Z);
				break;
			}

			case (0 + 2 + 4):
			{
				px = new IndexedVector3(_min.X, _max.Y, _max.Z);
				pi = new IndexedVector3(_max.X, _min.Y, _min.Z);
				break;
			}

			case (1 + 2 + 4):
			{
				px = new IndexedVector3(_max.X, _max.Y, _max.Z);
				pi = new IndexedVector3(_min.X, _min.Y, _min.Z);
				break;
			}

			default:
			{
				px = new IndexedVector3();
				pi = new IndexedVector3();
				break;
			}
		}

		if ((IndexedVector3.Dot(n, px) + o) < 0)
			return (-1);
		if ((IndexedVector3.Dot(n, pi) + o) >= 0)
			return (+1);
		return (0);
	}

	public float ProjectMinimum(ref IndexedVector3 v, uint signs)
	{
		IndexedVector3[] b =
		{
			_max,
			_min
		};
		IndexedVector3 p = new IndexedVector3(b[(signs >> 0) & 1].X, b[(signs >> 1) & 1].Y, b[(signs >> 2) & 1].Z);
		return (IndexedVector3.Dot(p, v));
	}

	public void AddSpan(ref IndexedVector3 d, ref float smi, ref float smx)
	{
		for (int i = 0; i < 3; ++i)
		{
			if (d[i] < 0)
			{
				smi += _max[i] * d[i];
				smx += _min[i] * d[i];
			}
			else
			{
				smi += _min[i] * d[i];
				smx += _max[i] * d[i];
			}
		}
	}

	public IndexedVector3 _min;
	public IndexedVector3 _max;
}