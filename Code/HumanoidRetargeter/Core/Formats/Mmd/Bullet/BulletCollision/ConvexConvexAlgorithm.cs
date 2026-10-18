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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class ConvexConvexAlgorithm : ActivatingCollisionAlgorithm
{
	ISimplexSolverInterface m_simplexSolver;
	IConvexPenetrationDepthSolver m_pdSolver;
	bool m_ownManifold;
	PersistentManifold m_manifoldPtr;
	bool m_lowLevelOfDetail;
	int m_numPerturbationIterations;
	int m_minimumPointsPerturbationThreshold;
	bool disableCcd = false;
	ObjectArray<IndexedVector3> m_vertices = new ObjectArray<IndexedVector3>();
	public ConvexConvexAlgorithm()
	{
	} // for pool

	public ConvexConvexAlgorithm(PersistentManifold mf, CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, ISimplexSolverInterface simplexSolver, IConvexPenetrationDepthSolver pdSolver, int numPerturbationIterations, int minimumPointsPerturbationThreshold) : base(ci, body0, body1)
	{
		m_simplexSolver = simplexSolver;
		m_pdSolver = pdSolver;
		m_ownManifold = false;
		m_manifoldPtr = mf;
		m_lowLevelOfDetail = false;
		m_numPerturbationIterations = numPerturbationIterations;
		m_minimumPointsPerturbationThreshold = minimumPointsPerturbationThreshold;
	}

	public void Initialize(PersistentManifold mf, CollisionAlgorithmConstructionInfo ci, CollisionObject body0, CollisionObject body1, ISimplexSolverInterface simplexSolver, IConvexPenetrationDepthSolver pdSolver, int numPerturbationIterations, int minimumPointsPerturbationThreshold)
	{
		base.Initialize(ci, body0, body1);
		m_simplexSolver = simplexSolver;
		m_pdSolver = pdSolver;
		m_ownManifold = false;
		m_manifoldPtr = mf;
		m_lowLevelOfDetail = false;
		m_numPerturbationIterations = numPerturbationIterations;
		m_minimumPointsPerturbationThreshold = minimumPointsPerturbationThreshold;
	}

	public override void Cleanup()
	{
		if (m_ownManifold)
		{
			if (m_manifoldPtr != null)
			{
				m_dispatcher.ReleaseManifold(m_manifoldPtr);
			}

			m_ownManifold = false;
		}

		m_manifoldPtr = null;
		BulletGlobals.ConvexConvexAlgorithmPool.Free(this);
	}

	public override void ProcessCollision(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		if (m_manifoldPtr == null)
		{
			//swapped?
			m_manifoldPtr = m_dispatcher.GetNewManifold(body0, body1);
			m_ownManifold = true;
		}

		//resultOut = new ManifoldResult();
		resultOut.SetPersistentManifold(m_manifoldPtr);
		//comment-out next line to test multi-contact generation
		//resultOut.GetPersistentManifold().ClearManifold();
		ConvexShape min0 = body0.GetCollisionShape() as ConvexShape;
		ConvexShape min1 = body1.GetCollisionShape() as ConvexShape;
		IndexedVector3 normalOnB;
		IndexedVector3 pointOnBWorld;
		if ((min0.GetShapeType() == BroadphaseNativeTypes.CAPSULE_SHAPE_PROXYTYPE) && (min1.GetShapeType() == BroadphaseNativeTypes.CAPSULE_SHAPE_PROXYTYPE))
		{
			CapsuleShape capsuleA = min0 as CapsuleShape;
			CapsuleShape capsuleB = min1 as CapsuleShape;
			//IndexedVector3 localScalingA = capsuleA.GetLocalScaling();
			//IndexedVector3 localScalingB = capsuleB.GetLocalScaling();
			float threshold = m_manifoldPtr.GetContactBreakingThreshold();
			float dist = CapsuleCapsuleDistance(out normalOnB, out pointOnBWorld, capsuleA.GetHalfHeight(), capsuleA.GetRadius(), capsuleB.GetHalfHeight(), capsuleB.GetRadius(), capsuleA.GetUpAxis(), capsuleB.GetUpAxis(), body0.GetWorldTransform(), body1.GetWorldTransform(), threshold);
			if (dist < threshold)
			{
				{
				}

				resultOut.AddContactPoint(ref normalOnB, ref pointOnBWorld, dist);
			}

			resultOut.RefreshContactPoints();
			return;
		}

		{
			ClosestPointInput input = ClosestPointInput.Default();
			using (GjkPairDetector gjkPairDetector = BulletGlobals.GjkPairDetectorPool.Get())
			{
				gjkPairDetector.Initialize(min0, min1, m_simplexSolver, m_pdSolver);
				//TODO: if (dispatchInfo.m_useContinuous)
				gjkPairDetector.SetMinkowskiA(min0);
				gjkPairDetector.SetMinkowskiB(min1);
				{
					input.m_maximumDistanceSquared = min0.GetMargin() + min1.GetMargin() + m_manifoldPtr.GetContactBreakingThreshold();
					input.m_maximumDistanceSquared *= input.m_maximumDistanceSquared;
				}

				//input.m_stackAlloc = dispatchInfo.m_stackAllocator;
				input.m_transformA = body0.GetWorldTransform();
				input.m_transformB = body1.GetWorldTransform();
				if (min0.IsPolyhedral() && min1.IsPolyhedral())
				{
					DummyResult dummy = new DummyResult();
					PolyhedralConvexShape polyhedronA = min0 as PolyhedralConvexShape;
					PolyhedralConvexShape polyhedronB = min1 as PolyhedralConvexShape;
					if (polyhedronA.GetConvexPolyhedron() != null && polyhedronB.GetConvexPolyhedron() != null)
					{
						float threshold = m_manifoldPtr.GetContactBreakingThreshold();
						float minDist = float.MinValue;
						IndexedVector3 sepNormalWorldSpace = new IndexedVector3(0, 1, 0);
						bool foundSepAxis = true;
						if (dispatchInfo.m_enableSatConvex)
						{
							foundSepAxis = PolyhedralContactClipping.FindSeparatingAxis(polyhedronA.GetConvexPolyhedron(), polyhedronB.GetConvexPolyhedron(), body0.GetWorldTransform(), body1.GetWorldTransform(), out sepNormalWorldSpace);
						}
						else
						{
							gjkPairDetector.GetClosestPoints(ref input, dummy, dispatchInfo.m_debugDraw);
							float l2 = gjkPairDetector.GetCachedSeparatingAxis().LengthSquared();
							if (l2 > MathUtil.SIMD_EPSILON)
							{
								sepNormalWorldSpace = gjkPairDetector.GetCachedSeparatingAxis() * (1.0f / l2);
								//minDist = -1e30f;//gjkPairDetector.getCachedSeparatingDistance();
								minDist = gjkPairDetector.GetCachedSeparatingDistance() - min0.GetMargin() - min1.GetMargin();
								foundSepAxis = gjkPairDetector.GetCachedSeparatingDistance() < (min0.GetMargin() + min1.GetMargin());
							}
						}

						if (foundSepAxis)
						{
							//				printf("sepNormalWorldSpace=%f,%f,%f\n",sepNormalWorldSpace.getX(),sepNormalWorldSpace.getY(),sepNormalWorldSpace.getZ());
							PolyhedralContactClipping.ClipHullAgainstHull(sepNormalWorldSpace, polyhedronA.GetConvexPolyhedron(), polyhedronB.GetConvexPolyhedron(), body0.GetWorldTransform(), body1.GetWorldTransform(), minDist - threshold, threshold, resultOut);
						}

						if (m_ownManifold)
						{
							resultOut.RefreshContactPoints();
						}

						return;
					}
					else
					{
						//we can also deal with convex versus triangle (without connectivity data)
						if (polyhedronA.GetConvexPolyhedron() != null && polyhedronB.GetShapeType() == BroadphaseNativeTypes.TRIANGLE_SHAPE_PROXYTYPE)
						{
							m_vertices.Clear();
							TriangleShape tri = polyhedronB as TriangleShape;
							m_vertices.Add(body1.GetWorldTransform() * tri.m_vertices1[0]);
							m_vertices.Add(body1.GetWorldTransform() * tri.m_vertices1[1]);
							m_vertices.Add(body1.GetWorldTransform() * tri.m_vertices1[2]);
							float threshold = m_manifoldPtr.GetContactBreakingThreshold();
							IndexedVector3 sepNormalWorldSpace = new IndexedVector3(0, 1, 0);
							;
							float minDist = float.MinValue;
							float maxDist = threshold;
							bool foundSepAxis = false;
							if (false)
							{
								polyhedronB.InitializePolyhedralFeatures();
								foundSepAxis = PolyhedralContactClipping.FindSeparatingAxis(polyhedronA.GetConvexPolyhedron(), polyhedronB.GetConvexPolyhedron(), body0.GetWorldTransform(), body1.GetWorldTransform(), out sepNormalWorldSpace);
							//	 printf("sepNormalWorldSpace=%f,%f,%f\n",sepNormalWorldSpace.getX(),sepNormalWorldSpace.getY(),sepNormalWorldSpace.getZ());
							}
							else
							{
								gjkPairDetector.GetClosestPoints(ref input, dummy, dispatchInfo.m_debugDraw);
								float l2 = gjkPairDetector.GetCachedSeparatingAxis().LengthSquared();
								if (l2 > MathUtil.SIMD_EPSILON)
								{
									sepNormalWorldSpace = gjkPairDetector.GetCachedSeparatingAxis() * (1.0f / l2);
									//minDist = gjkPairDetector.getCachedSeparatingDistance();
									//maxDist = threshold;
									minDist = gjkPairDetector.GetCachedSeparatingDistance() - min0.GetMargin() - min1.GetMargin();
									foundSepAxis = true;
								}
							}

							if (foundSepAxis)
							{
								PolyhedralContactClipping.ClipFaceAgainstHull(sepNormalWorldSpace, polyhedronA.GetConvexPolyhedron(), body0.GetWorldTransform(), m_vertices, minDist - threshold, maxDist, resultOut);
							}

							if (m_ownManifold)
							{
								resultOut.RefreshContactPoints();
							}

							return;
						}
					}
				}

				gjkPairDetector.GetClosestPoints(ref input, resultOut, dispatchInfo.getDebugDraw(), false);
				//now perform 'm_numPerturbationIterations' collision queries with the perturbated collision objects
				//perform perturbation when more then 'm_minimumPointsPerturbationThreshold' points
				if (m_numPerturbationIterations > 0 && resultOut.GetPersistentManifold().GetNumContacts() < m_minimumPointsPerturbationThreshold)
				{
					IndexedVector3 v0, v1;
					IndexedVector3 sepNormalWorldSpace = gjkPairDetector.GetCachedSeparatingAxis();
					sepNormalWorldSpace.Normalize();
					TransformUtil.PlaneSpace1(ref sepNormalWorldSpace, out v0, out v1);
					bool perturbeA = true;
					const float angleLimit = 0.125f * MathUtil.SIMD_PI;
					float perturbeAngle;
					float radiusA = min0.GetAngularMotionDisc();
					float radiusB = min1.GetAngularMotionDisc();
					if (radiusA < radiusB)
					{
						perturbeAngle = BulletGlobals.gContactBreakingThreshold / radiusA;
						perturbeA = true;
					}
					else
					{
						perturbeAngle = BulletGlobals.gContactBreakingThreshold / radiusB;
						perturbeA = false;
					}

					if (perturbeAngle > angleLimit)
					{
						perturbeAngle = angleLimit;
					}

					IndexedMatrix unPerturbedTransform;
					if (perturbeA)
					{
						unPerturbedTransform = input.m_transformA;
					}
					else
					{
						unPerturbedTransform = input.m_transformB;
					}

					for (int i = 0; i < m_numPerturbationIterations; i++)
					{
						if (v0.LengthSquared() > MathUtil.SIMD_EPSILON)
						{
							IndexedQuaternion perturbeRot = new IndexedQuaternion(v0, perturbeAngle);
							float iterationAngle = i * (MathUtil.SIMD_2_PI / (float)m_numPerturbationIterations);
							IndexedQuaternion rotq = new IndexedQuaternion(sepNormalWorldSpace, iterationAngle);
							if (perturbeA)
							{
								input.m_transformA._basis = (new IndexedBasisMatrix(MathUtil.QuaternionInverse(rotq) * perturbeRot * rotq) * body0.GetWorldTransform()._basis);
								input.m_transformB = body1.GetWorldTransform();
								input.m_transformB = body1.GetWorldTransform();
								dispatchInfo.m_debugDraw.DrawTransform(ref input.m_transformA, 10.0f);
							}
							else
							{
								input.m_transformA = body0.GetWorldTransform();
								input.m_transformB._basis = (new IndexedBasisMatrix(MathUtil.QuaternionInverse(rotq) * perturbeRot * rotq) * body1.GetWorldTransform()._basis);
								dispatchInfo.m_debugDraw.DrawTransform(ref input.m_transformB, 10.0f);
							}

							PerturbedContactResult perturbedResultOut = new PerturbedContactResult(resultOut, ref input.m_transformA, ref input.m_transformB, ref unPerturbedTransform, perturbeA, dispatchInfo.getDebugDraw());
							gjkPairDetector.GetClosestPoints(ref input, perturbedResultOut, dispatchInfo.getDebugDraw(), false);
						}
					}
				}
			}
		}

		if (m_ownManifold)
		{
			resultOut.RefreshContactPoints();
		}
	}

	public override float CalculateTimeOfImpact(CollisionObject body0, CollisionObject body1, DispatcherInfo dispatchInfo, ManifoldResult resultOut)
	{
		//(void)resultOut;
		//(void)dispatchInfo;
		float resultFraction = 1.0f;
		float squareMot0 = (body0.GetInterpolationWorldTransform()._origin - body0.GetWorldTransform()._origin).LengthSquared();
		float squareMot1 = (body1.GetInterpolationWorldTransform()._origin - body1.GetWorldTransform()._origin).LengthSquared();
		if (squareMot0 < body0.GetCcdSquareMotionThreshold() && squareMot1 < body1.GetCcdSquareMotionThreshold())
		{
			return resultFraction;
		}

		if (disableCcd)
		{
			return 1f;
		}

		//An adhoc way of testing the Continuous Collision Detection algorithms
		//One object is approximated as a sphere, to simplify things
		//Starting in penetration should report no time of impact
		//For proper CCD, better accuracy and handling of 'allowed' penetration should be added
		//also the mainloop of the physics should have a kind of toi queue (something like Brian Mirtich's application of Timewarp for Rigidbodies)
		{
			ConvexShape convex0 = body0.GetCollisionShape() as ConvexShape;
			SphereShape sphere1 = BulletGlobals.SphereShapePool.Get();
			sphere1.Initialize(body1.GetCcdSweptSphereRadius()); //todo: allow non-zero sphere sizes, for better approximation
			CastResult result = BulletGlobals.CastResultPool.Get();
			VoronoiSimplexSolver voronoiSimplex = BulletGlobals.VoronoiSimplexSolverPool.Get();
			//SubsimplexConvexCast ccd0(&sphere,min0,&voronoiSimplex);
			using (GjkConvexCast ccd1 = BulletGlobals.GjkConvexCastPool.Get())
			{
				ccd1.Initialize(convex0, sphere1, voronoiSimplex);
				//ContinuousConvexCollision ccd(min0,min1,&voronoiSimplex,0);
				if (ccd1.CalcTimeOfImpact(body0.GetWorldTransform(), body0.GetInterpolationWorldTransform(), body1.GetWorldTransform(), body1.GetInterpolationWorldTransform(), result))
				{
					//store result.m_fraction in both bodies
					if (body0.GetHitFraction() > result.m_fraction)
					{
						body0.SetHitFraction(result.m_fraction);
					}

					if (body1.GetHitFraction() > result.m_fraction)
					{
						body1.SetHitFraction(result.m_fraction);
					}

					if (resultFraction > result.m_fraction)
					{
						resultFraction = result.m_fraction;
					}
				}

				BulletGlobals.VoronoiSimplexSolverPool.Free(voronoiSimplex);
				BulletGlobals.SphereShapePool.Free(sphere1);
				result.Cleanup();
			}
		}

		{
			ConvexShape convex1 = body1.GetCollisionShape() as ConvexShape;
			SphereShape sphere0 = BulletGlobals.SphereShapePool.Get();
			sphere0.Initialize(body0.GetCcdSweptSphereRadius()); //todo: allow non-zero sphere sizes, for better approximation
			CastResult result = BulletGlobals.CastResultPool.Get();
			VoronoiSimplexSolver voronoiSimplex = BulletGlobals.VoronoiSimplexSolverPool.Get();
			//SubsimplexConvexCast ccd0(&sphere,min0,&voronoiSimplex);
			using (GjkConvexCast ccd1 = BulletGlobals.GjkConvexCastPool.Get())
			{
				ccd1.Initialize(sphere0, convex1, voronoiSimplex);
				//ContinuousConvexCollision ccd(min0,min1,&voronoiSimplex,0);
				if (ccd1.CalcTimeOfImpact(body0.GetWorldTransform(), body0.GetInterpolationWorldTransform(), body1.GetWorldTransform(), body1.GetInterpolationWorldTransform(), result))
				{
					//store result.m_fraction in both bodies
					if (body0.GetHitFraction() > result.m_fraction)
					{
						body0.SetHitFraction(result.m_fraction);
					}

					if (body1.GetHitFraction() > result.m_fraction)
					{
						body1.SetHitFraction(result.m_fraction);
					}

					if (resultFraction > result.m_fraction)
					{
						resultFraction = result.m_fraction;
					}
				}

				BulletGlobals.VoronoiSimplexSolverPool.Free(voronoiSimplex);
				BulletGlobals.SphereShapePool.Free(sphere0);
				result.Cleanup();
			}
		}

		return resultFraction;
	}

	public override void GetAllContactManifolds(PersistentManifoldArray manifoldArray)
	{
		if (m_manifoldPtr != null && m_ownManifold)
		{
			manifoldArray.Add(m_manifoldPtr);
		}
	}

	public void SetLowLevelOfDetail(bool useLowLevel)
	{
		m_lowLevelOfDetail = useLowLevel;
	}

	public PersistentManifold GetManifold()
	{
		return m_manifoldPtr;
	}

	public static void SegmentsClosestPoints(out IndexedVector3 ptsVector, out IndexedVector3 offsetA, out IndexedVector3 offsetB, out float tA, out float tB, ref IndexedVector3 translation, ref IndexedVector3 dirA, float hlenA, ref IndexedVector3 dirB, float hlenB)
	{
		// compute the parameters of the closest points on each line segment
		float dirA_dot_dirB = IndexedVector3.Dot(ref dirA, ref dirB);
		float dirA_dot_trans = IndexedVector3.Dot(ref dirA, ref translation);
		float dirB_dot_trans = IndexedVector3.Dot(ref dirB, ref translation);
		float denom = 1.0f - dirA_dot_dirB * dirA_dot_dirB;
		if (MathUtil.FuzzyZero(denom))
		{
			tA = 0.0f;
		}
		else
		{
			tA = (dirA_dot_trans - dirB_dot_trans * dirA_dot_dirB) / denom;
			if (tA < -hlenA)
			{
				tA = -hlenA;
			}
			else if (tA > hlenA)
			{
				tA = hlenA;
			}
		}

		tB = tA * dirA_dot_dirB - dirB_dot_trans;
		if (tB < -hlenB)
		{
			tB = -hlenB;
			tA = tB * dirA_dot_dirB + dirA_dot_trans;
			if (tA < -hlenA)
			{
				tA = -hlenA;
			}
			else if (tA > hlenA)
			{
				tA = hlenA;
			}
		}
		else if (tB > hlenB)
		{
			tB = hlenB;
			tA = tB * dirA_dot_dirB + dirA_dot_trans;
			if (tA < -hlenA)
			{
				tA = -hlenA;
			}
			else if (tA > hlenA)
			{
				tA = hlenA;
			}
		}

		// compute the closest points relative to segment centers.
		offsetA = dirA * tA;
		offsetB = dirB * tB;
		ptsVector = translation - offsetA + offsetB;
	}

	public static float CapsuleCapsuleDistance(out IndexedVector3 normalOnB, out IndexedVector3 pointOnB, float capsuleLengthA, float capsuleRadiusA, float capsuleLengthB, float capsuleRadiusB, int capsuleAxisA, int capsuleAxisB, IndexedMatrix transformA, IndexedMatrix transformB, float distanceThreshold)
	{
		return CapsuleCapsuleDistance(out normalOnB, out pointOnB, capsuleLengthA, capsuleRadiusA, capsuleLengthB, capsuleRadiusB, capsuleAxisA, capsuleAxisB, ref transformA, ref transformB, distanceThreshold);
	}

	public static float CapsuleCapsuleDistance(out IndexedVector3 normalOnB, out IndexedVector3 pointOnB, float capsuleLengthA, float capsuleRadiusA, float capsuleLengthB, float capsuleRadiusB, int capsuleAxisA, int capsuleAxisB, ref IndexedMatrix transformA, ref IndexedMatrix transformB, float distanceThreshold)
	{
		IndexedVector3 directionA = transformA._basis.GetColumn(capsuleAxisA);
		IndexedVector3 translationA = transformA._origin;
		IndexedVector3 directionB = transformB._basis.GetColumn(capsuleAxisB);
		IndexedVector3 translationB = transformB._origin;
		// translation between centers
		IndexedVector3 translation = translationB - translationA;
		// compute the closest points of the capsule line segments
		IndexedVector3 ptsVector; // the vector between the closest points
		IndexedVector3 offsetA, offsetB; // offsets from segment centers to their closest points
		float tA, tB; // parameters on line segment
		SegmentsClosestPoints(out ptsVector, out offsetA, out offsetB, out tA, out tB, ref translation, ref directionA, capsuleLengthA, ref directionB, capsuleLengthB);
		float distance = ptsVector.Length() - capsuleRadiusA - capsuleRadiusB;
		if (distance > distanceThreshold)
		{
			normalOnB = new IndexedVector3(0, 1, 0);
			pointOnB = IndexedVector3.Zero;
			return distance;
		}

		float lenSqr = ptsVector.LengthSquared();
		if (lenSqr <= (MathUtil.SIMD_EPSILON * MathUtil.SIMD_EPSILON))
		{
			//degenerate case where 2 capsules are likely at the same location: take a vector tangential to 'directionA'
			IndexedVector3 q;
			TransformUtil.PlaneSpace1(ref directionA, out normalOnB, out q);
		}
		else
		{
			// compute the contact normal
			normalOnB = ptsVector * -MathUtil.RecipSqrt(lenSqr);
		}

		pointOnB = transformB._origin + offsetB + normalOnB * capsuleRadiusB;
		return distance;
	}
}