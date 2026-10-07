using HumanoidRetargeter.Core.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class AutoMapperTests
{
    // ---------------------------------------------------------------- stage A: name tokens

    [Fact]
    public void StageA_MapsMixedStyleSyntheticRig()
    {
        // Mixed naming styles in one rig: 3ds-Max-Biped-style "Bip01_L_UpperArm"
        // (namespace prefix + side letter + camel case) alongside bare "spine03" style.
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("Bip01", null, new(0, 0, 0)),
            ("Bip01_Pelvis", "Bip01", new(0, 95, 0)),
            ("spine01", "Bip01_Pelvis", new(0, 105, 0)),
            ("spine02", "spine01", new(0, 118, 0)),
            ("spine03", "spine02", new(0, 132, 0)),
            ("Bip01_Neck", "spine03", new(0, 148, 0)),
            ("Bip01_Head", "Bip01_Neck", new(0, 156, 0)),
        };
        foreach (var (s, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"Bip01_{s}_Clavicle", "spine03", new(4 * x, 145, 0)));
            bones.Add(($"Bip01_{s}_UpperArm", $"Bip01_{s}_Clavicle", new(18 * x, 144, 0)));
            bones.Add(($"Bip01_{s}_Forearm", $"Bip01_{s}_UpperArm", new(44 * x, 144, 0)));
            bones.Add(($"Bip01_{s}_Hand", $"Bip01_{s}_Forearm", new(67 * x, 144, 0)));
            bones.Add(($"Bip01_{s}_Thigh", "Bip01_Pelvis", new(9 * x, 92, 0)));
            bones.Add(($"Bip01_{s}_Calf", $"Bip01_{s}_Thigh", new(9 * x, 50, 0)));
            bones.Add(($"Bip01_{s}_Foot", $"Bip01_{s}_Calf", new(9 * x, 10, 0)));
            bones.Add(($"Bip01_{s}_Toe0", $"Bip01_{s}_Foot", new(9 * x, 2, 12)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var result = AutoMapper.Map(skeleton);

        Assert.Equal(MappingSource.AutoName, result.Source);
        Assert.Equal("auto", result.ProfileName);
        Assert.True(result.Confidence >= 0.6f, $"Confidence {result.Confidence} < 0.6");

        AssertRole(skeleton, result, BoneRole.Hips, "Bip01_Pelvis");
        AssertRole(skeleton, result, BoneRole.Spine0, "spine01");
        AssertRole(skeleton, result, BoneRole.Spine1, "spine02");
        AssertRole(skeleton, result, BoneRole.Spine2, "spine03");
        AssertRole(skeleton, result, BoneRole.Neck, "Bip01_Neck");
        AssertRole(skeleton, result, BoneRole.Head, "Bip01_Head");
        AssertRole(skeleton, result, BoneRole.ClavicleR, "Bip01_R_Clavicle");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "Bip01_L_UpperArm");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "Bip01_R_Forearm");
        AssertRole(skeleton, result, BoneRole.HandL, "Bip01_L_Hand");
        AssertRole(skeleton, result, BoneRole.UpperLegR, "Bip01_R_Thigh");
        AssertRole(skeleton, result, BoneRole.LowerLegL, "Bip01_L_Calf");
        AssertRole(skeleton, result, BoneRole.FootL, "Bip01_L_Foot");
        AssertRole(skeleton, result, BoneRole.ToeL, "Bip01_L_Toe0");
    }

    [Fact]
    public void StageA_MapsAutoRigProStyleNames()
    {
        // Auto-Rig Pro export conventions (synthetic name list — NOT a user file): '.l/.r'
        // side suffixes, '.x' center markers (spine_01.x, neck.x), '_stretch' deform-twin
        // limb names (thigh_stretch.l, arm_stretch.l), 'toes_01.l' toes, and a pelvis named
        // 'root.x' under a ground bone 'root' (no hips/pelvis-named bone anywhere).
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("root", null, new(0, 0, 0)),
            ("root.x", "root", new(0, 99, 0)),
            ("spine_01.x", "root.x", new(0, 109, 0)),
            ("spine_02.x", "spine_01.x", new(0, 121, 0)),
            ("spine_03.x", "spine_02.x", new(0, 134, 0)),
            ("neck.x", "spine_03.x", new(0, 149, 0)),
            ("head.x", "neck.x", new(0, 160, 0)),
        };
        foreach (var (s, x) in new[] { ("l", 1f), ("r", -1f) })
        {
            bones.Add(($"shoulder.{s}", "spine_03.x", new(6 * x, 145, 2)));
            bones.Add(($"arm_stretch.{s}", $"shoulder.{s}", new(19 * x, 144, 0)));
            bones.Add(($"forearm_stretch.{s}", $"arm_stretch.{s}", new(46 * x, 144, 0)));
            bones.Add(($"hand.{s}", $"forearm_stretch.{s}", new(70 * x, 144, 0)));
            bones.Add(($"thigh_stretch.{s}", "root.x", new(9 * x, 92, 0)));
            bones.Add(($"leg_stretch.{s}", $"thigh_stretch.{s}", new(9 * x, 51, 0)));
            bones.Add(($"foot.{s}", $"leg_stretch.{s}", new(9 * x, 9, 0)));
            bones.Add(($"toes_01.{s}", $"foot.{s}", new(9 * x, 2, 12)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var result = AutoMapper.Map(skeleton);

        Assert.Equal(MappingSource.AutoName, result.Source);
        Assert.True(result.Confidence >= 0.6f, $"Confidence {result.Confidence} < 0.6");

        // 'root.x' becomes the hips: deepest 'root' bone above the mapped legs/spine —
        // NOT the ground bone 'root'.
        AssertRole(skeleton, result, BoneRole.Hips, "root.x");
        AssertRole(skeleton, result, BoneRole.Spine0, "spine_01.x");
        AssertRole(skeleton, result, BoneRole.Spine1, "spine_02.x");
        AssertRole(skeleton, result, BoneRole.Spine2, "spine_03.x");
        AssertRole(skeleton, result, BoneRole.Neck, "neck.x");
        AssertRole(skeleton, result, BoneRole.Head, "head.x");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "shoulder.l");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "arm_stretch.l");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "forearm_stretch.r");
        AssertRole(skeleton, result, BoneRole.HandL, "hand.l");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "thigh_stretch.l");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "leg_stretch.r");
        AssertRole(skeleton, result, BoneRole.FootL, "foot.l");
        AssertRole(skeleton, result, BoneRole.ToeR, "toes_01.r");
    }

    [Fact]
    public void StageA_RootFallback_NeverShadowsARealPelvisName()
    {
        // UE-Mannequin-style rig: 'root' ground bone AND a real 'pelvis' — the root
        // fallback must not fire, the named pelvis wins.
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("root", null, new(0, 0, 0)),
            ("pelvis", "root", new(0, 95, 0)),
            ("spine_01", "pelvis", new(0, 105, 0)),
            ("spine_02", "spine_01", new(0, 120, 0)),
            ("neck_01", "spine_02", new(0, 148, 0)),
            ("head", "neck_01", new(0, 158, 0)),
        };
        foreach (var (s, x) in new[] { ("l", 1f), ("r", -1f) })
        {
            bones.Add(($"clavicle_{s}", "spine_02", new(4 * x, 145, 0)));
            bones.Add(($"upperarm_{s}", $"clavicle_{s}", new(18 * x, 144, 0)));
            bones.Add(($"lowerarm_{s}", $"upperarm_{s}", new(44 * x, 144, 0)));
            bones.Add(($"hand_{s}", $"lowerarm_{s}", new(67 * x, 144, 0)));
            bones.Add(($"thigh_{s}", "pelvis", new(9 * x, 92, 0)));
            bones.Add(($"calf_{s}", $"thigh_{s}", new(9 * x, 50, 0)));
            bones.Add(($"foot_{s}", $"calf_{s}", new(9 * x, 10, 0)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var result = AutoMapper.Map(skeleton);

        AssertRole(skeleton, result, BoneRole.Hips, "pelvis");
    }

    [Fact]
    public void StageA_MapsZombieCrawlByTokens_WithoutPresetKnowledge()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();

        var result = AutoMapper.Map(skeleton);

        Assert.Equal(MappingSource.AutoName, result.Source);
        Assert.True(result.Confidence >= 0.5f, $"Confidence {result.Confidence} < 0.5");

        AssertRole(skeleton, result, BoneRole.Hips, "mixamorig1:Hips");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "mixamorig1:LeftArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "mixamorig1:LeftForeArm");
        AssertRole(skeleton, result, BoneRole.HandR, "mixamorig1:RightHand");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "mixamorig1:LeftUpLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegL, "mixamorig1:LeftLeg");
        AssertRole(skeleton, result, BoneRole.FootR, "mixamorig1:RightFoot");
        AssertRole(skeleton, result, BoneRole.ToeL, "mixamorig1:LeftToeBase");
        AssertRole(skeleton, result, BoneRole.IndexProxL, "mixamorig1:LeftHandIndex1");
        AssertRole(skeleton, result, BoneRole.ThumbDistR, "mixamorig1:RightHandThumb3");
    }

    // NOTE: the killed profile-support round (classic-BVH/CMU Stage-A naming rules for
    // Armchair1.bvh / 47_01.bvh) lost its Code-side work; its tests were removed with it.
    // Todo.txt still tracks those repros.

    [Fact]
    public void StageA_MixamoStyleShoulder_StaysClavicle_WhenArmExists()
    {
        // Mixamo naming: Shoulder→Arm→ForeArm. The upper-arm bucket is non-empty
        // ("LeftArm"), so the shoulder must NOT be promoted and keeps the clavicle role.
        var skeleton = MappingFixtures.LoadZombieCrawl();

        var result = AutoMapper.Map(skeleton);

        AssertRole(skeleton, result, BoneRole.ClavicleL, "mixamorig1:LeftShoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "mixamorig1:LeftArm");
    }

    // ---------------------------------------------------------------- stage B: topology fallback

    [Fact]
    public void StageB_RenamedZombieCrawl_ResolvedByTopology()
    {
        var original = MappingFixtures.LoadZombieCrawl();
        // Strip every name: hierarchy, order and world rest positions are all preserved,
        // so bone indices stay comparable against the original skeleton.
        var nameless = MappingFixtures.RenameAll(original, i => $"bone_{i:000}");

        var result = AutoMapper.Map(nameless);

        Assert.Equal(MappingSource.AutoTopology, result.Source);
        Assert.Equal("topology", result.ProfileName);
        Assert.True(result.Confidence > 0f, "Confidence must reflect resolved roles");
        Assert.True(result.Confidence < 1f, "Topology fallback must not claim full confidence");

        AssertRoleIndex(original, result, BoneRole.Hips, "mixamorig1:Hips");
        AssertRoleIndex(original, result, BoneRole.Head, "mixamorig1:Head");
        AssertRoleIndex(original, result, BoneRole.UpperLegL, "mixamorig1:LeftUpLeg");
        AssertRoleIndex(original, result, BoneRole.UpperLegR, "mixamorig1:RightUpLeg");
        AssertRoleIndex(original, result, BoneRole.LowerLegL, "mixamorig1:LeftLeg");
        AssertRoleIndex(original, result, BoneRole.LowerLegR, "mixamorig1:RightLeg");
        AssertRoleIndex(original, result, BoneRole.FootL, "mixamorig1:LeftFoot");
        AssertRoleIndex(original, result, BoneRole.FootR, "mixamorig1:RightFoot");
        AssertRoleIndex(original, result, BoneRole.UpperArmL, "mixamorig1:LeftArm");
        AssertRoleIndex(original, result, BoneRole.UpperArmR, "mixamorig1:RightArm");
        AssertRoleIndex(original, result, BoneRole.LowerArmL, "mixamorig1:LeftForeArm");
        AssertRoleIndex(original, result, BoneRole.LowerArmR, "mixamorig1:RightForeArm");
        AssertRoleIndex(original, result, BoneRole.HandL, "mixamorig1:LeftHand");
        AssertRoleIndex(original, result, BoneRole.HandR, "mixamorig1:RightHand");
    }

    [Fact]
    public void StageB_AlsoResolvesSpineAndNeckChain()
    {
        var original = MappingFixtures.LoadZombieCrawl();
        var nameless = MappingFixtures.RenameAll(original, i => $"bone_{i:000}");

        var result = AutoMapper.Map(nameless);

        AssertRoleIndex(original, result, BoneRole.Spine0, "mixamorig1:Spine");
        AssertRoleIndex(original, result, BoneRole.Spine1, "mixamorig1:Spine1");
        AssertRoleIndex(original, result, BoneRole.Spine2, "mixamorig1:Spine2");
        AssertRoleIndex(original, result, BoneRole.Neck, "mixamorig1:Neck");
        AssertRoleIndex(original, result, BoneRole.ClavicleL, "mixamorig1:LeftShoulder");
        AssertRoleIndex(original, result, BoneRole.ClavicleR, "mixamorig1:RightShoulder");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StageB_SharedLca_LegsAndArms_DisambiguatedGeometrically(bool armsFirst)
    {
        // Clavicles and thighs hang off the SAME node (both limb pairs share one LCA), so
        // the old depth-to-LCA tie-break degenerated to construction order. The geometric
        // rule (leg end effectors sit at the bottom of the vertical extent) must assign
        // legs/arms correctly for BOTH construction orderings.
        var rig = BuildSharedLcaRig(armsFirst);
        var result = AutoMapper.Map(rig.Skeleton);

        Assert.Equal(MappingSource.AutoTopology, result.Source);
        AssertRole(rig.Skeleton, result, BoneRole.UpperLegL, rig.ThighL);
        AssertRole(rig.Skeleton, result, BoneRole.UpperLegR, rig.ThighR);
        AssertRole(rig.Skeleton, result, BoneRole.FootL, rig.FootL);
        AssertRole(rig.Skeleton, result, BoneRole.ClavicleL, rig.ClavL);
        AssertRole(rig.Skeleton, result, BoneRole.ClavicleR, rig.ClavR);
        AssertRole(rig.Skeleton, result, BoneRole.HandL, rig.HandL);
    }

    [Fact]
    public void StageB_DisconnectedRoots_FailsGracefully_NoThrow()
    {
        // Two disconnected armatures, each with its own mirrored limb pair: the limb
        // pairs share no common ancestor, so topology mapping must fail with a note
        // instead of throwing InvalidOperationException.
        var bones = new List<(string, string?, System.Numerics.Vector3)>();
        string Add(string? parent, System.Numerics.Vector3 world)
        {
            var name = $"x{bones.Count:00}";
            bones.Add((name, parent, world));
            return name;
        }

        foreach (var baseY in new[] { 0f, 50f })
        {
            var root = Add(null, new(0, baseY, 0));
            foreach (var x in new[] { 1f, -1f })
            {
                var c1 = Add(root, new(10 * x, baseY, 0));
                var c2 = Add(c1, new(20 * x, baseY, 0));
                Add(c2, new(30 * x, baseY, 0));
            }
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var result = AutoMapper.Map(skeleton); // must not throw

        Assert.False(result.RoleToBone.ContainsKey(BoneRole.Hips));
        Assert.True(result.Confidence < 0.5f, $"Confidence {result.Confidence} should reflect failure");
        Assert.Contains(result.Notes, n => n.Contains("no common ancestor"));
    }

    /// <summary>
    /// Rig whose arm and leg pairs are children of the same node, with neutral bone names
    /// (stage A maps nothing). Returns the names of the key bones for assertions.
    /// </summary>
    private static (HumanoidRetargeter.Core.Skeleton.Skeleton Skeleton,
        string ThighL, string ThighR, string ClavL, string ClavR, string HandL, string FootL)
        BuildSharedLcaRig(bool armsFirst)
    {
        var bones = new List<(string, string?, System.Numerics.Vector3)>();
        string Add(string? parent, System.Numerics.Vector3 world)
        {
            var name = $"j{bones.Count:00}";
            bones.Add((name, parent, world));
            return name;
        }

        var node = Add(null, new(0, 100, 0));
        var neck = Add(node, new(0, 150, 0));
        Add(neck, new(0, 160, 0)); // head

        string thighL = "", thighR = "", clavL = "", clavR = "", handL = "", footL = "";

        void AddLegs()
        {
            foreach (var x in new[] { 1f, -1f })
            {
                var thigh = Add(node, new(10 * x, 95, 0));
                var calf = Add(thigh, new(10 * x, 50, 0));
                var foot = Add(calf, new(10 * x, 5, 0));
                if (x > 0) { thighL = thigh; footL = foot; }
                else thighR = thigh;
            }
        }

        void AddArms()
        {
            foreach (var x in new[] { 1f, -1f })
            {
                var clav = Add(node, new(8 * x, 140, 0));
                var upper = Add(clav, new(25 * x, 140, 0));
                var lower = Add(upper, new(45 * x, 140, 0));
                var hand = Add(lower, new(65 * x, 140, 0));
                if (x > 0) { clavL = clav; handL = hand; }
                else clavR = clav;
            }
        }

        if (armsFirst) { AddArms(); AddLegs(); }
        else { AddLegs(); AddArms(); }

        return (MappingFixtures.FromWorldPositions(bones), thighL, thighR, clavL, clavR, handL, footL);
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertRole(
        HumanoidRetargeter.Core.Skeleton.Skeleton skeleton, MappingResult result, BoneRole role, string expectedBone)
    {
        Assert.True(result.RoleToBone.TryGetValue(role, out var index), $"Role {role} unmapped");
        Assert.Equal(expectedBone, skeleton[index].Name);
    }

    /// <summary>Asserts the role maps to the bone index that carries the given name in the
    /// original (pre-rename) skeleton.</summary>
    private static void AssertRoleIndex(
        HumanoidRetargeter.Core.Skeleton.Skeleton original, MappingResult result, BoneRole role, string originalName)
    {
        Assert.True(result.RoleToBone.TryGetValue(role, out var index), $"Role {role} unmapped");
        Assert.Equal(original.IndexOf(originalName), index);
    }
}
