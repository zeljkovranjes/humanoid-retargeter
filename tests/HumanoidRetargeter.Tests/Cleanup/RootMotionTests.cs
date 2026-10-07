using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Cleanup;

public class RootMotionTests
{
    // Minimal scenario: a hips track translating forward (+Z) over 31 frames while
    // bobbing vertically (Y), with a dedicated root track at identity.
    private static (List<XForm[]> frames, RootMotionAxes axes) MakeWalk(
        int rootIndex = 0, int hipsIndex = 1, int frameCount = 31)
    {
        var frames = new List<XForm[]>();
        for (int i = 0; i < frameCount; i++)
        {
            float t = i / (float)(frameCount - 1);
            var locals = new XForm[2];
            locals[rootIndex] = new XForm(Vector3.Zero, Quaternion.Identity);
            // hips: forward travel 100cm, lateral wobble, vertical bob around 80cm
            locals[hipsIndex] = new XForm(
                new Vector3(
                    2f * MathF.Sin(t * MathF.Tau * 3f),
                    80f + 3f * MathF.Sin(t * MathF.Tau * 6f),
                    100f * t),
                Quaternion.Identity);
            frames.Add(locals);
        }
        var axes = new RootMotionAxes
        {
            Up = Vector3.UnitY,
            RootIndex = rootIndex,
            HipsIndex = hipsIndex,
            HipsParentIsRoot = false, // hips local == world here (parent = scene root)
        };
        return (frames, axes);
    }

    [Fact]
    public void Off_LeavesFramesUntouched()
    {
        var (frames, axes) = MakeWalk();
        var before = frames[10][1].Pos;
        RootMotion.Apply(frames, axes, RootMotionMode.Off);
        Assert.Equal(before, frames[10][1].Pos);
        Assert.Equal(Vector3.Zero, frames[10][0].Pos);
    }

    [Fact]
    public void Extract_RootCarriesHorizontalPath_HipsKeepBob()
    {
        var (frames, axes) = MakeWalk();
        RootMotion.Apply(frames, axes, RootMotionMode.Extract);

        // root advanced ~100cm in Z by the final frame, stays on the ground plane
        var rootEnd = frames[^1][0].Pos;
        Assert.InRange(rootEnd.Z, 95f, 105f);
        Assert.Equal(0f, rootEnd.Y, 3);

        // hips keep full vertical bob range (~6cm) relative to root
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var f in frames)
        {
            minY = MathF.Min(minY, f[1].Pos.Y);
            maxY = MathF.Max(maxY, f[1].Pos.Y);
        }
        Assert.InRange(maxY - minY, 5.5f, 6.5f);

        // root+hips composition reproduces the original world hips within smoothing tolerance
        var world10 = frames[10][0].Pos + frames[10][1].Pos;
        Assert.InRange(world10.Z, 100f * (10f / 30f) - 3f, 100f * (10f / 30f) + 3f);
    }

    [Fact]
    public void InPlace_RemovesHorizontalDrift()
    {
        var (frames, axes) = MakeWalk();
        RootMotion.Apply(frames, axes, RootMotionMode.InPlace);

        foreach (var f in frames)
        {
            Assert.InRange(f[1].Pos.Z, -4f, 4f);   // forward travel removed (wobble remains)
            Assert.Equal(Vector3.Zero, f[0].Pos);  // root untouched at origin
        }
        // vertical bob preserved
        Assert.InRange(frames[7][1].Pos.Y, 75f, 85f);
    }

    [Fact]
    public void Extract_RootMotion_IsSmooth()
    {
        var (frames, axes) = MakeWalk();
        RootMotion.Apply(frames, axes, RootMotionMode.Extract);

        // lateral wobble (X sine) must be smoothed out of the root, not copied verbatim:
        // successive root deltas should be nearly constant for our linear walk
        for (int i = 2; i < frames.Count; i++)
        {
            var d1 = frames[i][0].Pos - frames[i - 1][0].Pos;
            var d0 = frames[i - 1][0].Pos - frames[i - 2][0].Pos;
            Assert.True((d1 - d0).Length() < 1.0f, $"root jerk at {i}: {(d1 - d0).Length()}");
        }
    }

    // ============================================================ skeleton-aware overload

    /// <summary>The walk path used by the skeleton-aware tests (world space).</summary>
    private static Vector3 WalkWorld(int i, int frameCount)
    {
        float t = i / (float)(frameCount - 1);
        return new Vector3(
            2f * MathF.Sin(t * MathF.Tau * 3f),
            80f + 3f * MathF.Sin(t * MathF.Tau * 6f),
            100f * t);
    }

    /// <summary>Builds frames whose FK hips WORLD follows <see cref="WalkWorld"/> exactly.</summary>
    private static List<XForm[]> MakeWalkFrames(SkeletonModel skeleton, int hipsIndex, int frameCount)
    {
        int hipsParent = skeleton[hipsIndex].ParentIndex;
        var frames = new List<XForm[]>();
        for (int i = 0; i < frameCount; i++)
        {
            var locals = Pose.Rest(skeleton).Locals;
            var desired = new XForm(WalkWorld(i, frameCount), Quaternion.Identity);
            if (hipsParent < 0)
            {
                locals[hipsIndex] = desired;
            }
            else
            {
                var parentWorld = new Pose(locals).ToWorld(skeleton)[hipsParent];
                locals[hipsIndex] = XForm.ToLocal(parentWorld, desired);
            }
            frames.Add(locals);
        }
        return frames;
    }

    [Fact]
    public void SkeletonAware_Extract_HipsUnderIntermediate_NotDoubleCounted()
    {
        // root → offset → hips: the hips' world must come from real FK over the chain,
        // and writing the root must not double-count the offset.
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("root", null, XForm.Identity),
            new BoneDefinition("offset", "root", new XForm(new Vector3(0f, 0f, 5f), Quaternion.Identity)),
            new BoneDefinition("hips", "offset", new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
        });
        int root = skeleton.IndexOf("root"), hips = skeleton.IndexOf("hips");
        const int n = 31;
        var frames = MakeWalkFrames(skeleton, hips, n);
        var axes = new RootMotionAxes
        {
            Up = Vector3.UnitY,
            RootIndex = root,
            HipsIndex = hips,
            HipsParentIsRoot = false, // deliberately misleading: must be derived from the skeleton
        };

        RootMotion.Apply(frames, skeleton, axes, RootMotionMode.Extract);

        for (int i = 0; i < n; i++)
        {
            var world = new Pose(frames[i]).ToWorld(skeleton);

            // Composed FK world reproduces the source hips trajectory exactly (the hips
            // ride under the root, so extraction must not change the world pose at all).
            float miss = Vector3.Distance(world[hips].Pos, WalkWorld(i, n));
            Assert.True(miss < 1e-3f, $"frame {i}: hips world off by {miss} cm");

            // Root stays on the ground plane.
            Assert.Equal(0f, world[root].Pos.Y, 3);
        }

        // Root advanced ~100 cm in Z by the final frame.
        var rootEnd = new Pose(frames[^1]).ToWorld(skeleton)[root].Pos;
        Assert.InRange(rootEnd.Z, 95f, 105f);

        // Hips local keeps the full vertical bob (~6 cm) relative to its parent chain.
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var f in frames)
        {
            minY = MathF.Min(minY, f[hips].Pos.Y);
            maxY = MathF.Max(maxY, f[hips].Pos.Y);
        }
        Assert.InRange(maxY - minY, 5.5f, 6.5f);
    }

    [Fact]
    public void SkeletonAware_Extract_RotatedRoot_BobStaysVertical()
    {
        // Root with a non-identity rest rotation (90° about Y): the hips residual must be
        // expressed in the parent's frame via the inverse rotation, so the vertical bob
        // stays vertical in WORLD space instead of leaking into the ground plane.
        var rootRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("root", null, new XForm(Vector3.Zero, rootRot)),
            new BoneDefinition("hips", "root", new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
        });
        int root = skeleton.IndexOf("root"), hips = skeleton.IndexOf("hips");
        const int n = 31;
        var frames = MakeWalkFrames(skeleton, hips, n);
        var axes = new RootMotionAxes
        {
            Up = Vector3.UnitY,
            RootIndex = root,
            HipsIndex = hips,
            HipsParentIsRoot = true,
        };

        RootMotion.Apply(frames, skeleton, axes, RootMotionMode.Extract);

        for (int i = 0; i < n; i++)
        {
            var world = new Pose(frames[i]).ToWorld(skeleton);

            // World reproduction is exact: vertical bob stays on the world up axis and no
            // forward/lateral jitter is introduced by the rotated parent frame.
            float miss = Vector3.Distance(world[hips].Pos, WalkWorld(i, n));
            Assert.True(miss < 1e-3f, $"frame {i}: hips world off by {miss} cm (bob leaked?)");

            // Root: on the ground plane, world rest rotation preserved.
            Assert.Equal(0f, world[root].Pos.Y, 3);
            float rotDelta = MathQ.AngleBetween(world[root].Rot, rootRot);
            Assert.True(rotDelta < 1e-3f, $"frame {i}: root world rotation disturbed by {rotDelta} rad");
        }
    }

    [Fact]
    public void SkeletonAware_InPlace_RemovesWorldTravel_UnderIntermediate()
    {
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("root", null, XForm.Identity),
            new BoneDefinition("offset", "root", new XForm(new Vector3(0f, 0f, 5f), Quaternion.Identity)),
            new BoneDefinition("hips", "offset", new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
        });
        int root = skeleton.IndexOf("root"), offset = skeleton.IndexOf("offset"), hips = skeleton.IndexOf("hips");
        const int n = 31;
        var frames = MakeWalkFrames(skeleton, hips, n);
        var axes = new RootMotionAxes
        {
            Up = Vector3.UnitY,
            RootIndex = root,
            HipsIndex = hips,
            HipsParentIsRoot = false,
        };

        RootMotion.Apply(frames, skeleton, axes, RootMotionMode.InPlace);

        foreach (var f in frames)
        {
            var world = new Pose(f).ToWorld(skeleton);
            Assert.InRange(world[hips].Pos.Z, -4f, 4f);    // forward travel removed in WORLD space
            Assert.InRange(world[hips].Pos.Y, 75f, 85f);   // vertical bob preserved
            Assert.Equal(Vector3.Zero, world[root].Pos);   // root untouched
            Assert.Equal(new Vector3(0f, 0f, 5f), world[offset].Pos); // intermediate untouched
        }
    }
}
