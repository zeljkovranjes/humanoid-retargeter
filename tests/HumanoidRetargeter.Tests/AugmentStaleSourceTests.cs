using HumanoidRetargeter;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Regression tests for the user report (2026-07-01): augmenting a citizen vmdl copy that
/// had accumulated AnimFile entries from earlier batches whose DMX files were later
/// deleted/moved made the WHOLE model fail to recompile —
/// <c>CDataModel: Unable to open file …\animations\retargeted\surprised.dmx</c> →
/// <c>ModelDoc: Node 'Surprised' resolve failure</c> → <c>ResourceCompilerSystem [FAIL]</c> →
/// "vmdl did not compile: citizen_human_male.vmdl" — taking every newly converted animation
/// down with the stale ones. The IO-owning caller reports the missing sources
/// (<see cref="BatchOptions.MissingAnimSources"/>, probed from
/// <see cref="VmdlAugmenter.CollectAnimSourcePaths"/>) and the augmenter prunes those
/// entries, surfacing each removal on <see cref="RetargetBatchResult.Warnings"/>.
/// </summary>
public class AugmentStaleSourceTests
{
    private static string CitizenVmdlText() => File.ReadAllText(TestUtil.RepoFile(
        "dev", "HumanoidRetargeter.Tests", "fixtures", "kv3", "citizen_human_male.vmdl"));

    private static RetargetTargetSpec Target() => RetargetTargetSpec.SboxDefault(
        File.ReadAllText(TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));

    private static RetargetRequest BvhRequest()
    {
        var path = TestUtil.RepoFile(
            "dev", "HumanoidRetargeter.Tests", "fixtures", "bvh", "cmu_01_01.bvh");
        return new RetargetRequest
        {
            SourceData = File.ReadAllBytes(path),
            SourceFileName = "cmu_01_01.bvh",
            SourceId = path,
        };
    }

    private static string PlantStaleEntries(string vmdlText, params (string Name, string Source)[] stale)
    {
        var entries = stale.Select(s => new AnimEntry { Name = s.Name, SourceFilename = s.Source });
        return VmdlAugmenter.Augment(vmdlText, entries, out _,
            new AugmentOptions { DefaultRootBone = "pelvis" });
    }

    // ------------------------------------------------------------------ the user repro

    [Fact]
    public void Augment_PrunesStaleEntriesWhoseDmxIsMissing_AndWarns()
    {
        // The user's vmdl shape: earlier batches left AnimFiles whose DMX files are gone.
        var staleSurprised = "animations/retargeted/surprised.dmx";
        var staleSideshoot = "animations/retargeted/cc02_sideshoot.dmx";
        var planted = PlantStaleEntries(CitizenVmdlText(),
            ("Surprised", staleSurprised), ("cc02_sideshoot", staleSideshoot));

        // The stale sources are discoverable for the IO-owning caller to probe.
        var collected = VmdlAugmenter.CollectAnimSourcePaths(planted);
        Assert.Contains(staleSurprised, collected);
        Assert.Contains(staleSideshoot, collected);

        var batch = Retargeter.ConvertBatch(new[] { BvhRequest() }, Target(), new BatchOptions
        {
            DmxFolderRelative = "animations",
            AugmentVmdlText = planted,
            MissingAnimSources = new[] { staleSurprised, staleSideshoot },
        });

        Assert.True(batch.Clips.All(c => c.Success));
        Assert.NotNull(batch.AugmentedVmdl);

        // The new clip is registered; the stale entries are gone.
        Assert.Contains("\"cmu_01_01\"", batch.AugmentedVmdl);
        Assert.DoesNotContain("Surprised", batch.AugmentedVmdl);
        Assert.DoesNotContain("cc02_sideshoot", batch.AugmentedVmdl);

        // The structural contract that would have caught the bug: the augmented vmdl must
        // not reference a single animation source the caller reported missing.
        var remaining = VmdlAugmenter.CollectAnimSourcePaths(batch.AugmentedVmdl!);
        Assert.DoesNotContain(remaining,
            s => string.Equals(s, staleSurprised, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, staleSideshoot, StringComparison.OrdinalIgnoreCase));

        // Each removal is surfaced (non-fatal) so the user knows what happened and why.
        Assert.Equal(2, batch.Warnings.Count);
        Assert.Contains(batch.Warnings, w => w.Contains("'Surprised'"));
        Assert.Contains(batch.Warnings, w => w.Contains("'cc02_sideshoot'"));
        Assert.Empty(batch.Errors);
    }

    [Fact]
    public void Augment_WithoutMissingSet_KeepsStaleEntries()
    {
        // The facade never guesses about the filesystem: no MissingAnimSources = no pruning.
        var planted = PlantStaleEntries(CitizenVmdlText(),
            ("Surprised", "animations/retargeted/surprised.dmx"));

        var batch = Retargeter.ConvertBatch(new[] { BvhRequest() }, Target(), new BatchOptions
        {
            DmxFolderRelative = "animations",
            AugmentVmdlText = planted,
        });

        Assert.NotNull(batch.AugmentedVmdl);
        Assert.Contains("\"Surprised\"", batch.AugmentedVmdl);
        Assert.Empty(batch.Warnings);
    }

    [Fact]
    public void Augment_EntryRewrittenByThisBatch_IsReplacedNotPruned()
    {
        // A stale entry whose NAME this batch produces again: the splice replaces it in
        // place (fresh source_filename, DMX about to be written) - pruning must not touch
        // it even though its OLD source is in the missing set.
        var staleSource = "animations/cmu_01_01.dmx"; // same folder the new batch writes to
        var planted = PlantStaleEntries(CitizenVmdlText(), ("cmu_01_01", staleSource));

        var batch = Retargeter.ConvertBatch(new[] { BvhRequest() }, Target(), new BatchOptions
        {
            DmxFolderRelative = "animations",
            AugmentVmdlText = planted,
            MissingAnimSources = new[] { staleSource },
        });

        Assert.True(batch.Clips.All(c => c.Success));
        Assert.Equal("cmu_01_01", batch.Clips.Single().ClipName);
        Assert.NotNull(batch.AugmentedVmdl);
        Assert.Contains("\"cmu_01_01\"", batch.AugmentedVmdl);
        Assert.DoesNotContain("cmu_01_01_2", batch.AugmentedVmdl);
        Assert.Empty(batch.Warnings);
    }

    [Fact]
    public void Augment_LocomotionFolderWithAllSourcesMissing_IsPrunedWithBlendAndFolder()
    {
        // An earlier run's locomotion folder (Folder + 2DBlend + member AnimFiles) whose
        // member DMX files all vanished: members prune, the blend referencing them prunes,
        // the emptied folder prunes - none of them may fail the recompile.
        var members = new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" };
        var memberEntries = members
            .Select(m => new AnimEntry
            {
                Name = m,
                SourceFilename = $"animations/retargeted/{m.ToLowerInvariant()}.dmx",
            })
            .ToList();
        var set = new LocomotionSetSpec
        {
            FolderName = "Walk",
            BlendName = "Walk_2D",
            Looping = true,
            BlendGrid = new[]
            {
                new[] { "", "Walk_S", "" },
                new[] { "Walk_W", "", "Walk_E" },
                new[] { "", "Walk_N", "" },
            },
            MemberNames = members,
        };
        var planted = VmdlAugmenter.Augment(CitizenVmdlText(), memberEntries, out _,
            new AugmentOptions
            {
                DefaultRootBone = "pelvis",
                LocomotionSets = new[] { set },
                DmxFolderRelative = "animations/retargeted",
            });
        Assert.Contains("\"Walk_2D\"", planted);

        var batch = Retargeter.ConvertBatch(new[] { BvhRequest() }, Target(), new BatchOptions
        {
            DmxFolderRelative = "animations",
            AugmentVmdlText = planted,
            MissingAnimSources = memberEntries.Select(e => e.SourceFilename).ToList(),
        });

        Assert.NotNull(batch.AugmentedVmdl);
        Assert.Contains("\"cmu_01_01\"", batch.AugmentedVmdl);
        Assert.DoesNotContain("Walk_N", batch.AugmentedVmdl);
        Assert.DoesNotContain("Walk_2D", batch.AugmentedVmdl);
        Assert.DoesNotContain("\"Walk\"", batch.AugmentedVmdl);
        // 4 members + the blend + the emptied folder, each reported.
        Assert.Equal(6, batch.Warnings.Count);
    }
}
