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

internal class GJK : IDisposable
{
	public GJK()
	{
	//Initialise();
	}

	public void Initialise()
	{
		m_ray = IndexedVector3.Zero;
		m_nfree = 0;
		m_status = GJKStatus.Failed;
		m_current = 0;
		m_distance = 0f;
		for (int i = 0; i < m_simplices.Length; ++i)
		{
			if (m_simplices[i] == null)
			{
				m_simplices[i] = new sSimplex();
			}
		}

		for (int i = 0; i < m_store.Length; ++i)
		{
			if (m_store[i] == null)
			{
				m_store[i] = new sSV();
			}
		}
	}

	IndexedVector3[] lastw = new IndexedVector3[4];
	public GJKStatus Evaluate(GjkEpaSolver2MinkowskiDiff shapearg, ref IndexedVector3 guess)
	{
		uint iterations = 0;
		float sqdist = 0f;
		float alpha = 0f;
		uint clastw = 0;
		/* Initialize solver		*/
		m_free[0] = m_store[0];
		m_free[1] = m_store[1];
		m_free[2] = m_store[2];
		m_free[3] = m_store[3];
		m_nfree = 4;
		m_current = 0;
		m_status = GJKStatus.Valid;
		m_shape = shapearg;
		m_distance = 0f;
		/* Initialize simplex		*/
		m_simplices[0].rank = 0;
		m_ray = guess;
		float sqrl = m_ray.LengthSquared();
		IndexedVector3 temp = sqrl > 0 ? -m_ray : new IndexedVector3(1, 0, 0);
		AppendVertice(m_simplices[0], ref temp);
		m_simplices[0].p[0] = 1;
		m_ray = m_simplices[0].c[0].w;
		sqdist = sqrl;
		lastw[0] = lastw[1] = lastw[2] = lastw[3] = m_ray;
		/* Loop						*/
		do
		{
			uint next = 1 - m_current;
			sSimplex cs = m_simplices[m_current];
			sSimplex ns = m_simplices[next];
			/* Check zero							*/
			float rl = m_ray.Length();
			if (rl < GjkEpaSolver2.GJK_MIN_DISTANCE)
			{ /* Touching or inside				*/
				m_status = GJKStatus.Inside;
				break;
			}

			/* Append new vertice in -'v' direction	*/
			IndexedVector3 temp2 = -m_ray;
			AppendVertice(cs, ref temp2);
			IndexedVector3 w = cs.c[cs.rank - 1].w;
			bool found = false;
			for (int i = 0; i < 4; ++i)
			{
				if ((w - lastw[i]).LengthSquared() < GjkEpaSolver2.GJK_DUPLICATED_EPS)
				{
					found = true;
					break;
				}
			}

			if (found)
			{ /* Return old simplex				*/
				RemoveVertice(m_simplices[m_current]);
				break;
			}
			else
			{ /* Update lastw					*/
				lastw[clastw = (clastw + 1) & 3] = w;
			}

			/* Check for termination				*/
			float omega = IndexedVector3.Dot(ref m_ray, ref w) / rl;
			alpha = Math.Max(omega, alpha);
			if (((rl - alpha) - (GjkEpaSolver2.GJK_ACCURARY * rl)) <= 0)
			{ /* Return old simplex				*/
				RemoveVertice(m_simplices[m_current]);
				break;
			}

			/* Reduce simplex						*/
			IndexedVector4 weights = new IndexedVector4();
			uint mask = 0;
			switch (cs.rank)
			{
				case 2:
				{
					sqdist = GJK.ProjectOrigin(ref cs.c[0].w, ref cs.c[1].w, ref weights, ref mask);
					break;
				}

				case 3:
				{
					sqdist = GJK.ProjectOrigin(ref cs.c[0].w, ref cs.c[1].w, ref cs.c[2].w, ref weights, ref mask);
					break;
				}

				case 4:
				{
					sqdist = GJK.ProjectOrigin(ref cs.c[0].w, ref cs.c[1].w, ref cs.c[2].w, ref cs.c[3].w, ref weights, ref mask);
					break;
				}
			}

			if (sqdist >= 0)
			{ /* Valid	*/
				ns.rank = 0;
				m_ray = IndexedVector3.Zero;
				m_current = next;
				for (uint i = 0, ni = cs.rank; i < ni; ++i)
				{
					if ((mask & (1 << (int)i)) != 0)
					{
						ns.c[ns.rank] = cs.c[i];
						float weight = weights[(int)i];
						ns.p[ns.rank++] = weight;
						m_ray += cs.c[i].w * weight;
					}
					else
					{
						m_free[m_nfree++] = cs.c[i];
					}
				}

				if (mask == 15)
				{
					m_status = GJKStatus.Inside;
				}
			}
			else
			{ /* Return old simplex				*/
				RemoveVertice(m_simplices[m_current]);
				break;
			}

			m_status = ((++iterations) < GjkEpaSolver2.GJK_MAX_ITERATIONS) ? m_status : GJKStatus.Failed;
		}
		while (m_status == GJKStatus.Valid);
		m_simplex = m_simplices[m_current];
		switch (m_status)
		{
			case GJKStatus.Valid:
			{
				m_distance = m_ray.Length();
				break;
			}

			case GJKStatus.Inside:
			{
				m_distance = 0;
				break;
			}
		}

		return (m_status);
	}

	public bool EncloseOrigin()
	{
		switch (m_simplex.rank)
		{
			case 1:
			{
				for (int i = 0; i < 3; ++i)
				{
					IndexedVector3 axis = IndexedVector3.Zero;
					axis[i] = 1f;
					AppendVertice(m_simplex, ref axis);
					if (EncloseOrigin())
					{
						return (true);
					}

					RemoveVertice(m_simplex);
					IndexedVector3 temp = -axis;
					AppendVertice(m_simplex, ref temp);
					if (EncloseOrigin())
					{
						return (true);
					}

					RemoveVertice(m_simplex);
				}

				break;
			}

			case 2:
			{
				IndexedVector3 d = m_simplex.c[1].w - m_simplex.c[0].w;
				for (int i = 0; i < 3; ++i)
				{
					IndexedVector3 axis = IndexedVector3.Zero;
					axis[i] = 1f;
					IndexedVector3 p = IndexedVector3.Cross(d, axis);
					if (p.LengthSquared() > 0)
					{
						AppendVertice(m_simplex, ref p);
						if (EncloseOrigin())
						{
							return (true);
						}

						RemoveVertice(m_simplex);
						IndexedVector3 temp = -p;
						AppendVertice(m_simplex, ref temp);
						if (EncloseOrigin())
						{
							return (true);
						}

						RemoveVertice(m_simplex);
					}
				}

				break;
			}

			case 3:
			{
				IndexedVector3 n = IndexedVector3.Cross(m_simplex.c[1].w - m_simplex.c[0].w, m_simplex.c[2].w - m_simplex.c[0].w);
				if (n.LengthSquared() > 0)
				{
					AppendVertice(m_simplex, ref n);
					if (EncloseOrigin())
					{
						return (true);
					}

					RemoveVertice(m_simplex);
					IndexedVector3 temp = -n;
					AppendVertice(m_simplex, ref temp);
					if (EncloseOrigin())
					{
						return (true);
					}

					RemoveVertice(m_simplex);
				}

				break;
			}

			case 4:
			{
				if (Math.Abs(GJK.Det(m_simplex.c[0].w - m_simplex.c[3].w, m_simplex.c[1].w - m_simplex.c[3].w, m_simplex.c[2].w - m_simplex.c[3].w)) > 0)
					return (true);
				break;
			}

			default:
			{
				break;
			}
		}

		return (false);
	}

	public void GetSupport(ref IndexedVector3 d, ref sSV sv)
	{
		sv.d = d / d.Length();
		sv.w = m_shape.Support(ref sv.d);
	}

	public void RemoveVertice(sSimplex simplex)
	{
		m_free[m_nfree++] = simplex.c[--simplex.rank];
	}

	public void AppendVertice(sSimplex simplex, ref IndexedVector3 v)
	{
		simplex.p[simplex.rank] = 0;
		simplex.c[simplex.rank] = m_free[--m_nfree];
		GetSupport(ref v, ref simplex.c[simplex.rank++]);
	}

	public static float Det(IndexedVector3 a, IndexedVector3 b, IndexedVector3 c)
	{
		return Det(ref a, ref b, ref c);
	}

	public static float Det(ref IndexedVector3 a, ref IndexedVector3 b, ref IndexedVector3 c)
	{
		return (a.Y * b.Z * c.X + a.Z * b.X * c.Y - a.X * b.Z * c.Y - a.Y * b.X * c.Z + a.X * b.Y * c.Z - a.Z * b.Y * c.X);
	}

	public static float ProjectOrigin(ref IndexedVector3 a, ref IndexedVector3 b, ref IndexedVector4 w, ref uint m)
	{
		IndexedVector3 d = b - a;
		float l = d.LengthSquared();
		if (l > GjkEpaSolver2.GJK_SIMPLEX2_EPS)
		{
			float t = (l > 0f ? (-IndexedVector3.Dot(ref a, ref d) / l) : 0f);
			if (t >= 1)
			{
				w.X = 0f;
				w.Y = 1f;
				m = 2;
				return b.LengthSquared();
			}
			else if (t <= 0)
			{
				w.X = 1f;
				w.Y = 0f;
				m = 1;
				return a.LengthSquared();
			}
			else
			{
				w.X = 1 - (w.Y = t);
				m = 3;
				return (a + d * t).LengthSquared();
			}
		}

		return (-1);
	}

	static uint[] imd3 =
	{
		1,
		2,
		0
	};
	static IndexedVector3[] vt = new IndexedVector3[3];
	static IndexedVector3[] dl = new IndexedVector3[3];
	static bool inhere2 = false;
	public static float ProjectOrigin(ref IndexedVector3 a, ref IndexedVector3 b, ref IndexedVector3 c, ref IndexedVector4 w, ref uint m)
	{
		{
		}

		inhere2 = true;
		vt[0] = a;
		vt[1] = b;
		vt[2] = c;
		dl[0] = a - b;
		dl[1] = b - c;
		dl[2] = c - a;
		IndexedVector3 n = IndexedVector3.Cross(dl[0], dl[1]);
		float l = n.LengthSquared();
		if (l > GjkEpaSolver2.GJK_SIMPLEX3_EPS)
		{
			float mindist = -1f;
			IndexedVector4 subw = new IndexedVector4();
			uint subm = 0;
			for (int i = 0; i < 3; ++i)
			{
				if (IndexedVector3.Dot(vt[i], IndexedVector3.Cross(dl[i], n)) > 0)
				{
					uint j = imd3[i];
					float subd = GJK.ProjectOrigin(ref vt[i], ref vt[j], ref subw, ref subm);
					if ((mindist < 0) || (subd < mindist))
					{
						mindist = subd;
						m = (uint)((((subm & 1) != 0) ? 1 << i : 0) + (((subm & 2) != 0) ? 1 << (int)j : 0));
						w[(int)i] = subw.X;
						w[(int)j] = subw.Y;
						w[(int)imd3[j]] = 0f;
					}
				}
			}

			if (mindist < 0)
			{
				float d = IndexedVector3.Dot(ref a, ref n);
				float s = (float)Math.Sqrt(l);
				IndexedVector3 p = n * (d / l);
				mindist = p.LengthSquared();
				m = 7;
				w.X = (IndexedVector3.Cross(dl[1], b - p)).Length() / s;
				w.Y = (IndexedVector3.Cross(dl[2], c - p)).Length() / s;
				w.Z = 1 - (w.X + w.Y);
			}

			inhere2 = false;
			return (mindist);
		}

		inhere2 = false;
		return (-1);
	}

	private static uint[] imd3a =
	{
		1,
		2,
		0
	};
	private static IndexedVector3[] vta = new IndexedVector3[4];
	private static IndexedVector3[] dla = new IndexedVector3[3];
	private static bool inhere1 = false;
	public static float ProjectOrigin(ref IndexedVector3 a, ref IndexedVector3 b, ref IndexedVector3 c, ref IndexedVector3 d, ref IndexedVector4 w, ref uint m)
	{
		{
		}

		inhere1 = true;
		vta[0] = a;
		vta[1] = b;
		vta[2] = c;
		vta[3] = d;
		dla[0] = a - d;
		dla[1] = b - d;
		dla[2] = c - d;
		float vl = Det(dl[0], dl[1], dl[2]);
		bool ng = (vl * IndexedVector3.Dot(a, IndexedVector3.Cross(b - c, a - b))) <= 0;
		if (ng && (Math.Abs(vl) > GjkEpaSolver2.GJK_SIMPLEX4_EPS))
		{
			float mindist = -1;
			IndexedVector4 subw = new IndexedVector4();
			uint subm = 0;
			for (int i = 0; i < 3; ++i)
			{
				uint j = imd3[i];
				float s = vl * IndexedVector3.Dot(d, IndexedVector3.Cross(dl[i], dl[j]));
				if (s > 0)
				{
					float subd = GJK.ProjectOrigin(ref vt[i], ref vt[j], ref d, ref subw, ref subm);
					if ((mindist < 0) || (subd < mindist))
					{
						mindist = subd;
						m = (uint)((((subm & 1) != 0) ? 1 << i : 0) + (((subm & 2) != 0) ? 1 << (int)j : 0) + (((subm & 4) != 0) ? 8 : 0));
						w[(int)i] = subw.X;
						w[(int)j] = subw.Y;
						w[(int)imd3[j]] = 0f;
						w.W = subw.Z;
					}
				}
			}

			if (mindist < 0)
			{
				mindist = 0;
				m = 15;
				w.X = Det(c, b, d) / vl;
				w.Y = Det(a, c, d) / vl;
				w.Z = Det(b, a, d) / vl;
				w.W = 1 - (w.X + w.Y + w.Z);
			}

			inhere1 = false;
			return (mindist);
		}

		inhere1 = false;
		return (-1);
	}

	public void Dispose()
	{
		BulletGlobals.GJKPool.Free(this);
	}

	public GjkEpaSolver2MinkowskiDiff m_shape;
	public IndexedVector3 m_ray;
	public float m_distance;
	public sSimplex[] m_simplices = new sSimplex[2];
	public sSV[] m_store = new sSV[4];
	public sSV[] m_free = new sSV[4];
	public uint m_nfree;
	public uint m_current;
	public sSimplex m_simplex = new sSimplex();
	public GJKStatus m_status;
}