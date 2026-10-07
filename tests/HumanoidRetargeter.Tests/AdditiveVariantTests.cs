using System.Text;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using HumanoidRetargeter.Tests.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Tests for <see cref="RetargetRequest.CreateAdditiveVariant"/>: the additive
/// (<c>_delta</c>) companion AnimFile entries replicating the shipped citizen AnimSubtract
/// pattern, verified structurally against the shipped
/// <c>citizen_animationlist.vmdl_prefab</c> fixture.
/// </summary>
public class AdditiveVariantTests
{
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

    private static RetargetRequest WalkRequest(
        bool additive = true, string? nameOverride = null, bool mirrored = false) => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        CreateAdditiveVariant = additive,
        CreateMirroredVariant = mirrored,
        ClipNameOverride = nameOverride,
    };

    private static KvObject FindAnimationList(string vmdlText)
    {
        var rootNode = (KvObject)((KvObject)Kv3.Parse(vmdlText).Root)["rootNode"];
        return ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
    }

    private static KvObject AnimFileByName(string vmdlText, string name)
        => FindAnimationList(vmdlText).ChildObjects()
            .Single(o => o.GetString("_class") == "AnimFile" && o.GetString("name") == name);

    // ---------------------------------------------------------------- writer node shape

    [Fact]
    public void Writer_SubtractEntry_EmitsAnimSubtractFirstChild()
    {
        var text = VmdlWriter.GenerateStandalone("", new[]
        {
            new AnimEntry { Name = "walk", SourceFilename = "animations/walk.dmx", Looping = true },
            new AnimEntry
            {
                Name = "walk_delta",
                SourceFilename = "animations/walk.dmx", // SAME dmx — the compiler subtracts
                Looping = true,
                SubtractAnimName = "walk",
                SubtractFrame = 0,
            },
        }, 1.0f, "pelvis");

        var delta = AnimFileByName(text, "walk_delta");
        var child = Assert.Single(delta.ChildObjects());
        Assert.Equal("AnimSubtract", child.GetString("_class"));
        Assert.Equal(new[] { "_class", "anim_name", "frame" }, child.Keys);
        Assert.Equal("walk", child.GetString("anim_name"));
        Assert.Equal(0L, ((KvLong)child["frame"]).Value);

        // The AnimFile's own delta attribute stays false, exactly like every shipped
        // _delta sequence (the AnimSubtract child is what makes it additive at compile).
        Assert.False(((KvBool)delta["delta"]).Value);
        Assert.Equal("animations/walk.dmx", delta.GetString("source_filename"));

        // The base entry carries no AnimSubtract (and no children at all here).
        Assert.Null(AnimFileByName(text, "walk").GetOrNull("children"));

        // Generated text round-trips through the Kv3 layer semantically.
        var doc = Kv3.Parse(text);
        Assert.True(KvValue.DeepEquals(doc.Root, Kv3.Parse(Kv3.Serialize(doc)).Root));
    }

    [Fact]
    public void Writer_SubtractEntry_PrecedesExtractMotionAndEvents()
    {
        var text = VmdlWriter.GenerateStandalone("", new[]
        {
            new AnimEntry
            {
                Name = "run_delta",
                SourceFilename = "animations/run.dmx",
                ExtractMotion = true,
                SubtractAnimName = "run",
                SubtractFrame = 0,
            },
        }, 1.0f, "pelvis");

        // Shipped data orders AnimSubtract before the other children (e.g. before
        // AnimStartLoop on IdleLayer_Breathe_delta).
        var classes = AnimFileByName(text, "run_delta").ChildObjects()
            .Select(o => o.GetString("_class")).ToArray();
        Assert.Equal(new[] { "AnimSubtract", "ExtractMotion" }, classes);
    }

    // ---------------------------------------------------------------- shipped-schema fidelity

    [Fact]
    public void Writer_DeltaNode_MatchesShippedPrefabShapeExactly()
    {
        // The shipped IdleLayer_01/IdleLayer_01_delta pair is the exact pattern this feature
        // replicates: a second AnimFile reusing the base's animation source with an
        // AnimSubtract child naming the base sequence.
        var prefab = File.ReadAllText(
            SkeletonTests.FixturePath(Path.Combine("kv3", "citizen_animationlist.vmdl_prefab")));
        var shippedDelta = FindAnimFileRecursive(Kv3.Parse(prefab).Root, "IdleLayer_01_delta");
        var shippedBase = FindAnimFileRecursive(Kv3.Parse(prefab).Root, "IdleLayer_01");

        // Shipped semantics this feature relies on: same source file as the base (no extra
        // frame data — the compiler subtracts), subtract reference = the base sequence.
        Assert.Equal(shippedBase.GetString("source_filename"), shippedDelta.GetString("source_filename"));
        var shippedSubtract = Assert.Single(shippedDelta.ChildObjects());
        Assert.Equal("AnimSubtract", shippedSubtract.GetString("_class"));
        Assert.Equal("IdleLayer_01", shippedSubtract.GetString("anim_name"));
        Assert.Equal(0L, ((KvLong)shippedSubtract["frame"]).Value);

        // Generate our delta with the shipped names and compare node shape attribute by
        // attribute: identical key ORDER and identical value KINDS on the AnimFile, and a
        // deep-equal AnimSubtract child.
        var text = VmdlWriter.GenerateStandalone("", new[]
        {
            new AnimEntry
            {
                Name = "IdleLayer_01_delta",
                SourceFilename = shippedDelta.GetString("source_filename")!,
                Looping = true,
                SubtractAnimName = "IdleLayer_01",
                SubtractFrame = 0,
            },
        }, 1.0f, "pelvis");
        var ours = AnimFileByName(text, "IdleLayer_01_delta");

        Assert.Equal(shippedDelta.Keys, ours.Keys);
        foreach (var key in shippedDelta.Keys)
        {
            Assert.True(shippedDelta[key].GetType() == ours[key].GetType(),
                $"AnimFile attribute '{key}': shipped {shippedDelta[key].GetType().Name}, "
                + $"ours {ours[key].GetType().Name}");
        }
        Assert.True(KvValue.DeepEquals(shippedSubtract, Assert.Single(ours.ChildObjects())),
            "generated AnimSubtract child must be identical to the shipped one");
    }

    private static KvObject FindAnimFileRecursive(KvValue node, string name)
    {
        var matches = new List<KvObject>();
        Walk(node);
        return matches.Single();

        void Walk(KvValue current)
        {
            switch (current)
            {
                case KvObject o:
                    if (o.GetString("_class") == "AnimFile" && o.GetString("name") == name)
                        matches.Add(o);
                    foreach (var key in o.Keys)
                        Walk(o[key]);
                    break;
                case KvArray a:
                    foreach (var item in a.Items)
                        Walk(item);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- pipeline plumbing

    [Fact]
    public void Convert_AdditiveVariant_RegistersDeltaEntryReusingTheDmx()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);

        // No separate ClipResult: the delta is a vmdl entry reusing the base DMX.
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.True(clip.HasAdditiveVariant);
        Assert.Equal("synth_walk_delta", clip.AdditiveVariantName);

        var baseNode = AnimFileByName(result.StandaloneVmdl, "synth_walk");
        var delta = AnimFileByName(result.StandaloneVmdl, "synth_walk_delta");
        Assert.Equal(baseNode.GetString("source_filename"), delta.GetString("source_filename"));

        var subtract = Assert.Single(delta.ChildObjects());
        Assert.Equal("AnimSubtract", subtract.GetString("_class"));
        Assert.Equal("synth_walk", subtract.GetString("anim_name"));
        Assert.Equal(0L, ((KvLong)subtract["frame"]).Value);
        Assert.Equal(((KvBool)baseNode["looping"]).Value, ((KvBool)delta["looping"]).Value);
    }

    [Fact]
    public void Convert_DefaultOff_EmitsNoAnimSubtract()
    {
        var result = Retargeter.Convert(WalkRequest(additive: false), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.False(clip.HasAdditiveVariant);
        Assert.Null(clip.AdditiveVariantName);
        Assert.DoesNotContain("AnimSubtract", result.StandaloneVmdl);
        Assert.DoesNotContain("_delta", result.StandaloneVmdl);
    }

    [Fact]
    public void ConvertBatch_AdditiveNames_CollisionSuffixAsUsual()
    {
        var result = Retargeter.ConvertBatch(
            new[] { WalkRequest(), WalkRequest() }, SboxTarget.Value);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));

        // Per-batch unique: the second file's clip AND delta both get suffixed.
        Assert.Equal(new[] { "synth_walk", "synth_walk_2" },
            result.Clips.Select(c => c.ClipName));
        Assert.Equal(new[] { "synth_walk_delta", "synth_walk_2_delta" },
            result.Clips.Select(c => c.AdditiveVariantName));

        // A clip DELIBERATELY named like an already-taken delta gets suffixed too.
        var collide = Retargeter.ConvertBatch(new[]
        {
            WalkRequest(),
            WalkRequest(additive: false, nameOverride: "synth_walk_delta"),
        }, SboxTarget.Value);
        Assert.Equal(new[] { "synth_walk", "synth_walk_delta_2" },
            collide.Clips.Select(c => c.ClipName));
    }

    [Fact]
    public void Convert_MirroredPlusAdditive_BothClipsGetDeltas()
    {
        var result = Retargeter.Convert(WalkRequest(mirrored: true), SboxTarget.Value);
        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
        Assert.All(result.Clips, c => Assert.True(c.HasAdditiveVariant));

        var names = FindAnimationList(result.StandaloneVmdl).ChildObjects()
            .Select(o => o.GetString("name")).ToArray();
        Assert.Equal(
            new[] { "synth_walk", "synth_walk_delta", "synth_walk_M", "synth_walk_M_delta" },
            names);
    }

    [Fact]
    public void ConvertBatch_Augment_SplicesDeltaEntries_Idempotently()
    {
        var original = File.ReadAllText(
            SkeletonTests.FixturePath(Path.Combine("kv3", "citizen_human_male.vmdl")));
        var options = new BatchOptions { AugmentVmdlText = original };
        var result = Retargeter.ConvertBatch(new[] { WalkRequest() }, SboxTarget.Value, options);

        Assert.NotNull(result.AugmentedVmdl);
        var delta = AnimFileByName(result.AugmentedVmdl!, "synth_walk_delta");
        var subtract = Assert.Single(delta.ChildObjects());
        Assert.Equal("AnimSubtract", subtract.GetString("_class"));
        Assert.Equal("synth_walk", subtract.GetString("anim_name"));

        // Re-running the same batch against its own output replaces, never duplicates.
        var again = Retargeter.ConvertBatch(new[] { WalkRequest() }, SboxTarget.Value,
            new BatchOptions { AugmentVmdlText = result.AugmentedVmdl });
        Assert.Equal(result.AugmentedVmdl, again.AugmentedVmdl);
    }
}
