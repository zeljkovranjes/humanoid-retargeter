#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Trim;
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>Cuts a solved clip down to a list of kept frames.</summary>
public static class ClipTrim
{
    /// <summary>
    /// The frames at <paramref name="kept"/> (ascending, no repeats), in order. Where a section was cut
    /// out, the parentless bones (pelvis or root, which carry the travel) are shifted on the ground plane
    /// so the frame after the cut continues one step on from the frame before it: cutting the middle of a
    /// walk must not teleport the character. Height and rotation are untouched. Null
    /// <paramref name="up"/> skips the shift.
    /// </summary>
    public static List<XForm[]> Apply(IReadOnlyList<XForm[]> frames, IReadOnlyList<int> kept, SkeletonModel skeleton, Vector3? up)
    {
        if (kept.Count == 0)
            throw new ArgumentException("A trim must keep at least one frame.", nameof(kept));
        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i] < 0 || kept[i] >= frames.Count || (i > 0 && kept[i] <= kept[i - 1]))
                throw new ArgumentException($"Trim frame {kept[i]} is out of order or outside the clip's {frames.Count} frames.", nameof(kept));
        }

        var shift = new Vector3[skeleton.Count];
        var result = new List<XForm[]>(kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            var source = frames[kept[i]];
            if (i > 0 && kept[i] != kept[i - 1] + 1 && up is { } u)
            {
                var before = kept[i - 1];
                for (var b = 0; b < skeleton.Count; b++)
                {
                    if (skeleton[b].ParentIndex >= 0)
                        continue;
                    var step = before > 0 ? frames[before][b].Pos - frames[before - 1][b].Pos : Vector3.Zero;
                    var offset = result[i - 1][b].Pos + step - source[b].Pos;
                    shift[b] = offset - Vector3.Dot(offset, u) * u;
                }
            }

            var frame = new XForm[source.Length];
            for (var b = 0; b < frame.Length; b++)
                frame[b] = new XForm(source[b].Pos + shift[b], source[b].Rot);
            result.Add(frame);
        }
        return result;
    }
}
