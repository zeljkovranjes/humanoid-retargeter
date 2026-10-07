using System.Text;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Facade tests for <see cref="RetargetRequest.GenerateFootstepEvents"/>: plant-start
/// detection on the solved target clip → AE_FOOTSTEP AnimEvent nodes in the vmdl, shaped
/// exactly like the shipped citizen data.
/// </summary>
public class FootstepEventTests
{
    private static string FixturePath(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

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

    private static RetargetRequest WalkRequest(bool footsteps = true) => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        GenerateFootstepEvents = footsteps,
    };

    // ---------------------------------------------------------------- detection

    [Fact]
    public void Convert_SyntheticWalk_EmitsEventsAtKnownPlantStarts()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        var left = clip.FootstepEvents.Where(e => e.Foot == "0").ToList();
        var right = clip.FootstepEvents.Where(e => e.Foot == "1").ToList();

        // One touchdown per foot, at the authored plant-start frames (±3 frames of
        // solve/cleanup jitter allowed at plant boundaries).
        var leftEvent = Assert.Single(left);
        Assert.InRange(leftEvent.Frame,
            WalkFixture.LeftTouchdownFrame - 3, WalkFixture.LeftTouchdownFrame + 3);
        var rightEvent = Assert.Single(right);
        Assert.InRange(rightEvent.Frame,
            WalkFixture.RightTouchdownFrame - 3, WalkFixture.RightTouchdownFrame + 3);

        // The right foot is ALREADY planted at clip start: its frame-0 plant has no
        // touchdown, so no event fires there.
        Assert.DoesNotContain(clip.FootstepEvents, e => e.Frame == 0);

        // Shipped-data event shape on every event.
        Assert.All(clip.FootstepEvents, e =>
        {
            Assert.Equal("AE_FOOTSTEP", e.EventClass);
            Assert.Equal(e.Foot == "0" ? "foot_L" : "foot_R", e.Attachment);
            Assert.Equal(0.7, e.Volume!.Value, 10);
        });

        // Merged list is frame-ordered.
        Assert.Equal(clip.FootstepEvents.OrderBy(e => e.Frame).Select(e => e.Frame),
            clip.FootstepEvents.Select(e => e.Frame));
    }

    [Fact]
    public void Convert_DefaultOff_ProducesNoEvents()
    {
        var result = Retargeter.Convert(WalkRequest(footsteps: false), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Empty(clip.FootstepEvents);
        Assert.DoesNotContain("AnimEvent", result.StandaloneVmdl);
    }

    // ---------------------------------------------------------------- vmdl node shape

    [Fact]
    public void Convert_WithEvents_VmdlCarriesShippedShapeAnimEventNodes()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.NotEmpty(clip.FootstepEvents);

        var eventNodes = AnimEventNodes(result.StandaloneVmdl, clip.ClipName);
        Assert.Equal(clip.FootstepEvents.Count, eventNodes.Count);

        for (var i = 0; i < eventNodes.Count; i++)
        {
            var node = eventNodes[i];
            var expected = clip.FootstepEvents[i];

            Assert.Equal("AnimEvent", node.GetString("_class"));
            Assert.Equal("AE_FOOTSTEP", node.GetString("event_class"));
            Assert.Equal(expected.Frame, Assert.IsType<KvLong>(node["event_frame"]).Value);

            // event_keys: Attachment/Foot as STRINGS, Volume as a double — exactly like the
            // 28 shipped events in citizen_animationlist.vmdl_prefab.
            var keys = Assert.IsType<KvObject>(node["event_keys"]);
            Assert.Equal(expected.Attachment, keys.GetString("Attachment"));
            Assert.Equal(expected.Foot, keys.GetString("Foot"));
            Assert.Equal(0.7, Assert.IsType<KvDouble>(keys["Volume"]).Value, 10);
        }
    }

    [Fact]
    public void Convert_WithEvents_Kv3RoundTripsIntact()
    {
        var result = Retargeter.Convert(WalkRequest(), SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        // Parse → serialize → parse: semantically identical, and the writer is a fixed
        // point (third generation byte-identical).
        var first = Kv3.Parse(result.StandaloneVmdl);
        var serialized = Kv3.Serialize(first);
        var second = Kv3.Parse(serialized);
        Assert.True(KvValue.DeepEquals(first.Root, second.Root));
        Assert.Equal(serialized, Kv3.Serialize(second));

        // The round-tripped text still carries the events.
        Assert.Equal(clip.FootstepEvents.Count, AnimEventNodes(serialized, clip.ClipName).Count);
    }

    [Fact]
    public void ConvertBatch_Augment_SplicesEventsIntoExistingVmdl()
    {
        var original = File.ReadAllText(FixturePath("kv3", "citizen_human_male.vmdl"));
        var result = Retargeter.ConvertBatch(
            new[] { WalkRequest() }, SboxTarget.Value,
            new BatchOptions { AugmentVmdlText = original });

        Assert.NotNull(result.AugmentedVmdl);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.NotEmpty(clip.FootstepEvents);

        var eventNodes = AnimEventNodes(result.AugmentedVmdl!, clip.ClipName);
        Assert.Equal(clip.FootstepEvents.Count, eventNodes.Count);
        Assert.All(eventNodes, n => Assert.Equal("AE_FOOTSTEP", n.GetString("event_class")));
    }

    [Fact]
    public void Convert_TargetWithoutLegChains_SkipsWithNote()
    {
        // A target rig with incomplete leg chains (no Foot/Toe roles): events must be
        // skipped with a report note, not fail the clip.
        var scene = Retargeter.ImportSource(
            Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()), "synth_walk.bvh");
        var (full, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var footless = new HumanoidRetargeter.Core.Mapping.MappingResult("footless", full.Source)
        {
            Confidence = full.Confidence,
        };
        foreach (var (role, bone) in full.RoleToBone)
        {
            if (!role.ToString().StartsWith("Foot") && !role.ToString().StartsWith("Toe"))
                footless.RoleToBone[role] = bone;
        }
        var spec = new RetargetTargetSpec
        {
            Rig = TargetRig.FromSkeleton(scene.Skeleton, footless),
            VmdlScale = 1.0f,
            DefaultRootBone = "mixamorig:Hips",
        };

        var result = Retargeter.Convert(WalkRequest(), spec);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Empty(clip.FootstepEvents);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("Footstep events skipped"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The AnimEvent children of the named AnimFile node in a vmdl text.</summary>
    internal static List<KvObject> AnimEventNodes(string vmdlText, string clipName)
    {
        var rootNode = (KvObject)((KvObject)Kv3.Parse(vmdlText).Root)["rootNode"];
        var animList = ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
        var animFile = ((KvArray)animList["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimFile" && o.GetString("name") == clipName);
        if (animFile.GetOrNull("children") is not KvArray children)
            return new List<KvObject>();
        return children.Items.OfType<KvObject>()
            .Where(o => o.GetString("_class") == "AnimEvent")
            .ToList();
    }
}
