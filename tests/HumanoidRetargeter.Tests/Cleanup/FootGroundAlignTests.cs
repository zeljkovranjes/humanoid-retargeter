using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Cleanup;

/// <summary>
/// <see cref="FootGroundAlign"/> — grounded-foot stance recalibration: a planted sole offset
/// beyond the dead zone is a rest artifact and is removed by ONE constant on every frame;
/// offsets inside the dead zone and non-stance plants (crawls) leave the frames untouched.
/// </summary>
public class FootGroundAlignTests
{
    private const int FrameCount = 40;
    private static readonly FrameRange[] Plants = { new(10, 25) };
    private const float Rad2Deg = 180f / MathF.PI;

    // ---------------------------------------------------------------- fixture

    /// <summary>Two-leg skeleton (Y up, Z forward, cm): rest soles are ground-flat by
    /// construction (the same shape as the FootPlant fixture).</summary>
    private static SkeletonModel BuildSkeleton() => SkeletonModel.Create(new[]
    {
        new BoneDefinition("pelvis", null, new XForm(new Vector3(0f, 76f, 0f), Quaternion.Identity)),
        new BoneDefinition("hip_L", "pelvis", new XForm(new Vector3(8f, 0f, 0f), Quaternion.Identity)),
        new BoneDefinition("knee_L", "hip_L", new XForm(new Vector3(0f, -38f, 0f), Quaternion.Identity)),
        new BoneDefinition("ankle_L", "knee_L", new XForm(new Vector3(0f, -37f, 0f), Quaternion.Identity)),
        new BoneDefinition("toe_L", "ankle_L", new XForm(new Vector3(0f, -0.5f, 12f), Quaternion.Identity)),
        new BoneDefinition("hip_R", "pelvis", new XForm(new Vector3(-8f, 0f, 0f), Quaternion.Identity)),
        new BoneDefinition("knee_R", "hip_R", new XForm(new Vector3(0f, -38f, 0f), Quaternion.Identity)),
        new BoneDefinition("ankle_R", "knee_R", new XForm(new Vector3(0f, -37f, 0f), Quaternion.Identity)),
        new BoneDefinition("toe_R", "ankle_R", new XForm(new Vector3(0f, -0.5f, 12f), Quaternion.Identity)),
    });

    private static FootChain Chain(SkeletonModel s, string side) => new()
    {
        Hip = s.IndexOf($"hip_{side}"),
        Knee = s.IndexOf($"knee_{side}"),
        Ankle = s.IndexOf($"ankle_{side}"),
        Toe = s.IndexOf($"toe_{side}"),
    };

    /// <summary>Rest-pose frames with the LEFT ankle (and optionally its toe) locally
    /// pitched about lateral X on every frame — a constant rest-artifact offset.</summary>
    private static List<XForm[]> MakeFrames(
        SkeletonModel skeleton, float footPitchDeg, float toeExtraPitchDeg = 0f)
    {
        var ankle = skeleton.IndexOf("ankle_L");
        var toe = skeleton.IndexOf("toe_L");
        var frames = new List<XForm[]>(FrameCount);
        for (int i = 0; i < FrameCount; i++)
        {
            var locals = Pose.Rest(skeleton).Locals;
            locals[ankle] = new XForm(locals[ankle].Pos,
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, footPitchDeg / Rad2Deg));
            if (toeExtraPitchDeg != 0f)
            {
                locals[toe] = new XForm(locals[toe].Pos,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, toeExtraPitchDeg / Rad2Deg));
            }
            frames.Add(locals);
        }
        return frames;
    }

    private static FootGroundAlignReport Run(SkeletonModel skeleton, List<XForm[]> frames)
        => FootGroundAlign.Apply(
            frames, skeleton, Chain(skeleton, "L"), Chain(skeleton, "R"), Vector3.UnitY,
            Plants, Array.Empty<FrameRange>());

    private static float WorldRotDeg(
        SkeletonModel skeleton, XForm[] locals, string bone, Quaternion expectedWorldRot)
    {
        var world = new Pose(locals).ToWorld(skeleton)[skeleton.IndexOf(bone)].Rot;
        return MathQ.AngleBetween(world, expectedWorldRot) * Rad2Deg;
    }

    // ---------------------------------------------------------------- recalibration

    [Fact]
    public void RestArtifactOffset_IsRemovedOnEveryFrame_AnklePositionsUntouched()
    {
        var skeleton = BuildSkeleton();
        var frames = MakeFrames(skeleton, footPitchDeg: 15f);
        var ankle = skeleton.IndexOf("ankle_L");
        var anklePosBefore = new Pose(frames[0]).ToWorld(skeleton)[ankle].Pos;

        var report = Run(skeleton, frames);

        Assert.Equal(1, report.Left.StancePlants);
        Assert.InRange(report.Left.MeasuredOffsetDeg, 13f, 17f);
        Assert.InRange(report.Left.AppliedFootDeg, 13f, 17f);

        var restFoot = skeleton.RestWorld[ankle].Rot;
        var restToe = skeleton.RestWorld[skeleton.IndexOf("toe_L")].Rot;
        for (int f = 0; f < FrameCount; f++) // EVERY frame, not just the plant
        {
            Assert.True(WorldRotDeg(skeleton, frames[f], "ankle_L", restFoot) <= 0.5f,
                $"foot not recalibrated at frame {f}");
            // The toe rode the foot's artifact, so the foot fix levels it too — and the
            // toe must NOT double-rotate (its residual is inside the dead zone).
            Assert.True(WorldRotDeg(skeleton, frames[f], "toe_L", restToe) <= 0.5f,
                $"toe double-rotated at frame {f}");
        }
        Assert.Equal(0f, report.Left.AppliedToeDeg);

        var anklePosAfter = new Pose(frames[0]).ToWorld(skeleton)[ankle].Pos;
        Assert.True(Vector3.Distance(anklePosBefore, anklePosAfter) <= 1e-4f,
            "the recalibration must rotate the foot about its own joint");

        // The right foot had no plants and no offset: untouched.
        Assert.Equal(0f, report.Right.AppliedFootDeg);
    }

    [Fact]
    public void ToeOwnArtifact_GetsItsOwnResidualConstant()
    {
        var skeleton = BuildSkeleton();
        // Foot pitched 15°, toe locally counter-pitched -27° (net toe world -12°): once the
        // foot is leveled the toe sits at its full -27° local artifact and needs its own
        // residual constant.
        var frames = MakeFrames(skeleton, footPitchDeg: 15f, toeExtraPitchDeg: -27f);

        var report = Run(skeleton, frames);

        Assert.InRange(report.Left.AppliedFootDeg, 13f, 17f);
        Assert.InRange(report.Left.AppliedToeDeg, 25f, 29f);

        var restToe = skeleton.RestWorld[skeleton.IndexOf("toe_L")].Rot;
        for (int f = 0; f < FrameCount; f++)
        {
            Assert.True(WorldRotDeg(skeleton, frames[f], "toe_L", restToe) <= 0.5f,
                $"toe residual not recalibrated at frame {f}");
        }
    }

    // ---------------------------------------------------------------- dead zone / non-stance

    [Fact]
    public void OffsetInsideDeadZone_LeavesFramesByteIdentical()
    {
        var skeleton = BuildSkeleton();
        var frames = MakeFrames(skeleton, footPitchDeg: 4f);
        var before = frames.ConvertAll(f => (XForm[])f.Clone());

        var report = Run(skeleton, frames);

        Assert.InRange(report.Left.MeasuredOffsetDeg, 3f, 5f);
        Assert.Equal(0f, report.Left.AppliedFootDeg);
        for (int f = 0; f < FrameCount; f++)
            Assert.Equal(before[f], frames[f]);
    }

    [Fact]
    public void NonStancePlant_SoleFarOffGround_IsSkippedWhole()
    {
        var skeleton = BuildSkeleton();
        // Crawl-like contact: the foot is pitched 60° — the ankle may be low and slow, but
        // the character is not standing on the sole. Flattening it would wreck the pose.
        var frames = MakeFrames(skeleton, footPitchDeg: 60f);
        var before = frames.ConvertAll(f => (XForm[])f.Clone());

        var report = Run(skeleton, frames);

        Assert.Equal(0, report.Left.StancePlants);
        Assert.Equal(1, report.Left.SkippedPlants);
        Assert.Equal(0f, report.Left.AppliedFootDeg);
        for (int f = 0; f < FrameCount; f++)
            Assert.Equal(before[f], frames[f]);
    }

    [Fact]
    public void NoPlants_DoesNothing()
    {
        var skeleton = BuildSkeleton();
        var frames = MakeFrames(skeleton, footPitchDeg: 20f);
        var before = frames.ConvertAll(f => (XForm[])f.Clone());

        var report = FootGroundAlign.Apply(
            frames, skeleton, Chain(skeleton, "L"), Chain(skeleton, "R"), Vector3.UnitY,
            Array.Empty<FrameRange>(), Array.Empty<FrameRange>());

        Assert.Equal(0f, report.Left.MeasuredOffsetDeg);
        for (int f = 0; f < FrameCount; f++)
            Assert.Equal(before[f], frames[f]);
    }
}
