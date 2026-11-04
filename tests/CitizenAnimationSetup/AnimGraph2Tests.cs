using System.Text.Json.Nodes;
using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class AnimGraph2Tests
{
    // A Human-style AnimGraph2 graph: clips nested in a blend's point graphs, a pose node, a variation override
    const string Graph = """
        { "Version": 1, "Model": "models/human.vmdl", "Skeleton": "models/human.skel",
          "parameters": [ { "Identifier": "p", "Type": "Float", "Name": "move_x" } ],
          "nodes": [
            { "_class": "PoseResultNode", "Pose": { "Identifier": "blend", "Output": "Result" }, "Identifier": "result", "Position": "640,0", "HandleOffsets": {} },
            { "_class": "Blend1DNode", "Identifier": "blend", "Position": "320,0", "HandleOffsets": {}, "Points": [
              { "Name": "walk", "Value": 100, "Graph": { "Version": 1, "nodes": [
                { "_class": "AnimationClipNode", "Loop": true, "Identifier": "walk",
                  "DefaultVariationData": { "AnimationName": "models/clips/walk_forward.anim", "SpeedMultiplier": 1.25 },
                  "Overrides": [ { "VariationID": "Female", "VariationData": { "AnimationName": "models/clips/Walk_N.anim" } } ] } ] } },
              { "Name": "run", "Value": 250, "Graph": { "Version": 1, "nodes": [
                { "_class": "AnimationClipNode", "Identifier": "run", "DefaultVariationData": { "AnimationName": "models/clips/sprint_forward.anim" }, "Overrides": [] } ] } },
              { "Name": "back", "Value": -100, "Graph": { "Version": 1, "nodes": [
                { "_class": "AnimationPoseNode", "Identifier": "back", "DefaultVariationData": { "AnimationName": "models/clips/walk_backward.anim" }, "Overrides": [] } ] } }
            ] }
          ],
          "variations": [ { "ID": "Female", "ParentID": "Default", "Model": "models/female.vmdl", "Skeleton": "models/human.skel" } ] }
        """;

    static StockAnimationSlot Slot(string id) => StockAnimationGraph.Slots.Single(s => s.Id == id);

    static string[] AnimationNames(string graph) => AnimGraph2Document.Animations(graph).ToArray();

    [Fact]
    public void AnimationsListsEveryClipAndPoseOnceIncludingOverrides()
        => Assert.Equal(new[] { "models/clips/walk_forward.anim", "models/clips/Walk_N.anim", "models/clips/sprint_forward.anim",
            "models/clips/walk_backward.anim" }, AnimationNames(Graph));

    [Fact]
    public void CopyForModelKeepsLogicAndSkeletonButDropsVariations()
    {
        var copy = JsonNode.Parse(AnimGraph2Document.CopyForModel(Graph, "custom/body.vmdl"))!.AsObject();
        Assert.Equal("custom/body.vmdl", (string?)copy["Model"]);
        Assert.Equal("models/human.skel", (string?)copy["Skeleton"]);
        Assert.Null(copy["variations"]);
        Assert.Equal("move_x", (string?)copy["parameters"]![0]!["Name"]);
        Assert.DoesNotContain("models/clips/Walk_N.anim", AnimationNames(copy.ToJsonString()));
        Assert.Contains("\"SpeedMultiplier\": 1.25", copy.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void RewriteAnimationsReplacesOnlyWhatTheCallbackNames()
    {
        var rewritten = AnimGraph2Document.RewriteAnimations(Graph,
            name => name.EndsWith("sprint_forward.anim") ? "custom/run.anim" : null, out var references);
        Assert.Equal(1, references);
        Assert.Equal(new[] { "models/clips/walk_forward.anim", "models/clips/Walk_N.anim", "custom/run.anim",
            "models/clips/walk_backward.anim" }, AnimationNames(rewritten));
    }

    [Fact]
    public void ReplaceSwapsTheSlotsClipsInEveryNodeAndOverride()
    {
        var replacement = StockAnimationGraph.ReplaceAnimGraph2(Graph, Slot("walk_n"), "custom/hr_replace_walk_n_a.anim", "custom/body.vmdl");
        // walk_forward (the AnimGraph2 name) and Walk_N (the sequence name, in the variation override)
        Assert.Equal(2, replacement.References);
        Assert.Empty(replacement.Subgraphs);
        var names = AnimationNames(AnimGraph2Document.CopyForModel(replacement.Graph, "custom/body.vmdl"));
        Assert.Equal(new[] { "custom/hr_replace_walk_n_a.anim", "models/clips/sprint_forward.anim", "models/clips/walk_backward.anim" }, names);
        Assert.Equal("custom/body.vmdl", (string?)JsonNode.Parse(replacement.Graph)!["Model"]);
    }

    [Fact]
    public void ReplacingAgainTakesOverTheEarlierReplacement()
    {
        var first = StockAnimationGraph.ReplaceAnimGraph2(Graph, Slot("run_n"), "custom/hr_replace_run_n_a.anim", "custom/body.vmdl");
        var second = StockAnimationGraph.ReplaceAnimGraph2(first.Graph, Slot("run_n"), "custom/hr_replace_run_n_b.anim", "custom/body.vmdl");
        Assert.Equal(1, second.References);
        Assert.Contains("custom/hr_replace_run_n_b.anim", AnimationNames(second.Graph));
        Assert.DoesNotContain("custom/hr_replace_run_n_a.anim", AnimationNames(second.Graph));
    }

    [Fact]
    public void ReplaceFailsClosedWhenTheGraphHasNoClipForTheSlot()
        => Assert.Throws<InvalidOperationException>(() => StockAnimationGraph.ReplaceAnimGraph2(Graph, Slot("crouchwalk_ne"), "x.anim", "custom/body.vmdl"));

    [Theory]
    [InlineData("walk_ne", "walk_forward_right")]
    [InlineData("walk_sw", "walk_backward_left")]
    [InlineData("run_e", "sprint_right")]
    [InlineData("crouchwalk_n", "CrouchWalk_N")]
    [InlineData("idle", "IdlePose_Default")]
    public void SlotsKnowTheHumansAnimGraph2ClipNames(string slot, string clip)
        => Assert.True(StockAnimationGraph.IsSlotClip(Slot(slot), "models/citizen_human/animations/clips/" + clip + ".anim"));

    [Fact]
    public void NonGraphTextFailsLoudly()
    {
        Assert.Throws<InvalidOperationException>(() => AnimGraph2Document.CopyForModel("{ \"Version\": 1 }", "x.vmdl"));
        Assert.Throws<InvalidOperationException>(() => AnimGraph2Document.SkeletonOf("not json"));
    }

    const string Vmdl = VmdlWriter.Kv3Header + "{ rootNode = { anim_graph_name = \"models/human.vanmgrph\" children = [ ] } }";

    [Fact]
    public void VmdlGraphNameAttachAndDetach()
    {
        Assert.Equal("", AnimGraph2Document.GraphName(Vmdl));
        Assert.Same(Vmdl, AnimGraph2Document.Detach(Vmdl));
        var attached = AnimGraph2Document.Attach(Vmdl, "custom/graphs/body.ag");
        Assert.Equal("custom/graphs/body.ag", AnimGraph2Document.GraphName(attached));
        Assert.Equal("models/human.vanmgrph", StockAnimationGraph.GraphName(attached));
        var detached = AnimGraph2Document.Detach(attached);
        Assert.Equal("", AnimGraph2Document.GraphName(detached));
        Assert.Equal("models/human.vanmgrph", StockAnimationGraph.GraphName(detached));
        Assert.Equal("custom/graphs/body.ag", AnimGraph2Document.GraphPath("custom/", "body"));
    }

    [Fact]
    public void ClipAndSkeletonSourcesAreKv3Text()
    {
        var clip = AnimGraph2Sources.Clip("custom/walk.dmx", "models/human.skel", 0.5);
        Assert.StartsWith(AnimGraph2Sources.GenericHeader, clip);
        var root = (KvObject)Kv3.Parse(clip).Root;
        Assert.Equal("CClipDocument", root.GetString("_class"));
        Assert.Equal("custom/walk.dmx", root.GetString("m_sourceFilename"));
        Assert.Equal("models/human.skel", root.GetString("m_animationSkeletonName"));
        Assert.Equal(0.5, ((KvDouble)root["m_flImportScale"]).Value);
        var skeleton = (KvObject)Kv3.Parse(AnimGraph2Sources.Skeleton("custom/body.fbx", 0.3937)).Root;
        Assert.Equal("custom/body.fbx", skeleton.GetString("m_sourceFilename"));
        Assert.Equal(0.3937, ((KvDouble)skeleton["m_flGlobalScale"]).Value);
    }

    static string Shipped(bool withLegacyGraph) => VmdlWriter.Kv3Header + $$"""
        { rootNode = {
            {{(withLegacyGraph ? "anim_graph_name = \"models/human.vanmgrph\"" : "")}}
            animation_graph_name = "models/human.ag"
            children = [
                { _class = "ModelModifierList" children = [ { _class = "ModelModifier_ScaleAndMirror" scale = 0.3937 } ] },
                { _class = "AnimationList" children = [ { _class = "Prefab" target_file = "human_animations.vmdl_prefab" } ] }
            ]
        } }
        """;

    static string Custom() => VmdlWriter.GenerateStandalone("", Array.Empty<AnimEntry>(), 0.3937f, "pelvis", meshFilePath: "custom/skin.fbx");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetupCopiesTheAnimGraph2GraphBesideAnyLegacyOne(bool withLegacyGraph)
    {
        var setup = CitizenAnimationSetup.Apply(Custom(), Shipped(withLegacyGraph));
        Assert.Equal("models/human.ag", AnimGraph2Document.GraphName(setup));
        Assert.Equal(withLegacyGraph ? "models/human.vanmgrph" : "", StockAnimationGraph.GraphName(setup));
    }

    [Fact]
    public void SetupRefusesADifferentAnimGraph2Graph()
        => Assert.Throws<InvalidOperationException>(() =>
            CitizenAnimationSetup.Apply(AnimGraph2Document.Attach(Custom(), "other.ag"), Shipped(true)));
}
