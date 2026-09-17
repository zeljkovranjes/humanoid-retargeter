using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortExtensionTests
{
    static string Model(string nodes) => VmdlWriter.Kv3Header + "{ rootNode = { children = [" + nodes + "] custom_setting = 17 } }";
    static string Graph(string parameters = "") => VmdlWriter.Kv3Header + """
        { _class = "CAnimationGraph"
          m_nodeManager = { m_nodes = [
            { key = { m_id = 1 } value = { _class = "CustomGameNode" m_nNodeID = { m_id = 1 } custom_data = "untouched" } },
            { key = { m_id = 2 } value = { _class = "CRootAnimNode" m_nNodeID = { m_id = 2 }
              m_inputConnection = { m_nodeID = { m_id = 1 } m_outputID = { m_id = 4294967295 } } } }
          ] }
          m_pParameterList = { m_Parameters = [
        """ + parameters + "] } custom_settings = { enabled = true } }";

    [Fact]
    public void MergeKeepsExistingClipsSettingsAndNamespacedSourceEvents()
    {
        var target = Model("""
            { _class = "RenderMeshList" children = [{ filename = "target.dmx" }] },
            { _class = "AnimConstraintList" children = [{ name = "MyConstraint" }] },
            { _class = "AnimationList" children = [
              { _class = "AnimFile" name = "walk" source_filename = "original.dmx" },
              { _class = "AnimFile" name = "hr_port_walk" } ] },
            { _class = "WeightListList" children = [{ _class = "WeightList" name = "upper" weight = 1 }] }
            """);
        var source = Model("""
            { _class = "AnimationList" children = [ { _class = "AnimFile" name = "walk"
              source_filename = "source.dmx" weight_list_name = "upper" children = [
                { _class = "AnimEvent" event_class = "AE_FOOTSTEP" event_frame = 7 event_keys = { Foot = "1" } },
                { _class = "AnimSubtract" anim_name = "walk" frame = 0 }
              ] } ] },
            { _class = "WeightListList" children = [{ _class = "WeightList" name = "upper" weight = 0.5 }] }
            """);
        var result = SmartPortExtension.MergeModel(target, source, "own.vanmgrph", out var names);
        Assert.Equal("hr_port_walk_2", names["walk"]);
        foreach (var text in new[] { "target.dmx", "original.dmx", "source.dmx", "MyConstraint", "AE_FOOTSTEP", "custom_setting = 17", "hr_port_upper" })
            Assert.Contains(text, result);
        Assert.Contains("anim_name = \"hr_port_walk_2\"", result);
        Assert.Equal("own.vanmgrph", StockAnimationGraph.GraphName(result));
    }

    [Fact]
    public void AnyGraphKeepsItsNodesParametersAndDefaultOutput()
    {
        var original = Graph("{ _class = \"CBoolAnimParameter\" m_name = \"game_action\" m_id = { m_id = 3 } }");
        var result = SmartPortExtension.ExtendGraph(original, "custom.vmdl", new[] { new SmartPortClip("dance", true, false, false) }, out var parameter);
        Assert.Equal(SmartPortExtension.Parameter, parameter);
        var before = (KvObject)Kv3.Parse(original).Root;
        var after = (KvObject)Kv3.Parse(result).Root;
        Assert.True(KvValue.DeepEquals(Nodes(before).Items[0], Nodes(after).Items[0]));
        Assert.True(KvValue.DeepEquals(Parameters(before).Items[0], Parameters(after).Items[0]));
        Assert.True(KvValue.DeepEquals(before["custom_settings"], after["custom_settings"]));
        var selector = Nodes(after).Items.Cast<KvObject>().Select(n => (KvObject)n["value"]).Single(n => n.GetString("_class") == "CSelectorAnimNode");
        var originalOutput = (KvObject)((KvObject)Nodes(before).Items[1])["value"];
        Assert.True(KvValue.DeepEquals(originalOutput["m_inputConnection"], ((KvArray)selector["m_children"]).Items[0]));
        Assert.Equal(0, ((KvLong)((KvObject)Parameters(after).Items[1])["m_defaultValue"]).Value);
        var ids = Nodes(after).Items.Cast<KvObject>().Select(n => ((KvLong)((KvObject)n["key"])["m_id"]).Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(3, ids); // Existing parameter IDs are reserved too.
    }

    [Fact]
    public void AdditiveClipsLayerOverTheExistingGraphAndNamesDoNotCollide()
    {
        var input = Graph("{ _class = \"CEnumAnimParameter\" m_name = \"hr_smartport_clip\" m_id = { m_id = 3 } }");
        var result = SmartPortExtension.ExtendGraph(input, "target.vmdl", new[] { new SmartPortClip("breathe", true, true, false) }, out var name);
        Assert.Equal("hr_smartport_clip_2", name);
        Assert.Contains("CAddAnimNode", result);
        Assert.Contains("m_bResetBase = false", result);
        Assert.Contains("m_bLoop = true", result);
        Assert.Contains("m_sequenceName = \"breathe\"", result);
    }

    [Fact]
    public void GraphWithoutParametersCanBeExtendedAndDisconnectedOutputIsRejected()
    {
        var input = Graph();
        var start = input.IndexOf("m_pParameterList", StringComparison.Ordinal);
        var end = input.IndexOf("custom_settings", StringComparison.Ordinal);
        input = input.Remove(start, end - start);
        var clip = new[] { new SmartPortClip("wave", false, false, false) };
        Assert.Contains("CEnumAnimParameter", SmartPortExtension.ExtendGraph(input, "x", clip, out _));
        Assert.Throws<InvalidOperationException>(() => SmartPortExtension.ExtendGraph(
            input.Replace("m_nodeID = { m_id = 1 }", "m_nodeID = { m_id = 4294967295 }"), "x", clip, out _));
    }

    [Fact]
    public void UnsupportedGraphsAndEmptyImportsFailBeforeWriting()
    {
        var clip = new[] { new SmartPortClip("wave", false, false, false) };
        Assert.Throws<InvalidOperationException>(() => SmartPortExtension.ExtendGraph(VmdlWriter.Kv3Header + "{ _class = \"CAnimationGraph\" }", "x", clip, out _));
        Assert.Throws<InvalidOperationException>(() => SmartPortExtension.ExtendGraph(Graph(), "x", Array.Empty<SmartPortClip>(), out _));
        Assert.Throws<InvalidOperationException>(() => SmartPortExtension.MergeModel(Model(""), Model(""), "x", out _));
    }

    static KvArray Nodes(KvObject root) => (KvArray)((KvObject)root["m_nodeManager"])["m_nodes"];
    static KvArray Parameters(KvObject root) => (KvArray)((KvObject)root["m_pParameterList"])["m_Parameters"];
}
