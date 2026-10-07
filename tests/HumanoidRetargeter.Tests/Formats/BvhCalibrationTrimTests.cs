using System.Globalization;
using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Formats.Bvh;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

/// <summary>
/// The BVH importer's calibration rest-frame trim (user report: 47_01.bvh — "starts either
/// ends or starts at t-pose … when i move the preview slider to 0 then there's a t-pose").
/// Mocap conversions (CMU asf/amc exports among them) prepend a skeleton-calibration
/// segment: the rest pose itself, hard-cut — or blend-ramped over 2–3 frames
/// (makehuman-retarget exports) — into the real motion. Measured on the repro corpus:
/// calibration frames/ramps sit ≤ 24° (max joint rotation) from the identity-rotation bind
/// with a 25–177° discontinuity into the motion; every real clip edge sits ≥ 80° from the
/// bind with ≤ 8° edge continuity. The importer drops such a segment (≤ 4 frames) per clip
/// end, and ONLY when its frames are rest-like AND the clip beyond is not AND the segment's
/// exit discontinuity is large both absolutely and against the clip's median inter-frame
/// delta — a clip legitimately idling near rest must never lose its first frame.
/// </summary>
public class BvhCalibrationTrimTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    // ---------------------------------------------------------------- synthetic fixtures

    /// <summary>
    /// Two-joint rig (30 native fps = the import grid, so native frames map 1:1 onto clip
    /// frames) whose Head rotation per native frame is caller-chosen. Offsets keep the rest
    /// height above the meters heuristic threshold.
    /// </summary>
    private static byte[] BvhWithHeadAngles(params float[] headZDegPerFrame)
    {
        var sb = new StringBuilder();
        sb.AppendLine("HIERARCHY");
        sb.AppendLine("ROOT Hips");
        sb.AppendLine("{");
        sb.AppendLine("    OFFSET 0 0 0");
        sb.AppendLine("    CHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation");
        sb.AppendLine("    JOINT Head");
        sb.AppendLine("    {");
        sb.AppendLine("        OFFSET 0 70 0");
        sb.AppendLine("        CHANNELS 3 Zrotation Yrotation Xrotation");
        sb.AppendLine("        End Site");
        sb.AppendLine("        {");
        sb.AppendLine("            OFFSET 0 10 0");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("MOTION");
        sb.AppendLine($"Frames: {headZDegPerFrame.Length}");
        sb.AppendLine("Frame Time: 0.0333333");
        foreach (var z in headZDegPerFrame)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture, $"0 90 0 0 0 0 {z} 0 0"));
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static float HeadAngleDeg(SourceScene scene, int frame)
    {
        var head = scene.Skeleton.IndexOf("Head");
        return MathQ.AngleBetween(
            Quaternion.Identity, scene.Clips[0].Frames[frame][head].Rot) * Rad2Deg;
    }

    /// <summary>A held pose at 70° with ~1°/frame drift; interior deltas ≈ 1°.</summary>
    private static float[] HeldPose(int frames, float startDeg = 70f)
    {
        var values = new float[frames];
        for (var f = 0; f < frames; f++)
            values[f] = startDeg + f;
        return values;
    }

    // ---------------------------------------------------------------- trims

    [Fact]
    public void LeadingCalibrationRestFrame_IsTrimmed()
    {
        var angles = new float[10];
        HeldPose(9).CopyTo(angles, 1); // frame 0 stays 0 = the rest pose; 1..9 held at ~70°
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(9, clip.FrameCount); // 10 native − 1 calibration frame
        Assert.InRange(HeadAngleDeg(scene, 0), 65f, 75f); // clip now opens on the real pose
    }

    [Fact]
    public void TrailingCalibrationRestFrame_IsTrimmed()
    {
        var angles = new float[10];
        HeldPose(9).CopyTo(angles, 0); // frames 0..8 held at ~70°; frame 9 back to rest
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(9, clip.FrameCount);
        Assert.InRange(HeadAngleDeg(scene, clip.FrameCount - 1), 65f, 85f); // ends on the pose
    }

    [Fact]
    public void CalibrationFramesAtBothEnds_AreBothTrimmed()
    {
        var angles = new float[11];
        HeldPose(9).CopyTo(angles, 1); // frame 0 AND frame 10 at rest, 1..9 held
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(9, clip.FrameCount);
        Assert.InRange(HeadAngleDeg(scene, 0), 65f, 75f);
        Assert.InRange(HeadAngleDeg(scene, clip.FrameCount - 1), 65f, 85f);
    }

    /// <summary>A 2-frame rest→motion blend ramp (measured on a makehuman-retarget export:
    /// rest 10°, ramp 24°, then motion ≥ 49°) is a calibration segment too.</summary>
    [Fact]
    public void CalibrationBlendRamp_IsTrimmed()
    {
        var angles = new float[12];
        angles[1] = 12f; // frame 0 at rest, frame 1 mid-blend — both rest-like
        HeldPose(10).CopyTo(angles, 2); // frames 2..11 held at ~70°
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(10, clip.FrameCount); // both ramp frames trimmed
        Assert.InRange(HeadAngleDeg(scene, 0), 65f, 75f);
    }

    // ---------------------------------------------------------------- must-NOT-trim guards

    /// <summary>An idle that legitimately STARTS at the rest pose ramps continuously into
    /// motion — rest-similarity alone must never cost it its first frame.</summary>
    [Fact]
    public void IdleStartingAtRest_KeepsItsFirstFrame()
    {
        var angles = new float[20];
        for (var f = 0; f < angles.Length; f++)
            angles[f] = f * 3f; // 3°/frame ramp from exact rest
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(20, clip.FrameCount);
        Assert.InRange(HeadAngleDeg(scene, 0), 0f, 0.01f);
    }

    /// <summary>A clip resting throughout (e.g. a bind-pose export) has nothing to trim:
    /// the neighbor frame is rest-like too.</summary>
    [Fact]
    public void AllRestClip_IsNotTrimmed()
    {
        var scene = BvhImporter.Import(BvhWithHeadAngles(new float[8]));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(8, clip.FrameCount);
    }

    /// <summary>A hard cut between two NON-rest poses (action edit) is not a calibration
    /// frame — the edge must be rest-like for the trim to engage.</summary>
    [Fact]
    public void HardCutBetweenRealPoses_IsNotTrimmed()
    {
        var angles = HeldPose(10, startDeg: 120f);
        for (var f = 1; f < angles.Length; f++)
            angles[f] = 60f + f; // frame 0 at 120°, cut to ~60° held
        var scene = BvhImporter.Import(BvhWithHeadAngles(angles));

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(10, clip.FrameCount);
    }

    /// <summary>Two-frame clips are never trimmed (no interior to judge typical motion by).</summary>
    [Fact]
    public void TwoFrameClip_IsNotTrimmed()
    {
        var scene = BvhImporter.Import(BvhWithHeadAngles(0f, 70f));
        Assert.Equal(2, Assert.Single(scene.Clips).FrameCount);
    }

    // ---------------------------------------------------------------- real repro file

    /// <summary>47_01.bvh (CMU mocap, the user repro): native frame 0 is a calibration
    /// T-pose 21° from the bind with an 89.6° cut into frame 1 (measured); the imported
    /// clip must open on the real motion instead. Silently green when the local corpus
    /// file is absent.</summary>
    [Fact]
    public void Cmu4701_CalibrationTPose_DoesNotReachTheClip()
    {
        var path = TestUtil.RepoFile("dev", "corpus", "todo", "47_01.bvh");
        if (!File.Exists(path))
            return;

        var scene = BvhImporter.Import(File.ReadAllBytes(path));
        var clip = Assert.Single(scene.Clips);

        // 1320 native frames at 120 fps → 1319 after the trim → 330 on the 30 fps grid.
        Assert.Equal(330, clip.FrameCount);

        // Frame 0 is now a real pose, far from the identity-rotation bind …
        float MaxJointAngleDeg(int frame)
        {
            var max = 0f;
            foreach (var x in clip.Frames[frame])
                max = MathF.Max(max, MathQ.AngleBetween(Quaternion.Identity, x.Rot) * Rad2Deg);
            return max;
        }
        Assert.True(MaxJointAngleDeg(0) > 60f,
            $"clip still opens rest-like ({MaxJointAngleDeg(0):F1}° from bind) — calibration frame not trimmed");

        // … and continuous into its neighbor (the old frame 0 cut 89.6° deep).
        var maxDelta = 0f;
        for (var i = 0; i < scene.Skeleton.Count; i++)
        {
            maxDelta = MathF.Max(maxDelta, MathQ.AngleBetween(
                clip.Frames[0][i].Rot, clip.Frames[1][i].Rot) * Rad2Deg);
        }
        Assert.True(maxDelta < 30f, $"frame 0→1 still discontinuous ({maxDelta:F1}°)");
    }
}
