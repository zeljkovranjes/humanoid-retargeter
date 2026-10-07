using System.Numerics;
using HumanoidRetargeter.Core.Formats.Renderware;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Formats;

// =============================================================================================
// GROUND TRUTH: real FreeStyle 2 (FSB2) game data, extracted by dev/tools/renderware/
// rw_explore.py from the user's install:
//   fixtures/renderware/f_upper_b01.dff       character.pak: Character\body\body_upper\dff\
//                                             f_upper_b01.dff — 72 frames, 71 HAnim nodes,
//                                             3ds Max Biped names in RpUserData (0x11F)
//   fixtures/renderware/7FEA6AEBBEB5C5F3.an5  smallest character motion bank (2 takes)
//   fixtures/renderware/FE450CFBB1F2A8CA.an5  small bank (5 takes)
//   fixtures/renderware/BACK0098_CATCH.anm    item-prop clip, 20-node rig, 553 keyframes
//   fixtures/renderware/BACK0098_IDLE_1.anm   item-prop clip, 40 keyframes
//   fixtures/renderware/TWN_ABACK0016_FIX.anm tiny item clip (6 keyframes)
//
// Format structure was mapped empirically over the ENTIRE corpus (dev/tools/renderware/
// rw_explore.py sweep: 275/275 .anm + 307/307 .an5 parse clean; every .an5 take = one
// 3-node full-TRS anim + one 71-node rotation-only anim + a marker table). The rig's rest
// FK was verified plausible (pelvis (0, 98.7, 2.8), head top ~184 cm, feet at ground,
// toes +Z) and the (nodeId, name, parent) list is byte-identical across 5 characters
// sampled from character.pak.
// =============================================================================================
public class RenderwareTests
{
    private static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "renderware", name));

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

    private static RwDffSkeletonData Dff() => RwDffSkeleton.Parse(Fixture("f_upper_b01.dff"));

    private static SourceScene ImportBank(string name = "7FEA6AEBBEB5C5F3.an5")
        => RwAnmImporter.Import(Fixture(name), Fixture("f_upper_b01.dff"),
            new RwAnmImportOptions { ClipNameBase = Path.GetFileNameWithoutExtension(name) });

    // ================================================================ dff skeleton

    [Fact]
    public void Dff_Parses_71Nodes_72Frames()
    {
        var dff = Dff();
        Assert.Equal(71, dff.Nodes.Count);
        Assert.Equal(72, dff.FrameCount);
    }

    [Fact]
    public void Dff_NodeOrder_And_Names_MatchGameData()
    {
        var dff = Dff();
        // Node order = HAnim node table order = animation keyframe order.
        Assert.Equal(2000, dff.Nodes[0].NodeId);
        Assert.Equal("Dummy01", dff.Nodes[0].Name);
        Assert.Equal(2001, dff.Nodes[1].NodeId);
        Assert.Equal("Ball_Point", dff.Nodes[1].Name);
        Assert.Equal(1000, dff.Nodes[2].NodeId);
        Assert.Equal("Bip01", dff.Nodes[2].Name);
        Assert.Equal(1045, dff.Nodes[3].NodeId);
        Assert.Equal("Bip01 Pelvis", dff.Nodes[3].Name);

        // All names unique + non-empty (Skeleton.Create requirement).
        var names = dff.Nodes.Select(n => n.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(names, string.IsNullOrEmpty);
    }

    [Fact]
    public void Dff_Parents_FollowFrameHierarchy()
    {
        var dff = Dff();
        var byName = dff.Nodes.ToDictionary(n => n.Name);
        string ParentOf(string name)
        {
            var p = byName[name].ParentIndex;
            return p < 0 ? "<root>" : dff.Nodes[p].Name;
        }

        Assert.Equal("<root>", ParentOf("Dummy01"));
        Assert.Equal("Dummy01", ParentOf("Ball_Point"));
        Assert.Equal("Dummy01", ParentOf("Bip01"));
        Assert.Equal("Bip01", ParentOf("Bip01 Pelvis"));
        Assert.Equal("Bip01 Pelvis", ParentOf("Bip01 L Thigh"));
        Assert.Equal("Bip01 L Thigh", ParentOf("Bip01 L Calf"));
        Assert.Equal("Bip01 L Calf", ParentOf("Bip01 L Foot"));
        Assert.Equal("Bip01 L Foot", ParentOf("Bip01 L Toe0"));
        Assert.Equal("Bip01 Spine3", ParentOf("Bip01 Neck"));
        Assert.Equal("Bip01 Neck", ParentOf("Bip01 L Clavicle"));
        Assert.Equal("Bip01 L Clavicle", ParentOf("Bip01 L UpperArm"));
        Assert.Equal("Bip01 L UpperArm", ParentOf("Bip01 L Forearm"));
        Assert.Equal("Bip01 L Forearm", ParentOf("Bip01 L Hand"));

        // Parent-first ordering (what positional keyframe assignment relies on).
        for (var n = 0; n < dff.Nodes.Count; n++)
            Assert.True(dff.Nodes[n].ParentIndex < n);
    }

    [Fact]
    public void Dff_RestPose_IsPlausibleHuman_YUpCm()
    {
        var dff = Dff();
        var world = new XForm[dff.Nodes.Count];
        for (var n = 0; n < dff.Nodes.Count; n++)
        {
            world[n] = dff.Nodes[n].ParentIndex < 0
                ? dff.Nodes[n].RestLocal
                : XForm.Compose(world[dff.Nodes[n].ParentIndex], dff.Nodes[n].RestLocal);
        }
        var byName = dff.Nodes.Select((n, i) => (n.Name, i)).ToDictionary(t => t.Name, t => t.i);

        // Pelvis ≈ (0, 98.7, 2.8) — Y-up, centimeter scale (this pins the row-vector
        // 3x3 → quaternion conversion: a transposed read scrambles every world position).
        TestUtil.AssertVectorEqual(new Vector3(0f, 98.73f, 2.80f), world[byName["Bip01"]].Pos, 0.05f);
        // Feet at ground level, head top ~184 cm.
        Assert.InRange(world[byName["Bip01 L Toe0"]].Pos.Y, -2f, 3f);
        Assert.InRange(world[byName["Bip01 R Toe0"]].Pos.Y, -2f, 3f);
        Assert.InRange(world[byName["Bip01 HeadNub"]].Pos.Y, 175f, 195f);
        // Toes forward = +Z; left side = +X ("Bip01 L Thigh" verified against the game's
        // own authored names).
        Assert.True(world[byName["Bip01 L Toe0"]].Pos.Z > world[byName["Bip01 L Foot"]].Pos.Z);
        Assert.True(world[byName["Bip01 L Thigh"]].Pos.X > 0f);
        Assert.True(world[byName["Bip01 R Thigh"]].Pos.X < 0f);
    }

    [Fact]
    public void Dff_PeekNodeCount_71_AndNullOnGarbage()
    {
        Assert.Equal(71, RwDffSkeleton.PeekNodeCount(Fixture("f_upper_b01.dff")));
        Assert.Null(RwDffSkeleton.PeekNodeCount(new byte[] { 1, 2, 3 }));
        Assert.Null(RwDffSkeleton.PeekNodeCount(Fixture("BACK0098_CATCH.anm")));
    }

    // ================================================================ anm parse level

    [Fact]
    public void Anm_PeekNodeCount_MatchesItemRig()
    {
        // BACK0098 is an item-prop clip: 20-node rig (t=0 keyframe block), 553 keyframes.
        Assert.Equal(20, RwAnmImporter.PeekNodeCount(Fixture("BACK0098_CATCH.anm")));
        Assert.Equal(1, RwAnmImporter.PeekTakeCount(Fixture("BACK0098_CATCH.anm")));
        Assert.Equal(1, RwAnmImporter.PeekTakeCount(Fixture("BACK0098_IDLE_1.anm")));
        Assert.Equal(1, RwAnmImporter.PeekTakeCount(Fixture("TWN_ABACK0016_FIX.anm")));
        Assert.Null(RwAnmImporter.PeekNodeCount(Fixture("f_upper_b01.dff")));
    }

    [Fact]
    public void Anm_Sniff_RecognizesRenderwareStreams()
    {
        Assert.True(RwAnmImporter.LooksLikeRenderwareAnim(Fixture("BACK0098_CATCH.anm")));
        Assert.True(RwAnmImporter.LooksLikeRenderwareAnim(Fixture("7FEA6AEBBEB5C5F3.an5")));
        Assert.False(RwAnmImporter.LooksLikeRenderwareAnim(Fixture("f_upper_b01.dff")));
        Assert.False(RwAnmImporter.LooksLikeRenderwareAnim(new byte[] { 0, 1, 2, 3 }));
    }

    [Fact]
    public void Anm_MissingSkeleton_ThrowsInstructiveError()
    {
        var e = Assert.Throws<FormatException>(
            () => RwAnmImporter.Import(Fixture("BACK0098_CATCH.anm"), skeletonData: null));
        Assert.Contains(".dff", e.Message);
        Assert.Contains("next to the animation", e.Message);
    }

    [Fact]
    public void Anm_ImportsAgainstMatchingSkeleton_ViaFacadeExtension()
    {
        // Facade route: extension .anm → RenderWare importer; the 20-node item clip is
        // structurally importable against the 71-node rig (RenderWare anims may drive a
        // node subset — FSB2's own 3-node TRS anims do), though resolution in the editor
        // only pairs files whose node counts match.
        var scene = Retargeter.ImportSource(
            Fixture("BACK0098_CATCH.anm"), "BACK0098_CATCH.anm", skeletonData: Fixture("f_upper_b01.dff"));
        var clip = Assert.Single(scene.Clips);
        Assert.Equal("BACK0098_CATCH", clip.Name);
        Assert.Equal(71, scene.Skeleton.Count);
        Assert.True(clip.FrameCount >= 2);
        Assert.Equal(1f, scene.UnitScaleCm); // FSB2 is authored in centimeters
    }

    // ================================================================ an5 banks

    [Fact]
    public void An5_SplitsIntoTakes_NamedByBase()
    {
        var scene = ImportBank();
        Assert.Equal(3, scene.Clips.Count);
        Assert.Equal("7FEA6AEBBEB5C5F3_1", scene.Clips[0].Name);
        Assert.Equal("7FEA6AEBBEB5C5F3_2", scene.Clips[1].Name);
        Assert.Equal("7FEA6AEBBEB5C5F3_3", scene.Clips[2].Name);
        // A single-take bank keeps the plain base name (no _1 suffix).
        Assert.Equal(1, RwAnmImporter.PeekTakeCount(Fixture("FE450CFBB1F2A8CA.an5")));
        var single = RwAnmImporter.Import(
            Fixture("FE450CFBB1F2A8CA.an5"), Fixture("f_upper_b01.dff"),
            new RwAnmImportOptions { ClipNameBase = "FE450CFBB1F2A8CA" });
        Assert.Equal("FE450CFBB1F2A8CA", Assert.Single(single.Clips).Name);
        Assert.Equal(71, RwAnmImporter.PeekNodeCount(Fixture("7FEA6AEBBEB5C5F3.an5")));
    }

    [Fact]
    public void An5_Frames_AreCompleteAndAnimated()
    {
        var scene = ImportBank();
        foreach (var clip in scene.Clips)
        {
            Assert.True(clip.FrameCount >= 2, $"{clip.Name}: expected sampled frames");
            foreach (var frame in clip.Frames)
            {
                Assert.Equal(scene.Skeleton.Count, frame.Length);
                foreach (var x in frame)
                {
                    Assert.True(float.IsFinite(x.Pos.X) && float.IsFinite(x.Pos.Y) && float.IsFinite(x.Pos.Z));
                    Assert.True(MathF.Abs(x.Rot.Length() - 1f) < 1e-3f, "rotations must be normalized");
                }
            }

            // The body actually moves: some bone's rotation changes between first/last frame.
            var moved = false;
            for (var b = 0; b < scene.Skeleton.Count && !moved; b++)
            {
                var a = clip.Frames[0][b].Rot;
                var z = clip.Frames[clip.FrameCount - 1][b].Rot;
                moved = MathF.Abs(MathF.Abs(Quaternion.Dot(a, z)) - 1f) > 1e-4f;
            }
            Assert.True(moved, $"{clip.Name}: no bone rotation changed over the clip");
        }
    }

    [Fact]
    public void An5_RotationOnlyBones_HoldRestTranslation()
    {
        // FSB2 takes translate only Dummy01 / Ball_Point / Bip01 (the 3-node TRS anim);
        // every other bone must carry its .dff rest translation on every frame (bone
        // lengths come from the model, exactly like the runtime plays these).
        var dff = Dff();
        var scene = ImportBank();
        var clip = scene.Clips[0];
        var translated = new HashSet<string> { "Dummy01", "Ball_Point", "Bip01" };
        foreach (var node in dff.Nodes)
        {
            if (translated.Contains(node.Name))
                continue;
            var bone = scene.Skeleton.IndexOf(node.Name);
            foreach (var frame in clip.Frames)
                TestUtil.AssertVectorEqual(node.RestLocal.Pos, frame[bone].Pos, 1e-4f);
        }

        // ... and the hips DO translate (crouch/jump variation over the bank's takes).
        var hips = scene.Skeleton.IndexOf("Bip01");
        var ys = clip.Frames.Select(f => f[hips].Pos.Y).ToList();
        Assert.True(ys.Max() - ys.Min() > 0.5f, "hips should move vertically in a motion take");
    }

    [Fact]
    public void An5_Mapping_DetectsBipedPreset()
    {
        var scene = ImportBank();
        var (map, report) = Retargeter.ResolveMapping(scene.Skeleton);

        Assert.Equal("biped", map.ProfileName);
        Assert.Equal(MappingSource.Preset, map.Source);
        Assert.True(map.Confidence >= ProfileDetector.DetectionThreshold,
            $"confidence {map.Confidence} below detection threshold");
        Assert.False(report.NeedsUserDecision);

        string BoneOf(BoneRole role) => scene.Skeleton[map.RoleToBone[role]].Name;
        Assert.Equal("Bip01 Pelvis", BoneOf(BoneRole.Hips));
        Assert.Equal("Bip01 L Thigh", BoneOf(BoneRole.UpperLegL));
        Assert.Equal("Bip01 R Calf", BoneOf(BoneRole.LowerLegR));
        Assert.Equal("Bip01 L Foot", BoneOf(BoneRole.FootL));
        Assert.Equal("Bip01 R Toe0", BoneOf(BoneRole.ToeR));
        Assert.Equal("Bip01 L UpperArm", BoneOf(BoneRole.UpperArmL));
        Assert.Equal("Bip01 R Forearm", BoneOf(BoneRole.LowerArmR));
        Assert.Equal("Bip01 L Hand", BoneOf(BoneRole.HandL));
        Assert.Equal("Bip01 Head", BoneOf(BoneRole.Head));

        // Props/helpers must stay unmapped (ball, root dummy, twist/chest helpers, nubs).
        var mapped = map.RoleToBone.Values.Select(i => scene.Skeleton[i].Name).ToHashSet();
        Assert.DoesNotContain("Ball_Point", mapped);
        Assert.DoesNotContain("Dummy01", mapped);
        Assert.DoesNotContain("LFT_Bone01", mapped);
        Assert.DoesNotContain("RHT_Bone01", mapped);
        Assert.DoesNotContain("Bip01 L Toe0Nub", mapped);
    }

    // ================================================================ end to end

    [Fact]
    public void An5_Convert_ToCitizen_ProducesMovingDmx()
    {
        var target = RetargetTargetSpec.SboxCitizen(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json")));
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = Fixture("7FEA6AEBBEB5C5F3.an5"),
            SourceFileName = "7FEA6AEBBEB5C5F3.an5",
            SkeletonData = Fixture("f_upper_b01.dff"),
            TakeIndex = 0,
        }, target);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("7FEA6AEBBEB5C5F3_1", clip.ClipName);
        Assert.NotNull(clip.Mapping);
        Assert.Equal("biped", clip.Mapping!.ProfileName);
        Assert.Contains("pelvis_p", clip.DmxContent);
        Assert.Contains("pelvis_o", clip.DmxContent);
        Assert.Contains("hand_L_p", clip.DmxContent);

        // Solved target frames actually move.
        Assert.NotNull(clip.SolvedFrames);
        var frames = clip.SolvedFrames!;
        Assert.True(frames.Count >= 2);
        var pelvis = target.Rig.Skeleton.IndexOf("pelvis");
        var moved = false;
        for (var f = 1; f < frames.Count && !moved; f++)
            moved = (frames[f][pelvis].Pos - frames[0][pelvis].Pos).Length() > 0.5f
                || MathF.Abs(MathF.Abs(Quaternion.Dot(frames[f][pelvis].Rot, frames[0][pelvis].Rot)) - 1f) > 1e-4f;
        Assert.True(moved, "pelvis never moved in the solved clip");
    }

    // ================================================================ corpus sweep (opt-in)

    /// <summary>
    /// Full-corpus parse sweep with the C# parser. Runs only when HR_RENDERWARE_SWEEP
    /// points at a folder tree containing .anm/.an5 files (the full FSB2 sweep takes tens
    /// of seconds — see dev/tools/renderware/rw_explore.py for the standalone tool);
    /// otherwise it sweeps just the bundled fixtures.
    /// </summary>
    [Fact]
    public void Sweep_AllRenderwareFiles_Parse()
    {
        var root = Environment.GetEnvironmentVariable("HR_RENDERWARE_SWEEP");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            root = Path.Combine(AppContext.BaseDirectory, "fixtures", "renderware");

        var failures = new List<string>();
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".anm" or ".an5"))
                continue;
            count++;
            var bytes = File.ReadAllBytes(file);
            if (RwAnmImporter.PeekNodeCount(bytes) is null || RwAnmImporter.PeekTakeCount(bytes) == 0)
                failures.Add(file);
        }

        Assert.True(count > 0, $"no .anm/.an5 files under {root}");
        Assert.True(failures.Count == 0,
            $"{failures.Count}/{count} files failed to parse:\n" + string.Join("\n", failures.Take(20)));
    }
}
