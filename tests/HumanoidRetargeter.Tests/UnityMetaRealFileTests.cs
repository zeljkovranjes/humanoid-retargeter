using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Mapping;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Local-only end-to-end check against a REAL Unity animation pack file
/// (PunchPerfect "Stance.fbx" + its .fbx.meta sidecar: one 44-frame 30 fps take
/// "root|Animation" carrying two clipAnimations — 'Stance' frames 0–43 and
/// 'Stance No Move' frames 0–1, both loopTime 1; Auto-Rig-Pro-style bone names like
/// thigh_stretch.l / spine_01.x / root.x). The licensed file is NOT committed: every test
/// silently passes when it is absent.
/// </summary>
public class UnityMetaRealFileTests
{
    private const string RealFbx =
        @"P:\10 3 2025\Pointless Group\PunchPerfect Boxing Animations & Tools\Core\Animations\Stance.fbx";

    private static bool Available => File.Exists(RealFbx) && File.Exists(RealFbx + ".meta");

    /// <summary>Resolves a repo-relative path by walking up to the .sbproj directory.</summary>
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    [Fact] // skipped (silently green) when the local pack is not present
    public void RealStanceMeta_ParsesBothClipDefinitions()
    {
        if (!Available)
            return;

        var defs = UnityMeta.ParseClipAnimations(File.ReadAllText(RealFbx + ".meta"));

        Assert.Equal(2, defs.Count);
        Assert.Equal("Stance", defs[0].Name);
        Assert.Equal("Stance No Move", defs[1].Name);
        Assert.All(defs, d => Assert.Equal("root|Animation", d.TakeName));
        Assert.Equal(0f, defs[0].FirstFrame);
        Assert.Equal(43f, defs[0].LastFrame);
        Assert.Equal(0f, defs[1].FirstFrame);
        Assert.Equal(1f, defs[1].LastFrame);
        Assert.All(defs, d => Assert.True(d.Loop)); // loopTime: 1 on both
    }

    [Fact] // skipped (silently green) when the local pack is not present
    public void RealStanceFbx_AutoMapsTheAutoRigProSkeleton()
    {
        if (!Available)
            return;

        var scene = Retargeter.ImportSource(File.ReadAllBytes(RealFbx), "Stance.fbx");
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);

        string? BoneOf(BoneRole role)
            => map.RoleToBone.TryGetValue(role, out var i) ? scene.Skeleton[i].Name : null;

        Assert.Equal("root.x", BoneOf(BoneRole.Hips));
        Assert.Equal("thigh_stretch.l", BoneOf(BoneRole.UpperLegL));
        Assert.Equal("leg_stretch.r", BoneOf(BoneRole.LowerLegR));
        Assert.Equal("foot.l", BoneOf(BoneRole.FootL));
        Assert.Equal("toes_01.l", BoneOf(BoneRole.ToeL));
        Assert.Equal("spine_01.x", BoneOf(BoneRole.Spine0));
        Assert.Equal("spine_03.x", BoneOf(BoneRole.Spine2));
        Assert.Equal("neck.x", BoneOf(BoneRole.Neck));
        Assert.Equal("head.x", BoneOf(BoneRole.Head));
        Assert.Equal("arm_stretch.l", BoneOf(BoneRole.UpperArmL));
        Assert.Equal("forearm_stretch.r", BoneOf(BoneRole.LowerArmR));
        Assert.Equal("hand.l", BoneOf(BoneRole.HandL));
        Assert.True(map.Confidence >= 0.9f, $"confidence {map.Confidence}");
    }

    [Fact] // skipped (silently green) when the local pack is not present
    public void RealStanceFbx_MetaDefinitions_ConvertToSboxTarget()
    {
        if (!Available)
            return;

        var defs = UnityMeta.ParseClipAnimations(File.ReadAllText(RealFbx + ".meta"));
        Assert.Equal(2, defs.Count);

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = File.ReadAllBytes(RealFbx),
            SourceFileName = "Stance.fbx",
            ClipDefinitions = defs,
        }, SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
        Assert.Equal("Stance", result.Clips[0].ClipName);
        Assert.Equal("Stance_No_Move", result.Clips[1].ClipName); // sanitized like take names

        // The take is 44 frames (0..43) at native 30 fps == the 30 fps sample grid:
        // 'Stance' spans the whole timeline, 'Stance No Move' is the 2-frame pose range.
        Assert.Equal(44, result.Clips[0].SolvedFrames!.Count);
        Assert.Equal(2, result.Clips[1].SolvedFrames!.Count);
        Assert.True(result.Clips[0].SolvedFrames!.Count > 10);
        Assert.All(result.Clips, c => Assert.True(c.Looping)); // loopTime on both

        // Plausibility: every solved transform finite, pelvis at a sane standing height.
        var pelvis = SboxTarget.Value.Rig.BoneForRole(BoneRole.Hips)!.Value;
        foreach (var clip in result.Clips)
        {
            foreach (var frame in clip.SolvedFrames!)
            {
                foreach (var xf in frame)
                {
                    Assert.True(float.IsFinite(xf.Pos.X) && float.IsFinite(xf.Pos.Y) && float.IsFinite(xf.Pos.Z));
                    Assert.True(float.IsFinite(xf.Rot.X) && float.IsFinite(xf.Rot.Y)
                        && float.IsFinite(xf.Rot.Z) && float.IsFinite(xf.Rot.W));
                }
            }
            // s&box source space is Y-up cm; a standing pelvis sits roughly 70–120 cm up.
            var hipsY = clip.SolvedFrames![0][pelvis].Pos.Y;
            Assert.InRange(hipsY, 50f, 150f);
            Assert.NotNull(clip.DmxContent);
            Assert.NotEmpty(clip.DmxContent!);
        }
    }
}
