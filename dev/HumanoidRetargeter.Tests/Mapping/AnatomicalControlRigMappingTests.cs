using HumanoidRetargeter.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class AnatomicalControlRigMappingTests
{
    [Fact]
    public void AnatomicalNamesWithControlAndMechanismBones_MapAsHumanoid()
    {
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("scene", null, new(0, 0, 0)),
            ("PIV_HIPS", "scene", new(0, 95, 0)),
            ("PELVIS", "PIV_HIPS", new(0, 100, 0)),
            ("VERTEBRAE_L5", "PELVIS", new(0, 110, 0)),
            ("VERTEBRAE_L4", "VERTEBRAE_L5", new(0, 120, 0)),
            ("VERTEBRAE_T1", "VERTEBRAE_L4", new(0, 145, 0)),
            ("VERTEBRAE_C7", "VERTEBRAE_T1", new(0, 153, 0)),
            ("CONTROL_HIPS", "scene", new(0, 100, 0)),
            ("CONTROL_NECK", "CONTROL_HIPS", new(0, 153, 0)),
            ("HEAD", "CONTROL_NECK", new(0, 166, 0)),
        };
        foreach (var (side, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"PIV_SHOULDER_{side}", "VERTEBRAE_T1", new(5 * x, 145, 0)));
            bones.Add(($"HUMERUS_{side}", $"PIV_SHOULDER_{side}", new(18 * x, 145, 0)));
            bones.Add(($"MCH_forearm_{side}", $"HUMERUS_{side}", new(45 * x, 145, 0)));
            bones.Add(($"RADIUS_{side}", $"HUMERUS_{side}", new(45 * x, 145, 0)));
            bones.Add(($"HAND_{side}", $"MCH_forearm_{side}", new(68 * x, 145, 0)));
            bones.Add(($"FING_INDEX_A_{side}", $"HAND_{side}", new(72 * x, 145, 0)));
            bones.Add(($"FING_INDEX_B_{side}", $"FING_INDEX_A_{side}", new(75 * x, 145, 0)));
            bones.Add(($"FING_INDEX_C_{side}", $"FING_INDEX_B_{side}", new(78 * x, 145, 0)));
            bones.Add(($"MCH_femur_{side}", "PELVIS", new(9 * x, 96, 0)));
            bones.Add(($"FEMUR_{side}", "PELVIS", new(9 * x, 96, 0)));
            bones.Add(($"TIBIA_{side}", $"MCH_femur_{side}", new(9 * x, 53, 0)));
            bones.Add(($"FOOT_{side}", $"TIBIA_{side}", new(9 * x, 10, 0)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var map = AutoMapper.Map(skeleton);

        Assert.Equal(MappingSource.AutoName, map.Source);
        Assert.True(map.Confidence >= 0.6f, $"confidence={map.Confidence:0.###}");
        AssertRole(BoneRole.Hips, "PELVIS");
        AssertRole(BoneRole.Neck, "VERTEBRAE_C7");
        AssertRole(BoneRole.Head, "HEAD");
        AssertRole(BoneRole.ClavicleL, "PIV_SHOULDER_L");
        AssertRole(BoneRole.UpperArmR, "HUMERUS_R");
        AssertRole(BoneRole.LowerArmL, "MCH_forearm_L");
        AssertRole(BoneRole.HandR, "HAND_R");
        AssertRole(BoneRole.IndexProxL, "FING_INDEX_A_L");
        AssertRole(BoneRole.IndexMidR, "FING_INDEX_B_R");
        AssertRole(BoneRole.IndexDistL, "FING_INDEX_C_L");
        AssertRole(BoneRole.UpperLegL, "MCH_femur_L");
        AssertRole(BoneRole.LowerLegR, "TIBIA_R");
        AssertRole(BoneRole.FootL, "FOOT_L");

        void AssertRole(BoneRole role, string expected)
        {
            Assert.True(map.RoleToBone.TryGetValue(role, out var index), $"{role} was not mapped");
            Assert.Equal(expected, skeleton[index].Name);
        }
    }
}
