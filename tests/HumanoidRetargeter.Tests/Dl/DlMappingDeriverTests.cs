using HumanoidRetargeter.Core.Dl;
using HumanoidRetargeter.Core.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Dl;

/// <summary>
/// The DL preview's "Save as profile" derivation (design §6): trajectory correlation over
/// the DL output must recover the core role ↔ source-bone alignment of the fixture clip.
/// </summary>
public class DlMappingDeriverTests
{
    [Fact]
    public void DerivedMappingRecoversCoreRolesByTrajectoryCorrelation()
    {
        var (dl, _, scene) = DlSolverTests.Solved.Value;
        var rig = DlFixtures.SboxRig.Value;

        var derived = DlMappingDeriver.Derive(scene, 0, dl, rig);

        string? BoneOf(BoneRole role)
            => derived.RoleToBone.TryGetValue(role, out var index) ? scene.Skeleton[index].Name : null;

        Assert.Equal("mixamorig:Hips", BoneOf(BoneRole.Hips));
        Assert.Equal("mixamorig:LeftFoot", BoneOf(BoneRole.FootL));
        Assert.Equal("mixamorig:RightFoot", BoneOf(BoneRole.FootR));
        Assert.Equal("mixamorig:LeftLeg", BoneOf(BoneRole.LowerLegL));
        Assert.Equal("mixamorig:RightLeg", BoneOf(BoneRole.LowerLegR));

        // The head role must land in the source head subtree. Bone-exact recovery is
        // genuinely ambiguous for a name-free geometric method: the citizen head bone's
        // height sits between mixamo's Head (skull base) and its eye/head-top markers,
        // which all ride rigidly together — any of them yields equivalent retargets.
        var headBone = BoneOf(BoneRole.Head);
        Assert.NotNull(headBone);
        var headIndex = scene.Skeleton.IndexOf("mixamorig:Head");
        var derivedIndex = scene.Skeleton.IndexOf(headBone!);
        var inHeadSubtree = false;
        for (var b = derivedIndex; b >= 0; b = scene.Skeleton[b].ParentIndex)
        {
            if (b == headIndex)
            {
                inHeadSubtree = true;
                break;
            }
        }
        Assert.True(inHeadSubtree, $"Head derived to '{headBone}', outside the head subtree.");

        // No finger roles may be derived (fingers stay at rest under the DL solver).
        foreach (var role in derived.RoleToBone.Keys)
            Assert.DoesNotContain("Pinky", role.ToString());
    }
}
