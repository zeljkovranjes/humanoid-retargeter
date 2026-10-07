using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Local-only regression gates over EVERY .bvh in the dev corpus (<c>dev/corpus/**</c>, not
/// committed — the whole suite is silently green when the corpus is absent), pinning the two
/// user-reported BVH quality bugs so they can never come back for ANY corpus file:
/// <list type="bullet">
/// <item><b>No rest-pose flash at frame 0</b> (47_01.bvh: "when i move the preview slider to
/// 0 then there's a t-pose") — the solved clip's frame 0→1 discontinuity must be in line with
/// the clip's own inter-frame deltas, not a calibration-frame cut (measured 166° on the
/// repro before the importer's trim, ≤ 8° typical).</item>
/// <item><b>No skywalking / off-grid start</b> (Armchair1.bvh: "the model is in the air …
/// outside of the modeldoc grid") — the solved pelvis must START over the target origin and
/// the feet must reach the target's ground somewhere in the clip (measured 92 cm of hover at
/// (289, 68) horizontal before the placement normalization).</item>
/// </list>
/// </summary>
public class BvhCorpusQualityGateTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    public static TheoryData<string> CorpusBvhFiles()
    {
        var data = new TheoryData<string>();
        string root;
        try
        {
            root = TestUtil.RepoFile("dev", "corpus");
        }
        catch (InvalidOperationException)
        {
            data.Add(""); // theory data must be non-empty; the test skips "" silently
            return data;
        }
        if (!Directory.Exists(root))
        {
            data.Add("");
            return data;
        }
        foreach (var path in Directory.EnumerateFiles(root, "*.bvh", SearchOption.AllDirectories))
            data.Add(Path.GetRelativePath(root, path));
        return data;
    }

    [Theory] // skipped (silently green) when the local corpus is not present
    [MemberData(nameof(CorpusBvhFiles))]
    public void SolvedClip_HasNoRestFlashAtFrame0_AndStartsGroundedOverOrigin(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return;
        var path = Path.Combine(TestUtil.RepoFile("dev", "corpus"), relativePath);
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var target = RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = Path.GetFileName(path),
        }, target);
        var clip = result.Clips.FirstOrDefault(c => c.Success);
        Assert.True(clip is not null,
            $"{relativePath}: conversion failed — {string.Join("; ", result.Errors)}");
        var frames = clip!.SolvedFrames!;
        if (frames.Count < 3)
            return; // too short to judge continuity

        var rig = target.Rig;
        var skeleton = rig.Skeleton;

        // ---- gate 1: frame 0 is not a rest/T-pose outlier against frame 1 ----------------
        // The frame 0→1 max-bone rotation delta must be comparable to the clip's own typical
        // inter-frame delta (4× median of the following pairs) with an absolute floor for
        // slow clips. A trimmed-away calibration frame measured 166°; real openings ≤ 14°.
        float MaxRotDelta(XForm[] a, XForm[] b)
        {
            var max = 0f;
            for (var i = 0; i < skeleton.Count; i++)
                max = MathF.Max(max, MathQ.AngleBetween(a[i].Rot, b[i].Rot) * Rad2Deg);
            return max;
        }
        var interior = new List<float>();
        for (var f = 1; f < Math.Min(frames.Count - 1, 31); f++)
            interior.Add(MaxRotDelta(frames[f], frames[f + 1]));
        interior.Sort();
        var typical = interior[interior.Count / 2];
        var opening = MaxRotDelta(frames[0], frames[1]);
        Assert.True(opening <= MathF.Max(25f, 4f * typical),
            $"{relativePath}: frame 0→1 rotation jump {opening:F1}° vs typical {typical:F1}° — "
            + "frame 0 is a rest/calibration-pose outlier");

        // ---- gate 2: starts over the origin, feet reach the ground -----------------------
        var pelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        var restPelvis = skeleton.RestWorld[pelvis].Pos;
        var world0 = new Pose(frames[0]).ToWorld(skeleton);
        var d = world0[pelvis].Pos - restPelvis;
        var horizontal = MathF.Sqrt(d.X * d.X + d.Z * d.Z); // target space is Y-up cm
        Assert.True(horizontal <= 30f,
            $"{relativePath}: solved pelvis starts {horizontal:F0} cm off the target origin");

        var ankleL = rig.BoneForRole(BoneRole.FootL)!.Value;
        var ankleR = rig.BoneForRole(BoneRole.FootR)!.Value;
        var restAnkleY = MathF.Min(
            skeleton.RestWorld[ankleL].Pos.Y, skeleton.RestWorld[ankleR].Pos.Y);
        var minAnkleY = float.MaxValue;
        foreach (var frame in frames)
        {
            var world = new Pose(frame).ToWorld(skeleton);
            minAnkleY = MathF.Min(minAnkleY,
                MathF.Min(world[ankleL].Pos.Y, world[ankleR].Pos.Y));
        }
        Assert.True(MathF.Abs(minAnkleY - restAnkleY) <= 25f,
            $"{relativePath}: lowest solved ankle {minAnkleY:F1} cm vs rest ankle "
            + $"{restAnkleY:F1} cm — clip floats or sinks");
    }
}
