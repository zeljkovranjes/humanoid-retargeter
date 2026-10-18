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
using System.Diagnostics;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;
using System.IO;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal abstract class StridingMeshInterface
{
	public StridingMeshInterface()
	{
		m_scaling = new IndexedVector3(1f);
	}

	public virtual void Cleanup()
	{
	}

	public virtual void InternalProcessAllTriangles(IInternalTriangleIndexCallback callback, ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
		int numtotalphysicsverts = 0;
		int part, graphicssubparts = GetNumSubParts();
		Object vertexbase = null;
		Object indexbase = null;
		int indexstride = 3;
		PHY_ScalarType type = PHY_ScalarType.PHY_FLOAT;
		PHY_ScalarType gfxindextype = PHY_ScalarType.PHY_INTEGER;
		int stride = 0, numverts = 0, numtriangles = 0;
		IndexedVector3[] triangle = new IndexedVector3[3];
		IndexedVector3 meshScaling = GetScaling();
		for (part = 0; part < graphicssubparts; part++)
		{
			GetLockedReadOnlyVertexIndexBase(out vertexbase, out numverts, out type, out stride, out indexbase, out indexstride, out numtriangles, out gfxindextype, part);
			numtotalphysicsverts += numtriangles * 3; //upper bound
			switch (gfxindextype)
			{
				case PHY_ScalarType.PHY_INTEGER:
				{
					int[] indexList = ((ObjectArray<int>)indexbase).GetRawArray();
					if (vertexbase is ObjectArray<IndexedVector3>)
					{
						IndexedVector3[] vertexList = (vertexbase as ObjectArray<IndexedVector3>).GetRawArray();
						//string filename = "c:/tmp/xna-bvh-mesh-iv3.txt";
						//FileStream filestream = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
						//using (StreamWriter writer = new StreamWriter(filestream))
						//{
						//writer.WriteLine("XNA IV3");
						for (int gfxindex = 0; gfxindex < numtriangles; gfxindex++)
						{
							int triIndex = (gfxindex * indexstride);
							//writer.WriteLine(String.Format("indices[{0}][{1}][{2}]", indexList[triIndex], indexList[triIndex + 1], indexList[triIndex + 2]));
							int index1 = indexList[triIndex];
							int index2 = indexList[triIndex + 1];
							int index3 = indexList[triIndex + 2];
							triangle[0] = vertexList[index1] * meshScaling;
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", triangle[0].X,triangle[0].Y,triangle[0].Z));
							triangle[1] = vertexList[index2] * meshScaling;
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", triangle[1].X, triangle[1].Y, triangle[1].Z));
							triangle[2] = vertexList[index3] * meshScaling;
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", triangle[2].X, triangle[2].Y, triangle[2].Z));
							//writer.WriteLine(String.Format("index[{0}] triangle[0].X =[{1:0.00000000}] triangle[0].Y =[{2:0.00000000}] triangle[0].Z =[{3:0.00000000}]", gfxindex, triangle[0].X, triangle[0].Y, triangle[0].Z));
							//writer.WriteLine(String.Format("index[{0}] triangle[1].X =[{1:0.00000000}] triangle[1].Y =[{2:0.00000000}] triangle[1].Z =[{3:0.00000000}]", gfxindex, triangle[1].X, triangle[1].Y, triangle[1].Z));
							//writer.WriteLine(String.Format("index[{0}] triangle[2].X =[{1:0.00000000}] triangle[2].Y =[{2:0.00000000}] triangle[2].Z =[{3:0.00000000}]", gfxindex, triangle[2].X, triangle[2].Y, triangle[2].Z));
							//if(BulletGlobals.g_streamWriter != null && BulletGlobals.debugStridingMesh && !callback.graphics())
							//{
							//    MathUtil.PrintVector3(BulletGlobals.g_streamWriter,"SMI:T0",triangle[0]);
							//    MathUtil.PrintVector3(BulletGlobals.g_streamWriter, "SMI:T1", triangle[1]);
							//    MathUtil.PrintVector3(BulletGlobals.g_streamWriter, "SMI:T2", triangle[2]);
							//}
							callback.InternalProcessTriangleIndex(triangle, part, gfxindex);
						}
					//    writer.Flush();
					//}
					}
					else if (vertexbase is ObjectArray<float>)
					{
						//string filename = "c:/tmp/xna-bvh-mesh-float.txt";
						//FileStream filestream = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
						//using (StreamWriter writer = new StreamWriter(filestream))
						//{
						//writer.WriteLine("XNA FLOAT");
						float[] vertexList = (vertexbase as ObjectArray<float>).GetRawArray();
						for (int gfxindex = 0; gfxindex < numtriangles; gfxindex++)
						{
							int triIndex = (gfxindex * indexstride);
							//writer.WriteLine(String.Format("indices[{0}][{1}][{2}]", indexList[triIndex], indexList[triIndex + 1], indexList[triIndex + 2]));
							// ugly!!
							int index1 = indexList[triIndex];
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]));
							triangle[0] = new IndexedVector3(vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]) * meshScaling;
							index1 = indexList[triIndex + 1];
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]));
							triangle[1] = new IndexedVector3(vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]) * meshScaling;
							index1 = indexList[triIndex + 2];
							//writer.WriteLine(String.Format("GB1[{0:0.00000000}][{1:0.00000000}][{2:0.00000000}]", vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]));
							triangle[2] = new IndexedVector3(vertexList[index1 * stride], vertexList[(index1 * stride) + 1], vertexList[(index1 * stride) + 2]) * meshScaling;
							//writer.WriteLine(String.Format("index[{0}] triangle[0].X =[{1:0.00000000}] triangle[0].Y =[{2:0.00000000}] triangle[0].Z =[{3:0.00000000}]", gfxindex, triangle[0].X, triangle[0].Y, triangle[0].Z));
							//writer.WriteLine(String.Format("index[{0}] triangle[1].X =[{1:0.00000000}] triangle[1].Y =[{2:0.00000000}] triangle[1].Z =[{3:0.00000000}]", gfxindex, triangle[1].X, triangle[1].Y, triangle[1].Z));
							//writer.WriteLine(String.Format("index[{0}] triangle[2].X =[{1:0.00000000}] triangle[2].Y =[{2:0.00000000}] triangle[2].Z =[{3:0.00000000}]", gfxindex, triangle[2].X, triangle[2].Y, triangle[2].Z));
							callback.InternalProcessTriangleIndex(triangle, part, gfxindex);
						}
					//writer.Flush();
					//}
					}
					else
					{
						{
						}
					}

					break;
				}

				default:
				{
					{
					}

					break;
				}
			}

			UnLockReadOnlyVertexBase(part);
		}
	}

	public void CalculateAabbBruteForce(out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		//first calculate the total aabb for all triangles
		AabbCalculationCallback aabbCallback = new AabbCalculationCallback();
		aabbMin = MathUtil.MIN_VECTOR;
		aabbMax = MathUtil.MAX_VECTOR;
		InternalProcessAllTriangles(aabbCallback, ref aabbMin, ref aabbMax);
		aabbMin = aabbCallback.m_aabbMin;
		aabbMax = aabbCallback.m_aabbMax;
	}

	public abstract void GetLockedVertexIndexBase(out Object vertexbase, out int numverts, out PHY_ScalarType type, out int stride, out Object indexbase, out int indexstride, out int numfaces, out PHY_ScalarType indicestype, int subpart);
	public abstract void GetLockedReadOnlyVertexIndexBase(out Object vertexbase, out int numverts, out PHY_ScalarType type, out int stride, out Object indexbase, out int indexstride, out int numfaces, out PHY_ScalarType indicestype, int subpart);
	public abstract void UnLockVertexBase(int subpart);
	public abstract void UnLockReadOnlyVertexBase(int subpart);
	public abstract int GetNumSubParts();
	public abstract void PreallocateVertices(int numverts);
	public abstract void PreallocateIndices(int numindices);
	public virtual bool HasPremadeAabb()
	{
		return false;
	}

	public virtual void SetPremadeAabb(ref IndexedVector3 aabbMin, ref IndexedVector3 aabbMax)
	{
	}

	public virtual void GetPremadeAabb(out IndexedVector3 aabbMin, out IndexedVector3 aabbMax)
	{
		aabbMin = IndexedVector3.Zero;
		aabbMax = IndexedVector3.Zero;
	}

	public IndexedVector3 GetScaling()
	{
		return m_scaling;
	}

	public void SetScaling(ref IndexedVector3 scaling)
	{
		m_scaling = scaling;
	}

	protected IndexedVector3 m_scaling;
}