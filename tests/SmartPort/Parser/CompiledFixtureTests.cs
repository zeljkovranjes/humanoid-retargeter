using HumanoidRetargeter.Editor;
using HumanoidRetargeter.Target;
using Xunit;

namespace SmartPort.Parser.Tests;

public sealed class CompiledFixtureFactAttribute : FactAttribute
{
    public CompiledFixtureFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")))
            Skip = "Set HR_SMART_PORT_FIXTURE to an installed sboxmp.frank_mp package directory.";
    }
}

public class CompiledFixtureTests
{
    [CompiledFixtureFact]
    public void EmbeddedRecoveryKeepsSequencesEventsAndActualGraph()
    {
        var fixture = Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!;
        var output = Path.Combine(Path.GetTempPath(), "hr-smart-port-parser-" + Guid.NewGuid().ToString("N"));
        var paths = Directory.GetFiles(fixture, "*_c", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(fixture, p)[..^2].Replace('\\', '/'), p => p, StringComparer.OrdinalIgnoreCase);
        const string model = "models/player/human/frank_mp.vmdl";
        const string graph = "models/player/mplayer/mplayer_animgraph.vanmgrph";
        // Leave artifacts outside the source tree for native compiler verification.
        var recovered = CompiledAssetRecovery.Recover(paths[model], model, output, "recovered", paths, default);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "recovered.vmdl"), recovered);
        var graphText = CompiledAssetRecovery.Recover(paths[graph], graph, output, "recovered", paths, default);
        var copiedGraph = StockAnimationGraph.CopyForModel(graphText, "recovered/model.vmdl");
        File.WriteAllText(Path.Combine(output, "recovered.vanmgrph"), copiedGraph);
        var doc = Kv3.Parse(recovered);
        var root = (KvObject)((KvObject)doc.Root)["rootNode"];
        var children = (KvArray)root["children"];
        var animations = children.Items.OfType<KvObject>().Single(n => n["_class"] is KvString s && s.Value == "AnimationList");
        Assert.Equal(391, CountClips(animations));
        Assert.Contains("AnimEvent", recovered);
        Assert.Contains("CAnimationGraph", copiedGraph);
        Assert.Contains("recovered/model.vmdl", copiedGraph);
        Assert.Equal(398, Directory.GetFiles(output, "*.dmx", SearchOption.AllDirectories).Length);
        Assert.DoesNotContain("AnimIncludeModel", recovered);
    }

    static int CountClips(KvObject node) =>
        (node["_class"] is KvString s && s.Value == "AnimFile" ? 1 : 0)
        + (node.GetOrNull("children") is KvArray a ? a.Items.OfType<KvObject>().Sum(CountClips) : 0);
}
