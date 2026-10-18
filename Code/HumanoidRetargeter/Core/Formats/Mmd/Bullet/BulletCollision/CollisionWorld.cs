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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;
using System;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class CollisionWorld
{
	//this constructor doesn't own the dispatcher and paircache/broadphase
	public CollisionWorld(IDispatcher dispatcher, IBroadphaseInterface broadphasePairCache, ICollisionConfiguration collisionConfiguration)
	{
		m_dispatcher1 = dispatcher;
		m_broadphasePairCache = broadphasePairCache;
		m_collisionObjects = new ObjectArray<CollisionObject>();
		m_dispatchInfo = new DispatcherInfo();
		m_forceUpdateAllAabbs = true;
	}

	public virtual void Cleanup()
	{
		foreach (CollisionObject collisionObject in m_collisionObjects)
		{
			BroadphaseProxy bp = collisionObject.GetBroadphaseHandle();
			if (bp != null)
			{
				//
				// only clear the cached algorithms
				//
				if (GetBroadphase().GetOverlappingPairCache() != null)
				{
					GetBroadphase().GetOverlappingPairCache().CleanProxyFromPairs(bp, m_dispatcher1);
				}

				GetBroadphase().DestroyProxy(bp, m_dispatcher1);
				collisionObject.SetBroadphaseHandle(null);
			}
		}
	}

	public void SetBroadphase(IBroadphaseInterface pairCache)
	{
		m_broadphasePairCache = pairCache;
	}

	public IBroadphaseInterface GetBroadphase()
	{
		return m_broadphasePairCache;
	}

	public IOverlappingPairCache GetPairCache()
	{
		return m_broadphasePairCache.GetOverlappingPairCache();
	}

	public IDispatcher GetDispatcher()
	{
		return m_dispatcher1;
	}

	public void UpdateSingleAabb(CollisionObject colObj)
	{
		IndexedVector3 minAabb;
		IndexedVector3 maxAabb;
		IndexedMatrix wt = colObj.GetWorldTransform();
		colObj.GetCollisionShape().GetAabb(ref wt, out minAabb, out maxAabb);
		//need to increase the aabb for contact thresholds
		IndexedVector3 contactThreshold = new IndexedVector3(BulletGlobals.gContactBreakingThreshold);
		minAabb -= contactThreshold;
		maxAabb += contactThreshold;
		if (GetDispatchInfo().m_useContinuous && colObj.GetInternalType() == CollisionObjectTypes.CO_RIGID_BODY && !colObj.IsStaticOrKinematicObject())
		{
			IndexedVector3 minAabb2, maxAabb2;
			colObj.GetCollisionShape().GetAabb(colObj.GetInterpolationWorldTransform(), out minAabb2, out maxAabb2);
			minAabb2 -= contactThreshold;
			maxAabb2 += contactThreshold;
			MathUtil.VectorMin(ref minAabb2, ref minAabb);
			MathUtil.VectorMax(ref maxAabb2, ref maxAabb);
		}

		IBroadphaseInterface bp = m_broadphasePairCache as IBroadphaseInterface;
		//moving objects should be moderately sized, probably something wrong if not
		if (colObj.IsStaticObject() || ((maxAabb - minAabb).LengthSquared() < 1e12f))
		{
			bp.SetAabb(colObj.GetBroadphaseHandle(), ref minAabb, ref maxAabb, m_dispatcher1);
		}
		else
		{
			//something went wrong, investigate
			//this assert is unwanted in 3D modelers (danger of loosing work)
			colObj.SetActivationState(ActivationState.DISABLE_SIMULATION);
			//static bool reportMe = true;
			bool reportMe = true;
			if (reportMe && m_debugDrawer != null)
			{
				reportMe = false;
				m_debugDrawer.ReportErrorWarning("Overflow in AABB, object removed from simulation");
				m_debugDrawer.ReportErrorWarning("If you can reproduce this, please email bugs@continuousphysics.com\n");
				m_debugDrawer.ReportErrorWarning("Please include above information, your Platform, version of OS.\n");
				m_debugDrawer.ReportErrorWarning("Thanks.\n");
			}
		}
	}

	public virtual void UpdateAabbs()
	{
		BulletGlobals.StartProfile("updateAabbs");
		//IndexedMatrix predictedTrans = new IndexedMatrix();
		int count = m_collisionObjects.Count;
		for (int i = 0; i < count; i++)
		{
			CollisionObject colObj = m_collisionObjects[i];
			//only update aabb of active objects
			if (m_forceUpdateAllAabbs || colObj.IsActive())
			{
				UpdateSingleAabb(colObj);
			}
		}

		BulletGlobals.StopProfile();
	}

	public virtual void SetDebugDrawer(IDebugDraw debugDrawer)
	{
		m_debugDrawer = debugDrawer;
		BulletGlobals.gDebugDraw = debugDrawer;
	}

	public virtual IDebugDraw GetDebugDrawer()
	{
		return m_debugDrawer;
	}

	public virtual void DebugDrawWorld()
	{
		if (GetDebugDrawer() != null && ((GetDebugDrawer().GetDebugMode() & DebugDrawModes.DBG_DrawContactPoints) != 0))
		{
			int numManifolds = GetDispatcher().GetNumManifolds();
			IndexedVector3 color = new IndexedVector3(1, 1, 0.5f);
			for (int i = 0; i < numManifolds; i++)
			{
				PersistentManifold contactManifold = GetDispatcher().GetManifoldByIndexInternal(i);
				//btCollisionObject* obA = static_cast<btCollisionObject*>(contactManifold.getBody0());
				//btCollisionObject* obB = static_cast<btCollisionObject*>(contactManifold.getBody1());
				int numContacts = contactManifold.GetNumContacts();
				for (int j = 0; j < numContacts; j++)
				{
					ManifoldPoint cp = contactManifold.GetContactPoint(j);
					GetDebugDrawer().DrawContactPoint(cp.GetPositionWorldOnB(), cp.GetNormalWorldOnB(), cp.GetDistance(), cp.GetLifeTime(), color);
				}
			}
		}

		if (GetDebugDrawer() != null)
		{
			DebugDrawModes debugMode = GetDebugDrawer().GetDebugMode();
			bool wireFrame = (debugMode & DebugDrawModes.DBG_DrawWireframe) != 0;
			bool aabb = (debugMode & DebugDrawModes.DBG_DrawAabb) != 0;
			if (wireFrame || aabb)
			{
				int length = m_collisionObjects.Count;
				for (int i = 0; i < length; ++i)
				{
					CollisionObject colObj = m_collisionObjects[i];
					if (wireFrame)
					{
						IndexedVector3 color = new IndexedVector3(255, 255, 255);
						switch (colObj.GetActivationState())
						{
							case ActivationState.ACTIVE_TAG:
							{
								color = new IndexedVector3(255, 255, 255);
								break;
							}

							case ActivationState.ISLAND_SLEEPING:
							{
								color = new IndexedVector3(0, 255, 0);
								break;
							}

							case ActivationState.WANTS_DEACTIVATION:
							{
								color = new IndexedVector3(0, 255, 255);
								break;
							}

							case ActivationState.DISABLE_DEACTIVATION:
							{
								color = new IndexedVector3(255, 0, 0);
								break;
							}

							case ActivationState.DISABLE_SIMULATION:
							{
								color = new IndexedVector3(255, 255, 0);
								break;
							}

							default:
							{
								color = new IndexedVector3(255, 0, 0);
								break;
							}
						};
						IndexedMatrix transform = colObj.GetWorldTransform();
						DebugDrawObject(ref transform, colObj.GetCollisionShape(), ref color);
					}

					if (aabb)
					{
						IndexedVector3 minAabb;
						IndexedVector3 maxAabb;
						IndexedVector3 colorvec = new IndexedVector3(1, 0, 0);
						colObj.GetCollisionShape().GetAabb(colObj.GetWorldTransform(), out minAabb, out maxAabb);
						IndexedVector3 contactThreshold = new IndexedVector3(BulletGlobals.gContactBreakingThreshold);
						minAabb -= contactThreshold;
						maxAabb += contactThreshold;
						IndexedVector3 minAabb2, maxAabb2;
						if (GetDispatchInfo().m_useContinuous && colObj.GetInternalType() == CollisionObjectTypes.CO_RIGID_BODY && !colObj.IsStaticOrKinematicObject())
						{
							IndexedMatrix m = colObj.GetInterpolationWorldTransform();
							//(colObj as RigidBody).GetMotionState().GetWorldTransform(out m);
							colObj.GetCollisionShape().GetAabb(m, out minAabb2, out maxAabb2);
							minAabb2 -= contactThreshold;
							maxAabb2 += contactThreshold;
							MathUtil.VectorMin(ref minAabb2, ref minAabb);
							MathUtil.VectorMax(ref maxAabb2, ref maxAabb);
						}

						m_debugDrawer.DrawAabb(ref minAabb, ref maxAabb, ref colorvec);
					}
				}
			}
		}
	}

	public virtual void DebugDrawObject(ref IndexedMatrix worldTransform, CollisionShape shape, ref IndexedVector3 color)
	{
		// Draw a small simplex at the center of the object
		GetDebugDrawer().DrawTransform(ref worldTransform, 1.0f);
		switch (shape.GetShapeType())
		{
			case BroadphaseNativeTypes.COMPOUND_SHAPE_PROXYTYPE:
			{
				CompoundShape compoundShape = (CompoundShape)shape;
				for (int i = compoundShape.GetNumChildShapes() - 1; i >= 0; i--)
				{
					IndexedMatrix childTrans = compoundShape.GetChildTransform(i);
					CollisionShape colShape = compoundShape.GetChildShape(i);
					IndexedMatrix temp = worldTransform * childTrans;
					DebugDrawObject(ref temp, colShape, ref color);
				}

				break;
			}

			case (BroadphaseNativeTypes.BOX_SHAPE_PROXYTYPE):
			{
				BoxShape boxShape = shape as BoxShape;
				IndexedVector3 halfExtents = boxShape.GetHalfExtentsWithMargin();
				IndexedVector3 negHalfExtents = -halfExtents;
				GetDebugDrawer().DrawBox(ref negHalfExtents, ref halfExtents, ref worldTransform, ref color);
				break;
			}

			case BroadphaseNativeTypes.SPHERE_SHAPE_PROXYTYPE:
			{
				SphereShape sphereShape = shape as SphereShape;
				float radius = sphereShape.GetMargin(); //radius doesn't include the margin, so draw with margin
				GetDebugDrawer().DrawSphere(radius, ref worldTransform, ref color);
				break;
			}

			case BroadphaseNativeTypes.MULTI_SPHERE_SHAPE_PROXYTYPE:
			{
				MultiSphereShape multiSphereShape = (MultiSphereShape)shape;
				for (int i = multiSphereShape.GetSphereCount() - 1; i >= 0; i--)
				{
					IndexedMatrix childTransform = worldTransform;
					childTransform._origin += multiSphereShape.GetSpherePosition(i);
					GetDebugDrawer().DrawSphere(multiSphereShape.GetSphereRadius(i), ref childTransform, ref color);
				}

				break;
			}

			case BroadphaseNativeTypes.CAPSULE_SHAPE_PROXYTYPE:
			{
				CapsuleShape capsuleShape = shape as CapsuleShape;
				float radius = capsuleShape.GetRadius();
				float halfHeight = capsuleShape.GetHalfHeight();
				int upAxis = capsuleShape.GetUpAxis();
				GetDebugDrawer().DrawCapsule(radius, halfHeight, upAxis, ref worldTransform, ref color);
				break;
			}

			case BroadphaseNativeTypes.CONE_SHAPE_PROXYTYPE:
			{
				ConeShape coneShape = (ConeShape)shape;
				float radius = coneShape.GetRadius(); //+coneShape->getMargin();
				float height = coneShape.GetHeight(); //+coneShape->getMargin();
				int upAxis = coneShape.GetConeUpIndex();
				GetDebugDrawer().DrawCone(radius, height, upAxis, ref worldTransform, ref color);
				break;
			}

			case BroadphaseNativeTypes.CYLINDER_SHAPE_PROXYTYPE:
			{
				CylinderShape cylinder = (CylinderShape)shape;
				int upAxis = cylinder.GetUpAxis();
				float radius = cylinder.GetRadius();
				float halfHeight = cylinder.GetHalfExtentsWithMargin()[upAxis];
				GetDebugDrawer().DrawCylinder(radius, halfHeight, upAxis, ref worldTransform, ref color);
				break;
			}

			case BroadphaseNativeTypes.STATIC_PLANE_PROXYTYPE:
			{
				StaticPlaneShape staticPlaneShape = shape as StaticPlaneShape;
				float planeConst = staticPlaneShape.GetPlaneConstant();
				IndexedVector3 planeNormal = staticPlaneShape.GetPlaneNormal();
				GetDebugDrawer().DrawPlane(ref planeNormal, planeConst, ref worldTransform, ref color);
				break;
			}

			default:
			{
				if (shape.IsPolyhedral())
				{
					PolyhedralConvexShape polyshape = shape as PolyhedralConvexShape;
					ConvexPolyhedron poly = polyshape.GetConvexPolyhedron();
					if (poly != null)
					{
						for (int i = 0; i < poly.m_faces.Count; i++)
						{
							IndexedVector3 centroid = IndexedVector3.Zero;
							int numVerts = poly.m_faces[i].m_indices.Count;
							if (numVerts != 0)
							{
								int lastV = poly.m_faces[i].m_indices[numVerts - 1];
								for (int v = 0; v < poly.m_faces[i].m_indices.Count; v++)
								{
									int curVert = poly.m_faces[i].m_indices[v];
									centroid += poly.m_vertices[curVert];
									GetDebugDrawer().DrawLine(worldTransform * poly.m_vertices[lastV], worldTransform * poly.m_vertices[curVert], color);
									lastV = curVert;
								}
							}

							centroid *= 1.0f / (float)(numVerts);
							IndexedVector3 normalColor = new IndexedVector3(1, 1, 0);
							IndexedVector3 faceNormal = new IndexedVector3(poly.m_faces[i].m_plane[0], poly.m_faces[i].m_plane[1], poly.m_faces[i].m_plane[2]);
							GetDebugDrawer().DrawLine(worldTransform * centroid, worldTransform * (centroid + faceNormal), normalColor);
						}
					}
					else
					{
						for (int i = 0; i < polyshape.GetNumEdges(); i++)
						{
							IndexedVector3 a, b;
							polyshape.GetEdge(i, out a, out b);
							IndexedVector3 wa = worldTransform * a;
							IndexedVector3 wb = worldTransform * b;
							GetDebugDrawer().DrawLine(ref wa, ref wb, ref color);
						}
					}
				}

				if (shape.IsConcave())
				{
					ConcaveShape concaveMesh = (ConcaveShape)shape;
					IndexedVector3 aabbMax = MathUtil.MAX_VECTOR;
					IndexedVector3 aabbMin = MathUtil.MIN_VECTOR;
					using (HumanoidRetargeter.Core.Formats.Mmd.Bullet.DebugDrawcallback drawCallback = BulletGlobals.DebugDrawcallbackPool.Get())
					{
						drawCallback.Initialise(GetDebugDrawer(), ref worldTransform, ref color);
						concaveMesh.ProcessAllTriangles(drawCallback, ref aabbMin, ref aabbMax);
					}
				}
				else if (shape.GetShapeType() == BroadphaseNativeTypes.CONVEX_TRIANGLEMESH_SHAPE_PROXYTYPE)
				{
					ConvexTriangleMeshShape convexMesh = (ConvexTriangleMeshShape)shape;
					//todo: pass camera for some culling
					IndexedVector3 aabbMax = MathUtil.MAX_VECTOR;
					IndexedVector3 aabbMin = MathUtil.MIN_VECTOR;
				//DebugDrawcallback drawCallback;
				//DebugDrawcallback drawCallback = new DebugDrawcallback(debugDraw, ref worldTransform, ref color);
				//convexMesh.GetMeshInterface().InternalProcessAllTriangles(drawCallback, ref aabbMin, ref aabbMax);
				//drawCallback.Cleanup();
				}

				break;
			}
		}
	}

	public int GetNumCollisionObjects()
	{
		return m_collisionObjects.Count;
	}

	public virtual void RayTest(ref IndexedVector3 rayFromWorld, ref IndexedVector3 rayToWorld, RayResultCallback resultCallback)
	{
		BulletGlobals.StartProfile("rayTest");
		using (SingleRayCallback rayCB = BulletGlobals.SingleRayCallbackPool.Get())
		{
			rayCB.Initialize(ref rayFromWorld, ref rayToWorld, this, resultCallback);
			m_broadphasePairCache.RayTest(ref rayFromWorld, ref rayToWorld, rayCB);
		}

		BulletGlobals.StopProfile();
	}

	//fixed me == checked Debug draw world
	public void ContactTest(CollisionObject colObj, ContactResultCallback resultCallback)
	{
	}

	public void ContactPairTest(CollisionObject colObjA, CollisionObject colObjB, ContactResultCallback resultCallback)
	{
	}

	// convexTest performs a swept convex cast on all objects in the btCollisionWorld, and calls the resultCallback
	// This allows for several queries: first hit, all hits, any hit, dependent on the value return by the callback.
	public virtual void ConvexSweepTest(ConvexShape castShape, IndexedMatrix convexFromWorld, IndexedMatrix convexToWorld, ConvexResultCallback resultCallback, float allowedCcdPenetration)
	{
		ConvexSweepTest(castShape, ref convexFromWorld, ref convexToWorld, resultCallback, allowedCcdPenetration);
	}

	public virtual void ConvexSweepTest(ConvexShape castShape, ref IndexedMatrix convexFromWorld, ref IndexedMatrix convexToWorld, ConvexResultCallback resultCallback, float allowedCcdPenetration)
	{
		BulletGlobals.StartProfile("convexSweepTest");
		IndexedMatrix convexFromTrans;
		IndexedMatrix convexToTrans;
		convexFromTrans = convexFromWorld;
		convexToTrans = convexToWorld;
		IndexedVector3 castShapeAabbMin;
		IndexedVector3 castShapeAabbMax;
		/* Compute AABB that encompasses angular movement */
		{
			IndexedVector3 linVel;
			IndexedVector3 angVel;
			TransformUtil.CalculateVelocity(ref convexFromTrans, ref convexToTrans, 1.0f, out linVel, out angVel);
			IndexedVector3 zeroLinVel = new IndexedVector3();
			IndexedMatrix R = IndexedMatrix.Identity;
			R.SetRotation(convexFromTrans.GetRotation());
			castShape.CalculateTemporalAabb(ref R, ref zeroLinVel, ref angVel, 1.0f, out castShapeAabbMin, out castShapeAabbMax);
		}

		SingleSweepCallback convexCB = BulletGlobals.SingleSweepCallbackPool.Get();
		convexCB.Initialize(castShape, ref convexFromWorld, ref convexToWorld, this, resultCallback, allowedCcdPenetration);
		IndexedVector3 tempFrom = convexFromTrans._origin;
		IndexedVector3 tempTo = convexToTrans._origin;
		m_broadphasePairCache.RayTest(ref tempFrom, ref tempTo, convexCB, ref castShapeAabbMin, ref castShapeAabbMax);
		convexCB.Cleanup();
		BulletGlobals.SingleSweepCallbackPool.Free(convexCB);
		BulletGlobals.StopProfile();
	}

	public virtual void AddCollisionObject(CollisionObject collisionObject)
	{
		AddCollisionObject(collisionObject, CollisionFilterGroups.DefaultFilter, CollisionFilterGroups.AllFilter);
	}

	public virtual void AddCollisionObject(CollisionObject collisionObject, CollisionFilterGroups collisionFilterGroup, CollisionFilterGroups collisionFilterMask)
	{
		{
		}

		//Debug.Assert(!m_collisionObjects.Contains(collisionObject));
		if (m_collisionObjects.Contains(collisionObject))
		{
			return;
		}

		m_collisionObjects.Add(collisionObject);
		//calculate new AABB
		IndexedMatrix trans = collisionObject.GetWorldTransform();
		IndexedVector3 minAabb;
		IndexedVector3 maxAabb;
		collisionObject.GetCollisionShape().GetAabb(ref trans, out minAabb, out maxAabb);
		BroadphaseNativeTypes type = collisionObject.GetCollisionShape().GetShapeType();
		collisionObject.SetBroadphaseHandle(GetBroadphase().CreateProxy(ref minAabb, ref maxAabb, type, collisionObject, collisionFilterGroup, collisionFilterMask, m_dispatcher1, 0));
	}

	public ObjectArray<CollisionObject> GetCollisionObjectArray()
	{
		return m_collisionObjects;
	}

	public DispatcherInfo GetDispatchInfo()
	{
		return m_dispatchInfo;
	}

	public virtual void PerformDiscreteCollisionDetection()
	{
		BulletGlobals.StartProfile("performDiscreteCollisionDetection");
		DispatcherInfo dispatchInfo = GetDispatchInfo();
		UpdateAabbs();
		{
			BulletGlobals.StartProfile("calculateOverlappingPairs");
			m_broadphasePairCache.CalculateOverlappingPairs(m_dispatcher1);
			BulletGlobals.StopProfile();
		}

		IDispatcher dispatcher = GetDispatcher();
		{
			BulletGlobals.StartProfile("dispatchAllCollisionPairs");
			if (dispatcher != null)
			{
				dispatcher.DispatchAllCollisionPairs(m_broadphasePairCache.GetOverlappingPairCache(), dispatchInfo, m_dispatcher1);
			}

			BulletGlobals.StopProfile();
		}

		BulletGlobals.StopProfile();
	}

	public virtual void RemoveCollisionObject(CollisionObject collisionObject)
	{
		//bool removeFromBroadphase = false;
		{
			BroadphaseProxy bp = collisionObject.GetBroadphaseHandle();
			if (bp != null)
			{
				//
				// only clear the cached algorithms
				//
				GetBroadphase().GetOverlappingPairCache().CleanProxyFromPairs(bp, m_dispatcher1);
				GetBroadphase().DestroyProxy(bp, m_dispatcher1);
				collisionObject.SetBroadphaseHandle(null);
			}
		}

		//swapremove
		m_collisionObjects.Remove(collisionObject);
	}

	public bool GetForceUpdateAllAabbs()
	{
		return m_forceUpdateAllAabbs;
	}

	public void SetForceUpdateAllAabbs(bool forceUpdateAllAabbs)
	{
		m_forceUpdateAllAabbs = forceUpdateAllAabbs;
	}

	public static void RayTestSingle(ref IndexedMatrix rayFromTrans, ref IndexedMatrix rayToTrans, CollisionObject collisionObject, CollisionShape collisionShape, ref IndexedMatrix colObjWorldTransform, RayResultCallback resultCallback)
	{
		SphereShape pointShape = BulletGlobals.SphereShapePool.Get();
		pointShape.Initialize(0.0f);
		pointShape.SetMargin(0f);
		ConvexShape castShape = pointShape;
		if (collisionShape.IsConvex())
		{
			BulletGlobals.StartProfile("rayTestConvex");
			CastResult castResult = BulletGlobals.CastResultPool.Get();
			castResult.m_fraction = resultCallback.m_closestHitFraction;
			ConvexShape convexShape = collisionShape as ConvexShape;
			VoronoiSimplexSolver simplexSolver = BulletGlobals.VoronoiSimplexSolverPool.Get();
			//#define USE_SUBSIMPLEX_CONVEX_CAST 1
			//#ifdef USE_SUBSIMPLEX_CONVEX_CAST
			// FIXME - MAN - convexcat here seems to make big difference to forklift.
			SubSimplexConvexCast convexCaster = BulletGlobals.SubSimplexConvexCastPool.Get();
			convexCaster.Initialize(castShape, convexShape, simplexSolver);
			//GjkConvexCast convexCaster = new GjkConvexCast(castShape, convexShape, simplexSolver);
			//#else
			//btGjkConvexCast	convexCaster(castShape,convexShape,&simplexSolver);
			//btContinuousConvexCollision convexCaster(castShape,convexShape,&simplexSolver,0);
			//#endif //#USE_SUBSIMPLEX_CONVEX_CAST
			if (convexCaster.CalcTimeOfImpact(ref rayFromTrans, ref rayToTrans, ref colObjWorldTransform, ref colObjWorldTransform, castResult))
			{
				//add hit
				if (castResult.m_normal.LengthSquared() > 0.0001f)
				{
					if (castResult.m_fraction < resultCallback.m_closestHitFraction)
					{
						//if (resultCallback.m_closestHitFraction != 1f)
						//{
						//    int ibreak = 0;
						//    convexCaster.calcTimeOfImpact(ref rayFromTrans, ref rayToTrans, ref colObjWorldTransform, ref colObjWorldTransform, castResult);
						//}
						//#ifdef USE_SUBSIMPLEX_CONVEX_CAST
						//rotate normal into worldspace
						castResult.m_normal = rayFromTrans._basis * castResult.m_normal;
						//#endif //USE_SUBSIMPLEX_CONVEX_CAST
						castResult.m_normal.Normalize();
						LocalRayResult localRayResult = new LocalRayResult(collisionObject, //null, // updated to allow different ctor on struct
						ref castResult.m_normal, castResult.m_fraction);
						bool normalInWorldSpace = true;
						resultCallback.AddSingleResult(ref localRayResult, normalInWorldSpace);
					}
				}
			}

			castResult.Cleanup();
			BulletGlobals.SubSimplexConvexCastPool.Free(convexCaster);
			BulletGlobals.VoronoiSimplexSolverPool.Free(simplexSolver);
			BulletGlobals.StopProfile();
		}
		else
		{
			if (collisionShape.IsConcave())
			{
				BulletGlobals.StartProfile("rayTestConcave");
				if (collisionShape.GetShapeType() == BroadphaseNativeTypes.TRIANGLE_MESH_SHAPE_PROXYTYPE && collisionShape is BvhTriangleMeshShape)
				{
					BvhTriangleMeshShape triangleMesh = (BvhTriangleMeshShape)collisionShape;
					IndexedMatrix worldTocollisionObject = colObjWorldTransform.Inverse();
					IndexedVector3 rayFromLocal = worldTocollisionObject * rayFromTrans._origin;
					IndexedVector3 rayToLocal = worldTocollisionObject * rayToTrans._origin;
					IndexedMatrix transform = IndexedMatrix.Identity;
					using (BridgeTriangleRaycastCallback rcb = BulletGlobals.BridgeTriangleRaycastCallbackPool.Get())
					{
						rcb.Initialize(ref rayFromLocal, ref rayToLocal, resultCallback, collisionObject, triangleMesh, ref transform);
						rcb.m_hitFraction = resultCallback.m_closestHitFraction;
						triangleMesh.PerformRaycast(rcb, ref rayFromLocal, ref rayToLocal);
					}
				}
				else if (collisionShape.GetShapeType() == BroadphaseNativeTypes.TERRAIN_SHAPE_PROXYTYPE && collisionShape is HeightfieldTerrainShape)
				{
					HeightfieldTerrainShape heightField = (HeightfieldTerrainShape)collisionShape;
					IndexedMatrix worldTocollisionObject = colObjWorldTransform.Inverse();
					IndexedVector3 rayFromLocal = worldTocollisionObject * rayFromTrans._origin;
					IndexedVector3 rayToLocal = worldTocollisionObject * rayToTrans._origin;
					IndexedMatrix transform = IndexedMatrix.Identity;
					using (BridgeTriangleConcaveRaycastCallback rcb = BulletGlobals.BridgeTriangleConcaveRaycastCallbackPool.Get())
					{
						rcb.Initialize(ref rayFromLocal, ref rayToLocal, resultCallback, collisionObject, heightField, ref transform);
						rcb.m_hitFraction = resultCallback.m_closestHitFraction;
						heightField.PerformRaycast(rcb, ref rayFromLocal, ref rayToLocal);
					}
				}
				else
				{
					//generic (slower) case
					ConcaveShape concaveShape = (ConcaveShape)collisionShape;
					IndexedMatrix worldTocollisionObject = colObjWorldTransform.Inverse();
					IndexedVector3 rayFromLocal = worldTocollisionObject * rayFromTrans._origin;
					IndexedVector3 rayToLocal = worldTocollisionObject * rayToTrans._origin;
					//ConvexCast::CastResult
					IndexedMatrix transform = IndexedMatrix.Identity;
					using (BridgeTriangleConcaveRaycastCallback rcb = BulletGlobals.BridgeTriangleConcaveRaycastCallbackPool.Get())
					{
						rcb.Initialize(ref rayFromLocal, ref rayToLocal, resultCallback, collisionObject, concaveShape, ref transform);
						rcb.m_hitFraction = resultCallback.m_closestHitFraction;
						IndexedVector3 rayAabbMinLocal = rayFromLocal;
						MathUtil.VectorMin(ref rayToLocal, ref rayAabbMinLocal);
						IndexedVector3 rayAabbMaxLocal = rayFromLocal;
						MathUtil.VectorMax(ref rayToLocal, ref rayAabbMaxLocal);
						concaveShape.ProcessAllTriangles(rcb, ref rayAabbMinLocal, ref rayAabbMaxLocal);
					}
				}

				BulletGlobals.StopProfile();
			}
			else
			{
				BulletGlobals.StartProfile("rayTestCompound");
				if (collisionShape.IsCompound())
				{
					CompoundShape compoundShape = collisionShape as CompoundShape;
					Dbvt dbvt = compoundShape.GetDynamicAabbTree();
					RayTester rayCB = new RayTester(collisionObject, compoundShape, ref colObjWorldTransform, ref rayFromTrans, ref rayToTrans, resultCallback);
					if (dbvt != null)
					{
						IndexedVector3 localRayFrom = colObjWorldTransform.InverseTimes(ref rayFromTrans)._origin;
						IndexedVector3 localRayTo = colObjWorldTransform.InverseTimes(ref rayToTrans)._origin;
						Dbvt.RayTest(dbvt.m_root, ref localRayFrom, ref localRayTo, rayCB);
					}
					else
					{
						for (int i = 0, n = compoundShape.GetNumChildShapes(); i < n; ++i)
						{
							rayCB.Process(i);
						}
					}

					rayCB.Cleanup();
					BulletGlobals.StopProfile();
				}
			}
		}

		BulletGlobals.SphereShapePool.Free(pointShape);
	}

	public static void ObjectQuerySingle(ConvexShape castShape, ref IndexedMatrix convexFromTrans, ref IndexedMatrix convexToTrans, CollisionObject collisionObject, CollisionShape collisionShape, ref IndexedMatrix colObjWorldTransform, ConvexResultCallback resultCallback, float allowedPenetration)
	{
		if (collisionShape.IsConvex())
		{
			BulletGlobals.StartProfile("convexSweepConvex");
			CastResult castResult = BulletGlobals.CastResultPool.Get();
			castResult.m_allowedPenetration = allowedPenetration;
			castResult.m_fraction = resultCallback.m_closestHitFraction; //float(1.);//??
			ConvexShape convexShape = collisionShape as ConvexShape;
			VoronoiSimplexSolver simplexSolver = BulletGlobals.VoronoiSimplexSolverPool.Get();
			GjkEpaPenetrationDepthSolver gjkEpaPenetrationSolver = BulletGlobals.GjkEpaPenetrationDepthSolverPool.Get();
			ContinuousConvexCollision convexCaster1 = BulletGlobals.ContinuousConvexCollisionPool.Get();
			convexCaster1.Initialize(castShape, convexShape, simplexSolver, gjkEpaPenetrationSolver);
			//btGjkConvexCast convexCaster2(castShape,convexShape,&simplexSolver);
			//btSubsimplexConvexCast convexCaster3(castShape,convexShape,&simplexSolver);
			IConvexCast castPtr = convexCaster1;
			if (castPtr.CalcTimeOfImpact(ref convexFromTrans, ref convexToTrans, ref colObjWorldTransform, ref colObjWorldTransform, castResult))
			{
				//add hit
				if (castResult.m_normal.LengthSquared() > 0.0001f)
				{
					if (castResult.m_fraction < resultCallback.m_closestHitFraction)
					{
						castResult.m_normal.Normalize();
						LocalConvexResult localConvexResult = new LocalConvexResult(collisionObject, //null, // updated to allow different ctor on struct
						ref castResult.m_normal, ref castResult.m_hitPoint, castResult.m_fraction);
						bool normalInWorldSpace = true;
						resultCallback.AddSingleResult(ref localConvexResult, normalInWorldSpace);
					}
				}
			}

			BulletGlobals.ContinuousConvexCollisionPool.Free(convexCaster1);
			BulletGlobals.GjkEpaPenetrationDepthSolverPool.Free(gjkEpaPenetrationSolver);
			BulletGlobals.VoronoiSimplexSolverPool.Free(simplexSolver);
			castResult.Cleanup();
			BulletGlobals.StopProfile();
		}
		else
		{
			if (collisionShape.IsConcave())
			{
				if (collisionShape.GetShapeType() == BroadphaseNativeTypes.TRIANGLE_MESH_SHAPE_PROXYTYPE)
				{
					BulletGlobals.StartProfile("convexSweepbtBvhTriangleMesh");
					BvhTriangleMeshShape triangleMesh = (BvhTriangleMeshShape)collisionShape;
					IndexedMatrix worldTocollisionObject = colObjWorldTransform.Inverse();
					IndexedVector3 convexFromLocal = worldTocollisionObject * convexFromTrans._origin;
					IndexedVector3 convexToLocal = worldTocollisionObject * convexToTrans._origin;
					// rotation of box in local mesh space = MeshRotation^-1 * ConvexToRotation
					IndexedMatrix rotationXform = new IndexedMatrix(worldTocollisionObject._basis * convexToTrans._basis, new IndexedVector3(0));
					using (BridgeTriangleConvexcastCallback tccb = BulletGlobals.BridgeTriangleConvexcastCallbackPool.Get())
					{
						tccb.Initialize(castShape, ref convexFromTrans, ref convexToTrans, resultCallback, collisionObject, triangleMesh, ref colObjWorldTransform);
						tccb.m_hitFraction = resultCallback.m_closestHitFraction;
						tccb.m_allowedPenetration = allowedPenetration;
						IndexedVector3 boxMinLocal;
						IndexedVector3 boxMaxLocal;
						castShape.GetAabb(ref rotationXform, out boxMinLocal, out boxMaxLocal);
						triangleMesh.PerformConvexCast(tccb, ref convexFromLocal, ref convexToLocal, ref boxMinLocal, ref boxMaxLocal);
					}

					BulletGlobals.StopProfile();
				}
				else
				{
					if (collisionShape.GetShapeType() == BroadphaseNativeTypes.STATIC_PLANE_PROXYTYPE)
					{
						CastResult castResult = BulletGlobals.CastResultPool.Get();
						castResult.m_allowedPenetration = allowedPenetration;
						castResult.m_fraction = resultCallback.m_closestHitFraction;
						StaticPlaneShape planeShape = collisionShape as StaticPlaneShape;
						ContinuousConvexCollision convexCaster1 = new ContinuousConvexCollision(castShape, planeShape);
						if (convexCaster1.CalcTimeOfImpact(ref convexFromTrans, ref convexToTrans, ref colObjWorldTransform, ref colObjWorldTransform, castResult))
						{
							//add hit
							if (castResult.m_normal.LengthSquared() > 0.0001f)
							{
								if (castResult.m_fraction < resultCallback.m_closestHitFraction)
								{
									castResult.m_normal.Normalize();
									LocalConvexResult localConvexResult = new LocalConvexResult(collisionObject, //null, // updated to allow different ctor on struct
									ref castResult.m_normal, ref castResult.m_hitPoint, castResult.m_fraction);
									bool normalInWorldSpace = true;
									resultCallback.AddSingleResult(ref localConvexResult, normalInWorldSpace);
								}
							}
						}

						castResult.Cleanup();
					}
					else
					{
						BulletGlobals.StartProfile("convexSweepConcave");
						ConcaveShape concaveShape = (ConcaveShape)collisionShape;
						IndexedMatrix worldTocollisionObject = colObjWorldTransform.Inverse();
						IndexedVector3 convexFromLocal = worldTocollisionObject * convexFromTrans._origin;
						IndexedVector3 convexToLocal = worldTocollisionObject * convexToTrans._origin;
						// rotation of box in local mesh space = MeshRotation^-1 * ConvexToRotation
						IndexedMatrix rotationXform = new IndexedMatrix(worldTocollisionObject._basis * convexToTrans._basis, new IndexedVector3(0));
						using (BridgeTriangleConvexcastCallback tccb = BulletGlobals.BridgeTriangleConvexcastCallbackPool.Get())
						{
							tccb.Initialize(castShape, ref convexFromTrans, ref convexToTrans, resultCallback, collisionObject, concaveShape, ref colObjWorldTransform);
							tccb.m_hitFraction = resultCallback.m_closestHitFraction;
							tccb.m_allowedPenetration = allowedPenetration;
							IndexedVector3 boxMinLocal;
							IndexedVector3 boxMaxLocal;
							castShape.GetAabb(ref rotationXform, out boxMinLocal, out boxMaxLocal);
							IndexedVector3 rayAabbMinLocal = convexFromLocal;
							MathUtil.VectorMin(ref convexToLocal, ref rayAabbMinLocal);
							//rayAabbMinLocal.setMin(convexToLocal);
							IndexedVector3 rayAabbMaxLocal = convexFromLocal;
							//rayAabbMaxLocal.setMax(convexToLocal);
							MathUtil.VectorMax(ref convexToLocal, ref rayAabbMaxLocal);
							rayAabbMinLocal += boxMinLocal;
							rayAabbMaxLocal += boxMaxLocal;
							concaveShape.ProcessAllTriangles(tccb, ref rayAabbMinLocal, ref rayAabbMaxLocal);
							BulletGlobals.StopProfile();
						}
					}
				}
			}
			else
			{
				if (collisionShape.IsCompound())
				{
					BulletGlobals.StartProfile("convexSweepCompound");
					CompoundShape compoundShape = (CompoundShape)collisionShape;
					for (int i = 0; i < compoundShape.GetNumChildShapes(); i++)
					{
						IndexedMatrix childTrans = compoundShape.GetChildTransform(i);
						CollisionShape childCollisionShape = compoundShape.GetChildShape(i);
						IndexedMatrix childWorldTrans = colObjWorldTransform * childTrans;
						// replace collision shape so that callback can determine the triangle
						CollisionShape saveCollisionShape = collisionObject.GetCollisionShape();
						collisionObject.InternalSetTemporaryCollisionShape(childCollisionShape);
						LocalInfoAdder my_cb = new LocalInfoAdder(i, resultCallback);
						my_cb.m_closestHitFraction = resultCallback.m_closestHitFraction;
						ObjectQuerySingle(castShape, ref convexFromTrans, ref convexToTrans, collisionObject, childCollisionShape, ref childWorldTrans, my_cb, allowedPenetration);
						// restore
						collisionObject.InternalSetTemporaryCollisionShape(saveCollisionShape);
					}

					BulletGlobals.StopProfile();
				}
			}
		}
	}

	protected ObjectArray<CollisionObject> m_collisionObjects;
	protected IDispatcher m_dispatcher1;
	protected DispatcherInfo m_dispatchInfo;
	protected IBroadphaseInterface m_broadphasePairCache;
	protected IDebugDraw m_debugDrawer;
	protected bool m_forceUpdateAllAabbs;
	protected IProfileManager m_profileManager;
}