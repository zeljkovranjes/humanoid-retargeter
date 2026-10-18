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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class BoxBoxCollisionAlgorithm : ActivatingCollisionAlgorithm
{
	public BoxBoxCollisionAlgorithm()
	{
	} // for pool

	public BoxBoxCollisionAlgorithm(CollisionAlgorithmConstructionInfo ci) : base(ci)
	{
	}

	public override void Initialize(CollisionAlgorithmConstructionInfo ci)
	{
		base.Initialize(ci);
	}

	public BoxBoxCollisionAlgorithm(PersistentManifold mf, CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1) : base(ci)
	{
		m_ownManifold = false;
		m_manifoldPtr = mf;
		if (m_manifoldPtr == null && m_dispatcher.NeedsCollision(body0, body1))
		{
			m_manifoldPtr = m_dispatcher.GetNewManifold(body0, body1);
			m_ownManifold = true;
		}
	}

	public void Initialize(PersistentManifold mf, CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1)
	{
		base.Initialize(ci);
		m_ownManifold = false;
		m_manifoldPtr = mf;
		if (m_manifoldPtr == null && m_dispatcher.NeedsCollision(body0, body1))
		{
			m_manifoldPtr = m_dispatcher.GetNewManifold(body0, body1);
			m_ownManifold = true;
		}
	}

	public override void Cleanup()
	{
		if (m_ownManifold)
		{
			if (m_manifoldPtr != null)
			{
				m_dispatcher.ReleaseManifold(m_manifoldPtr);
				m_manifoldPtr = null;
			}

			m_ownManifold = false;
		}

		BulletGlobals.BoxBoxCollisionAlgorithmPool.Free(this);
		base.Cleanup();
	}

	public override void ProcessCollision(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		if (m_manifoldPtr == null)
		{
			return;
		}

		CollisionObject col0 = body0;
		CollisionObject col1 = body1;
		//resultOut = new ManifoldResult(body0, body1);
		BoxShape box0 = col0.GetCollisionShape() as BoxShape;
		BoxShape box1 = col1.GetCollisionShape() as BoxShape;
		//if (((String)col0.getUserPointer()).Contains("Box") &&
		//    ((String)col1.getUserPointer()).Contains("Box") )
		//{
		//    int ibreak = 0;
		//}
		resultOut.SetPersistentManifold(m_manifoldPtr);
		ClosestPointInput input = ClosestPointInput.Default();
		input.m_maximumDistanceSquared = float.MaxValue;
		input.m_transformA = body0.GetWorldTransform();
		input.m_transformB = body1.GetWorldTransform();
		BoxBoxDetector.GetClosestPoints(box0, box1, ref input, resultOut, dispatchInfo.getDebugDraw(), false);
		//  refreshContactPoints is only necessary when using persistent contact points. otherwise all points are newly added
		if (m_ownManifold)
		{
			resultOut.RefreshContactPoints();
		}
	}

	public override float CalculateTimeOfImpact(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		return 1f;
	}

	public override void GetAllContactManifolds(PersistentManifoldArray manifoldArray)
	{
		if (m_manifoldPtr != null && m_ownManifold)
		{
			manifoldArray.Add(m_manifoldPtr);
		}
	}

	bool m_ownManifold;
	PersistentManifold m_manifoldPtr;
}