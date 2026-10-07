using HumanoidRetargeter;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Repro for the user report (2026-07-01): "uploaded 47_01.bvh, Armchair1.bvh,
/// Neutral_throw_ball_001 and a bunch of animations from an .an5 bank, clicked Convert All,
/// and only &lt;stem&gt;_52 shows in the vmdl / not all animations get added to the
/// AnimationList." Builds the batch EXACTLY like RetargetWindow.BuildRequest does (one
/// request per take row, TakeIndex set for multi-take files) and asserts every successful
/// clip appears as an AnimFile in the generated vmdl.
/// </summary>
public class BatchMixedSourcesTests
{
    private readonly ITestOutputHelper _out;

    public BatchMixedSourcesTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ConvertAll_MixedBvhAndAn5Batch_EveryClipReachesTheVmdl()
    {
        var todo = new[]
        {
            TestUtil.RepoFile("dev", "corpus", "todo", "47_01.bvh"),
            TestUtil.RepoFile("dev", "corpus", "todo", "Armchair1.bvh"),
            TestUtil.RepoFile("dev", "corpus", "todo", "Neutral_throw_ball_001__A057.bvh"),
        };
        var an5Path = TestUtil.RepoFile("dev", "corpus", "renderware", "7FEA6AEBBEB5C5F3.an5");
        var dffPath = TestUtil.RepoFile("dev", "corpus", "renderware", "f_upper_b01.dff");
        if (todo.Any(p => !File.Exists(p)) || !File.Exists(an5Path) || !File.Exists(dffPath))
            return; // local-only corpus

        var target = RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));

        var requests = new List<RetargetRequest>();
        foreach (var p in todo)
        {
            requests.Add(new RetargetRequest
            {
                SourceData = File.ReadAllBytes(p),
                SourceFileName = Path.GetFileName(p),
                SourceId = p + "#0",
            });
        }

        // The UI imports once to enumerate takes, then submits one request per take row.
        var an5Bytes = File.ReadAllBytes(an5Path);
        var dffBytes = File.ReadAllBytes(dffPath);
        var scene = Retargeter.ImportSource(an5Bytes, Path.GetFileName(an5Path), null, dffBytes);
        Assert.True(scene.Clips.Count > 1, "an5 fixture should be multi-take");
        for (var i = 0; i < scene.Clips.Count; i++)
        {
            requests.Add(new RetargetRequest
            {
                SourceData = an5Bytes,
                SourceFileName = Path.GetFileName(an5Path),
                SkeletonData = dffBytes,
                SourceId = an5Path + "#" + i,
                TakeIndex = i,
            });
        }

        var batch = Retargeter.ConvertBatch(requests, target, new BatchOptions
        {
            DmxFolderRelative = "animations",
        });

        foreach (var clip in batch.Clips)
            _out.WriteLine($"{(clip.Success ? "OK  " : "FAIL")} {clip.ClipName}  ({clip.SourceFileName})  {clip.Error}");

        var succeeded = batch.Clips.Where(c => c.Success).Select(c => c.ClipName).ToList();
        var vmdl = batch.StandaloneVmdl!;
        var missing = succeeded.Where(n => !vmdl.Contains($"\"{n}\"")).ToList();

        _out.WriteLine($"requests={requests.Count} clipResults={batch.Clips.Count} " +
            $"succeeded={succeeded.Count} animFileNodes={CountOccurrences(vmdl, "_class = \"AnimFile\"")}");

        Assert.True(batch.Clips.Count == requests.Count,
            $"expected one clip result per request ({requests.Count}), got {batch.Clips.Count}");
        Assert.True(succeeded.Count == requests.Count,
            "some clips failed: " + string.Join("; ",
                batch.Clips.Where(c => !c.Success).Select(c => $"{c.ClipName}: {c.Error}")));
        Assert.True(missing.Count == 0,
            $"clips converted successfully but missing from the vmdl: {string.Join(", ", missing)}");
    }

    /// <summary>Same repro against the user's REAL bank (53+ takes, larger than the test
    /// fixture) and through BOTH vmdl generators (standalone + augment into the citizen
    /// vmdl fixture) — the reported symptom was a single surviving take in the vmdl.</summary>
    [Fact]
    public void ConvertAll_UsersRealAn5Bank_AllTakesReachBothVmdls()
    {
        var an5Path = @"P:\5 11 2026\Organization(s)\Zeljko Vranjes\openfs2\FSB2\animations\Character\OutPut_Mer\ATH_JASON\0F8ADDF0D3FCACB7.an5";
        var dffPath = TestUtil.RepoFile("dev", "corpus", "renderware", "f_upper_b01.dff");
        var baseVmdlPath = TestUtil.RepoFile(
            "dev", "HumanoidRetargeter.Tests", "fixtures", "kv3", "citizen_human_male.vmdl");
        if (!File.Exists(an5Path) || !File.Exists(dffPath) || !File.Exists(baseVmdlPath))
            return; // local-only

        var target = RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));

        var an5Bytes = File.ReadAllBytes(an5Path);
        var dffBytes = File.ReadAllBytes(dffPath);
        var scene = Retargeter.ImportSource(an5Bytes, Path.GetFileName(an5Path), null, dffBytes);
        _out.WriteLine($"takes in bank: {scene.Clips.Count}");

        var requests = new List<RetargetRequest>();
        var todo = new[] { "47_01.bvh", "Armchair1.bvh", "Neutral_throw_ball_001__A057.bvh" };
        foreach (var name in todo)
        {
            var p = TestUtil.RepoFile("dev", "corpus", "todo", name);
            if (!File.Exists(p))
                return;
            requests.Add(new RetargetRequest
            {
                SourceData = File.ReadAllBytes(p),
                SourceFileName = name,
                SourceId = p + "#0",
            });
        }
        for (var i = 0; i < scene.Clips.Count; i++)
        {
            requests.Add(new RetargetRequest
            {
                SourceData = an5Bytes,
                SourceFileName = Path.GetFileName(an5Path),
                SkeletonData = dffBytes,
                SourceId = an5Path + "#" + i,
                TakeIndex = i,
            });
        }

        var batch = Retargeter.ConvertBatch(requests, target, new BatchOptions
        {
            DmxFolderRelative = "animations",
            AugmentVmdlText = File.ReadAllText(baseVmdlPath),
            DetectLocomotionSets = true, // user-realistic toggles
        });

        var failed = batch.Clips.Where(c => !c.Success).ToList();
        foreach (var clip in failed)
            _out.WriteLine($"FAIL {clip.ClipName} ({clip.SourceFileName}): {clip.Error}");
        var succeeded = batch.Clips.Where(c => c.Success).Select(c => c.ClipName).ToList();
        _out.WriteLine($"requests={requests.Count} results={batch.Clips.Count} ok={succeeded.Count}");

        Assert.True(failed.Count == 0, $"{failed.Count} clip(s) failed (see output)");
        Assert.Equal(requests.Count, batch.Clips.Count);

        var missingStandalone = succeeded.Where(n => !batch.StandaloneVmdl!.Contains($"\"{n}\"")).ToList();
        Assert.True(missingStandalone.Count == 0,
            "missing from standalone vmdl: " + string.Join(", ", missingStandalone));

        Assert.NotNull(batch.AugmentedVmdl);
        var missingAugment = succeeded.Where(n => !batch.AugmentedVmdl!.Contains($"\"{n}\"")).ToList();
        Assert.True(missingAugment.Count == 0,
            "missing from augmented vmdl: " + string.Join(", ", missingAugment));

        // Distinct DMX files per clip — a filename collision would overwrite frame data
        // on disk even with all sequences present in the vmdl.
        var dmxNames = batch.Clips.Where(c => c.Success).Select(c => c.DmxFileName).ToList();
        Assert.Equal(dmxNames.Count, dmxNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>The standalone-accumulation contract behind the editor fix for the user
    /// report "convert a batch, then convert one more row → the vmdl only contains that
    /// row": a GenerateStandalone-produced vmdl must be augmentable, successive batches
    /// must UNION (batch A's sequences survive batch B), and re-running the same batch
    /// must replace in place (no _2 duplicates).</summary>
    [Fact]
    public void StandaloneVmdl_AccumulatesAcrossSuccessiveBatches()
    {
        var pathA = TestUtil.RepoFile("dev", "corpus", "bvh", "cmu_01_01.bvh");
        var pathB = TestUtil.RepoFile("dev", "corpus", "bvh", "cmu_01_14.bvh");
        if (!File.Exists(pathA) || !File.Exists(pathB))
            return; // local-only corpus

        var target = RetargetTargetSpec.SboxDefault(File.ReadAllText(
            TestUtil.RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));

        RetargetRequest Req(string p) => new()
        {
            SourceData = File.ReadAllBytes(p),
            SourceFileName = Path.GetFileName(p),
            SourceId = p,
        };

        // Run 1: batch A alone, standalone vmdl.
        var runA = Retargeter.ConvertBatch(new[] { Req(pathA) }, target,
            new BatchOptions { DmxFolderRelative = "animations" });
        Assert.True(runA.Clips.All(c => c.Success));
        Assert.Contains("\"cmu_01_01\"", runA.StandaloneVmdl);

        // Run 2: batch B only, augmenting run 1's standalone output (the editor's
        // accumulate path). A's sequence must survive, B's must be added.
        var runB = Retargeter.ConvertBatch(new[] { Req(pathB) }, target,
            new BatchOptions
            {
                DmxFolderRelative = "animations",
                AugmentVmdlText = runA.StandaloneVmdl,
            });
        Assert.True(runB.Clips.All(c => c.Success));
        Assert.NotNull(runB.AugmentedVmdl);
        Assert.Contains("\"cmu_01_01\"", runB.AugmentedVmdl);
        Assert.Contains("\"cmu_01_14\"", runB.AugmentedVmdl);

        // Run 3: batch B again over run 2's output - idempotent replace, no _2 suffix.
        var runB2 = Retargeter.ConvertBatch(new[] { Req(pathB) }, target,
            new BatchOptions
            {
                DmxFolderRelative = "animations",
                AugmentVmdlText = runB.AugmentedVmdl,
            });
        Assert.True(runB2.Clips.All(c => c.Success));
        Assert.NotNull(runB2.AugmentedVmdl);
        Assert.Contains("\"cmu_01_01\"", runB2.AugmentedVmdl);
        Assert.Contains("\"cmu_01_14\"", runB2.AugmentedVmdl);
        Assert.DoesNotContain("cmu_01_14_2", runB2.AugmentedVmdl);
        Assert.Equal("cmu_01_14", runB2.Clips.Single().ClipName);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(token, i, StringComparison.Ordinal)) >= 0) { count++; i += token.Length; }
        return count;
    }
}
