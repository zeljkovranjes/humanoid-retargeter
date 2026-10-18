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

internal class DebugDrawcallback : ITriangleCallback, IInternalTriangleIndexCallback, IDisposable
{
	IDebugDraw m_debugDrawer;
	IndexedVector3 m_color;
	IndexedMatrix m_worldTrans;
	public virtual bool graphics()
	{
		return true;
	}

	public DebugDrawcallback()
	{
	} // for pool

	public DebugDrawcallback(IDebugDraw debugDrawer, ref IndexedMatrix worldTrans, ref IndexedVector3 color)
	{
		m_debugDrawer = debugDrawer;
		m_color = color;
		m_worldTrans = worldTrans;
	}

	public void Initialise(IDebugDraw debugDrawer, ref IndexedMatrix worldTrans, ref IndexedVector3 color)
	{
		m_debugDrawer = debugDrawer;
		m_color = color;
		m_worldTrans = worldTrans;
	}

	public virtual void InternalProcessTriangleIndex(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		ProcessTriangle(triangle, partId, triangleIndex);
	}

	public virtual void ProcessTriangle(IndexedVector3[] triangle, int partId, int triangleIndex)
	{
		//(void)partId;
		//(void)triangleIndex;
		IndexedVector3 wv0, wv1, wv2;
		wv0 = m_worldTrans * triangle[0];
		wv1 = m_worldTrans * triangle[1];
		wv2 = m_worldTrans * triangle[2];
		if ((int)(m_debugDrawer.GetDebugMode() & DebugDrawModes.DBG_DrawNormals) != 0)
		{
			IndexedVector3 center = (wv0 + wv1 + wv2) * (1f / 3f);
			IndexedVector3 normal = (wv1 - wv0).Cross(wv2 - wv0);
			normal.Normalize();
			IndexedVector3 normalColor = new IndexedVector3(1, 1, 0);
		//m_debugDrawer.DrawLine(center, center + normal, normalColor);
		}

		m_debugDrawer.DrawLine(ref wv0, ref wv1, ref m_color);
		m_debugDrawer.DrawLine(ref wv1, ref wv2, ref m_color);
		m_debugDrawer.DrawLine(ref wv2, ref wv0, ref m_color);
	}

	//public static void drawUnitSphere(GraphicsDevice gd)
	//{
	//    gd.VertexDeclaration = s_vertexDeclaration;
	//    int primCount = s_sphereIndices.Length / 3;
	//    //int primCount = 2;
	//    int indexStart = 0;
	//    gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, s_sphereVertices, 0, s_sphereVertices.Length, s_sphereIndices, indexStart, primCount);
	//}
	public virtual void Cleanup()
	{
	}

	public void Dispose()
	{
		BulletGlobals.DebugDrawcallbackPool.Free(this);
	}
}