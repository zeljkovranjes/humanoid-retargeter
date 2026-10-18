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

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletDynamics;

internal enum SliderFlags
{
	BT_SLIDER_FLAGS_CFM_DIRLIN = (1 << 0),
	BT_SLIDER_FLAGS_ERP_DIRLIN = (1 << 1),
	BT_SLIDER_FLAGS_CFM_DIRANG = (1 << 2),
	BT_SLIDER_FLAGS_ERP_DIRANG = (1 << 3),
	BT_SLIDER_FLAGS_CFM_ORTLIN = (1 << 4),
	BT_SLIDER_FLAGS_ERP_ORTLIN = (1 << 5),
	BT_SLIDER_FLAGS_CFM_ORTANG = (1 << 6),
	BT_SLIDER_FLAGS_ERP_ORTANG = (1 << 7),
	BT_SLIDER_FLAGS_CFM_LIMLIN = (1 << 8),
	BT_SLIDER_FLAGS_ERP_LIMLIN = (1 << 9),
	BT_SLIDER_FLAGS_CFM_LIMANG = (1 << 10),
	BT_SLIDER_FLAGS_ERP_LIMANG = (1 << 11)};