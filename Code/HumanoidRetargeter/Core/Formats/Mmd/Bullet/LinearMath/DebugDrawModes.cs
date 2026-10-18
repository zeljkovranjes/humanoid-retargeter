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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

/*-------------------------------------------------------------------------------------*/
[Flags]
internal enum DebugDrawModes
{
	DBG_NoDebug = 0,
	DBG_DrawWireframe = 1,
	DBG_DrawAabb = 2,
	DBG_DrawFeaturesText = 4,
	DBG_DrawContactPoints = 8,
	DBG_NoDeactivation = 16,
	DBG_NoHelpText = 32,
	DBG_DrawText = 64,
	DBG_ProfileTimings = 128,
	DBG_EnableSatComparison = 256,
	DBG_DisableBulletLCP = 512,
	DBG_EnableCCD = 1024,
	DBG_DrawConstraints = (1 << 11),
	DBG_DrawConstraintLimits = (1 << 12),
	DBG_DrawFastWireframe = (1 << 13),
	DBG_DrawNormals = (1 << 14),
	ALL = DBG_DrawWireframe | DBG_DrawAabb | DBG_DrawFeaturesText | DBG_DrawContactPoints | DBG_DrawText | DBG_DrawConstraints | DBG_DrawConstraintLimits | DBG_DrawNormals,
	DBG_MAX_DEBUG_DRAW_MODE
}