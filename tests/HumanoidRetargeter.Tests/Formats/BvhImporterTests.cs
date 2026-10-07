using System.Numerics;
using System.Text;
using System.Text.Json;
using HumanoidRetargeter.Core.Formats.Bvh;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Formats;

// =============================================================================================
// SPACE / CONVENTION RESOLUTION (determined from Blender 5.1 io_anim_bvh/import_bvh.py and
// verified empirically against fixtures/anim_truth/freebvh.json, produced by
// research/extract_anim.py = import_anim.bvh(axis_forward='-Z', axis_up='Y', global_scale=1)):
//
//  1. AXES. Blender's BVH importer builds global_matrix = Rx(+90 deg) (file Y-up -> Blender
//     Z-up) and — unlike the FBX importer — BAKES it into the armature data
//     (`arm_ob.matrix_world = global_matrix; transform_apply(rotation=True)`, import_bvh.py).
//     The object ends up with identity transform (object_scale == 1). Therefore BOTH the
//     truth's rest data (`bones[].rest_world_*`, armature space) and its frame data
//     (`frames[][].wp/wr`, world space) are in Blender Z-up space, and converting EITHER back
//     to native BVH space is the same fixed rotation applied on the left:
//     p_bvh = Rx(-90 deg) * p_blender, q_bvh = Rx(-90 deg) * q_blender.
//     (Verified: file's Spine OFFSET (0, 9.9235, -1.2273) appears in the truth as rest world
//     (0, 1.2273, 9.9235) = Rx(+90 deg) of the native offset.)
//
//  2. BONE ORIENTATION. Blender bones are oriented along head->tail with the joint's world
//     position at the HEAD; BVH joints have identity rest rotation. The importer keys pose
//     rotations as basis = rest^-1 * R_bvh * rest, which makes the pose bone's world rotation
//     q_pose(t) = Q_joint(t) * q_rest (column-vector quaternion product, our a*b-applies-b-
//     first convention). The BVH joint world rotation is therefore recovered per bone as
//     Q_joint(t) = q_pose_bvh(t) * conj(q_rest_bvh); positions compare directly (heads).
//
//  3. END SITES. Blender turns End Sites into bone TAILS (no extra bones; the "<name>_end"
//     naming exists only in its non-armature OBJECT import path). Our importer synthesizes a
//     leaf bone "<parent>_end" per End Site, so: our joint count == truth bone count + number
//     of End Sites, and every extra bone name ends with "_end".
//
//  4. FRAME ALIGNMENT. Blender places BVH motion line 0 at action frame 1 (it prepends and
//     then skips an internal rest frame), and extract_anim.py samples from frame_start = 1 on
//     the same 30 fps grid our importer uses, so truth frame i == our clip frame i (the file's
//     30.000003 fps vs 30 fps grid mismatch is ~1e-7 s per frame — negligible over 40 frames;
//     Blender fcurves are keyed LINEAR so subframe evaluation is well-defined).
//
//  5. UNITS. global_scale=1, so truth values are in raw file units. freebvh is Mixamo-derived
//     (centimeters, rest height ~185) -> our meters-vs-cm heuristic keeps scale 1 and values
//     compare 1:1.
//
// TOLERANCES per the task gate: rest offsets ~exact (0.02 cm); animation world rotation
// within 1 deg and world position within 0.5% of skeleton height across 10 sampled frames.
// Observed residuals are float-precision noise (max 0.0002 cm / 0.0004 deg over all 40
// truth frames x 55 joints), so the gates have ~2500x margin. A deliberately flipped
// rotation-composition order fails 55/55 joints — the comparison is discriminating.
// =============================================================================================
public class BvhImporterTests
{
    private const float KDegPerRad = 180f / MathF.PI;

    private static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "bvh", name));

    private static SourceScene ImportFixture(string name, float fps = 30f)
        => BvhImporter.Import(Fixture(name), new BvhImportOptions { SampleFps = fps });

    // ---- ground truth -----------------------------------------------------------------

    private sealed record TruthBone(
        string Name, string? Parent, Vector3 RestWorldPos, Quaternion RestWorldRot);

    private sealed record TruthFrameBone(Vector3 WorldPos, Quaternion WorldRot);

    private sealed class Truth
    {
        public required int NSamples;
        public required List<TruthBone> Bones;
        public required List<TruthFrameBone[]> Frames;
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
            NSamples = r.GetProperty("n_samples").GetInt32(),
            Bones = bones,
            Frames = frames,
        };
    }

    // Blender Z-up world -> native BVH Y-up space (see header note 1).
    private static readonly Quaternion BlenderToBvh =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    private static Vector3 PosToBvh(Vector3 p) => Vector3.Transform(p, BlenderToBvh);

    private static Quaternion RotToBvh(Quaternion q) => BlenderToBvh * q;

    /// <summary>BVH joint world rotation from a truth pose rotation (see header note 2).</summary>
    private static Quaternion JointRot(Quaternion poseWorldRot, Quaternion restWorldRot)
        => RotToBvh(poseWorldRot) * Quaternion.Conjugate(RotToBvh(restWorldRot));

    private static float SkeletonHeight(Skel skeleton)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var w in skeleton.RestWorld)
        {
            min = MathF.Min(min, w.Pos.Y);
            max = MathF.Max(max, w.Pos.Y);
        }
        return max - min;
    }

    // ---- 1. freebvh vs Blender ground truth ---------------------------------------------

    [Fact]
    public void Freebvh_SkeletonTopology_MatchesBlender()
    {
        var scene = ImportFixture("bvhpython_test_freebvh.bvh");
        var truth = LoadTruth("freebvh.json");

        // Every Blender bone (= BVH joint) exists, with the same parent.
        var truthNames = new HashSet<string>();
        foreach (var b in truth.Bones)
        {
            truthNames.Add(b.Name);
            int idx = scene.Skeleton.IndexOf(b.Name);
            Assert.True(idx >= 0, $"missing joint {b.Name}");
            int parentIdx = scene.Skeleton[idx].ParentIndex;
            string? parent = parentIdx < 0 ? null : scene.Skeleton[parentIdx].Name;
            Assert.Equal(b.Parent, parent);
        }

        // Our extra bones are exactly the synthesized End Site leaves (15 in this file).
        var extras = scene.Skeleton.Bones
            .Where(b => !truthNames.Contains(b.Name))
            .Select(b => b.Name)
            .ToList();
        Assert.Equal(15, extras.Count);
        Assert.All(extras, n => Assert.EndsWith("_end", n, StringComparison.Ordinal));
        Assert.Equal(truth.Bones.Count + 15, scene.Skeleton.Count);
    }

    [Fact]
    public void Freebvh_RestPose_MatchesBlender()
    {
        var scene = ImportFixture("bvhpython_test_freebvh.bvh");
        var truth = LoadTruth("freebvh.json");

        Assert.Equal(1f, scene.UnitScaleCm); // centimeter-scale file, heuristic keeps x1
        Assert.Equal(1, scene.UpAxis);       // BVH convention: Y-up, recorded not converted

        foreach (var b in truth.Bones)
        {
            int idx = scene.Skeleton.IndexOf(b.Name);
            var ours = scene.Skeleton.RestWorld[idx];

            // Rest world position = accumulated OFFSETs (cm).
            TestUtil.AssertVectorEqual(PosToBvh(b.RestWorldPos), ours.Pos, 0.02f);

            // BVH rest rotations are identity by convention (Blender's nontrivial rest
            // rotations are bone head->tail orientations, cancelled in the frame tests).
            float identErr = MathQ.AngleBetween(Quaternion.Identity, ours.Rot) * KDegPerRad;
            Assert.True(identErr < 1e-3f, $"{b.Name}: rest rot not identity ({identErr} deg)");
        }
    }

    [Fact]
    public void Freebvh_Animation_MatchesBlender()
    {
        var scene = ImportFixture("bvhpython_test_freebvh.bvh");
        var truth = LoadTruth("freebvh.json");
        Assert.Single(scene.Clips);
        var clip = scene.Clips[0];

        float height = SkeletonHeight(scene.Skeleton);
        Assert.InRange(height, 100f, 400f); // sanity: a cm-scale humanoid
        float posTol = 0.005f * height;
        const float rotTolDeg = 1f;

        // 10 frames spread over the 40 extracted truth samples.
        int[] sampleFrames = { 0, 4, 8, 12, 16, 20, 24, 28, 32, 36 };
        foreach (int f in sampleFrames)
        {
            Assert.True(f < truth.NSamples && f < clip.FrameCount);
            var worlds = new Pose(clip.Frames[f]).ToWorld(scene.Skeleton);
            var truthFrame = truth.Frames[f];

            var failures = new List<string>();
            for (int i = 0; i < truth.Bones.Count; i++)
            {
                var tb = truth.Bones[i];
                int idx = scene.Skeleton.IndexOf(tb.Name);
                var expectedPos = PosToBvh(truthFrame[i].WorldPos);
                var expectedRot = JointRot(truthFrame[i].WorldRot, tb.RestWorldRot);

                float posErr = Vector3.Distance(worlds[idx].Pos, expectedPos);
                float rotErr = MathQ.AngleBetween(worlds[idx].Rot, expectedRot) * KDegPerRad;
                if (posErr > posTol || rotErr > rotTolDeg)
                    failures.Add($"  {tb.Name}: posErr={posErr:F3} cm (tol {posTol:F3}), rotErr={rotErr:F3} deg");
            }

            Assert.True(failures.Count == 0,
                $"frame {f}: {failures.Count}/{truth.Bones.Count} joints out of tolerance:\n" +
                string.Join("\n", failures.Take(12)));
        }
    }

    // ---- 2. cmu_01_01 basics --------------------------------------------------------------

    [Fact]
    public void Cmu0101_ImportBasics()
    {
        var scene = ImportFixture("cmu_01_01.bvh");

        // 31 joints + 7 End Sites.
        int endBones = scene.Skeleton.Bones.Count(b => b.Name.EndsWith("_end", StringComparison.Ordinal));
        Assert.Equal(7, endBones);
        Assert.Equal(31 + 7, scene.Skeleton.Count);
        Assert.True(scene.Skeleton.IndexOf("Hips") >= 0);

        Assert.Single(scene.Clips);
        var clip = scene.Clips[0];
        Assert.True(clip.Duration > 1f, $"duration {clip.Duration}s"); // 2752 frames at 120 fps ~ 22.9 s
        Assert.Equal(30f, clip.Fps);

        // No NaNs anywhere.
        static bool Finite(XForm x) =>
            float.IsFinite(x.Pos.X) && float.IsFinite(x.Pos.Y) && float.IsFinite(x.Pos.Z) &&
            float.IsFinite(x.Rot.X) && float.IsFinite(x.Rot.Y) && float.IsFinite(x.Rot.Z) &&
            float.IsFinite(x.Rot.W);
        for (int i = 0; i < scene.Skeleton.Count; i++)
            Assert.True(Finite(scene.Skeleton.RestWorld[i]), $"non-finite rest {scene.Skeleton[i].Name}");
        for (int f = 0; f < clip.FrameCount; f++)
            foreach (var x in clip.Frames[f])
                Assert.True(Finite(x), $"non-finite transform at frame {f}");

        // Root has 6 channels (3 position): its local translation is animated.
        int hips = scene.Skeleton.IndexOf("Hips");
        bool rootMoves = false;
        for (int f = 1; f < clip.FrameCount && !rootMoves; f++)
            rootMoves = Vector3.Distance(clip.Frames[f][hips].Pos, clip.Frames[0][hips].Pos) > 0.1f;
        Assert.True(rootMoves, "root translation never changes — position channels not applied");

        // A knee joint has only 3 (rotation) channels: local translation stays at the OFFSET.
        int knee = scene.Skeleton.IndexOf("LeftLeg");
        Assert.True(knee >= 0, "missing LeftLeg");
        var kneeRest = scene.Skeleton[knee].RestLocal.Pos;
        for (int f = 0; f < clip.FrameCount; f++)
            TestUtil.AssertVectorEqual(kneeRest, clip.Frames[f][knee].Pos, 1e-4f);
    }

    // ---- 3. resampling ---------------------------------------------------------------------

    // freebvh: Frames: 69, Frame Time: 0.0333333 -> duration = 68 * 0.0333333 s.
    // Expected frame count on the target grid = round(duration * fps) + 1.
    [Theory]
    [InlineData(30f, 69)]
    [InlineData(60f, 137)]
    [InlineData(15f, 35)]
    public void Freebvh_Resampling_FrameCountMatchesGrid(float fps, int expectedFrames)
    {
        var scene = ImportFixture("bvhpython_test_freebvh.bvh", fps);
        var clip = Assert.Single(scene.Clips);
        Assert.Equal(fps, clip.Fps);
        Assert.Equal(expectedFrames, clip.FrameCount);

        double duration = 68 * 0.0333333;
        Assert.Equal((int)Math.Round(duration * fps) + 1, clip.FrameCount);
    }

    // ---- 4. unit heuristic + channel-order unit check ---------------------------------------

    // Meter-scale skeleton (rest height 1.7 < 10) must be scaled x100 to centimeters,
    // including the root position channels. Frame 1 also pins down the rotation-channel
    // convention independently of Blender: channels "Zrotation Yrotation Xrotation" with
    // values (90, 0, 0) must yield world R = Rz(90 deg), so the Head joint at local offset
    // (0, 0.7, 0) m lands at hips + Rz90*(0, 70, 0) = hips + (-70, 0, 0) cm.
    private const string MeterBvh = @"HIERARCHY
ROOT Hips
{
    OFFSET 0 0.9 0
    CHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation
    JOINT Head
    {
        OFFSET 0 0.7 0
        CHANNELS 3 Zrotation Yrotation Xrotation
        End Site
        {
            OFFSET 0 0.1 0
        }
    }
}
MOTION
Frames: 2
Frame Time: 0.0333333
0 0.9 0 0 0 0 0 0 0
0 1.0 0.2 90 0 0 0 0 0
";

    [Fact]
    public void MeterScaleFile_IsConvertedToCentimeters()
    {
        var scene = BvhImporter.Import(Encoding.ASCII.GetBytes(MeterBvh), new BvhImportOptions());

        Assert.Equal(100f, scene.UnitScaleCm);

        int hips = scene.Skeleton.IndexOf("Hips");
        int head = scene.Skeleton.IndexOf("Head");
        int end = scene.Skeleton.IndexOf("Head_end");
        Assert.True(hips >= 0 && head >= 0 && end >= 0);

        TestUtil.AssertVectorEqual(new Vector3(0f, 90f, 0f), scene.Skeleton.RestWorld[hips].Pos, 1e-3f);
        TestUtil.AssertVectorEqual(new Vector3(0f, 160f, 0f), scene.Skeleton.RestWorld[head].Pos, 1e-3f);
        TestUtil.AssertVectorEqual(new Vector3(0f, 10f, 0f), scene.Skeleton[end].RestLocal.Pos, 1e-3f);

        var clip = Assert.Single(scene.Clips);
        Assert.Equal(2, clip.FrameCount);

        var worlds = new Pose(clip.Frames[1]).ToWorld(scene.Skeleton);
        TestUtil.AssertVectorEqual(new Vector3(0f, 100f, 20f), worlds[hips].Pos, 0.01f);
        TestUtil.AssertVectorEqual(new Vector3(-70f, 100f, 20f), worlds[head].Pos, 0.01f);

        var rz90 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        Assert.True(MathQ.AngleBetween(rz90, worlds[hips].Rot) * KDegPerRad < 0.01f,
            $"hips world rot {worlds[hips].Rot} != Rz(90)");
    }

    // ---- 5. malformed input ------------------------------------------------------------------

    [Fact]
    public void TruncatedFile_Throws()
    {
        var full = Fixture("bvhpython_test_freebvh.bvh");

        // Cut mid-MOTION (file is ~60% hierarchy, so 80% leaves a partial frame block).
        var midMotion = full.AsSpan(0, full.Length * 8 / 10).ToArray();
        Assert.Throws<FormatException>(() => BvhImporter.Import(midMotion, new BvhImportOptions()));

        // Cut mid-HIERARCHY.
        var midHierarchy = full.AsSpan(0, full.Length / 4).ToArray();
        Assert.Throws<FormatException>(() => BvhImporter.Import(midHierarchy, new BvhImportOptions()));
    }

    [Fact]
    public void NonBvhData_Throws()
    {
        Assert.Throws<FormatException>(() => BvhImporter.Import(
            Encoding.ASCII.GetBytes("this is not a bvh file"), new BvhImportOptions()));
        Assert.Throws<FormatException>(() => BvhImporter.Import(
            Array.Empty<byte>(), new BvhImportOptions()));
    }
}
