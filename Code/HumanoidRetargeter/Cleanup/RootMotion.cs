using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Maths;

namespace HumanoidRetargeter.Cleanup;

/// <summary>How root motion should be handled in the output clip.</summary>
public enum RootMotionMode
{
    /// <summary>Leave hips/root channels exactly as solved.</summary>
    Off,

    /// <summary>
    /// Move the ground-projected, smoothed hips trajectory onto the dedicated root bone;
    /// hips keep height and full rotation locally (Unity/UE convention).
    /// </summary>
    Extract,

    /// <summary>Remove horizontal hips travel entirely (in-place clip); root stays put.</summary>
    InPlace,
}

/// <summary>Axis/index context for root-motion processing, supplied by the solver.</summary>
public sealed class RootMotionAxes
{
    /// <summary>World up direction of the clip's space.</summary>
    public required Vector3 Up { get; init; }

    /// <summary>Frame-array index of the dedicated root bone.</summary>
    public required int RootIndex { get; init; }

    /// <summary>Frame-array index of the hips bone.</summary>
    public required int HipsIndex { get; init; }

    /// <summary>
    /// True when the hips bone's local transform is parented to the root bone (so moving the
    /// root must subtract from hips locals to preserve world positions). False when both are
    /// scene-root level.
    /// </summary>
    public required bool HipsParentIsRoot { get; init; }

    /// <summary>Half-window (in frames) of the moving-average smoothing for the root path.</summary>
    public int SmoothHalfWindow { get; init; } = 2;
}

/// <summary>
/// Root-motion post-pass over solved frames (lists of per-bone local transforms).
/// Convention (production standard): root motion = ground-plane projection of the hips
/// trajectory, low-pass filtered; vertical motion always stays on the hips.
/// </summary>
public static class RootMotion
{
    public static void Apply(List<XForm[]> frames, RootMotionAxes axes, RootMotionMode mode)
    {
        if (mode == RootMotionMode.Off || frames.Count == 0)
            return;

        var up = Vector3.Normalize(axes.Up);
        int n = frames.Count;

        // Hips world positions (hips local == world unless parented to the moving root,
        // which is identity before this pass by construction).
        var hipsWorld = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var hips = frames[i][axes.HipsIndex].Pos;
            if (axes.HipsParentIsRoot)
                hips += frames[i][axes.RootIndex].Pos;
            hipsWorld[i] = hips;
        }

        // Horizontal (ground-plane) component of the hips trajectory.
        var horizontal = new Vector3[n];
        for (int i = 0; i < n; i++)
            horizontal[i] = hipsWorld[i] - Vector3.Dot(hipsWorld[i], up) * up;

        switch (mode)
        {
            case RootMotionMode.Extract:
            {
                var smoothed = Smooth(horizontal, axes.SmoothHalfWindow);
                // Root starts where frame 0's smoothed path starts, relative to itself:
                // anchor at the first sample so clips begin at the origin.
                var origin = smoothed[0];
                for (int i = 0; i < n; i++)
                {
                    var rootPos = smoothed[i] - origin;
                    var f = frames[i];
                    f[axes.RootIndex] = new XForm(rootPos, f[axes.RootIndex].Rot);

                    // Hips keep the residual (wobble + height), so root ∘ hips ≈ original world.
                    var residual = hipsWorld[i] - rootPos;
                    f[axes.HipsIndex] = new XForm(
                        axes.HipsParentIsRoot ? residual : residual,
                        f[axes.HipsIndex].Rot);
                }
                break;
            }

            case RootMotionMode.InPlace:
            {
                var smoothed = Smooth(horizontal, axes.SmoothHalfWindow);
                var origin = horizontal[0];
                for (int i = 0; i < n; i++)
                {
                    var f = frames[i];
                    var travel = smoothed[i] - origin;
                    var newHips = hipsWorld[i] - travel;
                    if (axes.HipsParentIsRoot)
                        newHips -= f[axes.RootIndex].Pos;
                    f[axes.HipsIndex] = new XForm(newHips, f[axes.HipsIndex].Rot);
                }
                break;
            }
        }
    }

    /// <summary>Centered moving average with edge clamping.</summary>
    private static Vector3[] Smooth(Vector3[] values, int halfWindow)
    {
        if (halfWindow <= 0)
            return (Vector3[])values.Clone();

        var result = new Vector3[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var sum = Vector3.Zero;
            int count = 0;
            for (int k = -halfWindow; k <= halfWindow; k++)
            {
                int j = Math.Clamp(i + k, 0, values.Length - 1);
                sum += values[j];
                count++;
            }
            result[i] = sum / count;
        }
        return result;
    }
}
