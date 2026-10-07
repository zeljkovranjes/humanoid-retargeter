using HumanoidRetargeter.Core.Mapping;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// Local-only preset-detection checks against REAL rig files in <c>dev/corpus/</c> (not
/// committed; every test silently passes when the file is absent):
/// <list type="bullet">
/// <item><c>unknown_rigs/makehuman_cmu_03_03_dazNames.bvh</c> — MakeHuman CMU retarget with
/// Poser/DAZ classic names (hip/abdomen/chest, lShldr/lForeArm, lThigh/lShin, 2-segment
/// fingers, no toes). Previously fell through to the auto-mapper; must now hit the
/// <c>daz_poser</c> preset.</item>
/// <item><c>todo/Defenses.fbx</c> — Auto-Rig Pro export (PunchPerfect family): root.x hips,
/// *_stretch limbs, c_-prefixed fingers, leftover mixamorig:*4 finger-tip markers.
/// Previously auto-mapped at 0.94; must now hit the <c>auto_rig_pro</c> preset with a
/// full mapping.</item>
/// </list>
/// </summary>
public class RealRigPresetTests
{
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

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void MakehumanDazNames_DetectedAsDazPoser_LimbsFullyMapped()
    {
        var path = RepoFile("dev", "corpus", "unknown_rigs", "makehuman_cmu_03_03_dazNames.bvh");
        if (!File.Exists(path))
            return;

        var scene = Retargeter.ImportSource(File.ReadAllBytes(path), Path.GetFileName(path));
        var (map, report) = Retargeter.ResolveMapping(scene.Skeleton);

        Assert.Equal(MappingSource.Preset, map.Source);
        Assert.Equal("daz_poser", map.ProfileName);
        Assert.True(map.Confidence >= 0.8f, $"Confidence {map.Confidence} < 0.8");
        Assert.False(report.NeedsUserDecision);

        string BoneOf(BoneRole role) => scene.Skeleton[map.RoleToBone[role]].Name;
        Assert.Equal("hip", BoneOf(BoneRole.Hips));
        Assert.Equal("abdomen", BoneOf(BoneRole.Spine0));
        Assert.Equal("chest", BoneOf(BoneRole.Spine1)); // no abdomen2 in this file
        Assert.Equal("neck", BoneOf(BoneRole.Neck));
        Assert.Equal("head", BoneOf(BoneRole.Head));
        Assert.Equal("lCollar", BoneOf(BoneRole.ClavicleL));
        Assert.Equal("lShldr", BoneOf(BoneRole.UpperArmL));
        Assert.Equal("rShldr", BoneOf(BoneRole.UpperArmR));
        Assert.Equal("lForeArm", BoneOf(BoneRole.LowerArmL));
        Assert.Equal("rForeArm", BoneOf(BoneRole.LowerArmR));
        Assert.Equal("lHand", BoneOf(BoneRole.HandL));
        Assert.Equal("rHand", BoneOf(BoneRole.HandR));
        Assert.Equal("lThigh", BoneOf(BoneRole.UpperLegL));
        Assert.Equal("rThigh", BoneOf(BoneRole.UpperLegR));
        Assert.Equal("lShin", BoneOf(BoneRole.LowerLegL));
        Assert.Equal("rShin", BoneOf(BoneRole.LowerLegR));
        Assert.Equal("lFoot", BoneOf(BoneRole.FootL));
        Assert.Equal("rFoot", BoneOf(BoneRole.FootR));
        // 2-segment fingers: Prox+Mid resolve, Dist roles stay unmapped, honestly.
        Assert.Equal("lThumb1", BoneOf(BoneRole.ThumbProxL));
        Assert.Equal("rMid2", BoneOf(BoneRole.MiddleMidR));
        Assert.False(map.RoleToBone.ContainsKey(BoneRole.ThumbDistL));

        // The lButtock/rButtock thigh helpers and eyes carry no role.
        foreach (var (role, boneIndex) in map.RoleToBone)
        {
            var name = scene.Skeleton[boneIndex].Name;
            Assert.DoesNotContain("Buttock", name);
            Assert.DoesNotContain("Eye", name);
        }
    }

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void DefensesFbx_DetectedAsAutoRigPro_FullMappingBeatsAutoMap()
    {
        var path = RepoFile("dev", "corpus", "todo", "Defenses.fbx");
        if (!File.Exists(path))
            return;

        var scene = Retargeter.ImportSource(File.ReadAllBytes(path), Path.GetFileName(path));
        var (map, report) = Retargeter.ResolveMapping(scene.Skeleton);

        Assert.Equal(MappingSource.Preset, map.Source);
        Assert.Equal("auto_rig_pro", map.ProfileName);
        Assert.True(map.Confidence >= 0.9f, $"Confidence {map.Confidence} < 0.9");
        Assert.False(report.NeedsUserDecision);

        // Must beat the auto-mapper that used to handle this rig (0.94).
        var auto = AutoMapper.Map(scene.Skeleton);
        Assert.True(map.Confidence > auto.Confidence,
            $"Preset {map.Confidence} must beat auto-map {auto.Confidence}");

        string BoneOf(BoneRole role) => scene.Skeleton[map.RoleToBone[role]].Name;
        Assert.Equal("root.x", BoneOf(BoneRole.Hips));
        Assert.Equal("spine_01.x", BoneOf(BoneRole.Spine0));
        Assert.Equal("spine_02.x", BoneOf(BoneRole.Spine1));
        Assert.Equal("spine_03.x", BoneOf(BoneRole.Spine2));
        Assert.Equal("neck.x", BoneOf(BoneRole.Neck));
        Assert.Equal("head.x", BoneOf(BoneRole.Head));
        Assert.Equal("shoulder.l", BoneOf(BoneRole.ClavicleL));
        Assert.Equal("arm_stretch.l", BoneOf(BoneRole.UpperArmL));
        Assert.Equal("forearm_stretch.r", BoneOf(BoneRole.LowerArmR));
        Assert.Equal("hand.l", BoneOf(BoneRole.HandL));
        Assert.Equal("thigh_stretch.l", BoneOf(BoneRole.UpperLegL));
        Assert.Equal("leg_stretch.r", BoneOf(BoneRole.LowerLegR));
        Assert.Equal("foot.l", BoneOf(BoneRole.FootL));
        Assert.Equal("toes_01.l", BoneOf(BoneRole.ToeL));
        Assert.Equal("c_thumb1.l", BoneOf(BoneRole.ThumbProxL));
        Assert.Equal("c_middle2.r", BoneOf(BoneRole.MiddleMidR));
        Assert.Equal("c_pinky3.l", BoneOf(BoneRole.PinkyDistL));

        // All 30 finger phalanx roles resolve; the leftover mixamorig:*4 tip markers and
        // the ground bone "root" never carry a role.
        var fingers =
            from finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }
            from segment in new[] { "Prox", "Mid", "Dist" }
            from side in new[] { "L", "R" }
            select Enum.Parse<BoneRole>(finger + segment + side);
        foreach (var role in fingers)
            Assert.True(map.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");
        foreach (var (role, boneIndex) in map.RoleToBone)
        {
            var name = scene.Skeleton[boneIndex].Name;
            Assert.False(name == "root" || name.StartsWith("mixamorig:"),
                $"{role} mapped to non-deform bone {name}");
        }
    }
}
