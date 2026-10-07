using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Multi-take handling through the facade: <see cref="RetargetRequest.TakeIndex"/> restricts
/// a conversion to one take (per-take UI entries submit one request per selected take),
/// null keeps the historical all-takes behavior, and <see cref="Retargeter.Inspect"/>
/// exposes the take metadata listings need to expand a file into per-take entries.
/// Fixture: fixtures/fbx/synthetic_two_takes.fbx — a humanoid ASCII FBX with two stacks
/// ("take_alpha", "take_beta"), each driving the hips over 1 s.
/// </summary>
public class MultiTakeTests
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
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static byte[] FixtureBytes() => File.ReadAllBytes(FixturePath("fbx", FixtureName));

    private static RetargetRequest Request(int? takeIndex = null) => new()
    {
        SourceData = FixtureBytes(),
        SourceFileName = FixtureName,
        TakeIndex = takeIndex,
    };

    // ---------------------------------------------------------------- import + inspect

    [Fact]
    public void ImportSource_TwoTakeFixture_YieldsBothClipsInStackOrder()
    {
        var scene = Retargeter.ImportSource(FixtureBytes(), FixtureName);

        Assert.Equal(2, scene.Clips.Count);
        Assert.Equal("take_alpha", scene.Clips[0].Name);
        Assert.Equal("take_beta", scene.Clips[1].Name);

        // 1 s per take at the default 30 fps grid.
        Assert.All(scene.Clips, c => Assert.Equal(31, c.FrameCount));

        // The takes really differ (hips Y rises in alpha, sinks in beta).
        int hips = scene.Skeleton.IndexOf("Hips");
        Assert.True(hips >= 0);
        Assert.True(scene.Clips[0].Frames[^1][hips].Pos.Y > 100f);
        Assert.True(scene.Clips[1].Frames[^1][hips].Pos.Y < 90f);
    }

    [Fact]
    public void Inspect_ExposesTakeNamesAndCount()
    {
        var inspected = Retargeter.Inspect(FixtureBytes(), FixtureName);

        Assert.Equal(2, inspected.TakeCount);
        Assert.Equal(new[] { "take_alpha", "take_beta" }, inspected.TakeNames);
        Assert.NotNull(inspected.Mapping);
        Assert.True(inspected.Mapping.MappedRoleCount > 0);
    }

    // ---------------------------------------------------------------- TakeIndex behavior

    [Fact]
    public void Convert_NullTakeIndex_ConvertsAllTakes()
    {
        var result = Retargeter.Convert(Request(takeIndex: null), SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, $"{c.ClipName}: {c.Error}"));
        Assert.Equal("take_alpha", result.Clips[0].ClipName);
        Assert.Equal("take_beta", result.Clips[1].ClipName);
    }

    [Theory]
    [InlineData(0, "take_alpha")]
    [InlineData(1, "take_beta")]
    public void Convert_TakeIndex_ConvertsOnlyThatTake(int takeIndex, string expectedName)
    {
        var result = Retargeter.Convert(Request(takeIndex), SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal(expectedName, clip.ClipName);
        Assert.False(string.IsNullOrEmpty(clip.DmxContent));
    }

    [Fact]
    public void Convert_PerTakeRequests_MatchAllTakesConversion()
    {
        // One request per take (the window's Convert All shape for expanded entries)
        // produces the same clips as one all-takes request.
        var perTake = Retargeter.ConvertBatch(
            new[] { Request(0), Request(1) }, SboxTarget.Value);
        var allTakes = Retargeter.Convert(Request(), SboxTarget.Value);

        Assert.Equal(2, perTake.Clips.Count);
        Assert.All(perTake.Clips, c => Assert.True(c.Success, c.Error));
        Assert.Equal(
            allTakes.Clips.Select(c => c.ClipName),
            perTake.Clips.Select(c => c.ClipName));
        Assert.Equal(
            allTakes.Clips.Select(c => c.DmxContent),
            perTake.Clips.Select(c => c.DmxContent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void Convert_TakeIndexOutOfRange_FailsThatRequestOnly(int takeIndex)
    {
        // The bad request fails with a clear error; the rest of the batch continues.
        var result = Retargeter.ConvertBatch(
            new[] { Request(takeIndex), Request(0) }, SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        var bad = result.Clips[0];
        Assert.False(bad.Success);
        Assert.Contains("out of range", bad.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 take(s)", bad.Error);

        var good = result.Clips[1];
        Assert.True(good.Success, good.Error);
        Assert.Equal("take_alpha", good.ClipName);
    }

    [Fact]
    public void Convert_TakeIndex_SourceIdFlowsThrough()
    {
        // Per-take requests carry distinct SourceIds so callers can join results per row.
        var requests = new[]
        {
            new RetargetRequest
            {
                SourceData = FixtureBytes(),
                SourceFileName = FixtureName,
                SourceId = @"C:\anims\two_takes.fbx::take:0",
                TakeIndex = 0,
            },
            new RetargetRequest
            {
                SourceData = FixtureBytes(),
                SourceFileName = FixtureName,
                SourceId = @"C:\anims\two_takes.fbx::take:1",
                TakeIndex = 1,
            },
        };

        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.Equal(@"C:\anims\two_takes.fbx::take:0", result.Clips[0].SourceId);
        Assert.Equal(@"C:\anims\two_takes.fbx::take:1", result.Clips[1].SourceId);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
    }

    // ------------------------------------------------------ exporter placeholder names

    /// <summary>The ASCII two-take fixture with both AnimStack names replaced.</summary>
    private static byte[] FixtureWithStackNames(string name)
    {
        var text = File.ReadAllText(FixturePath("fbx", FixtureName))
            .Replace("AnimStack::take_alpha", $"AnimStack::{name}")
            .Replace("AnimStack::take_beta", $"AnimStack::{name}");
        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Unreal's FBX exporter stamps "Unreal Take" into every AnimStack it writes, so the
    /// name identifies the exporter and never the animation. It must fall through to the
    /// FILE stem the way BVH's "motion" and Mixamo's "mixamo.com" already do — otherwise
    /// every UE clip in a batch lands on one name and collision-suffixing renames the lot
    /// to Unreal_Take, Unreal_Take_2, Unreal_Take_3 …, losing each file's real name.
    /// </summary>
    [Theory]
    [InlineData("Unreal Take")]
    [InlineData("Unreal Take 001")]
    [InlineData("unreal_take")]
    [InlineData("UNREAL TAKE")]
    public void Convert_UnrealPlaceholderTakeName_UsesTheFileStem(string placeholder)
    {
        var result = Retargeter.Convert(
            new RetargetRequest
            {
                SourceData = FixtureWithStackNames(placeholder),
                SourceFileName = "Run_Fwd.fbx",
            },
            SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, $"{c.ClipName}: {c.Error}"));
        Assert.Equal(
            new[] { "Run_Fwd_1", "Run_Fwd_2" },
            result.Clips.Select(c => c.ClipName).ToArray());
    }

    /// <summary>
    /// The real defect as reported: a batch of UE-exported files keeps each file's own
    /// name instead of collapsing onto one placeholder and being pulled apart by the
    /// collision suffixer.
    /// </summary>
    [Fact]
    public void ConvertBatch_UnrealTakesAcrossFiles_KeepPerFileNames()
    {
        var bytes = FixtureWithStackNames("Unreal Take");
        var result = Retargeter.ConvertBatch(
            new[]
            {
                new RetargetRequest
                {
                    SourceData = bytes, SourceFileName = "Run_Fwd.fbx", TakeIndex = 0,
                },
                new RetargetRequest
                {
                    SourceData = bytes, SourceFileName = "Walk_Fwd.fbx", TakeIndex = 0,
                },
            },
            SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, $"{c.ClipName}: {c.Error}"));
        Assert.All(result.Clips,
            c => Assert.DoesNotContain("nreal", c.ClipName, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Run_Fwd", result.Clips[0].ClipName);
        Assert.StartsWith("Walk_Fwd", result.Clips[1].ClipName);
    }

    /// <summary>
    /// Regression guard for the blocklist change: Mixamo's "mixamo.com" placeholder keeps
    /// falling through to the file stem exactly as it did before — the Unreal entry sits
    /// BESIDE the existing ones, it does not replace them.
    /// Fixture: fixtures/fbx/Zombie Crawl.fbx — a real Mixamo export.
    /// </summary>
    [Fact]
    public void Convert_MixamoPlaceholderTakeName_StillUsesTheFileStem()
    {
        const string mixamoFixture = "Zombie Crawl.fbx";
        var result = Retargeter.Convert(
            new RetargetRequest
            {
                SourceData = File.ReadAllBytes(FixturePath("fbx", mixamoFixture)),
                SourceFileName = mixamoFixture,
            },
            SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("Zombie_Crawl", clip.ClipName);
    }

    /// <summary>A genuinely authored take name still wins over the file stem.</summary>
    [Fact]
    public void Convert_AuthoredTakeName_StillBeatsTheFileStem()
    {
        var result = Retargeter.Convert(
            new RetargetRequest
            {
                SourceData = FixtureBytes(), SourceFileName = "Run_Fwd.fbx",
            },
            SboxTarget.Value);

        Assert.Equal(
            new[] { "take_alpha", "take_beta" },
            result.Clips.Select(c => c.ClipName).ToArray());
    }
}
