# Third-party notices

Existing editor recovery dependencies and their notices are documented in [Editor/Embedded/README.md](Editor/Embedded/README.md).

## BulletXNA

The managed physics implementation under `Code/HumanoidRetargeter/Core/Formats/Mmd/Bullet` is adapted from [xexuxjy/bullet-xna](https://github.com/xexuxjy/bullet-xna/tree/73b2e1b9f559ad6a270d5afbd3f17ffc2eb4c9ae), commit `73b2e1b9f559ad6a270d5afbd3f17ffc2eb4c9ae`. It is a C# port of Bullet 2.79. No XNA runtime or native Bullet binary is included or required.

Local adaptations: isolated namespaces, internal top-level types, one type per file, inactive platform branches removed, XNA drawing omitted, and diagnostic assertions/file/console logging removed for the s&box whitelist. The collision and constraint algorithms retain the upstream implementation. MMD file parsing, coordinate conversion and animation baking are implemented by the retargeter around that solver. The original source notices are retained.

Copyright (c) 2011 Mark Neale

Bullet Continuous Collision Detection and Physics Library
Copyright (c) 2003-2008 Erwin Coumans

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the use of this software.

Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.

## SDEF and cloth/rope solver adaptations

The SDEF calculation in `MmdSkinning.cs` is adapted to System.Numerics from [babylon-mmd's sdefVertex.ts](https://github.com/noname0310/babylon-mmd/blob/ccb750db2998ed1067017eaf67f3fc987cfba6f8/src/Loader/Shaders/sdefVertex.ts), commit `ccb750db2998ed1067017eaf67f3fc987cfba6f8`.

Position prediction, distance projection and bending graph construction in `MmdSoftBodySolver.cs` are adapted from [mmd2gltf-cs-physics SoftBody.cs](https://github.com/masaka1024/mmd2gltf-cs-physics/blob/6d71e39d195cabf021846045f10e8de1e0cc207c/Assets/MmdPhysics/Core/SoftBody.cs), commit `6d71e39d195cabf021846045f10e8de1e0cc207c`. The adapter uses System.Numerics, the retargeter's parsed records, sampled rigid transforms and explicit shape collision projection. No Unity dependency is included. This is the upstream position-based cloth/rope approximation, not a complete port of Bullet soft bodies.

Both adaptations are licensed under the following MIT terms:

Copyright (c) 2024 noname

Copyright (c) 2026 masaka1024

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
