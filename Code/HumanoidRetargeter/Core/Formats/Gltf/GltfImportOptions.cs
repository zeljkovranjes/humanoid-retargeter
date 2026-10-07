#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Gltf;
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>Options for <see cref="GltfImporter.Import"/>.</summary>
public sealed class GltfImportOptions
{
    /// <summary>Use authored skin binds as the target rest instead of a potentially posed
    /// scene state when the skins share a compatible bind space. Animation samples
    /// remain in their original node hierarchy.</summary>
    public bool UseSkinBindPose { get; init; }
    /// <summary>Fixed resampling rate for all clips, frames per second.</summary>
    public float SampleFps { get; init; } = 30f;

    /// <summary>
    /// Optional reader for external buffer URIs in plain <c>.gltf</c> files. The core
    /// importer remains file-system agnostic; IO-owning callers can resolve paths relative
    /// to the picked document. Null preserves the self-contained GLB/data-URI-only policy.
    /// </summary>
    public Func<string, byte[]>? ExternalBufferResolver { get; init; }
}
