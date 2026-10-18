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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet;

internal static class TransformUtil
{
	public static IList<float> FloatToList(float f)
	{
		IList<float> list = new List<float>();
		list.Add(f);
		return list;
	}

	public static IList<float> VectorToList(IndexedVector3 vector)
	{
		return VectorToList(ref vector);
	}

	public static IList<float> VectorToList(ref IndexedVector3 vector)
	{
		IList<float> list = new List<float>();
		list.Add(vector.X);
		list.Add(vector.Y);
		list.Add(vector.Z);
		return list;
	}

	public static IList<IndexedVector3> VectorsFromList(IList<float> list)
	{
		IList<IndexedVector3> vecList = new List<IndexedVector3>();
		int numVectors = list.Count / 3;
		for (int i = 0; i < numVectors; ++i)
		{
			IndexedVector3 vec = new IndexedVector3(list[3 * i], list[(3 * i) + 1], list[(3 * i) + 2]);
			vecList.Add(vec);
		}

		return vecList;
	}

	//public static IndexedVector3 InverseTransform(ref IndexedVector3 v, ref IndexedMatrix m)
	//{
	//    return IndexedVector3.Transform(v, IndexedMatrix.Invert(m));
	//}
	public static void PlaneSpace1(ref IndexedVector3 n, out IndexedVector3 p, out IndexedVector3 q)
	{
		if (Math.Abs(n.Z) > MathUtil.SIMDSQRT12)
		{
			// choose p in y-z plane
			float a = n.Y * n.Y + n.Z * n.Z;
			float k = MathUtil.RecipSqrt(a);
			p = new IndexedVector3(0, -n.Z * k, n.Y * k);
			// set q = n x p
			q = new IndexedVector3(a * k, -n.X * p.Z, n.X * p.Y);
		}
		else
		{
			// choose p in x-y plane
			float a = n.X * n.X + n.Y * n.Y;
			float k = MathUtil.RecipSqrt(a);
			p = new IndexedVector3(-n.Y * k, n.X * k, 0);
			// set q = n x p
			q = new IndexedVector3(-n.Z * p.Y, n.Z * p.X, a * k);
		}
	}

	public static IndexedVector3 AabbSupport(ref IndexedVector3 halfExtents, ref IndexedVector3 supportDir)
	{
		return new IndexedVector3(supportDir.X < 0f ? -halfExtents.X : halfExtents.X, supportDir.Y < 0f ? -halfExtents.Y : halfExtents.Y, supportDir.Z < 0f ? -halfExtents.Z : halfExtents.Z);
	}

	public static void IntegrateTransform(IndexedMatrix curTrans, IndexedVector3 linvel, IndexedVector3 angvel, float timeStep, out IndexedMatrix predictedTransform)
	{
		IntegrateTransform(ref curTrans, ref linvel, ref angvel, timeStep, out predictedTransform);
	}

	public static void IntegrateTransform(ref IndexedMatrix curTrans, ref IndexedVector3 linvel, ref IndexedVector3 angvel, float timeStep, out IndexedMatrix predictedTransform)
	{
		predictedTransform = IndexedMatrix.CreateTranslation(curTrans._origin + linvel * timeStep);
		//	#define QUATERNION_DERIVATIVE
		//Exponential map
		//google for "Practical Parameterization of Rotations Using the Exponential Map", F. Sebastian Grassia
		IndexedVector3 axis;
		float fAngle = angvel.Length();
		//limit the angular motion
		if (fAngle * timeStep > ANGULAR_MOTION_THRESHOLD)
		{
			fAngle = ANGULAR_MOTION_THRESHOLD / timeStep;
		}

		if (fAngle < 0.001f)
		{
			// use Taylor's expansions of sync function
			axis = angvel * (0.5f * timeStep - (timeStep * timeStep * timeStep) * (0.020833333333f) * fAngle * fAngle);
		}
		else
		{
			// sync(fAngle) = sin(c*fAngle)/t
			axis = angvel * ((float)Math.Sin(0.5f * fAngle * timeStep) / fAngle);
		}

		IndexedQuaternion dorn = new IndexedQuaternion(axis.X, axis.Y, axis.Z, (float)Math.Cos(fAngle * timeStep * .5f));
		IndexedQuaternion orn0 = curTrans.GetRotation();
		IndexedQuaternion predictedOrn = dorn * orn0;
		predictedOrn.Normalize();
		IndexedMatrix newMatrix = IndexedMatrix.CreateFromQuaternion(predictedOrn);
		predictedTransform._basis = newMatrix._basis;
	}

	public static void CalculateVelocityQuaternion(ref IndexedVector3 pos0, ref IndexedVector3 pos1, ref IndexedQuaternion orn0, ref IndexedQuaternion orn1, float timeStep, out IndexedVector3 linVel, out IndexedVector3 angVel)
	{
		linVel = (pos1 - pos0) / timeStep;
		if (orn0 != orn1)
		{
			IndexedVector3 axis;
			float angle;
			CalculateDiffAxisAngleQuaternion(ref orn0, ref orn1, out axis, out angle);
			angVel = axis * (angle / timeStep);
		}
		else
		{
			angVel = IndexedVector3.Zero;
		}
	}

	public static void CalculateDiffAxisAngleQuaternion(ref IndexedQuaternion orn0, ref IndexedQuaternion orn1a, out IndexedVector3 axis, out float angle)
	{
		IndexedQuaternion orn1 = MathUtil.QuatFurthest(ref orn0, ref orn1a);
		IndexedQuaternion dorn = orn1 * MathUtil.QuaternionInverse(ref orn0);
		dorn.Normalize();
		angle = MathUtil.QuatAngle(ref dorn);
		axis = new IndexedVector3(dorn.X, dorn.Y, dorn.Z);
		//check for axis length
		float len = axis.LengthSquared();
		if (len < MathUtil.SIMD_EPSILON * MathUtil.SIMD_EPSILON)
		{
			axis = new IndexedVector3(1f, 0, 0);
		}
		else
		{
			axis.Normalize();
		}
	}

	public static void CalculateVelocity(ref IndexedMatrix transform0, ref IndexedMatrix transform1, float timeStep, out IndexedVector3 linVel, out IndexedVector3 angVel)
	{
		linVel = (transform1._origin - transform0._origin) / timeStep;
		MathUtil.SanityCheckVector(ref linVel);
		IndexedVector3 axis;
		float angle;
		CalculateDiffAxisAngle(ref transform0, ref transform1, out axis, out angle);
		angVel = axis * (angle / timeStep);
		MathUtil.SanityCheckVector(ref angVel);
	}

	public static void CalculateDiffAxisAngle(ref IndexedMatrix transform0, ref IndexedMatrix transform1, out IndexedVector3 axis, out float angle)
	{
		//IndexedMatrix dmat = GetRotateMatrix(ref transform1) * IndexedMatrix.Invert(GetRotateMatrix(ref transform0));
		IndexedBasisMatrix dmat = transform1._basis * transform0._basis.Inverse();
		IndexedQuaternion dorn = IndexedQuaternion.Identity;
		GetRotation(ref dmat, out dorn);
		dorn.Normalize();
		angle = MathUtil.QuatAngle(ref dorn);
		axis = new IndexedVector3(dorn.X, dorn.Y, dorn.Z);
		//axis[3] = float(0.);
		//check for axis length
		float len = axis.LengthSquared();
		if (len < MathUtil.SIMD_EPSILON * MathUtil.SIMD_EPSILON)
		{
			axis = new IndexedVector3(1, 0, 0);
		}
		else
		{
			axis.Normalize();
		}
	}

	public static void GetRotation(ref IndexedBasisMatrix a, out IndexedQuaternion rot)
	{
		rot = a.GetRotation();
	}

	public static IndexedQuaternion GetRotation(ref IndexedBasisMatrix a)
	{
		return a.GetRotation();
	}

	public static float ANGULAR_MOTION_THRESHOLD = .5f * MathUtil.SIMD_HALF_PI;
}