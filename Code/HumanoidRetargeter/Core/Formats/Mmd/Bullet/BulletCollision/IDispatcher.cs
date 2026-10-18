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
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal interface IDispatcher
{
	CollisionAlgorithm FindAlgorithm(CollisionObject body0, CollisionObject body1);
	CollisionAlgorithm FindAlgorithm(CollisionObject body0, CollisionObject body1, PersistentManifold sharedManifold);
	PersistentManifold GetNewManifold(CollisionObject body0, CollisionObject body1);
	void ReleaseManifold(PersistentManifold manifold);
	void ClearManifold(PersistentManifold manifold);
	bool NeedsCollision(CollisionObject body0, CollisionObject body1);
	bool NeedsResponse(CollisionObject body0, CollisionObject body1);
	void DispatchAllCollisionPairs(IOverlappingPairCache pairCache, DispatcherInfo dispatchInfo, IDispatcher dispatcher);
	int GetNumManifolds();
	PersistentManifold GetManifoldByIndexInternal(int index);
	PersistentManifoldArray GetInternalManifoldPointer();
	Object AllocateCollisionAlgorithm(int size);
	void FreeCollisionAlgorithm(CollisionAlgorithm collisionAlgorithm);
}