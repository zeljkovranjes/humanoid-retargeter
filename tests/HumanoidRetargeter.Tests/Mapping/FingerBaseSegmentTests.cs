using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using Xunit.Abstractions;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// User repro (2026-07-04): "the fingers look weird" on custom FBX targets (simpson,
/// catwalk clips). Auto-Rig Pro exports name each finger chain
/// <c>index1_base → index1 → index2 → index3</c>: the <c>_base</c> bone is the long
/// METACARPAL inside the palm, the digits are the phalanges. The digit-only segment rule
/// made <c>index1_base</c> and <c>index1</c> BOTH read as the proximal phalanx — first
/// wins, so curls bent the metacarpal from inside the palm while the real first knuckle
/// (<c>index1</c>) never moved.
/// </summary>
public class FingerBaseSegmentTests
{
    private readonly ITestOutputHelper _out;
    public FingerBaseSegmentTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void BaseSuffixedFingerBones_MapAsMetacarpals()
    {
        var bones = new List<BoneDefinition>
        {
            new("root", null, new XForm(new Vector3(0, 100, 0), Quaternion.Identity)),
            new("hand.l", "root", new XForm(new Vector3(70, 50, 0), Quaternion.Identity)),
        };
        foreach (var finger in new[] { "index", "middle", "ring", "pinky" })
        {
            bones.Add(new($"{finger}1_base.l", "hand.l", new XForm(new Vector3(8, 0, 0), Quaternion.Identity)));
            bones.Add(new($"{finger}1.l", $"{finger}1_base.l", new XForm(new Vector3(8, 0, 0), Quaternion.Identity)));
            bones.Add(new($"{finger}2.l", $"{finger}1.l", new XForm(new Vector3(3, 0, 0), Quaternion.Identity)));
            bones.Add(new($"{finger}3.l", $"{finger}2.l", new XForm(new Vector3(3, 0, 0), Quaternion.Identity)));
        }
        var skeleton = SkeletonModel.Create(bones);
        var map = AutoMapper.Map(skeleton);

        int Bone(string name) => Enumerable.Range(0, skeleton.Count).Single(i => skeleton[i].Name == name);
        foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky" })
        {
            var lower = finger.ToLowerInvariant();
            Assert.True(map.RoleToBone.TryGetValue(Enum.Parse<BoneRole>(finger + "MetaL"), out var meta)
                && meta == Bone($"{lower}1_base.l"),
                $"{lower}1_base.l must map as the metacarpal");
            Assert.True(map.RoleToBone.TryGetValue(Enum.Parse<BoneRole>(finger + "ProxL"), out var prox)
                && prox == Bone($"{lower}1.l"),
                $"{lower}1.l must map as the proximal phalanx");
            Assert.Equal(Bone($"{lower}2.l"), map.RoleToBone[Enum.Parse<BoneRole>(finger + "MidL")]);
            Assert.Equal(Bone($"{lower}3.l"), map.RoleToBone[Enum.Parse<BoneRole>(finger + "DistL")]);
        }
    }

    /// <summary>The real asset: every finger chain fully articulated — the base bones as
    /// metacarpals, the true knuckles as proximals, no bone skipped.</summary>
    [Fact]
    public void Simpson_FingerChains_FullyMapped()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        if (!File.Exists(fbx)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        int Bone(string name) => Enumerable.Range(0, scene.Skeleton.Count)
            .Single(i => scene.Skeleton[i].Name == name);

        foreach (var (finger, lower) in new[] { ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Pinky", "pinky") })
        foreach (var (roleSide, nameSide) in new[] { ("L", "l"), ("R", "r") })
        {
            Assert.Equal(Bone($"{lower}1_base.{nameSide}"), map.RoleToBone[Enum.Parse<BoneRole>($"{finger}Meta{roleSide}")]);
            Assert.Equal(Bone($"{lower}1.{nameSide}"), map.RoleToBone[Enum.Parse<BoneRole>($"{finger}Prox{roleSide}")]);
            Assert.Equal(Bone($"{lower}2.{nameSide}"), map.RoleToBone[Enum.Parse<BoneRole>($"{finger}Mid{roleSide}")]);
            Assert.Equal(Bone($"{lower}3.{nameSide}"), map.RoleToBone[Enum.Parse<BoneRole>($"{finger}Dist{roleSide}")]);
        }
        // Thumbs (no base bone) stay Prox/Mid/Dist.
        Assert.Equal(Bone("thumb1.l"), map.RoleToBone[BoneRole.ThumbProxL]);
        Assert.Equal(Bone("thumb2.l"), map.RoleToBone[BoneRole.ThumbMidL]);
        Assert.Equal(Bone("thumb3.l"), map.RoleToBone[BoneRole.ThumbDistL]);
    }
}
