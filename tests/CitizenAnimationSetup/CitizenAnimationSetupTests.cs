using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class CitizenAnimationSetupTests
{
    private static SkeletonModel Rig(string child = "hand_L", string? parent = "pelvis", float offset = 1)
        => SkeletonModel.Create(new[]
        {
            new BoneDefinition("pelvis", null, XForm.Identity),
            new BoneDefinition(child, parent, new XForm(new Vector3(offset, 0, 0), Quaternion.Identity))
        });

    [Fact]
    public void CompatibleSkeletonPasses() => Assert.Null(CitizenAnimationSetup.CompatibilityError(Rig(), Rig()));

    [Fact]
    public void MissingBoneDisablesSetup() => Assert.Contains("Missing bone", CitizenAnimationSetup.CompatibilityError(Rig("other"), Rig()));

    [Fact]
    public void SameNamesDifferentHierarchyFail() => Assert.Contains("parent", CitizenAnimationSetup.CompatibilityError(Rig(parent: null), Rig()));

    [Fact]
    public void SameNamesDifferentProportionsFail() => Assert.Contains("bind pose", CitizenAnimationSetup.CompatibilityError(Rig(offset: 2), Rig()));

    [Fact]
    public void SmallCompilerRoundingPasses() => Assert.Null(CitizenAnimationSetup.CompatibilityError(Rig(offset: 1.001f), Rig()));

    [Fact]
    public void MissingIkFails() => Assert.Contains("root_IK", CitizenAnimationSetup.CompatibilityError(Rig(), Rig("root_IK")));

    [Fact]
    public void MissingFaceIsOptional() => Assert.Null(CitizenAnimationSetup.CompatibilityError(Rig(), Rig("face_lid_L")));

    private static string Custom(float scale = 0.3937f) => VmdlWriter.GenerateStandalone("", Array.Empty<AnimEntry>(), scale, "pelvis",
        meshFilePath: "custom/skin.fbx", materialRemaps: new Dictionary<string, string> { ["skin"] = "custom/skin.vmat" });

    private static string Shipped(bool human)
    {
        var prefix = human ? "human" : "citizen";
        return VmdlWriter.Kv3Header + $$"""
        { rootNode = {
            anim_graph_name = "models/{{prefix}}.vanmgrph"
            children = [
                { _class = "ModelModifierList" children = [ { _class = "ModelModifier_ScaleAndMirror" scale = 0.3937 } ] },
                { _class = "AnimationList" children = [
                    { _class = "Prefab" target_file = "{{prefix}}_animations.vmdl_prefab" },
                    { _class = "Prefab" target_file = "{{prefix}}_animations_menu.vmdl_prefab" } ] },
                { _class = "AnimConstraintList" children = [
                    { _class = "Prefab" target_file = "{{prefix}}_constraints.vmdl_prefab" },
                    {{(human ? "{ _class = \"Folder\" name = \"CopyPinky\" children = [ { _class = \"AnimConstraintOrient\" name = \"pinky\" weight = 1.0 } ] }" : "")}}
                ] },
                { _class = "IKData" children = [ { _class = "Prefab" target_file = "ik.vmdl_prefab" } ] },
                { _class = "WeightListList" children = [ { _class = "WeightList" name = "Blink" } ] },
                { _class = "RenderMeshList" children = [ { _class = "RenderMeshFile" filename = "stock.fbx" } ] },
                { _class = "MaterialGroupList" children = [ { _class = "MaterialGroup" name = "stock" } ] }
            ]
        } }
        """;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupIncludesAllAnimationPrefabsAndGraphWithoutReplacingSkin(bool human)
    {
        var result = CitizenAnimationSetup.Apply(Custom(), Shipped(human));
        var prefix = human ? "human" : "citizen";
        Assert.Contains($"models/{prefix}.vanmgrph", result);
        Assert.Contains($"{prefix}_animations.vmdl_prefab", result);
        Assert.Contains($"{prefix}_animations_menu.vmdl_prefab", result);
        Assert.Contains("ik.vmdl_prefab", result);
        Assert.Contains("Blink", result);
        Assert.Contains("custom/skin.fbx", result);
        Assert.Contains("custom/skin.vmat", result);
        Assert.DoesNotContain("stock.fbx", result);
        Assert.Equal(human, result.Contains("CopyPinky"));
        Assert.Equal(human, result.Contains("HumanoidRetargeter_CitizenConstraints"));
        Assert.Equal(result, CitizenAnimationSetup.Apply(result, Shipped(human)));
    }

    [Fact]
    public void AddedClipsCannotDisableStockCopyPinky()
    {
        var setup = CitizenAnimationSetup.Apply(Custom(), Shipped(true));
        var result = VmdlAugmenter.Augment(setup, new[] { new AnimEntry { Name = "custom_punch", SourceFilename = "punch.dmx" } }, out _,
            new AugmentOptions { NeutralizePinkyConstraints = true });
        Assert.Contains("weight = 1.0", result);
        Assert.Contains("custom_punch", result);
        Assert.Contains("human_animations.vmdl_prefab", result);
    }

    [Fact]
    public void WrongScaleFails() => Assert.Throws<InvalidOperationException>(() => CitizenAnimationSetup.Apply(Custom(1), Shipped(true)));

    [Fact]
    public void ExistingAnimationsAreNotOverwritten()
    {
        var custom = VmdlWriter.GenerateStandalone("", new[] { new AnimEntry { Name = "mine", SourceFilename = "mine.dmx" } }, .3937f, "pelvis", meshFilePath: "skin.fbx");
        Assert.Throws<InvalidOperationException>(() => CitizenAnimationSetup.Apply(custom, Shipped(true)));
    }

    [Fact]
    public void AnimationOnlyBaseModelRejected() => Assert.Throws<InvalidOperationException>(() =>
        CitizenAnimationSetup.Apply(VmdlWriter.GenerateStandalone("base.vmdl", Array.Empty<AnimEntry>(), .3937f, "pelvis"), Shipped(true)));
}
