using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class AnimationMetadataTests
{
    private static KvObject Root(string text) => (KvObject)((KvObject)Kv3.Parse(text).Root)["rootNode"];
    private static KvObject Category(string text, string category)
        => ((KvArray)Root(text)["children"]).Items.OfType<KvObject>().Single(n => n.GetString("_class") == category);

    [Fact]
    public void CompleteNestedAnimationMetadataSurvivesSetupAndGraphCopy()
    {
        var stock = VmdlWriter.Kv3Header + """
        { rootNode = { anim_graph_name = "stock.vanmgrph" children = [
            { _class = "AnimationList" default_root_bone_name = "pelvis" children = [
                { _class = "Prefab" target_file = "complete_stock_animations.vmdl_prefab" },
                { _class = "Folder" name = "Locomotion" children = [
                    { _class = "AnimFile" name = "Walk_N" source_filename = "walk.fbx" looping = true fps = 30 children = [
                        { _class = "AnimEvent" event_class = "AE_FOOTSTEP" event_frame = 7 event_keys = { Foot = "0" Attachment = "foot_L" Volume = 0.7 } },
                        { _class = "ExtractMotion" root_bone_name = "pelvis" },
                        { _class = "AnimTag" name = "contact" frame = 7 },
                        { _class = "FutureAnimationSetting" data = [ 1, 2, 3 ] }
                    ] },
                    { _class = "AnimFile" name = "idle_delta" source_filename = "idle.fbx" children = [
                        { _class = "AnimSubtract" anim_name = "idle" frame = 12 },
                        { _class = "AnimWeightList" name = "UpperBody" }
                    ] },
                    { _class = "2DBlend" name = "Walk" blend_anim_list = [ [ "Walk_N" ] ] }
                ] }
            ] },
            { _class = "AnimConstraintList" children = [ { _class = "Folder" name = "CopyPinky" children = [ { _class = "AnimConstraintOrient" weight = 1.0 } ] } ] },
            { _class = "BoneMarkupList" children = [ { _class = "BoneMarkup" name = "leg" } ] },
            { _class = "AttachmentList" children = [ { _class = "Attachment" name = "foot_L" bone = "ankle_L" } ] },
            { _class = "IKData" children = [ { _class = "IKChain" name = "leg_L" } ] },
            { _class = "PoseParamList" children = [ { _class = "PoseParameter" name = "move_x" } ] },
            { _class = "WeightListList" children = [ { _class = "WeightList" name = "UpperBody" } ] },
            { _class = "GameDataList" children = [ { _class = "Prefab" target_file = "game_data.vmdl_prefab" } ] }
        ] } }
        """;
        var custom = VmdlWriter.GenerateStandalone("", Array.Empty<AnimEntry>(), .3937f, "pelvis", meshFilePath: "custom.fbx",
            materialRemaps: new Dictionary<string, string> { ["body"] = "custom.vmat" });
        var setup = CitizenAnimationSetup.Apply(custom, stock);
        setup = StockAnimationGraph.Attach(setup, "output/graphs/custom.vanmgrph");
        foreach (var category in new[] { "AnimationList", "BoneMarkupList", "AttachmentList", "IKData", "PoseParamList", "WeightListList", "GameDataList" })
            Assert.True(KvValue.DeepEquals(Category(stock, category), Category(setup, category)), category + " was not copied completely.");
        var constraints = Category(setup, "AnimConstraintList");
        var wrapper = Assert.IsType<KvObject>(Assert.Single(((KvArray)constraints["children"]).Items));
        Assert.True(KvValue.DeepEquals(Category(stock, "AnimConstraintList")["children"], wrapper["children"]));
        Assert.True(KvValue.DeepEquals(Category(custom, "RenderMeshList"), Category(setup, "RenderMeshList")));
        Assert.True(KvValue.DeepEquals(Category(custom, "MaterialGroupList"), Category(setup, "MaterialGroupList")));

        // Adding a replacement sequence must not flatten, rebuild or strip stock metadata.
        var augmented = VmdlAugmenter.Augment(setup, new[] { new AnimEntry { Name = "replacement", SourceFilename = "replacement.dmx" } }, out _);
        var originalEntries = ((KvArray)Category(setup, "AnimationList")["children"]).Items;
        var augmentedEntries = ((KvArray)Category(augmented, "AnimationList")["children"]).Items;
        foreach (var original in originalEntries) Assert.Contains(augmentedEntries, n => KvValue.DeepEquals(original, n));
    }
}
