using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Cleanup;

public class FootPlantTests
{
    private const float Fps = 30f;
    private const float Rad2Deg = 180f / MathF.PI;

    // Truth for the synthetic gait below: the left foot is planted on frames 10–25.
    private const int TruthStart = 10;
    private const int TruthEnd = 25;
    private const int FrameCount = 40;
    private static readonly Vector3 PlantPos = new(8f, 0.5f, 10f);

    // ---------------------------------------------------------------- fixture

    /// <summary>Two-leg skeleton: pelvis + L/R hip/knee/ankle/toe (Y up, Z forward, cm).</summary>
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

    private static FootChain LeftChain(SkeletonModel s) => new()
    {
        Hip = s.IndexOf("hip_L"),
        Knee = s.IndexOf("knee_L"),
        Ankle = s.IndexOf("ankle_L"),
        Toe = s.IndexOf("toe_L"),
    };

    private static FootChain RightChain(SkeletonModel s) => new()
    {
        Hip = s.IndexOf("hip_R"),
        Knee = s.IndexOf("knee_R"),
        Ankle = s.IndexOf("ankle_R"),
        Toe = s.IndexOf("toe_R"),
    };

    /// <summary>
    /// Left (stance) ankle truth: descend (0–9), planted at PlantPos for frames 10–25 with an
    /// injected 1.5 cm sliding drift plus ~0.5 cm smooth noise, then lift away (26–39).
    /// </summary>
    private static Vector3 LeftAnkleTruth(int i)
    {
        if (i < TruthStart)
        {
            float t = i / (float)TruthStart;
            return Vector3.Lerp(new Vector3(8f, 6f, 3f), PlantPos, t);
        }
        if (i <= TruthEnd)
        {
            float drift = 1.5f * (i - TruthStart) / (float)(TruthEnd - TruthStart);
            return PlantPos + new Vector3(
                0.5f * MathF.Sin(0.3f * i),       // horizontal noise
                0.2f * MathF.Sin(0.25f * i + 1f), // vertical noise
                drift);                            // the footskate being fixed
        }
        float lift = (i - TruthEnd) / (float)(FrameCount - 1 - TruthEnd);
        return LeftAnkleTruth(TruthEnd) + new Vector3(0f, 8f, 10f) * lift;
    }

    /// <summary>Right (swing) ankle truth: always moving, always ≥ ~8 cm above ground.</summary>
    private static Vector3 RightAnkleTruth(int i)
    {
        float t = i / (float)(FrameCount - 1);
        return new Vector3(-8f, 10f + 3f * MathF.Sin(t * MathF.Tau), -5f + 30f * t);
    }

    /// <summary>
    /// Builds the parametric gait: pelvis travels forward with a small bob; ankle world
    /// trajectories are baked into consistent FK locals via the (separately tested)
    /// effector-IK pass.
    /// </summary>
    private static (SkeletonModel skeleton, List<XForm[]> frames) MakeGait(
        Func<int, Vector3>? leftTruth = null,
        Func<int, Vector3>? rightTruth = null,
        Func<int, Vector3>? pelvisPos = null)
    {
        leftTruth ??= LeftAnkleTruth;
        rightTruth ??= RightAnkleTruth;
        pelvisPos ??= i => new Vector3(
            0f, 72f + 0.5f * MathF.Sin(i * 0.4f), 4f + 14f * i / (FrameCount - 1));
        var skeleton = BuildSkeleton();
        var frames = new List<XForm[]>();
        var goalsL = new List<Vector3>();
        var goalsR = new List<Vector3>();
        for (int i = 0; i < FrameCount; i++)
        {
            var locals = Pose.Rest(skeleton).Locals;
            locals[0] = new XForm(pelvisPos(i), Quaternion.Identity);
            frames.Add(locals);
            goalsL.Add(leftTruth(i));
            goalsR.Add(rightTruth(i));
        }
        EffectorIk.ApplyGoals(frames, skeleton, ToLimb(LeftChain(skeleton)), goalsL, Vector3.UnitX, soften: 0f);
        EffectorIk.ApplyGoals(frames, skeleton, ToLimb(RightChain(skeleton)), goalsR, Vector3.UnitX, soften: 0f);
        return (skeleton, frames);
    }

    private static LimbChain ToLimb(FootChain c) => new() { Upper = c.Hip, Lower = c.Knee, End = c.Ankle };

    private static Vector3[] AnkleTrack(List<XForm[]> frames, SkeletonModel skeleton, int ankle)
    {
        var track = new Vector3[frames.Count];
        for (int i = 0; i < frames.Count; i++)
            track[i] = new Pose(frames[i]).ToWorld(skeleton)[ankle].Pos;
        return track;
    }

    private static List<XForm[]> DeepCopy(List<XForm[]> frames)
    {
        var copy = new List<XForm[]>(frames.Count);
        foreach (var f in frames)
            copy.Add((XForm[])f.Clone());
        return copy;
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public void SyntheticWalk_PlantDetectedNearTruth()
    {
        var (skeleton, frames) = MakeGait();
        var report = FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps);

        var plant = Assert.Single(report.Left.Plants);
        Assert.InRange(plant.Start, TruthStart - 2, TruthStart + 2);
        Assert.InRange(plant.End, TruthEnd - 2, TruthEnd + 2);
        Assert.Empty(report.Right.Plants);
    }

    [Fact]
    public void SyntheticWalk_SlidingRemoved_InsidePlant()
    {
        var (skeleton, frames) = MakeGait();
        var leftAnkleIdx = LeftChain(skeleton).Ankle;
        var report = FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps);

        // Inside the truth interval (shrunk by the ±2-frame detection tolerance) the foot
        // must not move more than 0.3 cm in total.
        var track = AnkleTrack(frames, skeleton, leftAnkleIdx);
        float maxDisplacement = 0f;
        for (int i = TruthStart + 2; i <= TruthEnd - 2; i++)
            for (int j = i + 1; j <= TruthEnd - 2; j++)
                maxDisplacement = MathF.Max(maxDisplacement, Vector3.Distance(track[i], track[j]));
        Assert.True(maxDisplacement <= 0.3f, $"planted foot still moved {maxDisplacement} cm");

        // Report agrees: residual slide within the detected plant is ~0.
        Assert.True(report.Left.ResidualSlideCm <= 0.05f,
            $"residual slide {report.Left.ResidualSlideCm} cm");

        // The pass actually did something: it had ~1.5 cm of drift to absorb.
        Assert.True(report.Left.MaxCorrectionCm >= 0.3f, $"correction {report.Left.MaxCorrectionCm} cm");
        Assert.True(report.Left.MaxCorrectionDeg > 0f);
    }

    [Fact]
    public void SyntheticWalk_VelocityContinuous_AtBlendBoundaries()
    {
        var (skeleton, frames) = MakeGait();
        var leftAnkleIdx = LeftChain(skeleton).Ankle;
        var before = AnkleTrack(frames, skeleton, leftAnkleIdx);

        FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps);

        var after = AnkleTrack(frames, skeleton, leftAnkleIdx);
        for (int i = 1; i < FrameCount; i++)
        {
            float corrected = Vector3.Distance(after[i], after[i - 1]);
            float neighborhood = 0f;
            for (int k = Math.Max(1, i - 1); k <= Math.Min(FrameCount - 1, i + 1); k++)
                neighborhood = MathF.Max(neighborhood, Vector3.Distance(before[k], before[k - 1]));
            Assert.True(corrected <= 2f * neighborhood + 0.05f,
                $"pop at frame {i}: corrected delta {corrected} vs uncorrected neighborhood {neighborhood}");
        }
    }

    [Fact]
    public void SyntheticWalk_SwingFootUntouched()
    {
        var (skeleton, frames) = MakeGait();
        var rightAnkleIdx = RightChain(skeleton).Ankle;
        var before = AnkleTrack(frames, skeleton, rightAnkleIdx);
        var beforeLocals = DeepCopy(frames);

        FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps);

        var after = AnkleTrack(frames, skeleton, rightAnkleIdx);
        var right = RightChain(skeleton);
        for (int i = 0; i < FrameCount; i++)
        {
            Assert.True(Vector3.Distance(before[i], after[i]) <= 0.01f, $"swing foot moved at frame {i}");
            Assert.Equal(beforeLocals[i][right.Hip], frames[i][right.Hip]);
            Assert.Equal(beforeLocals[i][right.Knee], frames[i][right.Knee]);
            Assert.Equal(beforeLocals[i][right.Ankle], frames[i][right.Ankle]);
        }
    }

    [Fact]
    public void SyntheticWalk_StretchNeverExceedsMaxStretch()
    {
        var (skeleton, frames) = MakeGait();
        var options = new FootPlantOptions();
        FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps, options);

        var left = LeftChain(skeleton);
        float restThigh = skeleton[left.Knee].RestLocal.Pos.Length();
        float restShin = skeleton[left.Ankle].RestLocal.Pos.Length();
        float limit = 1f + options.MaxStretch + 1e-4f;
        foreach (var f in frames)
        {
            Assert.True(f[left.Knee].Pos.Length() <= restThigh * limit, "thigh over-stretched");
            Assert.True(f[left.Ankle].Pos.Length() <= restShin * limit, "shin over-stretched");
        }
    }

    [Fact]
    public void StretchEngages_WhenAnchorBeyondReach_AndStaysBounded()
    {
        // Both feet planted; the pelvis rises mid-clip until the left anchor is ~1 cm beyond
        // full leg extension (75 cm), so the input foot hovers/slides — the pass must absorb
        // it with a per-frame segment stretch within MaxStretch and keep the foot pinned.
        var (skeleton, frames) = MakeGait(
            leftTruth: _ => PlantPos,
            rightTruth: RightAnkleTruth,
            pelvisPos: i =>
            {
                float s = MathF.Sin(MathF.PI * i / (FrameCount - 1));
                return new Vector3(0f, 72f + 4.5f * s * s, 10f);
            });
        var left = LeftChain(skeleton);
        var options = new FootPlantOptions();

        var report = FootPlant.Apply(frames, skeleton, left, RightChain(skeleton), Vector3.UnitY, Fps, options);

        var plant = Assert.Single(report.Left.Plants);

        // Foot pinned despite the over-reach: no residual slide inside the plant.
        var track = AnkleTrack(frames, skeleton, left.Ankle);
        float maxDisplacement = 0f;
        for (int i = plant.Start; i < plant.End; i++)
            maxDisplacement = MathF.Max(maxDisplacement, Vector3.Distance(track[i + 1], track[i]));
        Assert.True(maxDisplacement <= 0.05f, $"foot slid {maxDisplacement} cm despite stretch");

        // Stretch actually engaged at the pelvis peak, and never beyond MaxStretch.
        float restThigh = skeleton[left.Knee].RestLocal.Pos.Length();
        float peakThigh = 0f;
        foreach (var f in frames)
        {
            float thigh = f[left.Knee].Pos.Length();
            peakThigh = MathF.Max(peakThigh, thigh);
            Assert.True(thigh <= restThigh * (1f + options.MaxStretch + 1e-4f), "thigh over-stretched");
        }
        Assert.True(peakThigh >= restThigh * 1.003f,
            $"stretch never engaged (peak thigh {peakThigh} vs rest {restThigh})");
    }

    [Fact]
    public void PerfectlyPlantedFoot_IsNearNoOp()
    {
        // Both feet pinned to constant world positions for the whole clip (zero drift).
        var (skeleton, frames) = MakeGait(
            leftTruth: _ => PlantPos,
            rightTruth: _ => new Vector3(-8f, 0.5f, 6f));
        var beforeLocals = DeepCopy(frames);
        var left = LeftChain(skeleton);
        var beforeTrack = AnkleTrack(frames, skeleton, left.Ankle);

        var report = FootPlant.Apply(frames, skeleton, left, RightChain(skeleton), Vector3.UnitY, Fps);

        Assert.NotEmpty(report.Left.Plants); // it IS planted — but already consistent
        var afterTrack = AnkleTrack(frames, skeleton, left.Ankle);
        for (int i = 0; i < FrameCount; i++)
        {
            Assert.True(Vector3.Distance(beforeTrack[i], afterTrack[i]) <= 0.05f,
                $"no-op pass moved a consistent foot {Vector3.Distance(beforeTrack[i], afterTrack[i])} cm at frame {i}");
            for (int b = 0; b < skeleton.Count; b++)
            {
                float deg = MathQ.AngleBetween(beforeLocals[i][b].Rot, frames[i][b].Rot) * Rad2Deg;
                Assert.True(deg <= 0.1f, $"frame {i} bone {skeleton[b].Name} rotated {deg}°");
            }
        }
        Assert.True(report.Left.MaxCorrectionCm <= 0.05f);
        Assert.True(report.Left.MaxCorrectionDeg <= 0.1f);
    }

    [Fact]
    public void ClipShorterThanMinPlantFrames_NoPlants_Untouched()
    {
        var skeleton = BuildSkeleton();
        var frames = new List<XForm[]> { Pose.Rest(skeleton).Locals, Pose.Rest(skeleton).Locals };
        var beforeLocals = DeepCopy(frames);

        var report = FootPlant.Apply(frames, skeleton, LeftChain(skeleton), RightChain(skeleton), Vector3.UnitY, Fps);

        Assert.Empty(report.Left.Plants);
        Assert.Empty(report.Right.Plants);
        for (int i = 0; i < frames.Count; i++)
            for (int b = 0; b < skeleton.Count; b++)
                Assert.Equal(beforeLocals[i][b], frames[i][b]);
    }
}
