using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortGraphLayoutTests
{
    static string Graph => VmdlWriter.Kv3Header + """
        { _class = "CAnimationGraph"
          m_nodeManager = { m_nodes = [
            { key = { m_id = 1 } value = { _class = "CSequenceAnimNode" m_nNodeID = { m_id = 1 }
              m_vecPosition = [-725.25, 113.125] m_sequenceName = "idle" m_bone = "hand_r" } },
            { key = { m_id = 2 } value = { _class = "CRootAnimNode" m_nNodeID = { m_id = 2 }
              m_vecPosition = [1500, -30.5]
              m_inputConnection = { m_nodeID = { m_id = 1 } m_outputID = { m_id = 4294967295 } } } }
          ] m_nodeGroups = [{ name = "Weapon poses" position = [-800, -200] size = [2500, 600] }] }
        }
        """;

    static KvObject Manager(string text) => (KvObject)((KvObject)Kv3.Parse(text).Root)["m_nodeManager"];
    static KvObject[] Nodes(KvObject manager) => ((KvArray)manager["m_nodes"]).Items.Cast<KvObject>()
        .Select(n => (KvObject)n["value"]).ToArray();

    [Fact]
    public void CopyAndBoneRebaseKeepNodePositionsAndGroups()
    {
        var copied = StockAnimationGraph.CopyForModel(SmartPortSetup.Rebase(Graph,
            new Dictionary<string, string> { ["hand_r"] = "hand_R" }), "custom.vmdl");
        var before = Manager(Graph);
        var after = Manager(copied);
        for (var i = 0; i < Nodes(before).Length; i++)
            Assert.True(KvValue.DeepEquals(Nodes(before)[i]["m_vecPosition"], Nodes(after)[i]["m_vecPosition"]));
        Assert.True(KvValue.DeepEquals(before["m_nodeGroups"], after["m_nodeGroups"]));
        Assert.Equal("hand_R", Nodes(after)[0].GetString("m_bone"));
    }

    [Fact]
    public void ExtendingKeepsExistingLayoutAndSeparatesAddedNodes()
    {
        var clips = Enumerable.Range(0, 12).Select(i => new SmartPortClip("clip_" + i, true, i % 2 == 0, false)).ToArray();
        var output = SmartPortExtension.ExtendGraph(Graph, "custom.vmdl", clips, out _);
        var before = Manager(Graph);
        var after = Manager(output);
        Assert.True(KvValue.DeepEquals(before["m_nodeGroups"], after["m_nodeGroups"]));
        for (var i = 0; i < Nodes(before).Length; i++)
            Assert.True(KvValue.DeepEquals(Nodes(before)[i]["m_vecPosition"], Nodes(after)[i]["m_vecPosition"]));
        var positions = Nodes(after).Skip(2).Select(n => (KvArray)n["m_vecPosition"]).ToArray();
        Assert.All(positions, p => Assert.True(((KvDouble)p.Items[0]).Value >= 2000));
        Assert.Equal(positions.Length, positions.Select(p => (((KvDouble)p.Items[0]).Value, ((KvDouble)p.Items[1]).Value)).Distinct().Count());
    }
}
