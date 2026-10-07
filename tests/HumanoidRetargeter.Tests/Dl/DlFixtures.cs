using System.Text.Json;
using HumanoidRetargeter.Core.Dl;
using HumanoidRetargeter.Core.Formats.Bvh;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Tests.Dl;

/// <summary>Shared loaders for the DL (SAME) test fixtures: the committed weight blob,
/// the Python-generated golden tensors, the source BVH and the shipped target rig.</summary>
internal static class DlFixtures
{
    /// <summary>Repo-root-relative file (walks up from the test output directory).</summary>
    public static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static string FixtureFile(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

    /// <summary>The committed inference weights (Assets/humanoid_retargeter/dl/same_v1.weights).</summary>
    public static readonly Lazy<SameWeights> Weights = new(() => SameWeights.Parse(
        File.ReadAllBytes(RepoFile("Assets", "data", "humanoid_retargeter", "dl", "same_v1.weights"))));

    /// <summary>Golden reference tensors (dev/m10/scripts/export_golden.py).</summary>
    public static readonly Lazy<SameWeights> Golden = new(() => SameWeights.Parse(
        File.ReadAllBytes(FixtureFile("dl", "golden_same_v1.bin"))));

    public static readonly Lazy<SameModel> Model = new(() => new SameModel(Weights.Value));

    public static readonly Lazy<SameStats> Stats = new(() => new SameStats(Weights.Value));

    /// <summary>Source/target joint name order of the golden tensors.</summary>
    public static (string[] Src, string[] Tgt, int Frames) GoldenNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixtureFile("dl", "golden_names.json")));
        var root = doc.RootElement;
        return (
            root.GetProperty("src_joints").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            root.GetProperty("tgt_joints").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            root.GetProperty("frames").GetInt32());
    }

    /// <summary>The mocap fixture both the spike and the golden export used.</summary>
    public static SourceScene FixtureBvh()
        => BvhImporter.Import(File.ReadAllBytes(FixtureFile("bvh", "bvhpython_test_freebvh.bvh")));

    /// <summary>Same scene with the clip truncated to the golden export's native frame count.</summary>
    public static SourceScene FixtureBvhTruncated(int frames)
    {
        var scene = FixtureBvh();
        var clip = scene.Clips[0];
        var truncated = new Clip(clip.Name, clip.Fps, clip.Looping,
            clip.Frames.Take(frames).ToList());
        return new SourceScene(
            scene.Skeleton, new[] { truncated }, scene.UnitScaleCm,
            scene.UpAxis, scene.UpAxisSign, scene.FrontAxis, scene.FrontAxisSign,
            scene.CoordAxis, scene.CoordAxisSign, scene.OriginalUpAxis);
    }

    /// <summary>The shipped s&amp;box target rig from the committed asset (carries tail_world).</summary>
    public static readonly Lazy<TargetRig> SboxRig = new(() => TargetRig.Load(
        File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    /// <summary>Maps golden joint names to this port's node names: identical up to the End
    /// Site suffix casing (fairmotion emits <c>_End</c>, the importers emit <c>_end</c>)
    /// and the namespace prefix (fairmotion strips <c>mixamorig:</c>).</summary>
    public static int[] MapNames(string[] golden, string[] ours)
    {
        static string Normalize(string name)
        {
            var colon = name.LastIndexOf(':');
            return (colon >= 0 ? name[(colon + 1)..] : name).ToLowerInvariant();
        }

        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ours.Length; i++)
            byName[Normalize(ours[i])] = i;
        var map = new int[golden.Length];
        for (var i = 0; i < golden.Length; i++)
        {
            if (!byName.TryGetValue(Normalize(golden[i]), out map[i]))
                throw new InvalidOperationException($"Golden joint '{golden[i]}' not found in port graph.");
        }
        return map;
    }

    public static int[] ToInts(float[] values) => values.Select(v => (int)MathF.Round(v)).ToArray();
}
