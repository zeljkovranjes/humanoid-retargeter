using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Mapping;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// External clip definitions (Unity <c>.fbx.meta</c> clipAnimations) through the facade:
/// <see cref="RetargetRequest.ClipDefinitions"/> unpacks a single-timeline source into one
/// output clip per definition — sliced to the definition's native-frame range, named after
/// it, looped per its flag — and <see cref="RetargetRequest.TakeIndex"/> indexes into the
/// DEFINITIONS. Fixture: fixtures/fbx/synthetic_two_takes.fbx (two 31-frame takes at the
/// default 30 fps grid, native rate 30) with fabricated definitions targeting its take names.
/// </summary>
public class UnityClipDefinitionTests
{
    private const string FixtureName = "synthetic_two_takes.fbx";

    private static string FixturePath(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

    /// <summary>Resolves a repo-relative path by walking up to the .sbproj directory.</summary>
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static byte[] FixtureBytes() => File.ReadAllBytes(FixturePath("fbx", FixtureName));

    private static ExternalClipDef Def(
        string name, string takeName, float first, float last, bool loop = false)
        => new() { Name = name, TakeName = takeName, FirstFrame = first, LastFrame = last, Loop = loop };

    private static RetargetRequest Request(
        IReadOnlyList<ExternalClipDef> defs, int? takeIndex = null, bool? loopingOverride = null) => new()
    {
        SourceData = FixtureBytes(),
        SourceFileName = FixtureName,
        ClipDefinitions = defs,
        TakeIndex = takeIndex,
        LoopingOverride = loopingOverride,
    };

    // ---------------------------------------------------------------- per-definition clips

    [Fact]
    public void Convert_AllDefinitions_OneClipPerDefinition()
    {
        var defs = new[]
        {
            Def("walk_a", "take_alpha", 0, 10),
            Def("walk_b", "take_beta", 5, 15, loop: true),
        };

        var result = Retargeter.Convert(Request(defs), SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));

        // Named after the definitions, sliced to round(first/native*sample)..round(last/...):
        // native == sample (30 fps) here, so 11 frames each.
        Assert.Equal("walk_a", result.Clips[0].ClipName);
        Assert.Equal("walk_b", result.Clips[1].ClipName);
        Assert.Equal(11, result.Clips[0].SolvedFrames!.Count);
        Assert.Equal(11, result.Clips[1].SolvedFrames!.Count);

        // Looping comes from the definition (Unity loopTime), not the source take.
        Assert.False(result.Clips[0].Looping);
        Assert.True(result.Clips[1].Looping);
    }

    [Fact]
    public void Convert_DefinitionsSliceTheRightTake()
    {
        // take_alpha raises the hips, take_beta sinks them (see MultiTakeTests) — a
        // definition slicing the END of take_beta must produce sunk hips, proving both the
        // TakeName→take match and the range slice survive the solve.
        var defs = new[]
        {
            Def("alpha_end", "take_alpha", 25, 30),
            Def("beta_end", "take_beta", 25, 30),
        };

        var result = Retargeter.Convert(Request(defs), SboxTarget.Value);

        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
        Assert.All(result.Clips, c => Assert.Equal(6, c.SolvedFrames!.Count));

        var pelvis = SboxTarget.Value.Rig.BoneForRole(BoneRole.Hips)!.Value;
        var alphaZ = result.Clips[0].SolvedFrames![0][pelvis].Pos;
        var betaZ = result.Clips[1].SolvedFrames![0][pelvis].Pos;
        Assert.NotEqual(alphaZ, betaZ); // distinct source ranges → distinct solved poses
    }

    [Fact]
    public void Convert_UnknownTakeName_FallsBackToFirstTake()
    {
        var defs = new[] { Def("fallback", "no|such|take", 0, 4) };

        var result = Retargeter.Convert(Request(defs), SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal(5, clip.SolvedFrames!.Count);
    }

    [Fact]
    public void Convert_DefinitionNamesAreSanitizedAndCollisionSuffixed()
    {
        var defs = new[]
        {
            Def("My Clip", "take_alpha", 0, 5),
            Def("My Clip", "take_beta", 0, 5),
        };

        var result = Retargeter.Convert(Request(defs), SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.Equal("My_Clip", result.Clips[0].ClipName);
        Assert.Equal("My_Clip_2", result.Clips[1].ClipName);
    }

    [Fact]
    public void Convert_LoopingOverride_BeatsDefinitionLoopFlag()
    {
        var defs = new[] { Def("looper", "take_alpha", 0, 5, loop: true) };

        var result = Retargeter.Convert(Request(defs, loopingOverride: false), SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.False(clip.Looping);
    }

    // ---------------------------------------------------------------- TakeIndex semantics

    [Fact]
    public void Convert_TakeIndex_IndexesIntoDefinitions()
    {
        var defs = new[]
        {
            Def("walk_a", "take_alpha", 0, 10),
            Def("walk_b", "take_beta", 5, 15, loop: true),
        };

        var result = Retargeter.Convert(Request(defs, takeIndex: 1), SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("walk_b", clip.ClipName);
        Assert.Equal(11, clip.SolvedFrames!.Count);
        Assert.True(clip.Looping);
    }

    [Fact]
    public void Convert_TakeIndexOutOfDefinitionRange_FailsWithClearError()
    {
        var defs = new[] { Def("only", "take_alpha", 0, 10) };

        var result = Retargeter.Convert(Request(defs, takeIndex: 5), SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.False(clip.Success);
        Assert.Contains("Clip-definition index 5", clip.Error);
        Assert.Contains("1 clip definition(s)", clip.Error);
    }

    [Fact]
    public void Convert_NullOrEmptyDefinitions_KeepTakeBehavior()
    {
        // Empty definition list = no definitions: the historical all-takes path runs.
        var result = Retargeter.Convert(
            Request(Array.Empty<ExternalClipDef>()), SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.Equal("take_alpha", result.Clips[0].ClipName);
        Assert.Equal("take_beta", result.Clips[1].ClipName);
    }
}
