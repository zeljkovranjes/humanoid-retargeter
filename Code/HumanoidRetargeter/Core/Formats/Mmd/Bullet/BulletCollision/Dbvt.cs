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

internal class Dbvt
{
	// Helpers
	public static int Nearest(int[] i, sStkNPS[] a, float v, int l, int h)
	{
		int m = 0;
		while (l < h)
		{
			m = (l + h) >> 1;
			if (a[i[m]].value >= v)
				l = m + 1;
			else
				h = m;
		}

		return (h);
	}

	public static int Allocate(ObjectArray<int> ifree, ObjectArray<sStkNPS> stock, sStkNPS value)
	{
		//int	i;
		//if(ifree.Count>0)
		//{
		//    i = ifree[ifree.Count - 1];
		//    ifree.pop_back();
		//    stock[i] = value;
		//}
		//else
		//{
		//    i=stock.Count;
		//    stock.push_back(value);
		//}
		//return(i);
		return 0;
	}

	public static void SetMin(ref IndexedVector3 a, ref IndexedVector3 b)
	{
		a.X = Math.Min(a.X, b.X);
		a.Y = Math.Min(a.Y, b.Y);
		a.Z = Math.Min(a.Z, b.Z);
	}

	public static void SetMax(ref IndexedVector3 a, ref IndexedVector3 b)
	{
		a.X = Math.Max(a.X, b.X);
		a.Y = Math.Max(a.Y, b.Y);
		a.Z = Math.Max(a.Z, b.Z);
	}

	public Dbvt()
	{
		m_lkhd = -1;
	}

	public DbvtNode Root
	{
		get
		{
			return m_root;
		}

		set
		{
			m_root = value;
		}
	}

	public void Clear()
	{
		if (m_root != null)
		{
			RecurseDeleteNode(this, m_root);
		}

		//btAlignedFree(m_free);
		//m_free = 0;
		m_lkhd = -1;
		m_stkStack.Clear();
		m_opath = 0;
	}

	public bool Empty()
	{
		return Root == null;
	}

	public void OptimizeBottomUp()
	{
		if (Root != null)
		{
			ObjectArray<DbvtNode> leafs = new ObjectArray<DbvtNode>(m_leaves);
			FetchLeafs(this, Root, leafs);
			BottomUp(this, leafs);
			Root = leafs[0];
		}
	}

	public void OptimizeTopDown()
	{
		OptimizeTopDown(128);
	}

	public void OptimizeTopDown(int bu_threshold)
	{
		// threshhold defaults to 128
		if (Root != null)
		{
			ObjectArray<DbvtNode> leafs = new ObjectArray<DbvtNode>(m_leaves);
			FetchLeafs(this, Root, leafs);
			Root = TopDown(this, leafs, bu_threshold);
		}
	}

	public virtual void Cleanup()
	{
		Clear();
	}

	public void OptimizeIncremental(int passes)
	{
		if (passes < 0)
		{
			passes = m_leaves;
		}

		if (Root != null && (passes > 0))
		{
			int sizeOfUnsigned = 4;
			int computedValue = (sizeOfUnsigned * 8 - 1);
			do
			{
				DbvtNode node = Root;
				int bit = 0;
				while (node.IsInternal())
				{
					node = Sort(node, m_root)._children[(m_opath >> bit) & 1];
					bit = (bit + 1) & (sizeof(UInt32) * 8 - 1);
				}

				Update(node);
				++m_opath;
			}
			while (--passes > 0);
		}
	}

	public DbvtNode Insert(ref DbvtAabbMm box, int data)
	{
		DbvtNode leaf = CreateNode(this, null, ref box, data);
		InsertLeaf(this, Root, leaf);
		++m_leaves;
		return leaf;
	}

	public DbvtNode Insert(ref DbvtAabbMm box, Object data)
	{
		DbvtNode leaf = CreateNode(this, null, ref box, data);
		InsertLeaf(this, Root, leaf);
		++m_leaves;
		return leaf;
	}

	public void Update(DbvtNode leaf)
	{
		Update(leaf, -1);
	}

	public void Update(DbvtNode leaf, int lookahead)
	{
		DbvtNode root = RemoveLeaf(this, leaf);
		if (root != null)
		{
			if (lookahead >= 0)
			{
				for (int i = 0; (i < lookahead) && (root.parent != null); ++i)
				{
					root = root.parent;
				}
			}
			else
			{
				root = Root;
			}
		}

		InsertLeaf(this, root, leaf);
	}

	public void Update(DbvtNode leaf, ref DbvtAabbMm volume)
	{
		DbvtNode root = RemoveLeaf(this, leaf);
		if (root != null)
		{
			if (m_lkhd >= 0)
			{
				for (int i = 0; (i < m_lkhd) && (root.parent != null); ++i)
				{
					root = root.parent;
				}
			}
			else
			{
				root = Root;
			}
		}

		leaf.volume = volume;
		InsertLeaf(this, root, leaf);
	}

	public bool Update(DbvtNode leaf, ref DbvtAabbMm volume, ref IndexedVector3 velocity, float margin)
	{
		if (leaf.volume.Contain(ref volume))
		{
			return (false);
		}

		volume.Expand(new IndexedVector3(margin));
		volume.SignedExpand(velocity);
		Update(leaf, ref volume);
		return (true);
	}

	public bool Update(DbvtNode leaf, ref DbvtAabbMm volume, ref IndexedVector3 velocity)
	{
		if (leaf.volume.Contain(ref volume))
		{
			return (false);
		}

		volume.SignedExpand(velocity);
		Update(leaf, ref volume);
		return (true);
	}

	public bool Update(DbvtNode leaf, ref DbvtAabbMm volume, float margin)
	{
		if (leaf.volume.Contain(ref volume))
		{
			return (false);
		}

		volume.Expand(new IndexedVector3(margin));
		Update(leaf, ref volume);
		return (true);
	}

	public void Remove(DbvtNode leaf)
	{
		RemoveLeaf(this, leaf);
		DeleteNode(this, leaf);
		--m_leaves;
	}

	public static DbvtNode Sort(DbvtNode n, DbvtNode r)
	{
		DbvtNode p = n.parent;
		{
		}

		if (p != null && (p.id > n.id))
		{
			int i = IndexOf(n);
			int j = 1 - i;
			DbvtNode s = p._children[j];
			DbvtNode q = p.parent;
			{
			}

			if (q != null)
			{
				q._children[IndexOf(p)] = n;
			}
			else
			{
				r = n;
			}

			s.parent = n;
			p.parent = n;
			n.parent = q;
			p._children[0] = n._children[0];
			p._children[1] = n._children[1];
			n._children[0].parent = p;
			n._children[1].parent = p;
			n._children[i] = p;
			n._children[j] = s;
			// swap id's? as well - probably not.
			Swap(ref p.volume, ref n.volume);
			return (p);
		}

		return (n);
	}

	public static void Swap(ref DbvtAabbMm a, ref DbvtAabbMm b)
	{
		DbvtAabbMm temp = b;
		b = a;
		a = temp;
	}

	//
	// depth is defaulted to -1
	public static void FetchLeafs(Dbvt pdbvt, DbvtNode root, ObjectArray<DbvtNode> leafs)
	{
		FetchLeafs(pdbvt, root, leafs, -1);
	}

	public static void FetchLeafs(Dbvt pdbvt, DbvtNode root, ObjectArray<DbvtNode> leafs, int depth)
	{
		if (root.IsInternal() && depth != 0)
		{
			FetchLeafs(pdbvt, root._children[0], leafs, depth - 1);
			FetchLeafs(pdbvt, root._children[1], leafs, depth - 1);
			DeleteNode(pdbvt, root);
		}
		else
		{
			leafs.Add(root);
		}
	}

	public static void Split(ObjectArray<DbvtNode> leaves, ObjectArray<DbvtNode> left, ObjectArray<DbvtNode> right, ref IndexedVector3 org, ref IndexedVector3 axis)
	{
		left.Resize(0);
		right.Resize(0);
		for (int i = 0, ni = leaves.Count; i < ni; ++i)
		{
			if (IndexedVector3.Dot(axis, leaves[i].volume.Center() - org) < 0)
			{
				left.Add(leaves[i]);
			}
			else
			{
				right.Add(leaves[i]);
			}
		}
	}

	public static void GetMaxDepth(DbvtNode node, int depth, ref int maxDepth)
	{
		if (node.IsInternal())
		{
			GetMaxDepth(node._children[0], depth + 1, ref maxDepth);
			GetMaxDepth(node._children[1], depth + 1, ref maxDepth);
		}
		else
		{
			maxDepth = Math.Max(depth, maxDepth);
		}
	}

	public static void EnumNodes(DbvtNode root, ICollide collideable)
	{
		collideable.Process(root);
		if (root.IsInternal())
		{
			EnumNodes(root._children[0], collideable);
			EnumNodes(root._children[1], collideable);
		}
	}

	public static void EnumLeaves(DbvtNode root, ICollide collideable)
	{
		if (root.IsInternal())
		{
			EnumLeaves(root._children[0], collideable);
			EnumLeaves(root._children[1], collideable);
		}
		else
		{
			collideable.Process(root);
		}
	}

	private static ObjectArray<sStkNN> CollideTTStack = new ObjectArray<sStkNN>(DOUBLE_STACKSIZE);
	private static int CollideTTCount = 0;
	public static void CollideTT(DbvtNode root0, DbvtNode root1, ICollide collideable)
	{
		CollideTTCount++;
		{
		}

		CollideTTStack.Clear();
		if (root0 != null && root1 != null)
		{
			int depth = 1;
			int treshold = DOUBLE_STACKSIZE - 4;
			CollideTTStack[0] = new sStkNN(root0, root1);
			do
			{
				sStkNN p = CollideTTStack[--depth];
				if (depth > treshold)
				{
					CollideTTStack.Resize(CollideTTStack.Count * 2);
					treshold = CollideTTStack.Count - 4;
				}

				if (p.a == p.b)
				{
					if (p.a.IsInternal())
					{
						CollideTTStack[depth++] = new sStkNN(p.a._children[0], p.a._children[0]);
						CollideTTStack[depth++] = new sStkNN(p.a._children[1], p.a._children[1]);
						CollideTTStack[depth++] = new sStkNN(p.a._children[0], p.a._children[1]);
					}
				}
				else if (DbvtAabbMm.Intersect(ref p.a.volume, ref p.b.volume))
				{
					if (p.a.IsInternal())
					{
						if (p.b.IsInternal())
						{
							CollideTTStack[depth++] = new sStkNN(p.a._children[0], p.b._children[0]);
							CollideTTStack[depth++] = new sStkNN(p.a._children[1], p.b._children[0]);
							CollideTTStack[depth++] = new sStkNN(p.a._children[0], p.b._children[1]);
							CollideTTStack[depth++] = new sStkNN(p.a._children[1], p.b._children[1]);
						}
						else
						{
							CollideTTStack[depth++] = new sStkNN(p.a._children[0], p.b);
							CollideTTStack[depth++] = new sStkNN(p.a._children[1], p.b);
						}
					}
					else
					{
						if (p.b.IsInternal())
						{
							CollideTTStack[depth++] = new sStkNN(p.a, p.b._children[0]);
							CollideTTStack[depth++] = new sStkNN(p.a, p.b._children[1]);
						}
						else
						{
							collideable.Process(p.a, p.b);
						}
					}
				}
			}
			while (depth > 0);
		}

		CollideTTCount--;
	}

	static ObjectArray<sStkNN> m_stkStack = new ObjectArray<sStkNN>();
	public static void CollideTTpersistentStack(DbvtNode root0, DbvtNode root1, ICollide collideable)
	{
		//CollideTT(root0, root1, collideable);
		//return;
		if (root0 != null && root1 != null)
		{
			int depth = 1;
			int treshold = DOUBLE_STACKSIZE - 4;
			m_stkStack.Resize(DOUBLE_STACKSIZE);
			m_stkStack[0] = new sStkNN(root0, root1);
			do
			{
				sStkNN p = m_stkStack[--depth];
				if (depth > treshold)
				{
					m_stkStack.Resize(m_stkStack.Count * 2);
					treshold = m_stkStack.Count - 4;
				}

				if (p.a == p.b)
				{
					if (p.a.IsInternal())
					{
						m_stkStack[depth++] = new sStkNN(p.a._children[0], p.a._children[0]);
						m_stkStack[depth++] = new sStkNN(p.a._children[1], p.a._children[1]);
						m_stkStack[depth++] = new sStkNN(p.a._children[0], p.a._children[1]);
					}
				}
				else if (DbvtAabbMm.Intersect(ref p.a.volume, ref p.b.volume))
				{
					if (p.a.IsInternal())
					{
						if (p.b.IsInternal())
						{
							m_stkStack[depth++] = new sStkNN(p.a._children[0], p.b._children[0]);
							m_stkStack[depth++] = new sStkNN(p.a._children[1], p.b._children[0]);
							m_stkStack[depth++] = new sStkNN(p.a._children[0], p.b._children[1]);
							m_stkStack[depth++] = new sStkNN(p.a._children[1], p.b._children[1]);
						}
						else
						{
							m_stkStack[depth++] = new sStkNN(p.a._children[0], p.b);
							m_stkStack[depth++] = new sStkNN(p.a._children[1], p.b);
						}
					}
					else
					{
						if (p.b.IsInternal())
						{
							m_stkStack[depth++] = new sStkNN(p.a, p.b._children[0]);
							m_stkStack[depth++] = new sStkNN(p.a, p.b._children[1]);
						}
						else
						{
							collideable.Process(p.a, p.b);
						}
					}
				}
			}
			while (depth > 0);
		}
	}

	public static void RayTest(DbvtNode root, ref IndexedVector3 rayFrom, ref IndexedVector3 rayTo, ICollide policy)
	{
		using (DbvtStackDataBlock stackDataBlock = BulletGlobals.DbvtStackDataBlockPool.Get())
		{
			if (root != null)
			{
				IndexedVector3 rayDir = (rayTo - rayFrom);
				rayDir.Normalize();
				IndexedVector3 rayDirectionInverse = new IndexedVector3(rayDir.X == 0.0f ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.X, rayDir.Y == 0.0f ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.Y, rayDir.Z == 0.0f ? MathUtil.BT_LARGE_FLOAT : 1.0f / rayDir.Z);
				stackDataBlock.signs[0] = rayDirectionInverse.X < 0.0f;
				stackDataBlock.signs[1] = rayDirectionInverse.Y < 0.0f;
				stackDataBlock.signs[2] = rayDirectionInverse.Z < 0.0f;
				float lambda_max = IndexedVector3.Dot(rayDir, (rayTo - rayFrom));
				int depth = 1;
				int treshold = DOUBLE_STACKSIZE - 2;
				stackDataBlock.stack.Resize(DOUBLE_STACKSIZE);
				stackDataBlock.stack[0] = root;
				do
				{
					DbvtNode node = stackDataBlock.stack[--depth];
					stackDataBlock.bounds[0] = node.volume.Mins();
					stackDataBlock.bounds[1] = node.volume.Maxs();
					float tmin = 1.0f, lambda_min = 0.0f;
					bool result1 = AabbUtil2.RayAabb2(ref rayFrom, ref rayDirectionInverse, stackDataBlock.signs, stackDataBlock.bounds, out tmin, lambda_min, lambda_max);
					if (result1)
					{
						if (node.IsInternal())
						{
							if (depth > treshold)
							{
								stackDataBlock.stack.Resize(stackDataBlock.stack.Count * 2);
								treshold = stackDataBlock.stack.Count - 2;
							}

							stackDataBlock.stack[depth++] = node._children[0];
							stackDataBlock.stack[depth++] = node._children[1];
						}
						else
						{
							policy.Process(node);
						}
					}
				}
				while (depth != 0);
			}
		}
	}

	public void RayTestInternal(DbvtNode root, ref IndexedVector3 rayFrom, ref IndexedVector3 rayTo, ref IndexedVector3 rayDirectionInverse, bool[] signs, float lambda_max, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax, ICollide policy)
	{
		using (DbvtStackDataBlock stackDataBlock = BulletGlobals.DbvtStackDataBlockPool.Get())
		{
			//    (void) rayTo;
			//DBVT_CHECKTYPE
			if (root != null)
			{
				IndexedVector3 resultNormal = new IndexedVector3(0, 1, 0);
				int depth = 1;
				int treshold = DOUBLE_STACKSIZE - 2;
				stackDataBlock.stack[0] = root;
				do
				{
					DbvtNode node = stackDataBlock.stack[--depth];
					stackDataBlock.bounds[0] = node.volume.Mins() - aabbMax;
					stackDataBlock.bounds[1] = node.volume.Maxs() - aabbMin;
					float tmin = 1.0f, lambda_min = 0.0f;
					bool result1 = AabbUtil2.RayAabb2(ref rayFrom, ref rayDirectionInverse, signs, stackDataBlock.bounds, out tmin, lambda_min, lambda_max);
					if (result1)
					{
						if (node.IsInternal())
						{
							if (depth > treshold)
							{
								stackDataBlock.stack.Resize(stackDataBlock.stack.Count * 2);
								treshold = stackDataBlock.stack.Count - 2;
							}

							stackDataBlock.stack[depth++] = node._children[0];
							stackDataBlock.stack[depth++] = node._children[1];
						}
						else
						{
							policy.Process(node);
						}
					}
				}
				while (depth != 0);
			}
		}
	}

	private static Stack<DbvtNode> CollideTVStack = new Stack<DbvtNode>(SIMPLE_STACKSIZE);
	private static int CollideTVCount = 0;
	public static void CollideTV(DbvtNode root, ref DbvtAabbMm volume, ICollide collideable)
	{
		CollideTVCount++;
		{
		}

		CollideTVStack.Clear();
		if (root != null)
		{
			CollideTVStack.Push(root);
			do
			{
				DbvtNode n = CollideTVStack.Pop();
				if (DbvtAabbMm.Intersect(ref n.volume, ref volume))
				{
					if (n.IsInternal())
					{
						CollideTVStack.Push(n._children[0]);
						CollideTVStack.Push(n._children[1]);
					}
					else
					{
						collideable.Process(n);
					}
				}
			}
			while (CollideTVStack.Count > 0);
		}

		CollideTVCount--;
	}

	//
	public static DbvtAabbMm Bounds(ObjectArray<DbvtNode> leafs)
	{
		DbvtAabbMm volume = leafs[0].volume;
		for (int i = 1, ni = leafs.Count; i < ni; ++i)
		{
			DbvtAabbMm.Merge(ref volume, ref leafs[i].volume, ref volume);
		}

		return (volume);
	}

	//
	public static void BottomUp(Dbvt pdbvt, ObjectArray<DbvtNode> leaves)
	{
		while (leaves.Count > 1)
		{
			float minsize = float.MaxValue;
			int[] minidx =
			{
				-1,
				-1
			};
			for (int i = 0; i < leaves.Count; ++i)
			{
				for (int j = i + 1; j < leaves.Count; ++j)
				{
					DbvtAabbMm mergeResults = DbvtAabbMm.Merge(ref leaves[i].volume, ref leaves[j].volume);
					float sz = Size(ref mergeResults);
					if (sz < minsize)
					{
						minsize = sz;
						minidx[0] = i;
						minidx[1] = j;
					}
				}
			}

			DbvtNode[] n =
			{
				leaves[minidx[0]],
				leaves[minidx[1]]
			};
			DbvtNode p = CreateNode(pdbvt, null, ref n[0].volume, ref n[1].volume, null);
			p._children[0] = n[0];
			p._children[1] = n[1];
			n[0].parent = p;
			n[1].parent = p;
			leaves[minidx[0]] = p;
			leaves.Swap(minidx[1], leaves.Count - 1);
			leaves.PopBack();
		}
	}

	//
	public static IndexedVector3[] axis =
	{
		new IndexedVector3(1, 0, 0),
		new IndexedVector3(0, 1, 0),
		new IndexedVector3(0, 0, 1)
	};
	public static DbvtNode TopDown(Dbvt pdbvt, ObjectArray<DbvtNode> leaves, int bu_treshold)
	{
		if (leaves.Count > 1)
		{
			if (leaves.Count > bu_treshold)
			{
				DbvtAabbMm vol = Bounds(leaves);
				IndexedVector3 org = vol.Center();
				ObjectArray<DbvtNode>[] sets =
				{
					new ObjectArray<DbvtNode>(),
					new ObjectArray<DbvtNode>()
				};
				int bestaxis = -1;
				int bestmidp = leaves.Count;
				int[] a1 = new int[]
				{
					0,
					0
				};
				int[] a2 = new int[]
				{
					0,
					0
				};
				int[] a3 = new int[]
				{
					0,
					0
				};
				int[][] splitcount = new int[][]
				{
					a1,
					a2,
					a3
				};
				int i;
				for (i = 0; i < leaves.Count; ++i)
				{
					IndexedVector3 x = leaves[i].volume.Center() - org;
					for (int j = 0; j < 3; ++j)
					{
						++splitcount[j][IndexedVector3.Dot(x, axis[j]) > 0 ? 1 : 0];
					}
				}

				for (i = 0; i < 3; ++i)
				{
					if ((splitcount[i][0] > 0) && (splitcount[i][1] > 0))
					{
						int midp = (int)Math.Abs((splitcount[i][0] - splitcount[i][1]));
						if (midp < bestmidp)
						{
							bestaxis = i;
							bestmidp = midp;
						}
					}
				}

				if (bestaxis >= 0)
				{
					sets[0].EnsureCapacity(splitcount[bestaxis][0]);
					sets[1].EnsureCapacity(splitcount[bestaxis][1]);
					Split(leaves, sets[0], sets[1], ref org, ref axis[bestaxis]);
				}
				else
				{
					sets[0].EnsureCapacity(leaves.Count / 2 + 1);
					sets[1].EnsureCapacity(leaves.Count / 2);
					for (int i2 = 0, ni = leaves.Count; i2 < ni; ++i2)
					{
						sets[i2 & 1].Add(leaves[i2]);
					}
				}

				DbvtNode node = CreateNode(pdbvt, null, ref vol, null);
				node._children[0] = TopDown(pdbvt, sets[0], bu_treshold);
				node._children[1] = TopDown(pdbvt, sets[1], bu_treshold);
				node._children[0].parent = node;
				node._children[1].parent = node;
				return (node);
			}
			else
			{
				BottomUp(pdbvt, leaves);
				return (leaves[0]);
			}
		}

		return (leaves[0]);
	}

	public static DbvtNode CreateNode(Dbvt pdbvt, DbvtNode parent, int data)
	{
		DbvtNode node = BulletGlobals.DbvtNodePool.Get();
		node.parent = parent;
		node.data = null;
		node.dataAsInt = data;
		node._children[0] = null;
		node._children[1] = null;
		return (node);
	}

	public static DbvtNode CreateNode(Dbvt pdbvt, DbvtNode parent, Object data)
	{
		DbvtNode node = BulletGlobals.DbvtNodePool.Get();
		node.parent = parent;
		node.data = data;
		if (node.data is int)
		{
			//Debug.Assert(false);
			node.dataAsInt = (int)node.data;
		}

		node._children[0] = null;
		node._children[1] = null;
		return (node);
	}

	public static DbvtNode CreateNode2(Dbvt tree, DbvtNode aparent, ref DbvtAabbMm avolume, Object adata)
	{
		DbvtNode node = BulletGlobals.DbvtNodePool.Get();
		node.volume = avolume;
		node.parent = aparent;
		node.data = adata;
		node._children[0] = null;
		node._children[1] = null;
		if (node.data is int)
		{
			{
			}

			node.dataAsInt = (int)node.data;
		}

		return node;
	}

	public static DbvtNode CreateNode(Dbvt pdbvt, DbvtNode parent, ref DbvtAabbMm volume, int data)
	{
		DbvtNode node = CreateNode(pdbvt, parent, data);
		node.volume = volume;
		return (node);
	}

	//
	public static DbvtNode CreateNode(Dbvt pdbvt, DbvtNode parent, ref DbvtAabbMm volume, Object data)
	{
		DbvtNode node = CreateNode(pdbvt, parent, data);
		node.volume = volume;
		return (node);
	}

	//
	public static DbvtNode CreateNode(Dbvt pdbvt, DbvtNode parent, ref DbvtAabbMm volume0, ref DbvtAabbMm volume1, Object data)
	{
		DbvtNode node = CreateNode(pdbvt, parent, data);
		DbvtAabbMm.Merge(ref volume0, ref volume1, ref node.volume);
		return (node);
	}

	public static void DeleteNode(Dbvt pdbvt, DbvtNode node)
	{
		//btAlignedFree(pdbvt.m_free);
		//pdbvt.m_free = node;
		node.Reset();
		BulletGlobals.DbvtNodePool.Free(node);
	}

	public static void RecurseDeleteNode(Dbvt pdbvt, DbvtNode node)
	{
		if (!node.IsLeaf())
		{
			RecurseDeleteNode(pdbvt, node._children[0]);
			RecurseDeleteNode(pdbvt, node._children[1]);
		}

		if (node == pdbvt.m_root)
		{
			pdbvt.m_root = null;
		}

		DeleteNode(pdbvt, node);
	}

	public static void InsertLeaf(Dbvt pdbvt, DbvtNode root, DbvtNode leaf)
	{
		if (pdbvt.Root == null)
		{
			pdbvt.Root = leaf;
			leaf.parent = null;
		}
		else
		{
			if (!root.IsLeaf())
			{
				do
				{
					root = root._children[DbvtAabbMm.Select(ref leaf.volume, ref root._children[0].volume, ref root._children[1].volume)];
				}
				while (!root.IsLeaf());
			}

			DbvtNode prev = root.parent;
			DbvtAabbMm mergeResults = DbvtAabbMm.Merge(ref leaf.volume, ref root.volume);
			DbvtNode node = CreateNode2(pdbvt, prev, ref mergeResults, null);
			if (prev != null)
			{
				prev._children[IndexOf(root)] = node;
				node._children[0] = root;
				root.parent = node;
				node._children[1] = leaf;
				leaf.parent = node;
				do
				{
					if (!prev.volume.Contain(ref node.volume))
					{
						DbvtAabbMm.Merge(ref prev._children[0].volume, ref prev._children[1].volume, ref prev.volume);
					}
					else
					{
						break;
					}

					node = prev;
				}
				while (null != (prev = node.parent));
			}
			else
			{
				node._children[0] = root;
				root.parent = node;
				node._children[1] = leaf;
				leaf.parent = node;
				pdbvt.Root = node;
			}
		}
	}

	public static DbvtNode RemoveLeaf(Dbvt pdbvt, DbvtNode leaf)
	{
		if (leaf == pdbvt.Root)
		{
			pdbvt.Root = null;
			return null;
		}
		else
		{
			DbvtNode parent = leaf.parent;
			DbvtNode prev = parent.parent;
			DbvtNode sibling = parent._children[1 - IndexOf(leaf)];
			if (prev != null)
			{
				prev._children[IndexOf(parent)] = sibling;
				sibling.parent = prev;
				DeleteNode(pdbvt, parent);
				while (prev != null)
				{
					DbvtAabbMm pb = prev.volume;
					DbvtAabbMm.Merge(ref prev._children[0].volume, ref prev._children[1].volume, ref prev.volume);
					if (DbvtAabbMm.NotEqual(ref pb, ref prev.volume))
					{
						sibling = prev;
						prev = prev.parent;
					}
					else
					{
						break;
					}
				}

				return (prev != null ? prev : pdbvt.Root);
			}
			else
			{
				pdbvt.Root = sibling;
				sibling.parent = null;
				DeleteNode(pdbvt, parent);
				return (pdbvt.Root);
			}
		}
	}

	public static int IndexOf(DbvtNode node)
	{
		return (node.parent._children[1] == node) ? 1 : 0;
	}

	// volume+edge lengths
	public static float Size(ref DbvtAabbMm a)
	{
		IndexedVector3 edges = a.Lengths();
		return (edges.X * edges.Y * edges.Z + edges.X + edges.Y + edges.Z);
	}

	public static int SIMPLE_STACKSIZE = 64;
	public static int DOUBLE_STACKSIZE = SIMPLE_STACKSIZE * 2;
	public DbvtNode m_root;
	//public DbvtNode m_free;
	public int m_lkhd;
	public int m_leaves;
	public uint m_opath;
}