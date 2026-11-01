#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using HumanoidRetargeter.Core.Mapping;

namespace HumanoidRetargeter.Core.Sprites;

using Vector2 = System.Numerics.Vector2; // s&box compat: shadow engine's global-namespace Vector2 (see Code/HumanoidRetargeter/Assembly.cs)
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Writes a sprite as a rigged flat model: a binary glTF with the image embedded, one mesh
/// laid out in layered parts over one armature in the plane, ready for the same path any
/// custom model takes (material, compile, preview, DMX).
/// </summary>
/// <remarks>
/// As in Bonebase each part (head, body, arms, legs) is a layer of its own, so a limb that
/// moves leaves the body whole behind it. A limb takes the pixels as wide as the art really
/// is around each bone, as well as whatever lies under it, and on a profile both arms and both
/// legs cover the same pixels: the far limb is the near one's copy, and it shows once they
/// swing apart. Bones keep Mixamo's names so the rig is recognized outright, even when a
/// profile's legs share a position.
/// </remarks>
public static class SpriteGlb
{
    private const float FigureHeightMeters = 1.8f;

    /// <summary>The grid over the image: at most this many cells along its long side.</summary>
    private const int MaxCells = 192;

    /// <summary>The gap between layers, as a share of the figure's height.</summary>
    private const float LayerGap = 0.004f;

    /// <summary>Points read along a bone to measure how wide the art is around it.</summary>
    private const int WidthSamples = 9;

    /// <summary>How far past the last joint a chain's tip reaches, as a share of its last bone.</summary>
    private const float TipReach = 0.4f;

    private const int ComponentFloat = 5126;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const uint GlbMagic = 0x46546C67;
    private const uint ChunkJson = 0x4E4F534A;
    private const uint ChunkBinary = 0x004E4942;

    private static readonly (BoneRole Role, BoneRole? Parent, string Name)[] Bones =
    {
        (BoneRole.Hips, null, "Hips"), (BoneRole.Spine0, BoneRole.Hips, "Spine"),
        (BoneRole.Spine1, BoneRole.Spine0, "Spine1"), (BoneRole.Spine2, BoneRole.Spine1, "Spine2"),
        (BoneRole.Neck, BoneRole.Spine2, "Neck"), (BoneRole.Head, BoneRole.Neck, "Head"),
        (BoneRole.UpperArmL, BoneRole.Spine2, "LeftArm"), (BoneRole.LowerArmL, BoneRole.UpperArmL, "LeftForeArm"),
        (BoneRole.HandL, BoneRole.LowerArmL, "LeftHand"),
        (BoneRole.UpperArmR, BoneRole.Spine2, "RightArm"), (BoneRole.LowerArmR, BoneRole.UpperArmR, "RightForeArm"),
        (BoneRole.HandR, BoneRole.LowerArmR, "RightHand"),
        (BoneRole.UpperLegL, BoneRole.Hips, "LeftUpLeg"), (BoneRole.LowerLegL, BoneRole.UpperLegL, "LeftLeg"),
        (BoneRole.FootL, BoneRole.LowerLegL, "LeftFoot"), (BoneRole.ToeL, BoneRole.FootL, "LeftToeBase"),
        (BoneRole.UpperLegR, BoneRole.Hips, "RightUpLeg"), (BoneRole.LowerLegR, BoneRole.UpperLegR, "RightLeg"),
        (BoneRole.FootR, BoneRole.LowerLegR, "RightFoot"), (BoneRole.ToeR, BoneRole.FootR, "RightToeBase"),
    };

    private sealed record Part(BoneRole[] Chain, float MaxRadius, int FrontLayer, int ProfileLayer)
    {
        public List<(Vector2 From, Vector2 To, float Radius)> Segments { get; } = new();
        public int[] Joints { get; set; } = Array.Empty<int>();
        public HashSet<(int X, int Y)> Cells { get; } = new();
    }

    private static Part[] Parts() => new[]
    {
        // Bonebase's part order: the far side under the body, the near side over it, and the
        // character's right is the near side of a profile facing screen-right. The head is one
        // rigid piece on top, as a 2D rig has it: a face stretched over two bones crumples.
        new Part(new[] { BoneRole.UpperArmL, BoneRole.LowerArmL, BoneRole.HandL }, 0.05f, 2, -2),
        new Part(new[] { BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL }, 0.07f, -1, -1),
        new Part(new[] { BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Neck }, 0.2f, 0, 0),
        new Part(new[] { BoneRole.Head }, 0.2f, 3, 1),
        new Part(new[] { BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR }, 0.07f, -1, 2),
        new Part(new[] { BoneRole.UpperArmR, BoneRole.LowerArmR, BoneRole.HandR }, 0.05f, 2, 3),
    };

    /// <summary>
    /// Builds the model. <paramref name="alpha"/> is one byte per pixel, row by row;
    /// <paramref name="png"/> the image file, embedded as it is.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the alpha does not match the rig, or a
    /// joint is missing.</exception>
    public static byte[] Write(SpriteRig rig, byte[] alpha, byte[] png, string materialName)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(alpha);
        ArgumentNullException.ThrowIfNull(png);
        if (alpha.Length != rig.Width * rig.Height)
            throw new ArgumentException($"Sprite alpha holds {alpha.Length} pixels, expected {rig.Width} x {rig.Height}.", nameof(alpha));
        foreach (var (role, _, _) in Bones)
        {
            if (!rig.Joints.ContainsKey(role))
                throw new ArgumentException($"The sprite rig has no {role} joint.", nameof(rig));
        }

        var boneIndex = new Dictionary<BoneRole, int>();
        for (var i = 0; i < Bones.Length; i++)
            boneIndex[Bones[i].Role] = i;

        var figureSize = rig.Figure.Bottom - rig.Figure.Top + 1f;
        var metersPerPixel = FigureHeightMeters / figureSize;
        var origin = new Vector2(rig.Joints[BoneRole.Hips].X, rig.Figure.Bottom + 1f);
        Vector3 World(Vector2 pixel) => new((pixel.X - origin.X) * metersPerPixel, (origin.Y - pixel.Y) * metersPerPixel, 0f);

        var parts = Parts();
        foreach (var part in parts)
        {
            part.Joints = part.Chain.Select(role => boneIndex[role]).ToArray();
            for (var k = 0; k < part.Chain.Length; k++)
            {
                var from = rig.Joints[part.Chain[k]];
                var to = k + 1 < part.Chain.Length
                    ? rig.Joints[part.Chain[k + 1]]
                    : part.Chain[k] == BoneRole.Head
                        ? new Vector2(from.X, rig.Figure.Top)
                        : from + (from - rig.Joints[part.Chain[k - 1]]) * TipReach;
                part.Segments.Add((from, to, MeasuredRadius(rig, alpha, from, to, part.Chain[k] == BoneRole.Head, part.MaxRadius * figureSize)));
            }
        }

        var cellSize = Math.Max(1, (int)MathF.Ceiling(Math.Max(rig.Width, rig.Height) / (float)MaxCells));
        AssignCells(rig, alpha, parts, cellSize);

        using var buffer = new MemoryStream();
        using var binary = new BinaryWriter(buffer);
        var views = new List<object>();
        var accessors = new List<object>();
        int View(Action write)
        {
            while (buffer.Position % 4 != 0)
                binary.Write((byte)0);
            var start = buffer.Position;
            write();
            views.Add(new { buffer = 0, byteOffset = start, byteLength = buffer.Position - start });
            return views.Count - 1;
        }
        int Accessor(int count, string type, int componentType, Action write)
        {
            var view = View(write);
            accessors.Add(new { bufferView = view, componentType, count, type });
            return accessors.Count - 1;
        }

        var vertices = new List<(Vector3 Position, Vector2 Uv, int JointA, int JointB, float WeightB)>();
        var triangles = new List<int>();
        var layerGap = LayerGap * FigureHeightMeters;
        foreach (var part in parts)
        {
            var layer = (rig.IsProfile ? part.ProfileLayer : part.FrontLayer) * layerGap;
            if (rig.IsProfile && rig.FacesLeft)
                layer = -layer; // the far side swaps with the way it faces
            var index = new Dictionary<(int X, int Y), int>();
            int Vertex(int gridX, int gridY)
            {
                if (index.TryGetValue((gridX, gridY), out var known))
                    return known;
                var pixel = new Vector2(gridX * cellSize, gridY * cellSize);
                var (jointA, jointB, weightB) = Skin(part, pixel);
                var position = World(pixel) + new Vector3(0f, 0f, layer);
                vertices.Add((position, new Vector2(pixel.X / rig.Width, pixel.Y / rig.Height), jointA, jointB, weightB));
                return index[(gridX, gridY)] = vertices.Count - 1;
            }
            foreach (var (x, y) in part.Cells)
            {
                int a = Vertex(x, y), b = Vertex(x + 1, y), c = Vertex(x + 1, y + 1), d = Vertex(x, y + 1);
                triangles.AddRange(new[] { a, d, c, a, c, b });
            }
        }

        // A second skin facing the other way: the camera may look at a flat rig from either
        // side, and the mesh path has no double-sided material.
        var frontCount = vertices.Count;
        var frontTriangles = triangles.Count;
        for (var i = 0; i < frontTriangles; i += 3)
            triangles.AddRange(new[] { triangles[i] + frontCount, triangles[i + 2] + frontCount, triangles[i + 1] + frontCount });

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        var total = vertices.Count * 2;
        var positionView = View(() =>
        {
            for (var side = 0; side < 2; side++)
            {
                foreach (var vertex in vertices)
                {
                    binary.Write(vertex.Position.X);
                    binary.Write(vertex.Position.Y);
                    binary.Write(vertex.Position.Z);
                }
            }
        });
        accessors.Add(new
        {
            bufferView = positionView, componentType = ComponentFloat, count = total, type = "VEC3",
            min = new[] { min.X, min.Y, min.Z }, max = new[] { max.X, max.Y, max.Z },
        });
        var positions = accessors.Count - 1;
        var normals = Accessor(total, "VEC3", ComponentFloat, () =>
        {
            foreach (var z in new[] { 1f, -1f })
            {
                for (var i = 0; i < vertices.Count; i++)
                {
                    binary.Write(0f);
                    binary.Write(0f);
                    binary.Write(z);
                }
            }
        });
        var uvs = Accessor(total, "VEC2", ComponentFloat, () =>
        {
            for (var side = 0; side < 2; side++)
            {
                foreach (var vertex in vertices)
                {
                    binary.Write(vertex.Uv.X);
                    binary.Write(vertex.Uv.Y);
                }
            }
        });
        var joints = Accessor(total, "VEC4", ComponentUnsignedShort, () =>
        {
            for (var side = 0; side < 2; side++)
            {
                foreach (var vertex in vertices)
                {
                    binary.Write((ushort)vertex.JointA);
                    binary.Write((ushort)vertex.JointB);
                    binary.Write((ushort)0);
                    binary.Write((ushort)0);
                }
            }
        });
        var weights = Accessor(total, "VEC4", ComponentFloat, () =>
        {
            for (var side = 0; side < 2; side++)
            {
                foreach (var vertex in vertices)
                {
                    binary.Write(1f - vertex.WeightB);
                    binary.Write(vertex.WeightB);
                    binary.Write(0f);
                    binary.Write(0f);
                }
            }
        });
        var indices = Accessor(triangles.Count, "SCALAR", ComponentUnsignedInt, () =>
        {
            foreach (var index in triangles)
                binary.Write(index);
        });

        var inverseBinds = Accessor(Bones.Length, "MAT4", ComponentFloat, () =>
        {
            foreach (var (role, _, _) in Bones)
            {
                var position = World(rig.Joints[role]);
                foreach (var value in new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, -position.X, -position.Y, -position.Z, 1f })
                    binary.Write(value);
            }
        });
        var imageView = View(() => binary.Write(png));

        var nodes = new List<object>();
        for (var i = 0; i < Bones.Length; i++)
        {
            var (role, parent, name) = Bones[i];
            var position = World(rig.Joints[role]) - (parent is { } up ? World(rig.Joints[up]) : Vector3.Zero);
            nodes.Add(new
            {
                name = "mixamorig_" + name,
                translation = new[] { position.X, position.Y, position.Z },
                children = Enumerable.Range(0, Bones.Length).Where(c => Bones[c].Parent == role).ToArray(),
            });
        }
        nodes.Add(new { name = "sprite_mesh", mesh = 0, skin = 0 });

        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0", generator = "Humanoid Retargeter sprite import" },
            buffers = new[] { new { byteLength = buffer.Length } },
            bufferViews = views,
            accessors,
            nodes,
            skins = new[] { new { joints = Enumerable.Range(0, Bones.Length).ToArray(), inverseBindMatrices = inverseBinds } },
            meshes = new[]
            {
                new
                {
                    name = "sprite_mesh",
                    primitives = new[]
                    {
                        new
                        {
                            attributes = new { POSITION = positions, NORMAL = normals, TEXCOORD_0 = uvs, JOINTS_0 = joints, WEIGHTS_0 = weights },
                            indices,
                            material = 0,
                        },
                    },
                },
            },
            materials = new[]
            {
                new
                {
                    name = materialName,
                    pbrMetallicRoughness = new { baseColorTexture = new { index = 0 }, metallicFactor = 0f, roughnessFactor = 1f },
                    doubleSided = true,
                    alphaMode = "MASK",
                    alphaCutoff = 0.5f,
                },
            },
            textures = new[] { new { source = 0 } },
            images = new[] { new { name = materialName, bufferView = imageView, mimeType = "image/png" } },
            scenes = new[] { new { nodes = new[] { 0, Bones.Length } } },
            scene = 0,
        });
        return Container(json, buffer.ToArray());
    }

    /// <summary>How far the art reaches either side of a bone, up to <paramref name="cap"/>:
    /// the cap stands when a bone lies outside the art. A round head is as wide as its widest
    /// row; anything else is read by its middle, past the joints that run into other parts.</summary>
    private static float MeasuredRadius(SpriteRig rig, byte[] alpha, Vector2 from, Vector2 to, bool isHead, float cap)
    {
        var line = to - from;
        if (line.LengthSquared() == 0f)
            return cap;
        var across = Vector2.Normalize(new Vector2(-line.Y, line.X));
        var reaches = new float[WidthSamples];
        for (var i = 0; i < WidthSamples; i++)
        {
            var point = from + line * ((i + 0.5f) / WidthSamples);
            reaches[i] = Math.Max(InkReach(rig, alpha, point, across, cap), InkReach(rig, alpha, point, -across, cap));
        }
        Array.Sort(reaches);
        var reach = isHead ? reaches[^1] : reaches[WidthSamples / 2];
        return reach > 0f ? reach : cap;
    }

    /// <summary>How far the opaque pixels run from a point along a direction, up to <paramref name="cap"/>.</summary>
    private static float InkReach(SpriteRig rig, byte[] alpha, Vector2 point, Vector2 direction, float cap)
    {
        var reach = 0f;
        while (reach < cap)
        {
            var sample = point + direction * (reach + 1f);
            int x = (int)sample.X, y = (int)sample.Y;
            if (x < 0 || y < 0 || x >= rig.Width || y >= rig.Height || alpha[y * rig.Width + x] < SpriteRig.OpaqueAlpha)
                break;
            reach += 1f;
        }
        return reach;
    }

    /// <summary>Gives every grid cell over the figure to the parts it belongs to. A cell goes
    /// to the part it sits deepest inside, and to any other it falls within: distance is
    /// measured in each bone's own width, or a thin arm would win the wide body's pixels.</summary>
    private static void AssignCells(SpriteRig rig, byte[] alpha, Part[] parts, int cellSize)
    {
        var body = Array.FindIndex(parts, part => part.Chain[0] == BoneRole.Hips);
        var neckRow = rig.Joints[BoneRole.Neck].Y;
        var gridWidth = rig.Width / cellSize + 1;
        var gridHeight = rig.Height / cellSize + 1;
        var depths = new float[parts.Length];
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                if (!CellHasInk(rig, alpha, x, y, cellSize))
                    continue;
                var centre = new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
                var deepest = 0;
                for (var p = 0; p < parts.Length; p++)
                {
                    depths[p] = float.MaxValue;
                    foreach (var (from, to, radius) in parts[p].Segments)
                        depths[p] = MathF.Min(depths[p], SegmentDistance(centre, from, to) / radius);
                    // Nothing of the head stays behind on the body but the chin, which reads as a neck.
                    if (p == body && centre.Y < neckRow)
                        depths[p] = float.MaxValue;
                    if (depths[p] < depths[deepest])
                        deepest = p;
                }
                parts[deepest].Cells.Add((x, y));
                for (var p = 0; p < parts.Length; p++)
                {
                    if (depths[p] <= 1f)
                        parts[p].Cells.Add((x, y));
                }
            }
        }
    }

    // Cells overlap their neighbours by a pixel so an alpha edge on a seam never opens a gap.
    private static bool CellHasInk(SpriteRig rig, byte[] alpha, int cellX, int cellY, int cellSize)
    {
        var left = Math.Max(0, cellX * cellSize - 1);
        var top = Math.Max(0, cellY * cellSize - 1);
        var right = Math.Min(rig.Width - 1, (cellX + 1) * cellSize);
        var bottom = Math.Min(rig.Height - 1, (cellY + 1) * cellSize);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (alpha[y * rig.Width + x] >= SpriteRig.OpaqueAlpha)
                    return true;
            }
        }
        return false;
    }

    private static float SegmentDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        var line = to - from;
        var lengthSquared = line.LengthSquared();
        var along = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(point - from, line) / lengthSquared, 0f, 1f) : 0f;
        return Vector2.Distance(point, from + line * along);
    }

    /// <summary>The bone a vertex rides and how much of it the next joint takes: a bone the
    /// solver shortens takes its skin along instead of folding it past the joint. The share
    /// rises as a cube: spread evenly, every bent limb curves end to end like a noodle.</summary>
    private static (int JointA, int JointB, float WeightB) Skin(Part part, Vector2 pixel)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var k = 0; k < part.Segments.Count; k++)
        {
            var distance = SegmentDistance(pixel, part.Segments[k].From, part.Segments[k].To);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k;
            }
        }

        if (best + 1 == part.Segments.Count)
            return (part.Joints[best], part.Joints[best], 0f);
        var (from, to, _) = part.Segments[best];
        var line = to - from;
        var along = Math.Clamp(Vector2.Dot(pixel - from, line) / line.LengthSquared(), 0f, 1f);
        return (part.Joints[best], part.Joints[best + 1], along * along * along);
    }

    private static byte[] Container(byte[] json, byte[] binary)
    {
        var jsonPadding = (4 - json.Length % 4) % 4;
        var binaryPadding = (4 - binary.Length % 4) % 4;
        var jsonLength = json.Length + jsonPadding;
        var binaryLength = binary.Length + binaryPadding;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(GlbMagic);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonLength + 8 + binaryLength));
        writer.Write((uint)jsonLength);
        writer.Write(ChunkJson);
        writer.Write(json);
        writer.Write(Encoding.ASCII.GetBytes(new string(' ', jsonPadding)));
        writer.Write((uint)binaryLength);
        writer.Write(ChunkBinary);
        writer.Write(binary);
        writer.Write(new byte[binaryPadding]);
        return output.ToArray();
    }
}
