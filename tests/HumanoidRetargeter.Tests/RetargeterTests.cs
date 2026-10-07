using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// End-to-end facade tests: real fixture bytes in, DMX/vmdl text out, fully in memory.
/// </summary>
public class RetargeterTests
{
    // ---------------------------------------------------------------- fixtures

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
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static RetargetRequest FbxRequest(string fixtureName, Action<RequestSettings>? configure = null)
    {
        var settings = new RequestSettings();
        configure?.Invoke(settings);
        return new RetargetRequest
        {
            SourceData = File.ReadAllBytes(FixturePath("fbx", fixtureName)),
            SourceFileName = fixtureName,
            RootMotion = settings.RootMotion,
        };
    }

    private sealed class RequestSettings
    {
        public RootMotionMode RootMotion { get; set; } = RootMotionMode.Off;
    }

    // ---------------------------------------------------------------- a. single-file convert

    [Fact]
    public void Convert_ZombieCrawl_ProducesDmxWithMixamoMapping()
    {
        var request = FbxRequest("Zombie Crawl.fbx");
        var result = Retargeter.Convert(request, SboxTarget.Value);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        // DMX content: non-empty, carries the pelvis channel pair (position + orientation).
        Assert.False(string.IsNullOrEmpty(clip.DmxContent));
        Assert.Contains("pelvis_p", clip.DmxContent);
        Assert.Contains("pelvis_o", clip.DmxContent);
        Assert.EndsWith(".dmx", clip.DmxFileName);

        // Mapping report: the shipped mixamo preset matched — no user decision needed.
        Assert.NotNull(clip.Mapping);
        Assert.Equal("mixamo", clip.Mapping!.ProfileName);
        Assert.Equal(MappingSource.Preset, clip.Mapping.Source);
        Assert.False(clip.Mapping.NeedsUserDecision);
        Assert.True(clip.Mapping.Confidence >= 0.8f);
        Assert.Equal(64, clip.Mapping.SkeletonSignature.Length);

        // Preview data: one local-XForm set per TARGET bone per source frame.
        var source = FbxImporter.Import(File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")));
        Assert.NotNull(clip.SolvedFrames);
        Assert.Equal(source.Clips[0].FrameCount, clip.SolvedFrames!.Count);
        Assert.All(clip.SolvedFrames, f => Assert.Equal(SboxTarget.Value.Rig.Skeleton.Count, f.Length));
        Assert.Equal(source.Clips[0].Fps, clip.Fps);

        // The standalone vmdl registers the clip.
        AssertVmdlHasAnimFiles(result.StandaloneVmdl, clip.ClipName);

        // Inspect (UI listing path) agrees with the conversion's detection and reports
        // the file's (single) take.
        var inspected = Retargeter.Inspect(request.SourceData, request.SourceFileName);
        Assert.Equal("mixamo", inspected.Mapping.ProfileName);
        Assert.False(inspected.Mapping.NeedsUserDecision);
        Assert.Equal(1, inspected.TakeCount);
    }

    // ---------------------------------------------------------------- b. 3-profile batch

    [Fact]
    public void ConvertBatch_MixedProfiles_OneVmdlWithAllClips()
    {
        var requests = new[]
        {
            FbxRequest("Zombie Crawl.fbx"),
            new RetargetRequest
            {
                SourceData = File.ReadAllBytes(
                    RepoFile("dev", "corpus", "actorcore", "catwalk-loop-378982.fbx")),
                SourceFileName = "catwalk-loop-378982.fbx",
            },
            new RetargetRequest
            {
                SourceData = File.ReadAllBytes(FixturePath("bvh", "bvhpython_test_freebvh.bvh")),
                SourceFileName = "bvhpython_test_freebvh.bvh",
            },
        };

        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value);

        Assert.True(result.Clips.Count >= 3);
        Assert.All(result.Clips, c => Assert.True(c.Success, $"{c.SourceFileName}: {c.Error}"));
        Assert.Empty(result.Errors);

        // Per-item profiles: every clip ran its own detection at preset-grade confidence,
        // and the batch mixes at least two distinct profiles (mixamo + actorcore_cc; the
        // freebvh file is mixamorig-named, so it legitimately detects as mixamo too).
        var profiles = result.Clips.Select(c => c.Mapping!.ProfileName).Distinct().ToList();
        Assert.True(profiles.Count >= 2, $"expected ≥2 distinct profiles, got: {string.Join(", ", profiles)}");
        Assert.Contains("mixamo", profiles);
        Assert.Contains("actorcore_cc", profiles);
        Assert.All(result.Clips, c => Assert.True(
            c.Mapping!.Confidence >= 0.8f, $"{c.SourceFileName}: confidence {c.Mapping.Confidence}"));

        // ONE standalone vmdl containing every successful clip's AnimFile entry.
        AssertVmdlHasAnimFiles(result.StandaloneVmdl, result.Clips.Select(c => c.ClipName).ToArray());
    }

    // ---------------------------------------------------------------- c. augment path

    [Fact]
    public void ConvertBatch_AugmentsExistingVmdl()
    {
        var original = File.ReadAllText(FixturePath("kv3", "citizen_human_male.vmdl"));
        var result = Retargeter.ConvertBatch(
            new[] { FbxRequest("Zombie Crawl.fbx") },
            SboxTarget.Value,
            new BatchOptions { AugmentVmdlText = original });

        Assert.NotNull(result.AugmentedVmdl);
        var clip = Assert.Single(result.Clips);
        AssertVmdlHasAnimFiles(result.AugmentedVmdl!, clip.ClipName); // parses + gained the entry
    }

    /// <summary>Minimal augmentable vmdl whose AnimationList already carries one AnimFile.</summary>
    private static string VmdlWithAnimFile(string name, string sourceFilename) =>
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
        "{\n\trootNode = \n\t{\n\t\t_class = \"RootNode\"\n\t\tchildren = \n\t\t[\n" +
        "\t\t\t{\n\t\t\t\t_class = \"AnimationList\"\n\t\t\t\tchildren = \n\t\t\t\t[\n" +
        "\t\t\t\t\t{\n\t\t\t\t\t\t_class = \"AnimFile\"\n" +
        $"\t\t\t\t\t\tname = \"{name}\"\n" +
        "\t\t\t\t\t\tlooping = false\n" +
        $"\t\t\t\t\t\tsource_filename = \"{sourceFilename}\"\n" +
        "\t\t\t\t\t},\n\t\t\t\t]\n\t\t\t\tdefault_root_bone_name = \"pelvis\"\n\t\t\t},\n" +
        "\t\t]\n\t\tbase_model_name = \"\"\n\t}\n}\n";

    [Fact]
    public void ConvertBatch_AugmentCollisionWithForeignAnimFile_SuffixesNewClip_KeepsForeignNode()
    {
        // The user's own 'Zombie_Crawl' AnimFile points at a DMX OUTSIDE our output folder:
        // it must never be silently repointed — our clip gets the _2 suffix instead.
        var existing = VmdlWithAnimFile("Zombie_Crawl", "animations/other/walk.dmx");
        var result = Retargeter.ConvertBatch(
            new[] { FbxRequest("Zombie Crawl.fbx") },
            SboxTarget.Value,
            new BatchOptions { AugmentVmdlText = existing });

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("Zombie_Crawl_2", clip.ClipName);

        var animFiles = AnimFilesByName(result.AugmentedVmdl!);
        Assert.Equal("animations/other/walk.dmx", animFiles["Zombie_Crawl"].GetString("source_filename"));
        Assert.Equal("animations/retargeted/zombie_crawl_2.dmx",
            animFiles["Zombie_Crawl_2"].GetString("source_filename"));
    }

    [Fact]
    public void ConvertBatch_AugmentCollisionWithOwnAnimFile_ReplacesItInPlace()
    {
        // An AnimFile whose source_filename lives under OUR output folder is a previous run
        // of this batch — replace it idempotently, no suffix.
        var existing = VmdlWithAnimFile("Zombie_Crawl", "animations/retargeted/zombie_crawl.dmx");
        var result = Retargeter.ConvertBatch(
            new[] { FbxRequest("Zombie Crawl.fbx") },
            SboxTarget.Value,
            new BatchOptions { AugmentVmdlText = existing });

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("Zombie_Crawl", clip.ClipName);

        var animFiles = AnimFilesByName(result.AugmentedVmdl!);
        var node = Assert.Single(animFiles).Value;
        Assert.Equal("animations/retargeted/zombie_crawl.dmx", node.GetString("source_filename"));
        // Replaced with the full generated attribute set, not the stub that was there.
        Assert.NotNull(node.GetOrNull("fade_in_time"));
    }

    private static Dictionary<string, KvObject> AnimFilesByName(string vmdlText)
    {
        var rootNode = (KvObject)((KvObject)Kv3.Parse(vmdlText).Root)["rootNode"];
        var animList = ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
        return ((KvArray)animList["children"]).Items.OfType<KvObject>()
            .Where(o => o.GetString("_class") == "AnimFile")
            .ToDictionary(o => o.GetString("name")!, o => o);
    }

    // ---------------------------------------------------------------- request identity / fps

    [Fact]
    public void ConvertBatch_SourceId_FlowsThroughToClips_AndDefaultsToFileName()
    {
        var withId = new RetargetRequest
        {
            SourceData = File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")),
            SourceFileName = "Zombie Crawl.fbx",
            SourceId = @"C:\anims\a\Zombie Crawl.fbx",
        };
        var withoutId = FbxRequest("Zombie Crawl.fbx");

        var result = Retargeter.ConvertBatch(new[] { withId, withoutId }, SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.Equal(@"C:\anims\a\Zombie Crawl.fbx", result.Clips[0].SourceId);
        Assert.Equal("Zombie Crawl.fbx", result.Clips[1].SourceId);
    }

    [Fact]
    public void Convert_SampleFps_DrivesImportResampling()
    {
        var at30 = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value).Clips[0];
        var at15 = Retargeter.Convert(new RetargetRequest
        {
            SourceData = File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")),
            SourceFileName = "Zombie Crawl.fbx",
            SampleFps = 15f,
        }, SboxTarget.Value).Clips[0];

        Assert.True(at15.Success, at15.Error);
        Assert.Equal(30f, at30.Fps);
        Assert.Equal(15f, at15.Fps);
        Assert.True(at15.SolvedFrames!.Count < at30.SolvedFrames!.Count);
    }

    // ---------------------------------------------------------------- DMX wiring (design §3 / axes)

    [Fact]
    public void Convert_SboxTarget_WritesNoChannelsForConstraintDrivenBones()
    {
        var clip = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value).Clips[0];
        Assert.True(clip.Success, clip.Error);

        // The twist bone keeps its DmeJoint/bind entry but gets no DmeChannel pair...
        Assert.Contains("\"arm_upper_L_twist0\"", clip.DmxContent);
        Assert.DoesNotContain("arm_upper_L_twist0_p", clip.DmxContent);
        Assert.DoesNotContain("arm_upper_L_twist0_o", clip.DmxContent);

        // ...while animated bones keep theirs.
        Assert.Contains("pelvis_p", clip.DmxContent);
    }

    [Fact]
    public void Convert_ZUpEngineTarget_DeclaresZUpAxisSystem_AndScaledThresholdsStillSolve()
    {
        // Mixamo rig reused as an "engine-space" target (units pretend-inches): the DMX must
        // declare Z-up so resourcecompiler does not run its Y-up conversion on top.
        var source = FbxImporter.Import(File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")));
        var map = ProfileDetector.Detect(source.Skeleton)!.Value.Result;
        var spec = new RetargetTargetSpec
        {
            Rig = TargetRig.FromSkeleton(source.Skeleton, map),
            VmdlScale = 1.0f,
            BaseModelPath = "",
            DefaultRootBone = "mixamorig1:Hips",
            UpAxis = TargetUpAxis.ZUpEngine,
        };

        var clip = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), spec).Clips[0];
        Assert.True(clip.Success, clip.Error);
        Assert.Contains("\"upAxis\" \"string\" \"Z\"", clip.DmxContent);
        Assert.Contains("\"upAxis\" \"int\" \"3\"", clip.DmxContent);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("Z-up"));

        // The default sbox target keeps declaring Y-up.
        var yUp = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value).Clips[0];
        Assert.Contains("\"upAxis\" \"string\" \"Y\"", yUp.DmxContent);
        Assert.Contains("\"upAxis\" \"int\" \"2\"", yUp.DmxContent);
    }

    // ---------------------------------------------------------------- CopyPinky notes

    [Fact]
    public void Convert_StandaloneWithPinkyMapping_NotesBaseModelCopyPinky()
    {
        // mixamo maps the pinky chain; the sbox standalone vmdl references the citizen base
        // model whose CopyPinky constraints cannot be overridden from a child vmdl.
        var clip = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value).Clips[0];
        Assert.True(clip.Success, clip.Error);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("CopyPinky"));

        // In augment mode the constraints ARE neutralized, so the note is not added.
        var augmented = Retargeter.ConvertBatch(
            new[] { FbxRequest("Zombie Crawl.fbx") },
            SboxTarget.Value,
            new BatchOptions
            {
                AugmentVmdlText = File.ReadAllText(FixturePath("kv3", "citizen_human_male.vmdl")),
            });
        Assert.DoesNotContain(augmented.Clips[0].Mapping!.Notes, n => n.Contains("CopyPinky"));
        Assert.Contains("weight = 0.0", augmented.AugmentedVmdl);
    }

    // ---------------------------------------------------------------- public mapping cascade

    [Fact]
    public void ResolveMapping_UserPresetLookup_RunsBeforePresetDetection()
    {
        var skeleton = FbxImporter.Import(
            File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx"))).Skeleton;

        var userPreset = new MappingResult("user_test", MappingSource.UserPreset) { Confidence = 1f };
        userPreset.RoleToBone[BoneRole.Hips] = 0;

        string? seenSignature = null;
        var (map, report) = Retargeter.ResolveMapping(skeleton, null, signature =>
        {
            seenSignature = signature;
            return userPreset;
        });

        Assert.Same(userPreset, map);
        Assert.Equal(MappingSource.UserPreset, report.Source);
        Assert.False(report.NeedsUserDecision);
        Assert.Equal(SkeletonSignature.Compute(skeleton), seenSignature);

        // Without the lookup the cascade falls through to preset detection.
        var (detected, _) = Retargeter.ResolveMapping(skeleton);
        Assert.Equal("mixamo", detected.ProfileName);
    }

    // ---------------------------------------------------------------- d. failure isolation

    [Fact]
    public void ConvertBatch_BadFileDoesNotAbortBatch()
    {
        var requests = new[]
        {
            FbxRequest("Zombie Crawl.fbx"),
            new RetargetRequest
            {
                SourceData = Encoding.UTF8.GetBytes("not a model"),
                SourceFileName = "garbage.fbx",
            },
        };

        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        var ok = result.Clips.Single(c => c.Success);
        var bad = result.Clips.Single(c => !c.Success);
        Assert.Equal("Zombie Crawl.fbx", ok.SourceFileName);
        Assert.Equal("garbage.fbx", bad.SourceFileName);
        Assert.False(string.IsNullOrEmpty(bad.Error));
        Assert.Single(result.Errors);

        // The batch result stays usable: the good clip is registered in the vmdl.
        AssertVmdlHasAnimFiles(result.StandaloneVmdl, ok.ClipName);
    }

    // ---------------------------------------------------------------- e. collision suffixing

    [Fact]
    public void ConvertBatch_SameFileTwice_SuffixesSecondClipName()
    {
        var result = Retargeter.ConvertBatch(
            new[] { FbxRequest("Zombie Crawl.fbx"), FbxRequest("Zombie Crawl.fbx") },
            SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));
        Assert.Equal(result.Clips[0].ClipName + "_2", result.Clips[1].ClipName);
        Assert.NotEqual(result.Clips[0].DmxFileName, result.Clips[1].DmxFileName);
        AssertVmdlHasAnimFiles(result.StandaloneVmdl, result.Clips[0].ClipName, result.Clips[1].ClipName);
    }

    // ---------------------------------------------------------------- f. NeedsUserDecision

    [Fact]
    public void Convert_NonsenseBoneNames_FlagsUserDecisionButStillConverts()
    {
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = Encoding.UTF8.GetBytes(ScrambledHumanoidBvh()),
            SourceFileName = "mystery_rig.bvh",
        }, SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.NotNull(clip.Mapping);

        // No preset can match the nonsense names AND the auto map stays below the preset
        // threshold (topology fallback caps its own confidence) → the UI must ask, but the
        // pipeline still produced a best-effort conversion.
        Assert.True(clip.Mapping!.NeedsUserDecision);
        Assert.True(clip.Mapping.Confidence < 0.8f);
        Assert.False(string.IsNullOrEmpty(clip.DmxContent));
    }

    // ---------------------------------------------------------------- g. root motion

    [Fact]
    public void Convert_RootMotionExtract_SetsExtractMotionInVmdlAndNotes()
    {
        var result = Retargeter.Convert(
            FbxRequest("Zombie Crawl.fbx", s => s.RootMotion = RootMotionMode.Extract),
            SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.True(clip.ExtractMotion);

        // The sbox target has no dedicated animated root distinct from the hips (pelvis is
        // parentless, root_IK is IkBaked) → frames untouched, extraction delegated to the
        // vmdl AnimFile ExtractMotion node, with a report note saying so.
        Assert.Contains("ExtractMotion", result.StandaloneVmdl);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("ExtractMotion"));
    }

    [Fact]
    public void Convert_RootMotionInPlace_RemovesHorizontalHipsTravel()
    {
        var result = Retargeter.Convert(
            FbxRequest("Zombie Crawl.fbx", s => s.RootMotion = RootMotionMode.InPlace),
            SboxTarget.Value);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);

        var rig = SboxTarget.Value.Rig;
        var up = TargetUp(rig);
        var pelvis = rig.BoneForRole(BoneRole.Hips)!.Value;

        // Zombie Crawl travels far; in-place must keep the pelvis horizontally near frame 0.
        var first = HorizontalPelvis(clip.SolvedFrames![0], rig, pelvis, up);
        var maxTravel = clip.SolvedFrames!
            .Select(f => Vector3.Distance(HorizontalPelvis(f, rig, pelvis, up), first))
            .Max();
        Assert.True(maxTravel < 2f, $"horizontal hips travel {maxTravel:0.00} cm, expected < 2 cm");

        // Sanity: without root-motion processing the same clip travels a lot.
        var off = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value);
        var firstOff = HorizontalPelvis(off.Clips[0].SolvedFrames![0], rig, pelvis, up);
        var maxTravelOff = off.Clips[0].SolvedFrames!
            .Select(f => Vector3.Distance(HorizontalPelvis(f, rig, pelvis, up), firstOff))
            .Max();
        Assert.True(maxTravelOff > 10f, $"expected the raw crawl to travel, got {maxTravelOff:0.00} cm");
    }

    // ---------------------------------------------------------------- optional arm IK

    [Fact]
    public void Convert_WithArmEffectorIk_StillSucceeds()
    {
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = File.ReadAllBytes(FixturePath("fbx", "Zombie Crawl.fbx")),
            SourceFileName = "Zombie Crawl.fbx",
            ArmEffectorIk = true,
        }, SboxTarget.Value);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.DoesNotContain(clip.Mapping!.Notes, n => n.Contains("Arm effector IK skipped"));

        // The pass must actually change the arm solution relative to the default pipeline.
        var baseline = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value);
        Assert.NotEqual(baseline.Clips[0].DmxContent, clip.DmxContent);
    }

    // ---------------------------------------------------------------- h. determinism

    [Fact]
    public void Convert_IsDeterministic()
    {
        var a = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value);
        var b = Retargeter.Convert(FbxRequest("Zombie Crawl.fbx"), SboxTarget.Value);

        Assert.Equal(a.Clips[0].DmxContent, b.Clips[0].DmxContent);
        Assert.Equal(a.Clips[0].ClipName, b.Clips[0].ClipName);
        Assert.Equal(a.StandaloneVmdl, b.StandaloneVmdl);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Parses the vmdl text and asserts its AnimationList carries an AnimFile per
    /// expected clip name.</summary>
    private static void AssertVmdlHasAnimFiles(string vmdlText, params string[] clipNames)
    {
        var doc = Kv3.Parse(vmdlText);
        var rootNode = (KvObject)((KvObject)doc.Root)["rootNode"];
        var animList = ((KvArray)rootNode["children"]).Items
            .OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
        var names = ((KvArray)animList["children"]).Items
            .OfType<KvObject>()
            .Where(o => o.GetString("_class") == "AnimFile")
            .Select(o => o.GetString("name"))
            .ToList();
        foreach (var clipName in clipNames)
            Assert.Contains(clipName, names);
    }

    /// <summary>Target character up from public rest geometry: midShoulders − midHips.</summary>
    private static Vector3 TargetUp(TargetRig rig)
    {
        Vector3 Pos(BoneRole role) => rig.Skeleton.RestWorld[rig.BoneForRole(role)!.Value].Pos;
        var midHips = (Pos(BoneRole.UpperLegL) + Pos(BoneRole.UpperLegR)) * 0.5f;
        var midShoulders = (Pos(BoneRole.UpperArmL) + Pos(BoneRole.UpperArmR)) * 0.5f;
        return Vector3.Normalize(midShoulders - midHips);
    }

    private static Vector3 HorizontalPelvis(
        HumanoidRetargeter.Core.Maths.XForm[] frame, TargetRig rig, int pelvis, Vector3 up)
    {
        var pos = new Pose(frame).ToWorld(rig.Skeleton)[pelvis].Pos;
        return pos - Vector3.Dot(pos, up) * up;
    }

    /// <summary>
    /// A syntactically valid BVH with humanoid TOPOLOGY but nonsense joint names (j00…j20):
    /// no preset profile can match by name, forcing the auto-mapper's topology fallback,
    /// whose capped confidence keeps the result below the preset threshold.
    /// </summary>
    private static string ScrambledHumanoidBvh()
    {
        var sb = new StringBuilder();
        sb.AppendLine("HIERARCHY");
        sb.AppendLine("ROOT j00");
        sb.AppendLine("{");
        sb.AppendLine("  OFFSET 0 0 0");
        sb.AppendLine("  CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation");

        void Joint(string name, float x, float y, float z, Action? children = null, (float X, float Y, float Z)? end = null)
        {
            sb.AppendLine($"  JOINT {name}");
            sb.AppendLine("  {");
            sb.AppendLine($"    OFFSET {x} {y} {z}");
            sb.AppendLine("    CHANNELS 3 Zrotation Xrotation Yrotation");
            children?.Invoke();
            if (end is { } e)
            {
                sb.AppendLine("    End Site");
                sb.AppendLine("    {");
                sb.AppendLine($"      OFFSET {e.X} {e.Y} {e.Z}");
                sb.AppendLine("    }");
            }
            sb.AppendLine("  }");
        }

        // Spine chain: j01→j02→j03 then neck j04 → head j05; arms branch off the chest j03.
        Joint("j01", 0, 10, 0, () =>
        Joint("j02", 0, 12, 0, () =>
        Joint("j03", 0, 12, 0, () =>
        {
            Joint("j04", 0, 14, 0, () =>
                Joint("j05", 0, 6, 0, end: (0, 12, 0)));
            // Left arm (left = +X, mixamo-style): clavicle → upper → fore → hand.
            Joint("j06", 6, 8, 0, () =>
            Joint("j07", 12, 0, 0, () =>
            Joint("j08", 26, 0, 0, () =>
            Joint("j09", 25, 0, 0, end: (10, 0, 0)))));
            // Right arm.
            Joint("j10", -6, 8, 0, () =>
            Joint("j11", -12, 0, 0, () =>
            Joint("j12", -26, 0, 0, () =>
            Joint("j13", -25, 0, 0, end: (-10, 0, 0)))));
        })));
        // Legs off the hips: upper → lower → foot → toe.
        Joint("j14", 9, -5, 0, () =>
        Joint("j15", 0, -40, 0, () =>
        Joint("j16", 0, -40, 0, () =>
        Joint("j17", 0, -8, 12, end: (0, 0, 8)))));
        Joint("j18", -9, -5, 0, () =>
        Joint("j19", 0, -40, 0, () =>
        Joint("j20", 0, -40, 0, () =>
        Joint("j21", 0, -8, 12, end: (0, 0, 8)))));

        sb.AppendLine("}");
        sb.AppendLine("MOTION");
        sb.AppendLine("Frames: 3");
        sb.AppendLine("Frame Time: 0.0333333");
        // 6 root channels + 21 joints x 3 channels = 69 values per frame; a tiny hip bob.
        for (var f = 0; f < 3; f++)
        {
            sb.Append($"0 {95 + f} 0 0 0 0");
            sb.AppendLine(string.Concat(Enumerable.Repeat(" 0 0 0", 21)));
        }
        return sb.ToString();
    }
}
