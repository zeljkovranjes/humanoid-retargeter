using System.Text;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using HumanoidRetargeter.Tests.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Tests for <see cref="BatchOptions.DetectLocomotionSets"/>: directional-family detection
/// (<see cref="LocomotionSetDetector"/>) and the Folder + 2DBlend vmdl emission, verified
/// structurally against the shipped <c>citizen_animationlist.vmdl_prefab</c> blends.
/// </summary>
public class LocomotionSetTests
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
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static AnimEntry Entry(string name, bool looping = true)
        => new() { Name = name, SourceFilename = $"animations/{name.ToLowerInvariant()}.dmx", Looping = looping };

    private static (List<LocomotionSetSpec> Sets, List<LocomotionSetReport> Reports) Detect(
        params string[] names)
        => LocomotionSetDetector.Detect(
            names.Select(n => Entry(n)).ToList(),
            new HashSet<string>(names, StringComparer.OrdinalIgnoreCase),
            autoSuffixCollisions: true);

    private static KvObject FindAnimationList(string vmdlText)
    {
        var rootNode = (KvObject)((KvObject)Kv3.Parse(vmdlText).Root)["rootNode"];
        return ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
    }

    private static string[][] Grid(KvObject blendNode)
        => ((KvArray)blendNode["blend_anim_list"]).Items.Cast<KvArray>()
            .Select(row => row.Items.Cast<KvString>().Select(s => s.Value).ToArray())
            .ToArray();

    // ---------------------------------------------------------------- detector

    [Fact]
    public void Detect_EightWayFamily_BuildsShippedGridLayout()
    {
        var (sets, reports) = Detect(
            "Walk_N", "Walk_NE", "Walk_E", "Walk_SE", "Walk_S", "Walk_SW", "Walk_W", "Walk_NW");

        var set = Assert.Single(sets);
        var report = Assert.Single(reports);
        Assert.True(report.Emitted);
        Assert.Equal("Walk", report.Stem);
        Assert.Equal("Walk", set.FolderName);
        Assert.Equal("Walk_2D", set.BlendName);
        Assert.Equal(8, report.Members.Count);

        // The exact shipped citizen grid: rows = move_x (-1,0,+1), cols = move_y (-1,0,+1);
        // +move_x = forward (N), +move_y = right (E). No idle in the batch, so the center
        // falls back to the forward member (noted).
        Assert.Equal(new[] { "Walk_SW", "Walk_S", "Walk_SE" }, set.BlendGrid[0]);
        Assert.Equal(new[] { "Walk_W", "Walk_N", "Walk_E" }, set.BlendGrid[1]);
        Assert.Equal(new[] { "Walk_NW", "Walk_N", "Walk_NE" }, set.BlendGrid[2]);
        Assert.Equal("Walk_N", report.CenterClipName);
        Assert.Contains(report.Notes, n => n.Contains("center"));
    }

    [Fact]
    public void Detect_CenterCell_PrefersIdleClipFromBatch()
    {
        var (sets, _) = Detect(
            "Walk_N", "Walk_E", "Walk_S", "Walk_W", "Walk_Idle");
        Assert.Equal("Walk_Idle", Assert.Single(sets).BlendGrid[1][1]);

        var (bareStem, _) = Detect("Walk_N", "Walk_E", "Walk_S", "Walk_W", "Walk");
        Assert.Equal("Walk", Assert.Single(bareStem).BlendGrid[1][1]);
    }

    [Fact]
    public void Detect_FourWayWordForms_MapToCardinals_DiagonalsFallBackToRowCardinal()
    {
        var (sets, reports) = Detect("Run_Forward", "Run_Back", "Run_Left", "Run_Right");

        var set = Assert.Single(sets);
        var report = Assert.Single(reports);
        Assert.True(report.Emitted);
        Assert.Equal("Run", report.Stem);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["N"] = "Run_Forward",
                ["E"] = "Run_Right",
                ["S"] = "Run_Back",
                ["W"] = "Run_Left",
            }, report.Members);

        // Missing diagonals reuse the row's cardinal (S row → Back, N row → Forward).
        Assert.Equal(new[] { "Run_Back", "Run_Back", "Run_Back" }, set.BlendGrid[0]);
        Assert.Equal(new[] { "Run_Left", "Run_Forward", "Run_Right" }, set.BlendGrid[1]);
        Assert.Equal(new[] { "Run_Forward", "Run_Forward", "Run_Forward" }, set.BlendGrid[2]);
        Assert.Contains(report.Notes, n => n.Contains("diagonal NE missing"));
    }

    [Fact]
    public void Detect_IncompleteFamily_EmitsNothing_ButReportsTheMissingCardinals()
    {
        // 5 of 8 — but the cardinals N and W are missing, so no blend can be built.
        var (sets, reports) = Detect("Walk_NE", "Walk_E", "Walk_SE", "Walk_S", "Walk_SW");

        Assert.Empty(sets);
        var report = Assert.Single(reports);
        Assert.False(report.Emitted);
        Assert.Equal("Walk", report.Stem);
        Assert.Contains(report.Notes, n => n.Contains("missing cardinal") && n.Contains("N, W"));
    }

    [Fact]
    public void Detect_IgnoresNoise()
    {
        // Single directional clips, additive/mirrored suffixes and undirectional names must
        // not create families.
        var (sets, reports) = Detect(
            "Turn_Left", "Jump", "Walk_N_delta", "Walk_M", "Idle");
        Assert.Empty(sets);
        Assert.Empty(reports);
    }

    [Fact]
    public void Detect_CaseInsensitive_AndCollisionSuffixesBlendNames()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "walk_n", "walk_e", "walk_s", "walk_w",
            "Walk", // a foreign node already owns the stem name
            "Walk_2D", // and the natural blend name
        };
        var (sets, _) = LocomotionSetDetector.Detect(
            new[] { Entry("walk_n"), Entry("walk_e"), Entry("walk_s"), Entry("walk_w") },
            used, autoSuffixCollisions: true);

        var set = Assert.Single(sets);
        Assert.Equal("walk_2", set.FolderName);
        Assert.Equal("walk_2D_2", set.BlendName);
        Assert.Equal(new[] { "walk_n", "walk_e", "walk_s", "walk_w" }, set.MemberNames);
    }

    // ---------------------------------------------------------------- ScanNames (UI dry-run)

    [Fact]
    public void ScanNames_EightWayFamily_CompleteWithEightMembers()
    {
        var families = LocomotionSetDetector.ScanNames(new[]
        {
            "Walk_N", "Walk_NE", "Walk_E", "Walk_SE", "Walk_S", "Walk_SW", "Walk_W", "Walk_NW",
        });

        Assert.Equal(new[] { ("Walk", true, 8) }, families);
    }

    [Fact]
    public void ScanNames_FourWayWordForms_CompleteWithFourMembers()
    {
        var families = LocomotionSetDetector.ScanNames(new[]
        {
            "Run_Forward", "Run_Back", "Run_Left", "Run_Right",
        });

        Assert.Equal(new[] { ("Run", true, 4) }, families);
    }

    [Fact]
    public void ScanNames_IncompleteFamily_ListedButNotComplete()
    {
        // Same shape as the detector's incomplete-family case: 5 of 8, cardinals N and W
        // missing - still reported (≥ 2 members) but not complete.
        var families = LocomotionSetDetector.ScanNames(new[]
        {
            "Walk_NE", "Walk_E", "Walk_SE", "Walk_S", "Walk_SW",
        });

        Assert.Equal(new[] { ("Walk", false, 5) }, families);
    }

    [Fact]
    public void ScanNames_IgnoresNoise()
    {
        // Single directional clips, additive/mirrored suffixes and undirectional names must
        // not create families (mirrors Detect_IgnoresNoise).
        var families = LocomotionSetDetector.ScanNames(new[]
        {
            "Turn_Left", "Jump", "Walk_N_delta", "Walk_M", "Idle",
        });

        Assert.Empty(families);
    }

    [Fact]
    public void ScanNames_MixedFamilies_KeepsFirstSeenOrderAndCasing_DuplicatesCountOnce()
    {
        var families = LocomotionSetDetector.ScanNames(new[]
        {
            "Run_S", "Walk_N", "walk_e", "WALK_s", "Walk_W",
            "Walk_Forward", // duplicate of Walk_N's direction - counted once
            "Run_N", "Run_E", // Run misses W: incomplete
            "Crouch_Left", // single member: noise
        });

        Assert.Equal(new[] { ("Run", false, 3), ("Walk", true, 4) }, families);
    }

    [Fact]
    public void ScanNames_MatchesDetectOnTheSameNames()
    {
        // The dry-run must agree with the real detector on completeness and membership.
        var names = new[]
        {
            "Walk_N", "Walk_E", "Walk_S", "Walk_W", "Walk_NE",
            "Run_Forward", "Run_Left", // incomplete
            "Jump",
        };
        var families = LocomotionSetDetector.ScanNames(names);
        var (sets, reports) = Detect(names);

        Assert.Equal(reports.Select(r => r.Stem), families.Select(f => f.Stem));
        Assert.Equal(reports.Select(r => r.Emitted), families.Select(f => f.Complete));
        Assert.Equal(reports.Select(r => r.Members.Count), families.Select(f => f.MemberCount));
        Assert.Equal(sets.Count, families.Count(f => f.Complete));
    }

    // ---------------------------------------------------------------- shipped-schema fidelity

    [Fact]
    public void Writer_2DBlendNode_MatchesShippedPrefabShapeExactly()
    {
        var prefab = Kv3.Parse(File.ReadAllText(
            SkeletonTests.FixturePath(Path.Combine("kv3", "citizen_animationlist.vmdl_prefab"))));
        var shipped = FindNodeRecursive(prefab.Root, "2DBlend", "CrouchWalk_Default_2D");

        // Document the shipped convention this feature replicates.
        Assert.Equal("move_x", shipped.GetString("row_pose_param_name"));
        Assert.Equal("move_y", shipped.GetString("col_pose_param_name"));
        var axis = new[] { -1.0, 0.0, 1.0 };
        Assert.Equal(axis, ((KvArray)shipped["row_weight_list"]).Items.Cast<KvDouble>().Select(d => d.Value));
        Assert.Equal(axis, ((KvArray)shipped["col_weight_list"]).Items.Cast<KvDouble>().Select(d => d.Value));
        var shippedGrid = Grid(shipped);
        Assert.Equal(new[] { "CrouchWalk_SW", "CrouchWalk_S", "CrouchWalk_SE" }, shippedGrid[0]);
        Assert.Equal(new[] { "CrouchWalk_W", "CrouchIdlePose_Default", "CrouchWalk_E" }, shippedGrid[1]);
        Assert.Equal(new[] { "CrouchWalk_NW", "CrouchWalk_N", "CrouchWalk_NE" }, shippedGrid[2]);

        // Our node (generated through the standalone writer, like the pipeline does):
        // identical attribute key ORDER and value KINDS.
        var entries = new[]
        {
            Entry("Walk_N"), Entry("Walk_NE"), Entry("Walk_E"), Entry("Walk_SE"),
            Entry("Walk_S"), Entry("Walk_SW"), Entry("Walk_W"), Entry("Walk_NW"),
        };
        var (sets, _) = Detect(entries.Select(e => e.Name).ToArray());
        var text = VmdlWriter.GenerateStandalone("", entries, 1.0f, "pelvis", sets);
        var ourFolder = FindNodeRecursive(Kv3.Parse(text).Root, "Folder", "Walk");
        var ours = FindNodeRecursive(Kv3.Parse(text).Root, "2DBlend", "Walk_2D");

        Assert.Equal(shipped.Keys, ours.Keys);
        foreach (var key in shipped.Keys)
        {
            Assert.True(shipped[key].GetType() == ours[key].GetType(),
                $"2DBlend attribute '{key}': shipped {shipped[key].GetType().Name}, "
                + $"ours {ours[key].GetType().Name}");
        }
        Assert.True(KvValue.DeepEquals(shipped["row_weight_list"], ours["row_weight_list"]));
        Assert.True(KvValue.DeepEquals(shipped["col_weight_list"], ours["col_weight_list"]));

        // And the shipped Folder shape (_class, name, children) is what we emit.
        var shippedFolder = FindNodeRecursive(prefab.Root, "Folder", "CrouchWalk");
        Assert.Equal(shippedFolder.Keys, ourFolder.Keys);
    }

    private static KvObject FindNodeRecursive(KvValue node, string cls, string name)
    {
        var matches = new List<KvObject>();
        Walk(node);
        return matches.Single();

        void Walk(KvValue current)
        {
            switch (current)
            {
                case KvObject o:
                    if (o.GetString("_class") == cls && o.GetString("name") == name)
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

    private static RetargetRequest DirectionalRequest(string clipName) => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        ClipNameOverride = clipName,
        LoopingOverride = true,
    };

    [Fact]
    public void ConvertBatch_DetectsFourWaySet_GroupsFolderInStandaloneVmdl()
    {
        var requests = new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" }
            .Select(DirectionalRequest).ToArray();
        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value,
            new BatchOptions { DetectLocomotionSets = true });
        Assert.All(result.Clips, c => Assert.True(c.Success, c.Error));

        var report = Assert.Single(result.LocomotionSets);
        Assert.True(report.Emitted);
        Assert.Equal("Walk", report.FolderName);
        Assert.Equal("Walk_2D", report.BlendNodeName);

        var animList = FindAnimationList(result.StandaloneVmdl);
        var folder = Assert.Single(
            animList.ChildObjects().Where(o => o.GetString("_class") == "Folder"));
        Assert.Equal("Walk", folder.GetString("name"));

        // The folder leads with the blend and contains the four members; no member remains
        // loose at the AnimationList top level.
        var children = folder.ChildObjects().ToList();
        Assert.Equal("2DBlend", children[0].GetString("_class"));
        Assert.Equal("Walk_2D", children[0].GetString("name"));
        Assert.Equal(new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" },
            children.Skip(1).Select(o => o.GetString("name")));
        Assert.All(children.Skip(1), o => Assert.Equal("AnimFile", o.GetString("_class")));
        Assert.DoesNotContain(animList.ChildObjects(),
            o => o.GetString("_class") == "AnimFile" && o.GetString("name")!.StartsWith("Walk_"));

        // Blend wiring: inherited citizen pose params, shipped grid layout, looping.
        var blend = children[0];
        Assert.Equal("move_x", blend.GetString("row_pose_param_name"));
        Assert.Equal("move_y", blend.GetString("col_pose_param_name"));
        Assert.True(((KvBool)blend["looping"]).Value);
        var grid = Grid(blend);
        Assert.Equal(new[] { "Walk_S", "Walk_S", "Walk_S" }, grid[0]);
        Assert.Equal(new[] { "Walk_W", "Walk_N", "Walk_E" }, grid[1]);
        Assert.Equal(new[] { "Walk_N", "Walk_N", "Walk_N" }, grid[2]);

        // The whole document still round-trips.
        var doc = Kv3.Parse(result.StandaloneVmdl);
        Assert.True(KvValue.DeepEquals(doc.Root, Kv3.Parse(Kv3.Serialize(doc)).Root));
    }

    [Fact]
    public void ConvertBatch_DefaultOff_EmitsNoBlendNodes()
    {
        var requests = new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" }
            .Select(DirectionalRequest).ToArray();
        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value);

        Assert.Empty(result.LocomotionSets);
        Assert.DoesNotContain("2DBlend", result.StandaloneVmdl);
        Assert.DoesNotContain("Folder", result.StandaloneVmdl);
    }

    [Fact]
    public void ConvertBatch_IncompleteFamily_NoNode_NoteOnReport()
    {
        var requests = new[] { "Walk_N", "Walk_E", "Walk_S" } // W missing
            .Select(DirectionalRequest).ToArray();
        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value,
            new BatchOptions { DetectLocomotionSets = true });

        var report = Assert.Single(result.LocomotionSets);
        Assert.False(report.Emitted);
        Assert.Contains(report.Notes, n => n.Contains("missing cardinal") && n.Contains("W"));
        Assert.DoesNotContain("2DBlend", result.StandaloneVmdl);
    }

    // ---------------------------------------------------------------- augmenter

    private static List<AnimEntry> FourWay()
        => new() { Entry("Walk_N"), Entry("Walk_E"), Entry("Walk_S"), Entry("Walk_W") };

    private static LocomotionSetSpec FourWaySpec(string folder = "Walk", string blend = "Walk_2D")
        => new()
        {
            FolderName = folder,
            BlendName = blend,
            Looping = true,
            BlendGrid = new[]
            {
                new[] { "Walk_S", "Walk_S", "Walk_S" },
                new[] { "Walk_W", "Walk_N", "Walk_E" },
                new[] { "Walk_N", "Walk_N", "Walk_N" },
            },
            MemberNames = new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" },
        };

    [Fact]
    public void Augment_LocomotionSet_SplicesFolder_Idempotently()
    {
        var original = File.ReadAllText(
            SkeletonTests.FixturePath(Path.Combine("kv3", "citizen_human_male.vmdl")));
        var options = new AugmentOptions
        {
            DefaultRootBone = "pelvis",
            LocomotionSets = new[] { FourWaySpec() },
            // Entries' source_filenames live under "animations/" — declare it so the
            // re-run recognizes the folder as pipeline-owned (the replaceability rule).
            DmxFolderRelative = "animations",
        };

        var once = VmdlAugmenter.Augment(original, FourWay(), out _, options);
        var animList = FindAnimationList(once);
        var folder = animList.ChildObjects()
            .Single(o => o.GetString("_class") == "Folder" && o.GetString("name") == "Walk");
        Assert.Equal(5, folder.ChildObjects().Count()); // 2DBlend + 4 members
        Assert.DoesNotContain(animList.ChildObjects(),
            o => o.GetString("_class") == "AnimFile" && o.GetString("name") == "Walk_N");

        var twice = VmdlAugmenter.Augment(once, FourWay(), out _, options);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Augment_LocomotionSet_AdoptsLooseMembersFromAPreviousRunWithoutGrouping()
    {
        // First run WITHOUT locomotion grouping leaves the members at the top level; a
        // re-run WITH grouping must move them into the folder, never duplicate them.
        var original = File.ReadAllText(
            SkeletonTests.FixturePath(Path.Combine("kv3", "citizen_human_male.vmdl")));
        var flat = VmdlAugmenter.Augment(original, FourWay(), out _,
            new AugmentOptions { DefaultRootBone = "pelvis" });
        var grouped = VmdlAugmenter.Augment(flat, FourWay(), out _, new AugmentOptions
        {
            DefaultRootBone = "pelvis",
            LocomotionSets = new[] { FourWaySpec() },
        });

        var animList = FindAnimationList(grouped);
        var walkNodes = CountNamed(animList, "Walk_N");
        Assert.Equal(1, walkNodes);
        Assert.Single(animList.ChildObjects(),
            o => o.GetString("_class") == "Folder" && o.GetString("name") == "Walk");
    }

    private static int CountNamed(KvObject node, string name)
    {
        var count = 0;
        Walk(node);
        return count;

        void Walk(KvObject current)
        {
            if (current.GetString("name") == name)
                count++;
            if (current.GetOrNull("children") is KvArray children)
            {
                foreach (var child in children.Items.OfType<KvObject>())
                    Walk(child);
            }
        }
    }

    [Fact]
    public void Augment_ForeignFolderWithSameName_Throws()
    {
        const string foreignFolderVmdl =
            "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
            "{\n" +
            "\trootNode = \n" +
            "\t{\n" +
            "\t\t_class = \"RootNode\"\n" +
            "\t\tchildren = \n" +
            "\t\t[\n" +
            "\t\t\t{\n" +
            "\t\t\t\t_class = \"AnimationList\"\n" +
            "\t\t\t\tchildren = \n" +
            "\t\t\t\t[\n" +
            "\t\t\t\t\t{\n" +
            "\t\t\t\t\t\t_class = \"Folder\"\n" +
            "\t\t\t\t\t\tname = \"Walk\"\n" +
            "\t\t\t\t\t\tchildren = \n" +
            "\t\t\t\t\t\t[\n" +
            "\t\t\t\t\t\t\t{\n" +
            "\t\t\t\t\t\t\t\t_class = \"AnimFile\"\n" +
            "\t\t\t\t\t\t\t\tname = \"UserClip\"\n" +
            "\t\t\t\t\t\t\t\tsource_filename = \"animations/userclip.fbx\"\n" +
            "\t\t\t\t\t\t\t},\n" +
            "\t\t\t\t\t\t]\n" +
            "\t\t\t\t\t},\n" +
            "\t\t\t\t]\n" +
            "\t\t\t\tdefault_root_bone_name = \"pelvis\"\n" +
            "\t\t\t},\n" +
            "\t\t]\n" +
            "\t\tbase_model_name = \"\"\n" +
            "\t}\n" +
            "}\n";

        var ex = Assert.Throws<VmdlAugmentException>(() => VmdlAugmenter.Augment(
            foreignFolderVmdl, FourWay(), out _, new AugmentOptions
            {
                DefaultRootBone = "pelvis",
                LocomotionSets = new[] { FourWaySpec() },
            }));
        Assert.Contains(ex.Collisions, c => c.Contains("Walk"));
    }

    [Fact]
    public void ConvertBatch_AugmentVmdlWithForeignWalkFolder_SuffixesTheGeneratedFolder()
    {
        // The batch seeds names from the existing vmdl: a FOREIGN folder named "Walk"
        // reserves the stem, so the generated folder gets suffixed instead of replacing it.
        const string vmdlWithForeignWalk =
            "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
            "{\n" +
            "\trootNode = \n" +
            "\t{\n" +
            "\t\t_class = \"RootNode\"\n" +
            "\t\tchildren = \n" +
            "\t\t[\n" +
            "\t\t\t{\n" +
            "\t\t\t\t_class = \"AnimationList\"\n" +
            "\t\t\t\tchildren = \n" +
            "\t\t\t\t[\n" +
            "\t\t\t\t\t{\n" +
            "\t\t\t\t\t\t_class = \"Folder\"\n" +
            "\t\t\t\t\t\tname = \"Walk\"\n" +
            "\t\t\t\t\t\tchildren = \n" +
            "\t\t\t\t\t\t[\n" +
            "\t\t\t\t\t\t\t{\n" +
            "\t\t\t\t\t\t\t\t_class = \"AnimFile\"\n" +
            "\t\t\t\t\t\t\t\tname = \"UserClip\"\n" +
            "\t\t\t\t\t\t\t\tsource_filename = \"animations/userclip.fbx\"\n" +
            "\t\t\t\t\t\t\t},\n" +
            "\t\t\t\t\t\t]\n" +
            "\t\t\t\t\t},\n" +
            "\t\t\t\t]\n" +
            "\t\t\t\tdefault_root_bone_name = \"pelvis\"\n" +
            "\t\t\t},\n" +
            "\t\t]\n" +
            "\t\tbase_model_name = \"\"\n" +
            "\t}\n" +
            "}\n";

        var requests = new[] { "Walk_N", "Walk_E", "Walk_S", "Walk_W" }
            .Select(DirectionalRequest).ToArray();
        var result = Retargeter.ConvertBatch(requests, SboxTarget.Value, new BatchOptions
        {
            DetectLocomotionSets = true,
            AugmentVmdlText = vmdlWithForeignWalk,
        });

        Assert.Empty(result.Errors);
        var report = Assert.Single(result.LocomotionSets);
        Assert.Equal("Walk_2", report.FolderName);

        var animList = FindAnimationList(result.AugmentedVmdl!);
        var folders = animList.ChildObjects()
            .Where(o => o.GetString("_class") == "Folder").ToList();
        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, f => f.GetString("name") == "Walk");   // untouched foreign
        Assert.Contains(folders, f => f.GetString("name") == "Walk_2"); // ours
        Assert.Equal(1, CountNamed(animList, "UserClip"));
    }
}
