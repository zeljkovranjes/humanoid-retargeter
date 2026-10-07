using HumanoidRetargeter.Core.Mapping;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// Local-only mapping/detection checks against the REAL user-reported repro files in
/// <c>dev/corpus/todo/</c> (not committed; every test silently passes when a file is
/// absent):
/// <list type="bullet">
/// <item><c>Neutral_throw_ball_001__A057.bvh</c> — NVIDIA SOMA uniform-proportion skeleton
/// (github.com/NVIDIA/soma-retargeter assets/motions/bvh). Mixamo-identical upper body and
/// fingers but legs <c>LeftLeg→LeftShin</c> ("Leg" is the THIGH). Used to falsely detect
/// "90% mixamo" with both upper legs unmapped.</item>
/// <item><c>Armchair1.bvh</c> — classic MotionBuilder/Character-Studio BVH naming
/// (<c>Chest..Chest4</c>, <c>Collar→Shoulder→Elbow→Wrist</c>, <c>Hip→Knee→Ankle→Toe</c>).</item>
/// <item><c>47_01.bvh</c> — CMU mocap naming (<c>LHipJoint</c> helpers, <c>LowerBack</c>
/// spine start, otherwise mixamo-style limb names).</item>
/// </list>
/// </summary>
public class TodoCorpusRealFileTests
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

    private static string TodoFile(string name) => RepoFile("dev", "corpus", "todo", name);

    private static readonly BoneRole[] CoreLimbAndAxisRoles =
    {
        BoneRole.Hips, BoneRole.Spine0, BoneRole.Neck, BoneRole.Head,
        BoneRole.ClavicleL, BoneRole.ClavicleR,
        BoneRole.UpperArmL, BoneRole.UpperArmR, BoneRole.LowerArmL, BoneRole.LowerArmR,
        BoneRole.HandL, BoneRole.HandR,
        BoneRole.UpperLegL, BoneRole.UpperLegR, BoneRole.LowerLegL, BoneRole.LowerLegR,
        BoneRole.FootL, BoneRole.FootR,
    };

    // NOTE: the killed profile-support round (SOMA/classic-BVH presets for
    // Neutral_throw_ball_001__A057.bvh and Armchair1.bvh) lost its Code-side work; its
    // tests were removed with it. Todo.txt still tracks those repros.

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Cmu4701_LimbsFullyMappedBothSides()
    {
        var path = TodoFile("47_01.bvh");
        if (!File.Exists(path))
            return;

        var scene = Retargeter.ImportSource(File.ReadAllBytes(path), Path.GetFileName(path));
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);

        string? BoneOf(BoneRole role)
            => map.RoleToBone.TryGetValue(role, out var i) ? scene.Skeleton[i].Name : null;

        // Mapping path is unpinned (CMU naming is mixamo-adjacent: a preset or the
        // auto-mapper may resolve it) — what matters is the anatomy.
        Assert.Equal("Hips", BoneOf(BoneRole.Hips));
        Assert.Equal("LeftUpLeg", BoneOf(BoneRole.UpperLegL));
        Assert.Equal("RightUpLeg", BoneOf(BoneRole.UpperLegR));
        Assert.Equal("LeftLeg", BoneOf(BoneRole.LowerLegL));
        Assert.Equal("RightLeg", BoneOf(BoneRole.LowerLegR));
        Assert.Equal("LeftFoot", BoneOf(BoneRole.FootL));
        Assert.Equal("LeftToeBase", BoneOf(BoneRole.ToeL));
        Assert.Equal("LeftShoulder", BoneOf(BoneRole.ClavicleL));
        Assert.Equal("LeftArm", BoneOf(BoneRole.UpperArmL));
        Assert.Equal("RightForeArm", BoneOf(BoneRole.LowerArmR));
        Assert.Equal("LeftHand", BoneOf(BoneRole.HandL));
        Assert.NotNull(BoneOf(BoneRole.Head));
        Assert.NotNull(BoneOf(BoneRole.Neck));
        Assert.NotNull(BoneOf(BoneRole.Spine0));
        foreach (var role in CoreLimbAndAxisRoles)
            Assert.True(map.RoleToBone.ContainsKey(role), $"{role} unmapped");

        // The LHipJoint/RHipJoint helper joints carry no role.
        foreach (var (role, boneIndex) in map.RoleToBone)
            Assert.DoesNotContain("HipJoint", scene.Skeleton[boneIndex].Name);
    }
}
