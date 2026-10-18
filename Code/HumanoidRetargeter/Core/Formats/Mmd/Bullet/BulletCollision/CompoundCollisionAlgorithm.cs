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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class CompoundCollisionAlgorithm : ActivatingCollisionAlgorithm
{
	public CompoundCollisionAlgorithm()
	{
	} // for pool

	public CompoundCollisionAlgorithm(CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, bool isSwapped) : base(ci, body0, body1)
	{
		m_isSwapped = isSwapped;
		m_sharedManifold = ci.GetManifold();
		m_ownsManifold = false;
		CollisionObject colObj = m_isSwapped ? body1 : body0;
		{
		}

		CompoundShape compoundShape = (CompoundShape)(colObj.GetCollisionShape());
		m_compoundShapeRevision = compoundShape.GetUpdateRevision();
		PreallocateChildAlgorithms(body0, body1);
	}

	public virtual void Initialize(CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, bool isSwapped)
	{
		base.Initialize(ci, body0, body1);
		m_isSwapped = isSwapped;
		m_sharedManifold = ci.GetManifold();
		m_ownsManifold = false;
		CollisionObject colObj = m_isSwapped ? body1 : body0;
		{
		}

		CompoundShape compoundShape = (CompoundShape)(colObj.GetCollisionShape());
		m_compoundShapeRevision = compoundShape.GetUpdateRevision();
		PreallocateChildAlgorithms(body0, body1);
	}

	public override void Cleanup()
	{
		RemoveChildAlgorithms();
		m_compoundShapeRevision = 0;
		BulletGlobals.CompoundCollisionAlgorithmPool.Free(this);
	}

	private void RemoveChildAlgorithms()
	{
		int numChildren = m_childCollisionAlgorithms.Count;
		int i;
		for (i = 0; i < numChildren; i++)
		{
			if (m_childCollisionAlgorithms[i] != null)
			{
				m_dispatcher.FreeCollisionAlgorithm(m_childCollisionAlgorithms[i]);
				m_childCollisionAlgorithms[i] = null;
			}
		}

		m_childCollisionAlgorithms.Clear();
	}

	private void PreallocateChildAlgorithms(CollisionObject body0, CollisionObject body1)
	{
		CollisionObject colObj = m_isSwapped ? body1 : body0;
		CollisionObject otherObj = m_isSwapped ? body0 : body1;
		{
		}

		CompoundShape compoundShape = (CompoundShape)(colObj.GetCollisionShape());
		int numChildren = compoundShape.GetNumChildShapes();
		int i;
		//m_childCollisionAlgorithms.resize(numChildren);
		m_childCollisionAlgorithms.Clear();
		for (i = 0; i < numChildren; i++)
		{
			if (compoundShape.GetDynamicAabbTree() != null)
			{
				m_childCollisionAlgorithms.Add(null);
			}
			else
			{
				CollisionShape tmpShape = colObj.GetCollisionShape();
				CollisionShape childShape = compoundShape.GetChildShape(i);
				colObj.InternalSetTemporaryCollisionShape(childShape);
				CollisionAlgorithm ca = m_dispatcher.FindAlgorithm(colObj, otherObj, m_sharedManifold);
				m_childCollisionAlgorithms.Add(ca);
				colObj.InternalSetTemporaryCollisionShape(tmpShape);
			}
		}
	}

	public override void ProcessCollision(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		//resultOut = null;
		CollisionObject colObj = m_isSwapped ? body1 : body0;
		CollisionObject otherObj = m_isSwapped ? body0 : body1;
		{
		}

		CompoundShape compoundShape = (CompoundShape)(colObj.GetCollisionShape());
		////make sure the internal child collision algorithm caches are still valid
		if (compoundShape.GetUpdateRevision() != m_compoundShapeRevision)
		{
			RemoveChildAlgorithms();
			PreallocateChildAlgorithms(body0, body1);
		}

		Dbvt tree = compoundShape.GetDynamicAabbTree();
		//use a dynamic aabb tree to cull potential child-overlaps
		using (CompoundLeafCallback callback = BulletGlobals.CompoundLeafCallbackPool.Get())
		{
			callback.Initialize(colObj, otherObj, m_dispatcher, dispatchInfo, resultOut, this, m_childCollisionAlgorithms, m_sharedManifold);
			{
				m_manifoldArray.Clear();
				for (int i = 0; i < m_childCollisionAlgorithms.Count; i++)
				{
					if (m_childCollisionAlgorithms[i] != null)
					{
						m_childCollisionAlgorithms[i].GetAllContactManifolds(m_manifoldArray);
						for (int m = 0; m < m_manifoldArray.Count; m++)
						{
							if (m_manifoldArray[m].GetNumContacts() > 0)
							{
								resultOut.SetPersistentManifold(m_manifoldArray[m]);
								resultOut.RefreshContactPoints();
								resultOut.SetPersistentManifold(null); //??necessary?
							}
						}

						m_manifoldArray.Clear();
					}
				}
			}

			if (tree != null)
			{
				IndexedVector3 localAabbMin;
				IndexedVector3 localAabbMax;
				IndexedMatrix otherInCompoundSpace;
				//otherInCompoundSpace = MathUtil.BulletMatrixMultiply(colObj.GetWorldTransform(),otherObj.GetWorldTransform());
				otherInCompoundSpace = colObj.GetWorldTransform().Inverse() * otherObj.GetWorldTransform();
				otherObj.GetCollisionShape().GetAabb(ref otherInCompoundSpace, out localAabbMin, out localAabbMax);
				DbvtAabbMm bounds = DbvtAabbMm.FromMM(ref localAabbMin, ref localAabbMax);
				//process all children, that overlap with  the given AABB bounds
				Dbvt.CollideTV(tree.m_root, ref bounds, callback);
			}
			else
			{
				//iterate over all children, perform an AABB check inside ProcessChildShape
				int numChildren = m_childCollisionAlgorithms.Count;
				for (int i = 0; i < numChildren; i++)
				{
					callback.ProcessChildShape(compoundShape.GetChildShape(i), i);
				}
			}

			{
				//iterate over all children, perform an AABB check inside ProcessChildShape
				int numChildren = m_childCollisionAlgorithms.Count;
				m_manifoldArray.Clear();
				CollisionShape childShape = null;
				IndexedMatrix orgTrans;
				IndexedMatrix orgInterpolationTrans;
				IndexedMatrix newChildWorldTrans;
				for (int i = 0; i < numChildren; i++)
				{
					if (m_childCollisionAlgorithms[i] != null)
					{
						childShape = compoundShape.GetChildShape(i);
						//if not longer overlapping, remove the algorithm
						orgTrans = colObj.GetWorldTransform();
						orgInterpolationTrans = colObj.GetInterpolationWorldTransform();
						IndexedMatrix childTrans = compoundShape.GetChildTransform(i);
						newChildWorldTrans = orgTrans * childTrans;
						//perform an AABB check first
						IndexedVector3 aabbMin0;
						IndexedVector3 aabbMax0;
						IndexedVector3 aabbMin1;
						IndexedVector3 aabbMax1;
						childShape.GetAabb(ref newChildWorldTrans, out aabbMin0, out aabbMax0);
						otherObj.GetCollisionShape().GetAabb(otherObj.GetWorldTransform(), out aabbMin1, out aabbMax1);
						if (!AabbUtil2.TestAabbAgainstAabb2(ref aabbMin0, ref aabbMax0, ref aabbMin1, ref aabbMax1))
						{
							m_dispatcher.FreeCollisionAlgorithm(m_childCollisionAlgorithms[i]);
							m_childCollisionAlgorithms[i] = null;
						}
					}
				}
			}
		}
	}

	public override void GetAllContactManifolds(PersistentManifoldArray manifoldArray)
	{
		for (int i = 0; i < m_childCollisionAlgorithms.Count; i++)
		{
			if (m_childCollisionAlgorithms[i] != null)
			{
				if (m_childCollisionAlgorithms[i] == this)
				{
					int ibreak = 0;
				}

				m_childCollisionAlgorithms[i].GetAllContactManifolds(manifoldArray);
			}
		}
	}

	public override float CalculateTimeOfImpact(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		resultOut = null;
		CollisionObject colObj = m_isSwapped ? body1 : body0;
		CollisionObject otherObj = m_isSwapped ? body0 : body1;
		{
		}

		CompoundShape compoundShape = (CompoundShape)(colObj.GetCollisionShape());
		//We will use the OptimizedBVH, AABB tree to cull potential child-overlaps
		//If both proxies are Compound, we will deal with that directly, by performing sequential/parallel tree traversals
		//given Proxy0 and Proxy1, if both have a tree, Tree0 and Tree1, this means:
		//determine overlapping nodes of Proxy1 using Proxy0 AABB against Tree1
		//then use each overlapping node AABB against Tree0
		//and vise versa.
		float hitFraction = 1f;
		int numChildren = m_childCollisionAlgorithms.Count;
		IndexedMatrix orgTrans;
		float frac;
		for (int i = 0; i < numChildren; i++)
		{
			//temporarily exchange parent btCollisionShape with childShape, and recurse
			CollisionShape childShape = compoundShape.GetChildShape(i);
			//backup
			orgTrans = colObj.GetWorldTransform();
			IndexedMatrix childTrans = compoundShape.GetChildTransform(i);
			IndexedMatrix newChildWorldTrans = orgTrans * childTrans;
			colObj.SetWorldTransform(ref newChildWorldTrans);
			CollisionShape tmpShape = colObj.GetCollisionShape();
			colObj.InternalSetTemporaryCollisionShape(childShape);
			frac = m_childCollisionAlgorithms[i].CalculateTimeOfImpact(colObj, otherObj, dispatchInfo, resultOut);
			if (frac < hitFraction)
			{
				hitFraction = frac;
			}

			//revert back
			colObj.InternalSetTemporaryCollisionShape(tmpShape);
			colObj.SetWorldTransform(ref orgTrans);
		}

		return hitFraction;
	}

	private IList<CollisionAlgorithm> m_childCollisionAlgorithms = new List<CollisionAlgorithm>();
	private PersistentManifoldArray m_manifoldArray = new PersistentManifoldArray();
	private bool m_isSwapped;
	private PersistentManifold m_sharedManifold;
	private bool m_ownsManifold;
	private int m_compoundShapeRevision; //to keep track of changes, so that childAlgorithm array can be updated
}