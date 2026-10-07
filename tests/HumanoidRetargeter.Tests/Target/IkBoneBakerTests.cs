using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

// =============================================================================================
// IK helper-bone baking — relationships DERIVED from shipped Facepunch clips (plan Task 6.1).
//
// Investigation (dev/scratch probe over Citizen@Run_N.fbx + Citizen@Dab.fbx, both Y-up cm,
// 79-bone citizen clip skeletons; residuals are max over all frames):
//
//   foot_L/R_IK_target  world == ankle_L/R world          (1e-4 cm / 0.000 deg)
//   hand_L/R_IK_target  world == hand_L/R world           (1e-4 cm / 0.000 deg)
//   hand_L_to_R_ikrule  world == hand_L world (parent hand_R)   (1e-4 cm / 0.000 deg)
//   hand_R_to_L_ikrule  world == hand_R world (parent hand_L)   (1e-4 cm / 0.000 deg)
//   hold_L/R            local == rest local                (1e-4 cm / 0.000 deg)
//   root_IK             world pos == pelvis world pos with the up (Y) component pinned to
//                       root_IK's rest height (0): lateral residual 0.0000 cm, up residual
//                       0.0000 cm; world rot == rest world rot ALWAYS (0.000 deg even while
//                       the pelvis yaws in Run_N) — it never follows pelvis rotation.
//   hand_L/R_IK_attach  absent from every probed shipped clip skeleton (targets are parented
//                       directly under root_IK there); in the human_male rig its rest local
//                       under root_IK is identity → bake keeps rest.
//   aim_matrix_*        absent from all regular clips; in the dedicated *_AimMatrix clips
//                       aim_matrix_02/03 are exactly rest local (0.0000 cm / 0.000 deg) and
//                       aim_matrix_01 holds an animator-authored constant (90 deg off the
//                       clip bind, not derivable from the body) → animgraph-only aim-space
//                       references, bake keeps rest.
//
// The reproduction gate below overwrites all IkBaked channels with rest, re-bakes them from
// the body bones only, and requires the shipped channels back within 0.5 cm / 0.5 deg.
// =============================================================================================
public class IkBoneBakerTests
{
    private const float Deg = 180f / MathF.PI;

    // Documented-relationship tolerance: measured residuals are ~1e-4 cm float noise; 0.05 is
    // ~500x above that and 10x tighter than the task gate.
    private const float RelPosTol = 0.05f;
    private const float RelRotTolDeg = 0.05f;

    // Task 6.1 reproduction gate.
    private const float GatePosTol = 0.5f;
    private const float GateRotTolDeg = 0.5f;

    // ---------------------------------------------------------------- fixture loading

    private sealed record Fx(SourceScene Scene, TargetRig Rig, Clip Clip)
    {
        public Skel Skeleton => Scene.Skeleton;
    }

    private static readonly Lazy<Fx> RunN = new(() => Load("Citizen@Run_N.fbx"));
    private static readonly Lazy<Fx> Dab = new(() => Load("Citizen@Dab.fbx"));

    private static Fx Get(string clip) => clip == "run" ? RunN.Value : Dab.Value;

    private static Fx Load(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fbx", name));
        var scene = FbxImporter.Import(bytes);

        // The shipped clips use the citizen skeleton with s&box bone names, so roles come
        // straight from the s&box name tables; classes from the generic name rules
        // (root_IK/IK_target/ikrule/hold/aim_matrix → IkBaked).
        var map = new MappingResult(name, MappingSource.Manual) { Confidence = 1f };
        for (var i = 0; i < scene.Skeleton.Count; i++)
        {
            if (SboxBoneClassifier.RoleFor(scene.Skeleton[i].Name) is { } role)
                map.RoleToBone[role] = i;
        }

        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        Assert.NotEmpty(rig.BonesOfClass(BoneClass.IkBaked));
        return new Fx(scene, rig, scene.Clips[0]);
    }

    private static List<XForm[]> WorldsPerFrame(Fx fx, List<XForm[]> frames)
    {
        var result = new List<XForm[]>(frames.Count);
        foreach (var locals in frames)
            result.Add(new Pose(locals).ToWorld(fx.Skeleton));
        return result;
    }

    private static List<XForm[]> CopyFrames(List<XForm[]> frames)
    {
        var copy = new List<XForm[]>(frames.Count);
        foreach (var f in frames)
            copy.Add((XForm[])f.Clone());
        return copy;
    }

    private static void AssertWorldsEqual(XForm actual, XForm expected, float posTol, float rotTolDeg, string context)
    {
        var posErr = Vector3.Distance(actual.Pos, expected.Pos);
        var rotErr = MathQ.AngleBetween(actual.Rot, expected.Rot) * Deg;
        Assert.True(posErr <= posTol && rotErr <= rotTolDeg,
            $"{context}: posErr={posErr:F4} cm (tol {posTol}), rotErr={rotErr:F4} deg (tol {rotTolDeg})");
    }

    // ---------------------------------------------------------------- documented relationships
    // These assert the investigation findings directly on the SHIPPED channels.

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void ShippedClips_FootAndHandIkTargets_EqualBodyBoneWorlds(string clip)
    {
        var fx = Get(clip);
        var worlds = WorldsPerFrame(fx, fx.Clip.Frames);

        foreach (var (ik, body) in new[]
        {
            ("foot_L_IK_target", "ankle_L"), ("foot_R_IK_target", "ankle_R"),
            ("hand_L_IK_target", "hand_L"), ("hand_R_IK_target", "hand_R"),
        })
        {
            int i = fx.Skeleton.IndexOf(ik), b = fx.Skeleton.IndexOf(body);
            Assert.True(i >= 0 && b >= 0, $"{ik}/{body} missing from clip skeleton");
            for (var f = 0; f < worlds.Count; f++)
                AssertWorldsEqual(worlds[f][i], worlds[f][b], RelPosTol, RelRotTolDeg, $"{clip} {ik} frame {f}");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void ShippedClips_IkruleBones_CarryTheNamedHandWorld(string clip)
    {
        var fx = Get(clip);
        var worlds = WorldsPerFrame(fx, fx.Clip.Frames);

        // hand_X_to_Y_ikrule carries hand X's world transform, parented under hand Y.
        foreach (var (ik, body, parent) in new[]
        {
            ("hand_L_to_R_ikrule", "hand_L", "hand_R"),
            ("hand_R_to_L_ikrule", "hand_R", "hand_L"),
        })
        {
            int i = fx.Skeleton.IndexOf(ik), b = fx.Skeleton.IndexOf(body);
            Assert.True(i >= 0 && b >= 0, $"{ik}/{body} missing from clip skeleton");
            Assert.Equal(parent, fx.Skeleton[fx.Skeleton[i].ParentIndex].Name);
            for (var f = 0; f < worlds.Count; f++)
                AssertWorldsEqual(worlds[f][i], worlds[f][b], RelPosTol, RelRotTolDeg, $"{clip} {ik} frame {f}");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void ShippedClips_HoldBones_StayAtRestLocal(string clip)
    {
        var fx = Get(clip);
        foreach (var name in new[] { "hold_L", "hold_R" })
        {
            var i = fx.Skeleton.IndexOf(name);
            Assert.True(i >= 0, $"{name} missing from clip skeleton");
            var rest = fx.Skeleton[i].RestLocal;
            for (var f = 0; f < fx.Clip.FrameCount; f++)
                AssertWorldsEqual(fx.Clip.Frames[f][i], rest, RelPosTol, RelRotTolDeg, $"{clip} {name} frame {f}");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void ShippedClips_RootIk_GroundProjectsPelvis_WithFixedRestRotation(string clip)
    {
        var fx = Get(clip);
        var worlds = WorldsPerFrame(fx, fx.Clip.Frames);
        int root = fx.Skeleton.IndexOf("root_IK"), pelvis = fx.Skeleton.IndexOf("pelvis");
        Assert.True(root >= 0 && pelvis >= 0);

        // Citizen FBX scene space is Y-up: lateral = X/Z, ground = Y == root_IK rest height.
        var groundY = fx.Skeleton.RestWorld[root].Pos.Y;
        var restRot = fx.Skeleton.RestWorld[root].Rot;
        for (var f = 0; f < worlds.Count; f++)
        {
            var r = worlds[f][root];
            var p = worlds[f][pelvis];
            Assert.True(MathF.Abs(r.Pos.X - p.Pos.X) <= RelPosTol, $"{clip} frame {f}: root_IK.X != pelvis.X");
            Assert.True(MathF.Abs(r.Pos.Z - p.Pos.Z) <= RelPosTol, $"{clip} frame {f}: root_IK.Z != pelvis.Z");
            Assert.True(MathF.Abs(r.Pos.Y - groundY) <= RelPosTol, $"{clip} frame {f}: root_IK.Y != ground");
            Assert.True(MathQ.AngleBetween(r.Rot, restRot) * Deg <= RelRotTolDeg,
                $"{clip} frame {f}: root_IK rotation deviates from rest (it must never follow pelvis yaw)");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void ShippedClips_HaveNoAttachOrAimBones(string clip)
    {
        // Documents the investigation: shipped clip skeletons parent hand IK targets directly
        // under root_IK and carry no aim_matrix bones — those exist only in the model rig
        // (and, for aim bones, the dedicated *_AimMatrix clips, where 02/03 are exactly rest).
        var fx = Get(clip);
        Assert.Equal(-1, fx.Skeleton.IndexOf("hand_L_IK_attach"));
        Assert.Equal(-1, fx.Skeleton.IndexOf("hand_R_IK_attach"));
        Assert.DoesNotContain(fx.Skeleton.Bones, b => b.Name.StartsWith("aim_matrix"));
        Assert.Equal("root_IK", fx.Skeleton[fx.Skeleton[fx.Skeleton.IndexOf("hand_L_IK_target")].ParentIndex].Name);
    }

    // ---------------------------------------------------------------- the reproduction gate
    // Wipe all IkBaked channels to rest, re-bake from the body bones only, and require the
    // shipped channels back within 0.5 cm / 0.5 deg world error per frame.

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void Bake_ReproducesShippedIkChannels_WithinGate(string clip)
    {
        var fx = Get(clip);
        var originalWorlds = WorldsPerFrame(fx, fx.Clip.Frames);

        var frames = CopyFrames(fx.Clip.Frames);
        foreach (var ik in fx.Rig.BonesOfClass(BoneClass.IkBaked))
        {
            var rest = fx.Skeleton[ik].RestLocal;
            foreach (var locals in frames)
                locals[ik] = rest; // destroy the shipped IK data
        }

        IkBoneBaker.Bake(frames, fx.Rig);

        var bakedWorlds = WorldsPerFrame(fx, frames);
        foreach (var ik in fx.Rig.BonesOfClass(BoneClass.IkBaked))
        {
            for (var f = 0; f < frames.Count; f++)
                AssertWorldsEqual(
                    bakedWorlds[f][ik], originalWorlds[f][ik], GatePosTol, GateRotTolDeg,
                    $"{clip} {fx.Skeleton[ik].Name} frame {f}");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void Bake_LeavesNonIkBonesUntouched(string clip)
    {
        var fx = Get(clip);
        var frames = CopyFrames(fx.Clip.Frames);

        IkBoneBaker.Bake(frames, fx.Rig);

        for (var i = 0; i < fx.Skeleton.Count; i++)
        {
            if (fx.Rig.ClassOf(i) == BoneClass.IkBaked)
                continue;
            for (var f = 0; f < frames.Count; f++)
                Assert.True(frames[f][i] == fx.Clip.Frames[f][i],
                    $"{clip}: non-IK bone '{fx.Skeleton[i].Name}' was modified at frame {f}");
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void Bake_IsDeterministic(string clip)
    {
        var fx = Get(clip);
        var a = CopyFrames(fx.Clip.Frames);
        var b = CopyFrames(fx.Clip.Frames);

        IkBoneBaker.Bake(a, fx.Rig);
        IkBoneBaker.Bake(b, fx.Rig);

        for (var f = 0; f < a.Count; f++)
            for (var i = 0; i < a[f].Length; i++)
                Assert.True(a[f][i] == b[f][i], $"{clip}: nondeterministic at frame {f} bone {i}");
    }

    [Theory]
    [InlineData("run")]
    [InlineData("dab")]
    public void Bake_IsIdempotentOnItsOwnOutput(string clip)
    {
        var fx = Get(clip);
        var once = CopyFrames(fx.Clip.Frames);
        IkBoneBaker.Bake(once, fx.Rig);

        var twice = CopyFrames(once);
        IkBoneBaker.Bake(twice, fx.Rig);

        for (var f = 0; f < once.Count; f++)
            for (var i = 0; i < once[f].Length; i++)
                AssertWorldsEqual(twice[f][i], once[f][i], 1e-3f, 1e-3f,
                    $"{clip}: bake not idempotent at frame {f} bone {fx.Skeleton[i].Name}");
    }

    // ---------------------------------------------------------------- s&box default rig
    // The committed human_male target rig has the bones the clip skeletons lack
    // (hand_*_IK_attach under root_IK, aim_matrix_01/02a/02b); exercise that parentage on a
    // synthetic clip: rest pose + a frame with the pelvis translated/yawed and an arm swung.

    private static TargetRig LoadSboxRig()
        => TargetRig.Load(TargetRigGenerator.Generate(
            File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json"))));

    private static List<XForm[]> SyntheticFrames(TargetRig rig)
    {
        var rest = Pose.Rest(rig.Skeleton).Locals;
        var moved = (XForm[])rest.Clone();

        var pelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        moved[pelvis] = new XForm(
            rest[pelvis].Pos + new Vector3(12f, -4f, 30f),
            MathQ.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f) * rest[pelvis].Rot));

        var armUpperL = rig.BoneForRole(BoneRole.UpperArmL)!.Value;
        moved[armUpperL] = new XForm(
            rest[armUpperL].Pos,
            MathQ.Normalize(rest[armUpperL].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f)));

        return new List<XForm[]> { (XForm[])rest.Clone(), moved };
    }

    [Fact]
    public void Bake_SboxDefaultRig_TargetsFollowBodyThroughAttachBones()
    {
        var rig = LoadSboxRig();
        var sk = rig.Skeleton;
        var frames = SyntheticFrames(rig);

        IkBoneBaker.Bake(frames, rig);

        for (var f = 0; f < frames.Count; f++)
        {
            var w = new Pose(frames[f]).ToWorld(sk);

            foreach (var (ik, body) in new[]
            {
                ("foot_L_IK_target", "ankle_L"), ("foot_R_IK_target", "ankle_R"),
                ("hand_L_IK_target", "hand_L"), ("hand_R_IK_target", "hand_R"),
                ("hand_L_to_R_ikrule", "hand_L"), ("hand_R_to_L_ikrule", "hand_R"),
            })
            {
                AssertWorldsEqual(
                    w[sk.IndexOf(ik)], w[sk.IndexOf(body)], 1e-2f, 1e-2f, $"frame {f} {ik}");
            }

            // root_IK: pelvis ground-projected (the rig rest is Y-up: pelvis is +93 cm above
            // root_IK on Y and matches it exactly on X/Z), rotation pinned to rest.
            var root = sk.IndexOf("root_IK");
            var pelvis = sk.IndexOf("pelvis");
            Assert.True(MathF.Abs(w[root].Pos.X - w[pelvis].Pos.X) <= 1e-2f, $"frame {f}: root_IK.X");
            Assert.True(MathF.Abs(w[root].Pos.Z - w[pelvis].Pos.Z) <= 1e-2f, $"frame {f}: root_IK.Z");
            Assert.True(MathF.Abs(w[root].Pos.Y - sk.RestWorld[root].Pos.Y) <= 1e-2f, $"frame {f}: root_IK.Y");
            Assert.True(MathQ.AngleBetween(w[root].Rot, sk.RestWorld[root].Rot) * Deg <= 1e-2f,
                $"frame {f}: root_IK rot must stay at rest");

            // Rest-keeping bones: attach (identity under root_IK), hold, aim_matrix.
            foreach (var name in new[]
            {
                "hand_L_IK_attach", "hand_R_IK_attach", "hold_L", "hold_R",
                "aim_matrix_01", "aim_matrix_02a", "aim_matrix_02b",
            })
            {
                var i = sk.IndexOf(name);
                Assert.True(i >= 0, $"{name} missing from s&box rig");
                AssertWorldsEqual(frames[f][i], sk[i].RestLocal, 1e-3f, 1e-3f, $"frame {f} {name} local");
            }
        }
    }

    [Fact]
    public void Bake_RejectsFrameLengthMismatch()
    {
        var rig = LoadSboxRig();
        var bad = new List<XForm[]> { new XForm[3] };
        Assert.Throws<ArgumentException>(() => IkBoneBaker.Bake(bad, rig));
    }
}
