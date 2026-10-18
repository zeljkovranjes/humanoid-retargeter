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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet;

internal static class DrawHelper
{
	public static void DebugDrawConstraint(TypedConstraint constraint, IDebugDraw debugDraw)
	{
		bool drawFrames = (debugDraw.GetDebugMode() & DebugDrawModes.DBG_DrawConstraints) != 0;
		bool drawLimits = (debugDraw.GetDebugMode() & DebugDrawModes.DBG_DrawConstraintLimits) != 0;
		float dbgDrawSize = constraint.GetDbgDrawSize();
		if (dbgDrawSize <= 0f)
		{
			return;
		}

		switch (constraint.GetConstraintType())
		{
			case TypedConstraintType.POINT2POINT_CONSTRAINT_TYPE:
			{
				Point2PointConstraint p2pC = constraint as Point2PointConstraint;
				IndexedMatrix tr = IndexedMatrix.Identity;
				IndexedVector3 pivot = p2pC.GetPivotInA();
				pivot = p2pC.GetRigidBodyA().GetCenterOfMassTransform() * pivot;
				tr._origin = pivot;
				debugDraw.DrawTransform(ref tr, dbgDrawSize);
				// that ideally should draw the same frame
				pivot = p2pC.GetPivotInB();
				pivot = p2pC.GetRigidBodyB().GetCenterOfMassTransform() * pivot;
				tr._origin = pivot;
				if (drawFrames)
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
			}

				break;
			case TypedConstraintType.HINGE_CONSTRAINT_TYPE:
			{
				HingeConstraint pHinge = constraint as HingeConstraint;
				IndexedMatrix tr = pHinge.GetRigidBodyA().GetCenterOfMassTransform() * pHinge.GetAFrame();
				if (drawFrames)
				{
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				}

				tr = pHinge.GetRigidBodyB().GetCenterOfMassTransform() * pHinge.GetBFrame();
				if (drawFrames)
				{
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				}

				float minAng = pHinge.GetLowerLimit();
				float maxAng = pHinge.GetUpperLimit();
				if (minAng == maxAng)
				{
					break;
				}

				bool drawSect = true;
				if (minAng > maxAng)
				{
					minAng = 0f;
					maxAng = MathUtil.SIMD_2_PI;
					drawSect = false;
				}

				if (drawLimits)
				{
					IndexedVector3 center = tr._origin;
					IndexedVector3 normal = tr._basis.GetColumn(2);
					IndexedVector3 axis = tr._basis.GetColumn(0);
					IndexedVector3 zero = IndexedVector3.Zero;
					debugDraw.DrawArc(ref center, ref normal, ref axis, dbgDrawSize, dbgDrawSize, minAng, maxAng, ref zero, drawSect);
				}
			}

				break;
			case TypedConstraintType.CONETWIST_CONSTRAINT_TYPE:
			{
				ConeTwistConstraint pCT = constraint as ConeTwistConstraint;
				IndexedMatrix tr = pCT.GetRigidBodyA().GetCenterOfMassTransform() * pCT.GetAFrame();
				if (drawFrames)
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				tr = pCT.GetRigidBodyB().GetCenterOfMassTransform() * pCT.GetBFrame();
				if (drawFrames)
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				IndexedVector3 zero = IndexedVector3.Zero;
				if (drawLimits)
				{
					//const float length = float(5);
					float length = dbgDrawSize;
					const int nSegments = 8 * 4;
					float fAngleInRadians = MathUtil.SIMD_2_PI * (float)(nSegments - 1) / (float)nSegments;
					IndexedVector3 pPrev = pCT.GetPointForAngle(fAngleInRadians, length);
					pPrev = tr * pPrev;
					for (int i = 0; i < nSegments; i++)
					{
						fAngleInRadians = MathUtil.SIMD_2_PI * (float)i / (float)nSegments;
						IndexedVector3 pCur = pCT.GetPointForAngle(fAngleInRadians, length);
						pCur = tr * pCur;
						debugDraw.DrawLine(ref pPrev, ref pCur, ref zero);
						if (i % (nSegments / 8) == 0)
						{
							IndexedVector3 origin = tr._origin;
							debugDraw.DrawLine(ref origin, ref pCur, ref zero);
						}

						pPrev = pCur;
					}

					float tws = pCT.GetTwistSpan();
					float twa = pCT.GetTwistAngle();
					bool useFrameB = (pCT.GetRigidBodyB().GetInvMass() > 0f);
					if (useFrameB)
					{
						tr = pCT.GetRigidBodyB().GetCenterOfMassTransform() * pCT.GetBFrame();
					}
					else
					{
						tr = pCT.GetRigidBodyA().GetCenterOfMassTransform() * pCT.GetAFrame();
					}

					IndexedVector3 pivot = tr._origin;
					IndexedVector3 normal = tr._basis.GetColumn(0);
					IndexedVector3 axis = tr._basis.GetColumn(1);
					debugDraw.DrawArc(ref pivot, ref normal, ref axis, dbgDrawSize, dbgDrawSize, -twa - tws, -twa + tws, ref zero, true);
				}
			}

				break;
			case TypedConstraintType.D6_CONSTRAINT_TYPE:
			case TypedConstraintType.D6_SPRING_CONSTRAINT_TYPE:
			{
				Generic6DofConstraint p6DOF = constraint as Generic6DofConstraint;
				IndexedMatrix tr = p6DOF.GetCalculatedTransformA();
				if (drawFrames)
				{
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				}

				tr = p6DOF.GetCalculatedTransformB();
				if (drawFrames)
				{
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				}

				IndexedVector3 zero = IndexedVector3.Zero;
				if (drawLimits)
				{
					tr = p6DOF.GetCalculatedTransformA();
					IndexedVector3 center = p6DOF.GetCalculatedTransformB()._origin;
					// up is axis 1 not 2 ?
					IndexedVector3 up = tr._basis.GetColumn(1);
					IndexedVector3 axis = tr._basis.GetColumn(0);
					float minTh = p6DOF.GetRotationalLimitMotor(1).m_loLimit;
					float maxTh = p6DOF.GetRotationalLimitMotor(1).m_hiLimit;
					float minPs = p6DOF.GetRotationalLimitMotor(2).m_loLimit;
					float maxPs = p6DOF.GetRotationalLimitMotor(2).m_hiLimit;
					debugDraw.DrawSpherePatch(ref center, ref up, ref axis, dbgDrawSize * .9f, minTh, maxTh, minPs, maxPs, ref zero);
					axis = tr._basis.GetColumn(1);
					float ay = p6DOF.GetAngle(1);
					float az = p6DOF.GetAngle(2);
					float cy = (float)Math.Cos(ay);
					float sy = (float)Math.Sin(ay);
					float cz = (float)Math.Cos(az);
					float sz = (float)Math.Sin(az);
					IndexedVector3 ref1 = new IndexedVector3(cy * cz * axis.X + cy * sz * axis.Y - sy * axis.Z, -sz * axis.X + cz * axis.Y, cz * sy * axis.X + sz * sy * axis.Y + cy * axis.Z);
					tr = p6DOF.GetCalculatedTransformB();
					IndexedVector3 normal = -tr._basis.GetColumn(0);
					float minFi = p6DOF.GetRotationalLimitMotor(0).m_loLimit;
					float maxFi = p6DOF.GetRotationalLimitMotor(0).m_hiLimit;
					if (minFi > maxFi)
					{
						debugDraw.DrawArc(ref center, ref normal, ref ref1, dbgDrawSize, dbgDrawSize, -MathUtil.SIMD_PI, MathUtil.SIMD_PI, ref zero, false);
					}
					else if (minFi < maxFi)
					{
						debugDraw.DrawArc(ref center, ref normal, ref ref1, dbgDrawSize, dbgDrawSize, minFi, maxFi, ref zero, false);
					}

					tr = p6DOF.GetCalculatedTransformA();
					IndexedVector3 bbMin = p6DOF.GetTranslationalLimitMotor().m_lowerLimit;
					IndexedVector3 bbMax = p6DOF.GetTranslationalLimitMotor().m_upperLimit;
					debugDraw.DrawBox(ref bbMin, ref bbMax, ref tr, ref zero);
				}
			}

				break;
			case TypedConstraintType.SLIDER_CONSTRAINT_TYPE:
			{
				SliderConstraint pSlider = constraint as SliderConstraint;
				IndexedMatrix tr = pSlider.GetCalculatedTransformA();
				if (drawFrames)
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				tr = pSlider.GetCalculatedTransformB();
				if (drawFrames)
					debugDraw.DrawTransform(ref tr, dbgDrawSize);
				IndexedVector3 zero = IndexedVector3.Zero;
				if (drawLimits)
				{
					IndexedMatrix tr2 = pSlider.GetCalculatedTransformA();
					IndexedVector3 li_min = tr2 * new IndexedVector3(pSlider.GetLowerLinLimit(), 0f, 0f);
					IndexedVector3 li_max = tr2 * new IndexedVector3(pSlider.GetUpperLinLimit(), 0f, 0f);
					debugDraw.DrawLine(ref li_min, ref li_max, ref zero);
					IndexedVector3 normal = tr._basis.GetColumn(0);
					IndexedVector3 axis = tr._basis.GetColumn(1);
					float a_min = pSlider.GetLowerAngLimit();
					float a_max = pSlider.GetUpperAngLimit();
					IndexedVector3 center = pSlider.GetCalculatedTransformB()._origin;
					debugDraw.DrawArc(ref center, ref normal, ref axis, dbgDrawSize, dbgDrawSize, a_min, a_max, ref zero, true);
				}
			}

				break;
			default:
				break;
		}

		return;
	}
}