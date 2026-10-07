using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// G8 regeneration ground-alignment fix (southpaw project, gate4_review.md 3.1 /
/// DEFERRED.md f2): after solving, every clip receives ONE constant vertical offset that
/// restores the source-authored foot-to-ground relationship (target support = target rest
/// support + hip-scaled source support delta). These tests lock the contract: solved
/// support sits ON the target rest support line for grounded sources, and within-clip
/// articulation (step lifts, heel raises) is preserved exactly (a constant offset can
/// never flatten it).
/// </summary>
public class SupportGroundAlignTests
{
    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static RetargetRequest WalkRequest() => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        CreateMirroredVariant = false,
    };

    private static (float MinCombined, float RestCombined, float MinAnkle, float RestAnkle) Measure(
        RetargetTargetSpec target, IReadOnlyList<HumanoidRetargeter.Core.Maths.XForm[]> frames)
    {
        var rig = target.Rig;
        var skeleton = rig.Skeleton;
        var ankleL = rig.BoneForRole(BoneRole.FootL)!.Value;
        var ankleR = rig.BoneForRole(BoneRole.FootR)!.Value;
        var support = new List<int> { ankleL, ankleR };
        if (rig.BoneForRole(BoneRole.ToeL) is { } toeL)
            support.Add(toeL);
        if (rig.BoneForRole(BoneRole.ToeR) is { } toeR)
            support.Add(toeR);

        var restCombined = float.MaxValue;
        foreach (var b in support)
            restCombined = MathF.Min(restCombined, skeleton.RestWorld[b].Pos.Y);
        var restAnkle = MathF.Min(
            skeleton.RestWorld[ankleL].Pos.Y, skeleton.RestWorld[ankleR].Pos.Y);

        var minCombined = float.MaxValue;
        var minAnkle = float.MaxValue;
        foreach (var frame in frames)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            foreach (var b in support)
                minCombined = MathF.Min(minCombined, world[b].Pos.Y);
            minAnkle = MathF.Min(minAnkle,
                MathF.Min(world[ankleL].Pos.Y, world[ankleR].Pos.Y));
        }
        return (minCombined, restCombined, minAnkle, restAnkle);
    }

    [Fact]
    public void Convert_GroundedWalk_SupportSitsOnRestLinePlusAuthoredGap()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        // The contract (DEFERRED.md f2): solved support = target rest support + the
        // SOURCE-AUTHORED support gap (the fixture's rigid-leg swings dip the toe
        // through its own floor line, so its authored gap is small but non-zero; the
        // alignment must preserve it, never invent flat grounding). The pass reports
        // the gap it measured in its mapping note; the solved support must land on
        // rest + gap exactly.
        var note = clip.Mapping.Notes.FirstOrDefault(
            n => n.StartsWith("Support ground alignment"));
        var sourceGap = 0f;
        if (note is not null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                note, @"support delta (-?\d+(\.\d+)?)");
            if (m.Success)
                sourceGap = float.Parse(m.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        // A grounded walk's authored gap is small (the fixture's through-floor swing
        // dip); a large value would mean the measurement regressed.
        Assert.True(MathF.Abs(sourceGap) < 4f,
            $"authored support gap {sourceGap:F2} cm is implausible for a grounded walk");

        var (minCombined, restCombined, minAnkle, restAnkle)
            = Measure(SboxTarget.Value, clip.SolvedFrames!);
        var alignedCombined = MathF.Abs(minCombined - (restCombined + sourceGap)) <= 1.0f;
        var alignedAnkle = MathF.Abs(minAnkle - (restAnkle + sourceGap)) <= 1.0f;
        Assert.True(alignedCombined || alignedAnkle,
            $"solved support combined {minCombined:F2} vs rest {restCombined:F2} + gap "
            + $"{sourceGap:F2}, ankle {minAnkle:F2} vs rest {restAnkle:F2} + gap (cm) - "
            + "the ground alignment must put the support on the rest line plus the "
            + "source-authored gap");
    }

    [Fact]
    public void Convert_GroundedWalk_AuthoredStepLiftIsPreserved()
    {
        // A constant per-clip offset can never flatten articulation: the walk's swing-leg
        // lift must survive the alignment (the ankle still travels well above its plant).
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        var rig = SboxTarget.Value.Rig;
        var skeleton = rig.Skeleton;
        var ankleL = rig.BoneForRole(BoneRole.FootL)!.Value;
        var ankleR = rig.BoneForRole(BoneRole.FootR)!.Value;
        float minL = float.MaxValue, maxL = float.MinValue;
        float minR = float.MaxValue, maxR = float.MinValue;
        foreach (var frame in clip.SolvedFrames!)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            minL = MathF.Min(minL, world[ankleL].Pos.Y);
            maxL = MathF.Max(maxL, world[ankleL].Pos.Y);
            minR = MathF.Min(minR, world[ankleR].Pos.Y);
            maxR = MathF.Max(maxR, world[ankleR].Pos.Y);
        }
        Assert.True(MathF.Max(maxL - minL, maxR - minR) > 2.0f,
            $"ankle excursion L {maxL - minL:F2} / R {maxR - minR:F2} cm - the authored "
            + "step lift must survive the ground alignment");
    }

    [Fact]
    public void Convert_ReportsAppliedSupportAlignment()
    {
        // The alignment documents itself: when it moves a clip it writes a mapping note
        // (per-clip provenance for the regeneration's ground-alignment evidence).
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        var (minCombined, restCombined, minAnkle, restAnkle)
            = Measure(SboxTarget.Value, clip.SolvedFrames!);
        // Either the clip solved already aligned (no note needed) or a note exists.
        var noted = clip.Mapping.Notes.Any(n => n.StartsWith("Support ground alignment"));
        var aligned = MathF.Abs(minCombined - restCombined) <= 1.0f
            || MathF.Abs(minAnkle - restAnkle) <= 1.0f;
        Assert.True(noted || aligned,
            "the alignment neither ran (no note) nor was the clip already aligned");
    }
}
