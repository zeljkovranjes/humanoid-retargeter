using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Cleanup;

/// <summary>User concern (2026-07-04): "some models have extra bones such as a sword...
/// make sure the system is incredibly aware". Orphan helper subtrees (no mapped ancestor)
/// ride the NEAREST plausible anchor: a prop bone resting in the hand follows the HAND,
/// not the hips; skirt/cloth bones near the torso keep riding the hips.</summary>
public class OrphanAnchorTests
{
    private readonly ITestOutputHelper _out;
    public OrphanAnchorTests(ITestOutputHelper o) => _out = o;

    // scene root -> body root (hips) -> spine -> hand chain; a sword bone parented to the
    // SCENE root but resting in the hand, and a cloth bone resting at the waist.
    private static SkeletonModel Build() => SkeletonModel.Create(new[]
    {
        new BoneDefinition("scene_root", null, new XForm(Vector3.Zero, Quaternion.Identity)),
        new BoneDefinition("hips", "scene_root", new XForm(new Vector3(0, 100, 0), Quaternion.Identity)),
        new BoneDefinition("spine", "hips", new XForm(new Vector3(0, 20, 0), Quaternion.Identity)),
        new BoneDefinition("hand", "spine", new XForm(new Vector3(60, 20, 0), Quaternion.Identity)),
        new BoneDefinition("sword", "scene_root", new XForm(new Vector3(62, 141, 2), Quaternion.Identity)),
        new BoneDefinition("cloth", "scene_root", new XForm(new Vector3(4, 95, 3), Quaternion.Identity)),
    });

    private static TargetRig Rig(SkeletonModel skeleton)
    {
        var map = new MappingResult("test", MappingSource.Manual);
        map.RoleToBone[BoneRole.Hips] = 1;
        map.RoleToBone[BoneRole.Spine0] = 2;
        map.RoleToBone[BoneRole.HandL] = 3;
        return TargetRig.FromSkeleton(skeleton, map);
    }

    [Fact]
    public void SwordRidesTheHand_ClothRidesTheHips()
    {
        var skeleton = Build();
        var rig = Rig(skeleton);

        // One frame: the hand swings forward 40cm (rotate spine->hand by translating the
        // hand's local), hips shift 10cm sideways.
        var frame = Enumerable.Range(0, skeleton.Count).Select(i => skeleton[i].RestLocal).ToArray();
        frame[1] = new XForm(new Vector3(10, 100, 0), skeleton[1].RestLocal.Rot);
        frame[3] = new XForm(new Vector3(60, 20, 40), skeleton[3].RestLocal.Rot);
        var frames = new List<XForm[]> { frame };

        HumanoidRetargeter.Core.Retargeter.TestHook_FollowOrphans(rig, frames);

        var world = new HumanoidRetargeter.Core.Skeleton.Pose(frames[0]).ToWorld(skeleton);
        var rest = skeleton.RestWorld;

        // Sword must keep its rest offset FROM THE HAND (rides the hand).
        var swordOffset = world[4].Pos - world[3].Pos;
        var swordRestOffset = rest[4].Pos - rest[3].Pos;
        _out.WriteLine($"sword offset {swordOffset} vs rest {swordRestOffset}");
        Assert.True((swordOffset - swordRestOffset).Length() < 0.5f,
            $"sword drifted {(swordOffset - swordRestOffset).Length():0.#}cm from the hand");

        // Cloth must keep its rest offset FROM THE HIPS.
        var clothOffset = world[5].Pos - world[1].Pos;
        var clothRestOffset = rest[5].Pos - rest[1].Pos;
        _out.WriteLine($"cloth offset {clothOffset} vs rest {clothRestOffset}");
        Assert.True((clothOffset - clothRestOffset).Length() < 0.5f,
            $"cloth drifted {(clothOffset - clothRestOffset).Length():0.#}cm from the hips");
    }
}
