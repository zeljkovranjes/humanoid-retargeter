using System.Text.Json.Nodes;
using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortAnimGraph2Tests
{
    static string Graph(string parameters = "") => """
        { "Version": 1, "Model": "models/target.vmdl", "Skeleton": "models/target.skel",
          "parameters": [
        """ + parameters + """
          ],
          "nodes": [
            { "_class": "PoseResultNode", "Pose": { "Identifier": "clip", "Output": "Result" }, "Identifier": "result", "Position": "320,0", "HandleOffsets": {} },
            { "_class": "AnimationClipNode", "Loop": true, "DefaultVariationData": { "AnimationName": "models/idle.anim" }, "Identifier": "clip", "Position": "0,0", "HandleOffsets": {}, "Custom": "untouched" }
          ],
          "variations": [ { "ID": "Female", "Model": "models/female.vmdl" } ] }
        """;

    static readonly SmartPortClip[] Clips =
    {
        new("hr_port_walk", true, false, false),
        new("hr_port_jump", false, false, false),
        new("hr_port_breathe", true, true, false),
    };

    static Dictionary<string, string> Animations(IEnumerable<SmartPortClip> clips)
        => clips.ToDictionary(c => c.Name, c => "port/clips/" + c.Name + ".anim");

    static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var (_, value) in obj)
                foreach (var child in Objects(value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                foreach (var child in Objects(item)) yield return child;
    }

    static string Class(JsonObject node) => node["_class"]?.GetValue<string>() ?? "";

    [Fact]
    public void ExtendKeepsTheOriginalGraphAsOptionZeroAndAddsEachClip()
    {
        var text = SmartPortAnimGraph2.ExtendGraph(Graph(), "port/out.vmdl", Clips, Animations(Clips), out var parameter);
        var root = JsonNode.Parse(text)!.AsObject();

        Assert.Equal("hr_smartport_clip", parameter);
        Assert.Equal("port/out.vmdl", root["Model"]!.GetValue<string>());
        Assert.Null(root["variations"]); // the copy drives one model
        var option = root["parameters"]!.AsArray().Single()!;
        Assert.Equal("ID", option["Type"]!.GetValue<string>());
        Assert.Equal(new[] { SmartPortExtension.OriginalOption, "hr_port_walk", "hr_port_jump", "hr_port_breathe" },
            option["ExpectedValues"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(SmartPortExtension.OriginalOption, option["RuntimeDefaultID"]!.GetValue<string>());

        // Root output -> additive layer blend -> state machine whose first state is the untouched original graph
        var nodes = root["nodes"]!.AsArray().OfType<JsonObject>().ToArray();
        var result = Assert.Single(nodes, n => Class(n) == "PoseResultNode");
        var blend = nodes.Single(n => n["Identifier"]!.GetValue<string>() == result["Pose"]!["Identifier"]!.GetValue<string>());
        Assert.Equal("LayerBlendNode", Class(blend));
        var main = nodes.Single(n => n["Identifier"]!.GetValue<string>() == blend["Base"]!["Identifier"]!.GetValue<string>());
        Assert.Equal("StateMachineNode", Class(main));
        var states = main["States"]!.AsArray();
        Assert.Equal(new[] { SmartPortExtension.OriginalOption, "hr_port_walk", "hr_port_jump" }, states.Select(s => s!["Name"]!.GetValue<string>()));
        Assert.Equal(SmartPortExtension.OriginalOption, main["DefaultState"]!.GetValue<string>());
        var original = states[0]!["Graph"]!["nodes"]!.AsArray();
        Assert.Contains(original, n => n!["Identifier"]!.GetValue<string>() == "result");
        Assert.Contains(original, n => n!["Custom"]?.GetValue<string>() == "untouched");

        // The additive clip keeps the main machine on the original graph and plays in the additive layer
        var toOriginal = main["GlobalGraph"]!["nodes"]!.AsArray().OfType<JsonObject>()
            .Single(n => n["StateName"]?.GetValue<string>() == SmartPortExtension.OriginalOption);
        var condition = main["GlobalGraph"]!["nodes"]!.AsArray().OfType<JsonObject>()
            .Single(n => n["Identifier"]!.GetValue<string>() == toOriginal["Condition"]!["Identifier"]!.GetValue<string>());
        Assert.Equal(new[] { SmartPortExtension.OriginalOption, "hr_port_breathe" }, condition["Ids"]!.AsArray().Select(v => v!.GetValue<string>()));
        var layer = nodes.Single(n => Class(n) == "StateMachineLayerNode");
        Assert.Equal("Additive", layer["BlendMode"]!.GetValue<string>());
        var additive = nodes.Single(n => n["Identifier"]!.GetValue<string>() == layer["StateMachine"]!["Identifier"]!.GetValue<string>());
        Assert.Equal(new[] { "Off", "BlendTree" }, additive["States"]!.AsArray().Select(s => s!["Type"]!.GetValue<string>()));

        var clips = Objects(root).Where(n => Class(n) == "AnimationClipNode").ToArray();
        Assert.Equal(4, clips.Length);
        Assert.Contains(clips, c => c["DefaultVariationData"]!["AnimationName"]!.GetValue<string>() == "port/clips/hr_port_jump.anim" && !c["Loop"]!.GetValue<bool>());
        var ids = Objects(root).Where(n => n["Identifier"] is JsonValue && n["_class"] is not null).Select(n => n["Identifier"]!.GetValue<string>()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void ExtendIsDeterministicAndAvoidsUsedParameterNames()
    {
        var used = """{ "Identifier": "p", "Type": "Float", "Name": "HR_SMARTPORT_CLIP" }""";
        var first = SmartPortAnimGraph2.ExtendGraph(Graph(used), "port/out.vmdl", Clips, Animations(Clips), out var parameter);
        var second = SmartPortAnimGraph2.ExtendGraph(Graph(used), "port/out.vmdl", Clips, Animations(Clips), out _);
        Assert.Equal("hr_smartport_clip_2", parameter);
        Assert.Equal(first, second);
    }

    [Fact]
    public void LongClipListsNestStateMachinesWithinTheStateLimit()
    {
        var clips = Enumerable.Range(0, 150).Select(i => new SmartPortClip("hr_port_clip" + i, true, i % 50 == 0, false)).ToArray();
        var root = JsonNode.Parse(SmartPortAnimGraph2.ExtendGraph(Graph(), "port/out.vmdl", clips, Animations(clips), out _))!;
        var machines = Objects(root).Where(n => Class(n) == "StateMachineNode").ToArray();
        Assert.All(machines, m => Assert.InRange(m["States"]!.AsArray().Count, 1, SmartPortAnimGraph2.MaxStates));
        Assert.Contains(machines, m => m["States"]![0]!["Name"]!.GetValue<string>() == SmartPortExtension.OriginalOption);
        var played = Objects(root).Where(n => Class(n) == "AnimationClipNode")
            .Select(n => n["DefaultVariationData"]!["AnimationName"]!.GetValue<string>()).ToHashSet();
        Assert.All(clips, c => Assert.Contains("port/clips/" + c.Name + ".anim", played));
    }

    [Fact]
    public void ExtendFailsClearly()
    {
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.ExtendGraph(Graph(), "out.vmdl", Clips, new Dictionary<string, string>(), out _));
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.ExtendGraph(Graph(), "out.vmdl", Array.Empty<SmartPortClip>(), Animations(Clips), out _));
        var disconnected = Graph().Replace("\"Identifier\": \"clip\", \"Output\"", "\"Identifier\": \"gone\", \"Output\"");
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.ExtendGraph(disconnected, "out.vmdl", Clips, Animations(Clips), out _));
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.ExtendGraph("not json", "out.vmdl", Clips, Animations(Clips), out _));
    }

    [Theory]
    [InlineData("m_flGlobalScale = 0.3937", 1 / 0.3937)]
    [InlineData("m_flGlobalScale = 1", 1.0)]
    [InlineData("m_rootBoneName = \"\"", 1.0)]
    public void RecoveredClipsUndoTheSkeletonScale(string setting, double expected)
    {
        var skeleton = AnimGraph2Sources.GenericHeader + "\n{ m_sourceFilename = \"body.fbx\" " + setting + " }";
        Assert.Equal(expected, SmartPortAnimGraph2.ClipImportScale(skeleton), 9);
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.ClipImportScale(AnimGraph2Sources.GenericHeader + "\n{ m_flGlobalScale = 0.0 }"));
    }

    [Theory]
    [InlineData("m_flGlobalScale = 0.3937", 1.0)]
    [InlineData("m_flGlobalScale = 1.0", 0.3937)]
    [InlineData("m_rootBoneName = \"\"", 0.3937)]
    public void CentimetreClipsLandInEngineUnitsOnAnySkeleton(string setting, double expected)
    {
        var skeleton = AnimGraph2Sources.GenericHeader + "\n{ m_sourceFilename = \"body.fbx\" " + setting + " }";
        // Exact: the scale is written into the clip source
        Assert.Equal(expected, SmartPortAnimGraph2.CentimetreClipScale(skeleton));
        Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2.CentimetreClipScale(AnimGraph2Sources.GenericHeader + "\n{ m_flGlobalScale = -1.0 }"));
    }

    [Fact]
    public void AnimationFilesMapsEachClipToItsSource()
    {
        var vmdl = VmdlWriter.Kv3Header + """
            { rootNode = { children = [ { _class = "AnimationList" children = [
              { _class = "AnimFile" name = "hr_port_walk" source_filename = "port/source/walk.dmx" },
              { _class = "Folder" children = [ { _class = "AnimFile" name = "hr_port_jump" source_filename = "port/source/jump.dmx" } ] } ] } ] } }
            """;
        var files = SmartPortAnimGraph2.AnimationFiles(vmdl);
        Assert.Equal("port/source/walk.dmx", files["hr_port_walk"]);
        Assert.Equal("port/source/jump.dmx", files["hr_port_jump"]);
    }
}
