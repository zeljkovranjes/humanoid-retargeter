using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Formats;

// =============================================================================================
// UNIT / SPACE RESOLUTION (determined empirically from research/extract_anim.py + the JSONs):
//
// The ground-truth JSONs (fixtures/anim_truth/*.json) were produced by Blender's FBX importer
// (automatic_bone_orientation=False) and mix TWO spaces:
//
//  1. `bones[]` rest data comes from `armature.data.bones[*].matrix_local`, which is in
//     ARMATURE space. Blender's FBX importer does not bake the axis/unit conversion into the
//     armature data — it stores it on the object (rotation_euler.x = +90 deg, scale = 0.01 *
//     UnitScaleFactor). Armature space therefore equals the FBX file's native space and units
//     (Y-up, centimeters for these fixtures, UnitScaleFactor = 1). Rest values compare to our
//     imported skeleton DIRECTLY, no conversion.
//
//  2. `frames[]` come from `obj.matrix_world @ pose_bone.matrix`, which is in BLENDER WORLD
//     space (Z-up, meters, the 0.01 object scale applied). To compare a truth world transform
//     against our FBX-space result: p_fbx = Rx(-90deg) * wp / object_scale,
//     q_fbx = Rx(-90deg) * wr (column-vector quaternion composition, parent on the left).
//
// Frame alignment: Blender places the first FBX key at action frame_start, and the truth was
// sampled on an fps grid from there; our importer samples from the earliest curve key on the
// same grid, so truth frame i == our clip frame i.
//
// Note: "Lead Jab.fbx" is NOT a Mixamo rig — it is a citizen-skeleton clip (84 s&box bones,
// pelvis/spine_0/...), which conveniently exercises a second rig family.
//
// TOLERANCES: the task gates were 0.5% of skeleton height / 1.0 deg (rest) and 0.7% / 1.0 deg
// (animation). Observed residuals are float-precision noise — max 0.0002 cm / 0.0004 deg over
// every bone of both rigs at all sampled frames — so the tolerances below are tightened to
// 0.25 cm / 0.25 deg absolute (~1000x above the observed residual, ~4x tighter than the gate).
// =============================================================================================
public class FbxImporterTests
{
    private const float KDegPerRad = 180f / MathF.PI;
    private const float PosTolCm = 0.25f;
    private const float RotTolDeg = 0.25f;

    private static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fbx", name));

    private static SourceScene ImportFixture(string name)
        => FbxImporter.Import(Fixture(name), new FbxImportOptions());

    // ---- ground truth -----------------------------------------------------------------

    private sealed record TruthBone(
        string Name, string? Parent, Vector3 RestWorldPos, Quaternion RestWorldRot);

    private sealed record TruthFrameBone(Vector3 WorldPos, Quaternion WorldRot);

    private sealed class Truth
    {
        public required float SampleFps;
        public required int NSamples;
        public required float ObjectScale;
        public required List<TruthBone> Bones;
        public required List<TruthFrameBone[]> Frames;

        /// <summary>Rest skeleton height (max-min world Y, FBX Y-up) in native units (cm).</summary>
        public float Height()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var b in Bones)
            {
                min = MathF.Min(min, b.RestWorldPos.Y);
                max = MathF.Max(max, b.RestWorldPos.Y);
            }
            return max - min;
        }
    }

    private static Truth LoadTruth(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "anim_truth", name);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;

        static Vector3 V3(JsonElement e) => new(
            e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        static Quaternion Q(JsonElement e) => new(
            e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());

        var bones = new List<TruthBone>();
        foreach (var b in r.GetProperty("bones").EnumerateArray())
        {
            bones.Add(new TruthBone(
                b.GetProperty("name").GetString()!,
                b.GetProperty("parent").ValueKind == JsonValueKind.Null
                    ? null : b.GetProperty("parent").GetString(),
                V3(b.GetProperty("rest_world_pos")),
                Q(b.GetProperty("rest_world_rot_xyzw"))));
        }

        var frames = new List<TruthFrameBone[]>();
        foreach (var f in r.GetProperty("frames").EnumerateArray())
        {
            var fb = new TruthFrameBone[bones.Count];
            int i = 0;
            foreach (var e in f.EnumerateArray())
                fb[i++] = new TruthFrameBone(V3(e.GetProperty("wp")), Q(e.GetProperty("wr")));
            frames.Add(fb);
        }

        return new Truth
        {
            SampleFps = r.GetProperty("sample_fps").GetSingle(),
            NSamples = r.GetProperty("n_samples").GetInt32(),
            ObjectScale = r.GetProperty("object_scale")[0].GetSingle(),
            Bones = bones,
            Frames = frames,
        };
    }

    // Blender world -> FBX native space: undo object rotation (X +90 deg) and object scale.
    private static readonly Quaternion BlenderToFbx =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    private static Vector3 FramePosToFbx(Vector3 wp, float objectScale)
        => Vector3.Transform(wp, BlenderToFbx) / objectScale;

    private static Quaternion FrameRotToFbx(Quaternion wr)
        => BlenderToFbx * wr;

    // ---- comparison helper -------------------------------------------------------------

    /// <summary>
    /// Compares world transforms of all common bones; fails with a per-bone diff dump of the
    /// worst offenders. Returns the number of common bones compared.
    /// </summary>
    private static int AssertWorldsMatch(
        Skel skeleton,
        IReadOnlyList<XForm> worlds,
        IEnumerable<(string Name, Vector3 Pos, Quaternion Rot)> truthWorlds,
        float posTolCm,
        float rotTolDeg,
        string context)
    {
        var failures = new List<(string Bone, float PosErr, float RotErrDeg)>();
        int common = 0;
        float maxPos = 0f, maxRot = 0f;

        foreach (var (name, tPos, tRot) in truthWorlds)
        {
            int idx = skeleton.IndexOf(name);
            if (idx < 0)
                continue;
            common++;

            float posErr = Vector3.Distance(worlds[idx].Pos, tPos);
            float rotErr = MathQ.AngleBetween(worlds[idx].Rot, tRot) * KDegPerRad;
            maxPos = MathF.Max(maxPos, posErr);
            maxRot = MathF.Max(maxRot, rotErr);

            if (posErr > posTolCm || rotErr > rotTolDeg)
                failures.Add((name, posErr, rotErr));
        }

        if (failures.Count > 0)
        {
            var worst = failures
                .OrderByDescending(f => MathF.Max(f.PosErr / posTolCm, f.RotErrDeg / rotTolDeg))
                .Take(12)
                .Select(f => $"  {f.Bone}: posErr={f.PosErr:F3} cm (tol {posTolCm:F3}), rotErr={f.RotErrDeg:F3} deg (tol {rotTolDeg:F2})");
            Assert.Fail(
                $"{context}: {failures.Count}/{common} bones out of tolerance " +
                $"(max posErr={maxPos:F3} cm, max rotErr={maxRot:F3} deg):\n" +
                string.Join("\n", worst));
        }

        return common;
    }

    // ---- 1. Zombie Crawl: skeleton + clip basics ----------------------------------------

    [Fact]
    public void ZombieCrawl_SkeletonAndClipBasics()
    {
        var scene = ImportFixture("Zombie Crawl.fbx");

        // Spot list of 10 well-known Mixamo bones (this export uses the "mixamorig1:" namespace).
        string[] expected =
        {
            "mixamorig1:Hips", "mixamorig1:Spine", "mixamorig1:Spine1", "mixamorig1:Spine2",
            "mixamorig1:Neck", "mixamorig1:Head", "mixamorig1:LeftUpLeg",
            "mixamorig1:LeftHandIndex1", "mixamorig1:RightFoot", "mixamorig1:LeftShoulder",
        };
        foreach (var name in expected)
            Assert.True(scene.Skeleton.IndexOf(name) >= 0, $"missing bone {name}");

        Assert.Single(scene.Clips);
        var clip = scene.Clips[0];
        Assert.Equal(30f, clip.Fps);
        Assert.InRange(clip.FrameCount, 154, 156);

        Assert.Equal(1f, scene.UnitScaleCm, 3);
        Assert.Equal(1, scene.UpAxis);   // Y-up source, axes recorded but NOT converted
        Assert.Equal(1, scene.UpAxisSign);
    }

    // ---- 2/3. Zombie Crawl vs Blender ground truth --------------------------------------

    [Fact]
    public void ZombieCrawl_RestPose_MatchesBlender()
    {
        var scene = ImportFixture("Zombie Crawl.fbx");
        var truth = LoadTruth("zombie_crawl.json");

        int common = AssertWorldsMatch(
            scene.Skeleton,
            scene.Skeleton.RestWorld,
            truth.Bones.Select(b => (b.Name, b.RestWorldPos, b.RestWorldRot)),
            PosTolCm, RotTolDeg, "rest pose");

        Assert.True(common >= 60, $"only {common} common bones (truth has {truth.Bones.Count})");
    }

    [Fact]
    public void ZombieCrawl_Animation_MatchesBlender()
    {
        var scene = ImportFixture("Zombie Crawl.fbx");
        var truth = LoadTruth("zombie_crawl.json");
        var clip = scene.Clips[0];

        Assert.InRange(clip.FrameCount, truth.NSamples - 1, truth.NSamples + 1);

        foreach (int f in new[] { 0, 38, 77, 116, 154 })
        {
            var worlds = new Pose(clip.Frames[Math.Min(f, clip.FrameCount - 1)]).ToWorld(scene.Skeleton);
            var truthFrame = truth.Frames[f];
            int common = AssertWorldsMatch(
                scene.Skeleton,
                worlds,
                truth.Bones.Select((b, i) => (
                    b.Name,
                    FramePosToFbx(truthFrame[i].WorldPos, truth.ObjectScale),
                    FrameRotToFbx(truthFrame[i].WorldRot))),
                PosTolCm, RotTolDeg, $"frame {f}");
            Assert.True(common >= 60, $"frame {f}: only {common} common bones");
        }
    }

    // ---- 4. Lead Jab (citizen-skeleton clip) vs Blender ground truth ---------------------

    [Fact]
    public void LeadJab_RestPose_MatchesBlender()
    {
        var scene = ImportFixture("Lead Jab.fbx");
        var truth = LoadTruth("lead_jab.json");

        int common = AssertWorldsMatch(
            scene.Skeleton,
            scene.Skeleton.RestWorld,
            truth.Bones.Select(b => (b.Name, b.RestWorldPos, b.RestWorldRot)),
            PosTolCm, RotTolDeg, "rest pose");

        Assert.True(common >= 75, $"only {common} common bones (truth has {truth.Bones.Count})");
    }

    [Fact]
    public void LeadJab_Animation_MatchesBlender()
    {
        var scene = ImportFixture("Lead Jab.fbx");
        var truth = LoadTruth("lead_jab.json");
        Assert.NotEmpty(scene.Clips);
        var clip = scene.Clips[0];

        Assert.InRange(clip.FrameCount, truth.NSamples - 1, truth.NSamples + 1);

        foreach (int f in new[] { 0, 27, 54 })
        {
            var worlds = new Pose(clip.Frames[Math.Min(f, clip.FrameCount - 1)]).ToWorld(scene.Skeleton);
            var truthFrame = truth.Frames[f];
            int common = AssertWorldsMatch(
                scene.Skeleton,
                worlds,
                truth.Bones.Select((b, i) => (
                    b.Name,
                    FramePosToFbx(truthFrame[i].WorldPos, truth.ObjectScale),
                    FrameRotToFbx(truthFrame[i].WorldRot))),
                PosTolCm, RotTolDeg, $"frame {f}");
            Assert.True(common >= 75, $"frame {f}: only {common} common bones");
        }
    }

    // ---- 5. Edge fixtures: parse without throwing, non-empty skeleton --------------------

    [Theory]
    [InlineData("prod_fix7400.fbx")]
    [InlineData("mut_strip-prerotation.fbx")]
    [InlineData("gen_inherit.fbx")]
    public void EdgeFixture_ImportsWithNonEmptySkeleton(string name)
    {
        var scene = ImportFixture(name);
        Assert.True(scene.Skeleton.Count > 0, "skeleton is empty");
    }

    // ---- 6. ActorCore -------------------------------------------------------------------

    [Fact]
    public void ActorCore_CatwalkLoop_ImportsFiniteSkeletonAndClip()
    {
        var scene = ImportFixture("catwalk-loop-378982.fbx");

        Assert.True(scene.Skeleton.IndexOf("CC_Base_Hip") >= 0, "missing CC_Base_Hip");
        Assert.True(scene.Skeleton.IndexOf("CC_Base_L_Thigh") >= 0, "missing CC_Base_L_Thigh");

        Assert.NotEmpty(scene.Clips);
        Assert.True(scene.Clips[0].FrameCount > 1, "clip has <= 1 frame");

        static void AssertFinite(XForm x, string what)
        {
            Assert.True(
                float.IsFinite(x.Pos.X) && float.IsFinite(x.Pos.Y) && float.IsFinite(x.Pos.Z) &&
                float.IsFinite(x.Rot.X) && float.IsFinite(x.Rot.Y) && float.IsFinite(x.Rot.Z) &&
                float.IsFinite(x.Rot.W),
                $"non-finite transform in {what}: {x}");
        }

        for (int i = 0; i < scene.Skeleton.Count; i++)
        {
            AssertFinite(scene.Skeleton[i].RestLocal, $"rest local {scene.Skeleton[i].Name}");
            AssertFinite(scene.Skeleton.RestWorld[i], $"rest world {scene.Skeleton[i].Name}");
        }

        foreach (var clip in scene.Clips)
            for (int f = 0; f < clip.FrameCount; f++)
                foreach (var x in clip.Frames[f])
                    AssertFinite(x, $"clip {clip.Name} frame {f}");
    }

    // ---- 7. UE Mannequin: static translation channels override bind rest geometry --------
    //
    // The UE animation FBX files carry rest data (BindPose/Lcl defaults) whose foot->ball
    // local offset disagrees with the clip's STATIC ball translation channels by ~7.9 deg on
    // both feet, while every other bone pair agrees to 0.000 deg (dev/verification/RESULTS.md
    // root-cause analysis). The animation plays the static channel value, so the rest must use
    // it — otherwise the canonical foot->ball direction is constantly ~7.9 deg off in output.

    [Theory]
    [InlineData("foot_l", "ball_l")]
    [InlineData("foot_r", "ball_r")]
    public void UeMannequin_StaticTranslationChannel_OverridesBindRest(string foot, string ball)
    {
        var scene = ImportFixture("ThirdPersonWalk.FBX");
        var skel = scene.Skeleton;

        int ballIdx = skel.IndexOf(ball);
        Assert.True(ballIdx >= 0, $"missing bone {ball}");
        Assert.Equal(skel.IndexOf(foot), skel[ballIdx].ParentIndex);

        Assert.NotEmpty(scene.Clips);
        var frame0 = scene.Clips[0].Frames[0];

        // The ball's translation channels are static, so its sampled frame-0 local translation
        // IS the channel value: the rest local must match it in direction (foot->ball, both
        // vectors live in the foot frame; ~7.94 deg apart before the fix) and in value.
        var restPos = skel[ballIdx].RestLocal.Pos;
        var animPos = frame0[ballIdx].Pos;

        float angleDeg = MathF.Acos(Math.Clamp(
            Vector3.Dot(Vector3.Normalize(restPos), Vector3.Normalize(animPos)),
            -1f, 1f)) * KDegPerRad;
        Assert.True(angleDeg < 0.5f,
            $"rest foot->ball direction is {angleDeg:F3} deg away from the animation's static geometry");
        Assert.True(Vector3.Distance(restPos, animPos) < 0.01f,
            $"rest local translation {restPos} != static channel value {animPos}");
    }

    [Fact]
    public void UeMannequin_SingleStack_ProducesNoImportNotes()
    {
        // One animation stack: there is nothing to disagree with, so the multi-stack
        // static-translation detection must stay silent.
        var scene = ImportFixture("ThirdPersonWalk.FBX");
        Assert.Empty(scene.Notes);
    }

    [Theory]
    [InlineData("foot_l", "ball_l")]
    [InlineData("foot_r", "ball_r")]
    public void UeMannequin_StaticOverride_EqualsFrame0SampledLocalTranslation(string foot, string ball)
    {
        // The invariant the override formula must satisfy regardless of ancestor scale:
        // a bone whose translation channels are static plays exactly one local translation,
        // so the rest local must equal the frame-0 sampled local translation.
        var scene = ImportFixture("ThirdPersonWalk.FBX");
        var skel = scene.Skeleton;

        int ballIdx = skel.IndexOf(ball);
        Assert.True(ballIdx >= 0, $"missing bone {ball}");
        Assert.Equal(skel.IndexOf(foot), skel[ballIdx].ParentIndex);

        var restPos = skel[ballIdx].RestLocal.Pos;
        var frame0Pos = scene.Clips[0].Frames[0][ballIdx].Pos;
        Assert.True(Vector3.Distance(restPos, frame0Pos) < 0.005f,
            $"rest local {restPos} != frame-0 sampled local {frame0Pos}");
    }

    [Fact]
    public void UeMannequin_VaryingTranslationChannels_DoNotTouchRest()
    {
        var scene = ImportFixture("ThirdPersonWalk.FBX");
        var skel = scene.Skeleton;
        int pelvis = skel.IndexOf("pelvis");
        Assert.True(pelvis >= 0, "missing pelvis");

        // The pelvis carries the walk trajectory (VARYING translation channels): its rest must
        // stay bind-derived — pinned at the UE Mannequin bind height (Z-up, cm) — and must NOT
        // equal the mid-stride frame-0 trajectory sample.
        Assert.InRange(skel.RestWorld[pelvis].Pos.Z, 96.7f, 96.8f);
        var frame0 = scene.Clips[0].Frames[0];
        Assert.NotEqual(frame0[pelvis].Pos, skel[pelvis].RestLocal.Pos);
    }
}
