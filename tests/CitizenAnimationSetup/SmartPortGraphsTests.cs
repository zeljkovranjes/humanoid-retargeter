using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>Bone renaming that follows a graph into its subgraphs (the stock Citizen graph keeps its logic there).</summary>
public class SmartPortGraphsTests
{
    static string Graph(string cls, string body) => VmdlWriter.Kv3Header + "{ _class = \"" + cls + "\" " + body + " }";

    static readonly Dictionary<string, string> Renames = new()
    {
        ["pelvis"] = "ValveBiped_Bip01_Pelvis", ["hand_R"] = "ValveBiped_Bip01_R_Hand", ["hold_R"] = "hold_R",
    };

    static Dictionary<string, string> Files(params (string Path, string Text)[] files)
        => files.ToDictionary(f => f.Path, f => f.Text, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void SubgraphsNamingRenamedBonesAreCopiedAndReferenced()
    {
        var entry = Graph("CAnimationGraph", """
            m_nodes = [
              { m_subGraphFilename = "models/citizen/subgraphs/core.vsubgrph" },
              { m_subGraphFilename = "models\\citizen\\subgraphs\\face.vsubgrph" },
              { m_bone = "hand_R" }
            ]
            """);
        var files = Files(
            ("models/citizen/subgraphs/core.vsubgrph", Graph("CAnimationSubGraph", "m_hipBoneName = \"pelvis\" m_attachmentName = \"hold_R\"")),
            ("models/citizen/subgraphs/face.vsubgrph", Graph("CAnimationSubGraph", "m_sequenceName = \"Eyes_Blink\"")));
        var copy = SmartPortGraphs.Rebase(entry, Renames, p => files.GetValueOrDefault(p), "models/x_src/subgraphs");

        var core = Assert.Single(copy.Subgraphs);
        Assert.Equal("models/x_src/subgraphs/core.vsubgrph", core.Key);
        Assert.Contains("m_hipBoneName = \"ValveBiped_Bip01_Pelvis\"", core.Value);
        Assert.Contains("m_attachmentName = \"hold_R\"", core.Value);
        Assert.Contains("\"models/x_src/subgraphs/core.vsubgrph\"", copy.Graph);
        Assert.Contains("face.vsubgrph", copy.Graph); // unchanged: still the shipped one
        Assert.DoesNotContain("x_src/subgraphs/face", copy.Graph);
        Assert.Contains("\"ValveBiped_Bip01_R_Hand\"", copy.Graph);
        Assert.Empty(copy.Unread);
    }

    [Fact]
    public void AChangeDeepInsideCopiesEveryGraphAboveIt()
    {
        var entry = Graph("CAnimationGraph", "m_subGraphFilename = \"a.vsubgrph\"");
        var files = Files(
            ("a.vsubgrph", Graph("CAnimationSubGraph", "m_subGraphFilename = \"b.vsubgrph\"")),
            ("b.vsubgrph", Graph("CAnimationSubGraph", "m_hipBoneName = \"pelvis\" m_subGraphFilename = \"a.vsubgrph\"")));
        var copy = SmartPortGraphs.Rebase(entry, Renames, p => files.GetValueOrDefault(p), "out");
        Assert.Equal(new[] { "out/a.vsubgrph", "out/b.vsubgrph" }, copy.Subgraphs.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Contains("\"out/b.vsubgrph\"", copy.Subgraphs["out/a.vsubgrph"]);
        Assert.Contains("\"out/a.vsubgrph\"", copy.Graph);
        Assert.Contains("\"a.vsubgrph\"", copy.Subgraphs["out/b.vsubgrph"]); // a reference back up the chain is not followed again
    }

    [Fact]
    public void UnreadableSubgraphsAreListedAndIdentityNamesChangeNothing()
    {
        var entry = Graph("CAnimationGraph", "m_subGraphFilename = \"missing.vsubgrph\" m_bone = \"hold_R\"");
        var copy = SmartPortGraphs.Rebase(entry, Renames, _ => null, "out");
        Assert.Empty(copy.Subgraphs);
        Assert.Equal(new[] { "missing.vsubgrph" }, copy.Unread);
        Assert.Contains("\"missing.vsubgrph\"", copy.Graph);
    }

    [Fact]
    public void SameNamedSubgraphsFromDifferentFoldersGetDistinctCopies()
    {
        var entry = Graph("CAnimationGraph", "m_nodes = [ { m_subGraphFilename = \"one/core.vsubgrph\" }, { m_subGraphFilename = \"two/core.vsubgrph\" } ]");
        var files = Files(
            ("one/core.vsubgrph", Graph("CAnimationSubGraph", "m_bone = \"pelvis\"")),
            ("two/core.vsubgrph", Graph("CAnimationSubGraph", "m_bone = \"hand_R\"")));
        var copy = SmartPortGraphs.Rebase(entry, Renames, p => files.GetValueOrDefault(p), "out");
        Assert.Equal(new[] { "out/core.vsubgrph", "out/core_2.vsubgrph" }, copy.Subgraphs.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
