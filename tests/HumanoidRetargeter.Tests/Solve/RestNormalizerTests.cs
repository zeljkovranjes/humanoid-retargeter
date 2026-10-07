using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class RestNormalizerTests
{
    private const float RadToDeg = 180f / MathF.PI;

    // ---------------------------------------------------------------- helpers

    private static float DegBetween(Vector3 a, Vector3 b) => MathQ.AngleBetween(a, b) * RadToDeg;

    private static Vector3 Pos(IReadOnlyList<XForm> world, MappingResult map, BoneRole role)
        => world[map.RoleToBone[role]].Pos;

    private static (Vector3 Up, Vector3 Forward, Vector3 Lateral, Vector3 MidHips) Frame(
        Skel skeleton, MappingResult map)
    {
        var frames = CanonicalFrames.Build(skeleton, map);
        var up = frames.CharacterUp;
        var forward = frames.CharacterForward;
        // Left-positive lateral completes the right-handed character basis (forward = lateral x up).
        var lateral = Vector3.Cross(up, forward);
        var midHips = (Pos(skeleton.RestWorld, map, BoneRole.UpperLegL)
            + Pos(skeleton.RestWorld, map, BoneRole.UpperLegR)) * 0.5f;
        return (up, forward, lateral, midHips);
    }

    /// <summary>Hierarchically rotates the world rest of <paramref name="root"/> and all its
    /// descendants about a pivot: positions orbit the pivot, orientations are premultiplied.</summary>
    private static void RotateWorldSubtree(Skel skeleton, XForm[] world, int root, Quaternion q, Vector3 pivot)
    {
        var inSubtree = new bool[skeleton.Count];
        inSubtree[root] = true;
        for (var i = root; i < skeleton.Count; i++)
        {
            if (i != root)
            {
                var parent = skeleton[i].ParentIndex;
                if (parent < 0 || !inSubtree[parent])
                    continue;
                inSubtree[i] = true;
            }
            world[i] = new XForm(
                pivot + Vector3.Transform(world[i].Pos - pivot, q),
                MathQ.Normalize(q * world[i].Rot));
        }
    }

    /// <summary>Rebuilds a skeleton with the same names/hierarchy/order but rest locals derived
    /// from the given world rests (so RestWorld == <paramref name="world"/>).</summary>
    private static Skel RebuildWithWorldRest(Skel skeleton, XForm[] world)
    {
        var definitions = new List<BoneDefinition>(skeleton.Count);
        for (var i = 0; i < skeleton.Count; i++)
        {
            var bone = skeleton[i];
            var local = bone.ParentIndex < 0 ? world[i] : XForm.ToLocal(world[bone.ParentIndex], world[i]);
            definitions.Add(new BoneDefinition(bone.Name, bone.ParentIndex < 0 ? null : skeleton[bone.ParentIndex].Name, local));
        }
        return Skel.Create(definitions);
    }

    private static bool IsDescendantOf(Skel skeleton, int bone, int ancestor)
    {
        for (var i = bone; i >= 0; i = skeleton[i].ParentIndex)
        {
            if (i == ancestor)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- (a) zombie: T-pose detection + near-no-op

    [Fact]
    public void Zombie_IsDetectedTPose_AndNormalizationIsNearNoOp()
    {
        var (skeleton, map) = CanonicalFramesTests.LoadZombie();

        var (normalized, report) = RestNormalizer.Normalize(skeleton, map);

        Assert.Equal(RestNormalizer.DetectedPose.TPose, report.Detected);
        Assert.True(report.UpperArmAngleDeg < 2f, $"arm angle {report.UpperArmAngleDeg:F2} deg");

        // Measured on the fixture: the Mixamo character frame leans ~6 deg back (shoulders sit
        // behind the hips), so snapping arms to the character-horizontal costs ~0.8 deg and the
        // palm-down roll convention costs ~6.5 deg on the hand subtrees. Everything outside the
        // arms must be bit-identical-ish; arm swings stay under 1.5 deg; hand subtrees absorb
        // the swing + roll under 12 deg.
        var handL = map.RoleToBone[BoneRole.HandL];
        var handR = map.RoleToBone[BoneRole.HandR];
        var armL = map.RoleToBone[BoneRole.UpperArmL];
        var armR = map.RoleToBone[BoneRole.UpperArmR];
        for (var i = 0; i < skeleton.Count; i++)
        {
            var deg = MathQ.AngleBetween(skeleton.RestWorld[i].Rot, normalized.WorldRest[i].Rot) * RadToDeg;
            var budget =
                IsDescendantOf(skeleton, i, handL) || IsDescendantOf(skeleton, i, handR) ? 12f
                : IsDescendantOf(skeleton, i, armL) || IsDescendantOf(skeleton, i, armR) ? 1.5f
                : 0.1f;
            Assert.True(deg <= budget, $"{skeleton[i].Name}: rest rotation changed {deg:F2} deg (budget {budget})");
        }

        // Legs untouched: chain directions preserved within 0.5 deg, foot positions identical.
        foreach (var (upper, lower, foot) in new[]
        {
            (BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL),
            (BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR),
        })
        {
            var before = Pos(skeleton.RestWorld, map, lower) - Pos(skeleton.RestWorld, map, upper);
            var after = Pos(normalized.WorldRest, map, lower) - Pos(normalized.WorldRest, map, upper);
            Assert.True(DegBetween(before, after) < 0.5f, "leg chain direction changed");
            Assert.True((Pos(skeleton.RestWorld, map, foot) - Pos(normalized.WorldRest, map, foot)).Length() < 1e-3f);
        }
    }

    // ---------------------------------------------------------------- (a) zombie: synthetic A-pose

    [Fact]
    public void Zombie_SyntheticAPose_IsDetected_AndNormalizesBackToT()
    {
        var (skeleton, map) = CanonicalFramesTests.LoadZombie();
        var (up, forward, lateral, midHips) = Frame(skeleton, map);

        // Synthesize an A-pose: rotate both upper-arm subtrees 40 deg downward about character
        // forward (left arm: negative angle lowers +lateral toward -up; right arm mirrored).
        var world = skeleton.RestWorld.ToArray();
        foreach (var (role, sign) in new[] { (BoneRole.UpperArmL, 1f), (BoneRole.UpperArmR, -1f) })
        {
            var bone = map.RoleToBone[role];
            var q = Quaternion.CreateFromAxisAngle(forward, -sign * 40f / RadToDeg);
            RotateWorldSubtree(skeleton, world, bone, q, world[bone].Pos);
        }
        var aPosed = RebuildWithWorldRest(skeleton, world);

        // Segment lengths to preserve, measured on the synthesized rig.
        float UpperLen(IReadOnlyList<XForm> w, string s)
            => (Pos(w, map, R("LowerArm" + s)) - Pos(w, map, R("UpperArm" + s))).Length();
        float LowerLen(IReadOnlyList<XForm> w, string s)
            => (Pos(w, map, R("Hand" + s)) - Pos(w, map, R("LowerArm" + s))).Length();

        var (normalized, report) = RestNormalizer.Normalize(aPosed, map);

        Assert.Equal(RestNormalizer.DetectedPose.APose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 38f, 42f);

        foreach (var (s, sign) in new[] { ("L", 1f), ("R", -1f) })
        {
            var lateralSide = lateral * sign;
            var upperDir = Pos(normalized.WorldRest, map, R("LowerArm" + s)) - Pos(normalized.WorldRest, map, R("UpperArm" + s));
            var lowerDir = Pos(normalized.WorldRest, map, R("Hand" + s)) - Pos(normalized.WorldRest, map, R("LowerArm" + s));
            Assert.True(DegBetween(upperDir, lateralSide) < 2f, $"upper arm {s} not horizontal");
            Assert.True(DegBetween(lowerDir, lateralSide) < 2f, $"forearm {s} not horizontal");

            // Arm segment lengths unchanged by normalization (swing-only, pivoted at joints).
            Assert.True(MathF.Abs(UpperLen(normalized.WorldRest, s) - UpperLen(aPosed.RestWorld, s)) < 0.01f);
            Assert.True(MathF.Abs(LowerLen(normalized.WorldRest, s) - LowerLen(aPosed.RestWorld, s)) < 0.01f);
        }

        // Hands end symmetric across the sagittal plane (through midHips, normal = lateral).
        var handL = Pos(normalized.WorldRest, map, BoneRole.HandL);
        var handR = Pos(normalized.WorldRest, map, BoneRole.HandR);
        var mirroredL = handL - lateral * (2f * Vector3.Dot(handL - midHips, lateral));
        Assert.True((mirroredL - handR).Length() < 1f,
            $"hands asymmetric by {(mirroredL - handR).Length():F2} cm");
    }

    // ---------------------------------------------------------------- (b) actorcore: real A-pose

    [Fact]
    public void ActorCore_IsDetectedAPose_AndArmsEndHorizontal()
    {
        var (skeleton, map) = CanonicalFramesTests.LoadActorCore();
        var (_, _, lateral, _) = Frame(skeleton, map);

        var (normalized, report) = RestNormalizer.Normalize(skeleton, map);

        // Measured on the fixture: ActorCore/CC exports rest ~30 deg below horizontal.
        Assert.Equal(RestNormalizer.DetectedPose.APose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 25f, 35f);

        foreach (var (s, sign) in new[] { ("L", 1f), ("R", -1f) })
        {
            var lateralSide = lateral * sign;
            var upperDir = Pos(normalized.WorldRest, map, R("LowerArm" + s)) - Pos(normalized.WorldRest, map, R("UpperArm" + s));
            var lowerDir = Pos(normalized.WorldRest, map, R("Hand" + s)) - Pos(normalized.WorldRest, map, R("LowerArm" + s));
            Assert.True(DegBetween(upperDir, lateralSide) < 2f, $"upper arm {s} {DegBetween(upperDir, lateralSide):F2} deg off");
            Assert.True(DegBetween(lowerDir, lateralSide) < 2f, $"forearm {s} {DegBetween(lowerDir, lateralSide):F2} deg off");
        }
    }

    // ---------------------------------------------------------------- s&box target rig pose (documented for Task 4.3)

    [Fact]
    public void SboxTargetRig_RestsInAStrongAPose()
    {
        // The s&box human male bind pose is NOT a T-pose: its arms rest ~52 deg below
        // horizontal (a strong A-pose). Task 4.3 must therefore normalize BOTH source and
        // target rests before building canonical frames.
        var (skeleton, map) = CanonicalFramesTests.LoadSbox();

        var (_, report) = RestNormalizer.Normalize(skeleton, map);

        Assert.Equal(RestNormalizer.DetectedPose.APose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 45f, 60f);
    }

    // ---------------------------------------------------------------- (c) legs untouched on normal rigs

    [Theory]
    [MemberData(nameof(CanonicalFramesTests.RigNames), MemberType = typeof(CanonicalFramesTests))]
    public void Legs_AreUntouched_ForNormalStanceRigs(string rigName)
    {
        var (skeleton, map) = rigName switch
        {
            "sbox" => CanonicalFramesTests.LoadSbox(),
            "zombie" => CanonicalFramesTests.LoadZombie(),
            _ => CanonicalFramesTests.LoadActorCore(),
        };

        var (normalized, _) = RestNormalizer.Normalize(skeleton, map);

        foreach (var (upper, lower) in new[]
        {
            (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.UpperLegR, BoneRole.LowerLegR),
        })
        {
            var before = Pos(skeleton.RestWorld, map, lower) - Pos(skeleton.RestWorld, map, upper);
            var after = Pos(normalized.WorldRest, map, lower) - Pos(normalized.WorldRest, map, upper);
            Assert.True(DegBetween(before, after) < 0.5f, $"{rigName}: leg chain direction changed");
        }
    }

    // ---------------------------------------------------------------- (d) determinism + report

    [Fact]
    public void Normalize_IsDeterministic_AndReportIsPopulated()
    {
        var (skeleton, map) = CanonicalFramesTests.LoadActorCore();

        var (a, reportA) = RestNormalizer.Normalize(skeleton, map);
        var (b, reportB) = RestNormalizer.Normalize(skeleton, map);

        Assert.Equal(a.WorldRest.Length, b.WorldRest.Length);
        for (var i = 0; i < a.WorldRest.Length; i++)
            Assert.Equal(a.WorldRest[i], b.WorldRest[i]);

        Assert.Equal(reportA.Detected, reportB.Detected);
        Assert.Equal(reportA.UpperArmAngleDeg, reportB.UpperArmAngleDeg);
        Assert.True(float.IsFinite(reportA.UpperArmAngleDeg));
        Assert.NotEmpty(reportA.Notes);
    }

    // ---------------------------------------------------------------- (e) non-anatomical stick binds (SOMA)

    /// <summary>
    /// Builds a SOMA-style uniform-skeleton "stick" rig: every rest local offset runs along
    /// ±X (left chains +X, right chains −X — bone-length encoding, not a pose) with identity
    /// rest rotations, plus a reference pose (parent-relative locals, like a clip frame)
    /// whose rotations stand the figure upright in a relaxed N-pose (arms hanging ~83° below
    /// horizontal). Mirrors the geometry measured on the SOMA repro BVH
    /// (Neutral_throw_ball_001__A057: bind thigh·up +1.00/−1.00, thighs 180° apart;
    /// frame 0 thighs down/parallel, arms 64–69° below horizontal).
    /// </summary>
    private static (Skel Skeleton, MappingResult Map, XForm[] ReferencePose) BuildStickRig()
    {
        // name, parent, stick offset (±X encodes side), desired WORLD rotation in the pose
        var x = Vector3.UnitX;
        var spineUp = MathQ.FromTo(x, Vector3.UnitY);                       // chain +X -> up
        var clavOut = MathQ.FromTo(x, Vector3.UnitZ);                       // chain ±X -> ±Z (lateral)
        var armDownL = MathQ.FromTo(x, Vector3.Normalize(new Vector3(0f, -1f, 0.12f)));
        var armDownR = MathQ.FromTo(x, Vector3.Normalize(new Vector3(0f, 1f, 0.12f))); // maps −X -> (0,−1,−0.12)
        var legDownL = MathQ.FromTo(x, -Vector3.UnitY);
        var legDownR = MathQ.FromTo(x, Vector3.UnitY);                      // maps −X -> down

        var bones = new (string Name, string? Parent, Vector3 Offset, Quaternion World)[]
        {
            ("Hips", null, new Vector3(0f, 100f, 0f), spineUp),
            ("Spine", "Hips", new Vector3(5f, 1f, 0f), spineUp),
            ("Neck", "Spine", new Vector3(30f, 0f, 0f), spineUp),
            ("ClavL", "Spine", new Vector3(24f, 4f, 1.5f), clavOut),
            ("ArmL", "ClavL", new Vector3(8f, 0f, 0f), armDownL),
            ("ForeArmL", "ArmL", new Vector3(25f, 0f, 0f), armDownL),
            ("HandL", "ForeArmL", new Vector3(22f, 0f, 0f), armDownL),
            ("ClavR", "Spine", new Vector3(24f, 4f, -1.5f), clavOut),
            ("ArmR", "ClavR", new Vector3(-8f, 0f, 0f), armDownR),
            ("ForeArmR", "ArmR", new Vector3(-25f, 0f, 0f), armDownR),
            ("HandR", "ForeArmR", new Vector3(-22f, 0f, 0f), armDownR),
            ("LegL", "Hips", new Vector3(-5f, 1f, 8f), legDownL),
            ("ShinL", "LegL", new Vector3(43f, 0f, 0f), legDownL),
            ("FootL", "ShinL", new Vector3(42f, 0f, 0f), legDownL),
            ("LegR", "Hips", new Vector3(-5f, 1f, -8f), legDownR),
            ("ShinR", "LegR", new Vector3(-43f, 0f, 0f), legDownR),
            ("FootR", "ShinR", new Vector3(-42f, 0f, 0f), legDownR),
        };

        var defs = bones
            .Select(b => new BoneDefinition(b.Name, b.Parent, new XForm(b.Offset, Quaternion.Identity)))
            .ToList();
        var skeleton = Skel.Create(defs);

        // Reference pose locals: local = conj(parentWorld) * world, positions = stick offsets.
        var reference = new XForm[skeleton.Count];
        for (var i = 0; i < skeleton.Count; i++)
        {
            var b = bones.First(bb => bb.Name == skeleton[i].Name);
            var parentWorld = b.Parent is null
                ? Quaternion.Identity
                : bones.First(bb => bb.Name == b.Parent).World;
            reference[i] = new XForm(
                b.Offset, MathQ.Normalize(Quaternion.Conjugate(parentWorld) * b.World));
        }

        var map = new MappingResult("stick_fixture", MappingSource.Manual) { Confidence = 1f };
        foreach (var (role, name) in new[]
        {
            (BoneRole.Hips, "Hips"), (BoneRole.Spine0, "Spine"), (BoneRole.Neck, "Neck"),
            (BoneRole.ClavicleL, "ClavL"), (BoneRole.UpperArmL, "ArmL"),
            (BoneRole.LowerArmL, "ForeArmL"), (BoneRole.HandL, "HandL"),
            (BoneRole.ClavicleR, "ClavR"), (BoneRole.UpperArmR, "ArmR"),
            (BoneRole.LowerArmR, "ForeArmR"), (BoneRole.HandR, "HandR"),
            (BoneRole.UpperLegL, "LegL"), (BoneRole.LowerLegL, "ShinL"), (BoneRole.FootL, "FootL"),
            (BoneRole.UpperLegR, "LegR"), (BoneRole.LowerLegR, "ShinR"), (BoneRole.FootR, "FootR"),
        })
        {
            map.RoleToBone[role] = skeleton.IndexOf(name);
        }
        return (skeleton, map, reference);
    }

    [Fact]
    public void StickBind_FailsAnatomicalRestCheck_AndPlausibleRigsPass()
    {
        var (stick, stickMap, _) = BuildStickRig();
        Assert.False(RestNormalizer.IsAnatomicalRest(stick, stickMap, stick.RestWorld));

        foreach (var load in new[] { CanonicalFramesTests.LoadSbox, CanonicalFramesTests.LoadZombie, CanonicalFramesTests.LoadActorCore })
        {
            var (skeleton, map) = load();
            Assert.True(RestNormalizer.IsAnatomicalRest(skeleton, map, skeleton.RestWorld));
        }
    }

    [Fact]
    public void StickBind_WithoutReferencePose_Throws()
    {
        var (stick, map, _) = BuildStickRig();
        var ex = Assert.Throws<ArgumentException>(() => RestNormalizer.Normalize(stick, map));
        Assert.Contains("not an anatomical", ex.Message);
    }

    [Fact]
    public void StickBind_WithReferencePose_RebuildsRest_DetectsIPose_AndNormalizesToT()
    {
        var (stick, map, reference) = BuildStickRig();

        var (normalized, report) = RestNormalizer.Normalize(stick, map, reference);

        Assert.True(report.RebuiltFromReferencePose);
        // Arms hang ~83 deg below horizontal in the reference pose -> I-pose class.
        Assert.Equal(RestNormalizer.DetectedPose.IPose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 60f, 95f);

        // The rebuilt+normalized rest is a proper T-pose: up from the torso, legs down,
        // arms swung to the horizontal lateral.
        var hips = Pos(normalized.WorldRest, map, BoneRole.Hips);
        var neck = Pos(normalized.WorldRest, map, BoneRole.Neck);
        var up = Vector3.Normalize(neck - hips);
        var legL = Pos(normalized.WorldRest, map, BoneRole.UpperLegL);
        var legR = Pos(normalized.WorldRest, map, BoneRole.UpperLegR);
        var lateral = Vector3.Normalize(legL - legR - up * Vector3.Dot(legL - legR, up));

        foreach (var (s, sign) in new[] { ("L", 1f), ("R", -1f) })
        {
            var upperDir = Pos(normalized.WorldRest, map, R("LowerArm" + s)) - Pos(normalized.WorldRest, map, R("UpperArm" + s));
            var lowerDir = Pos(normalized.WorldRest, map, R("Hand" + s)) - Pos(normalized.WorldRest, map, R("LowerArm" + s));
            Assert.True(DegBetween(upperDir, lateral * sign) < 2f,
                $"upper arm {s} {DegBetween(upperDir, lateral * sign):F2} deg off lateral");
            Assert.True(DegBetween(lowerDir, lateral * sign) < 2f,
                $"forearm {s} {DegBetween(lowerDir, lateral * sign):F2} deg off lateral");

            var thigh = Pos(normalized.WorldRest, map, R("LowerLeg" + s)) - Pos(normalized.WorldRest, map, R("UpperLeg" + s));
            Assert.True(DegBetween(thigh, -up) < 16f, $"thigh {s} {DegBetween(thigh, -up):F2} deg off down");
        }
    }

    [Fact]
    public void Zombie_SyntheticNPose_IsDetectedIPose_AndNormalizesBackToT()
    {
        var (skeleton, map) = CanonicalFramesTests.LoadZombie();
        var (up, forward, lateral, _) = Frame(skeleton, map);

        // Hang both arms ~80 deg below horizontal (relaxed N-pose, e.g. a first-frame rest).
        var world = skeleton.RestWorld.ToArray();
        foreach (var (role, sign) in new[] { (BoneRole.UpperArmL, 1f), (BoneRole.UpperArmR, -1f) })
        {
            var bone = map.RoleToBone[role];
            var q = Quaternion.CreateFromAxisAngle(forward, -sign * 80f / RadToDeg);
            RotateWorldSubtree(skeleton, world, bone, q, world[bone].Pos);
        }
        var nPosed = RebuildWithWorldRest(skeleton, world);

        var (normalized, report) = RestNormalizer.Normalize(nPosed, map);

        Assert.Equal(RestNormalizer.DetectedPose.IPose, report.Detected);
        Assert.InRange(report.UpperArmAngleDeg, 75f, 85f);

        foreach (var (s, sign) in new[] { ("L", 1f), ("R", -1f) })
        {
            var lateralSide = lateral * sign;
            var upperDir = Pos(normalized.WorldRest, map, R("LowerArm" + s)) - Pos(normalized.WorldRest, map, R("UpperArm" + s));
            var lowerDir = Pos(normalized.WorldRest, map, R("Hand" + s)) - Pos(normalized.WorldRest, map, R("LowerArm" + s));
            Assert.True(DegBetween(upperDir, lateralSide) < 2f, $"upper arm {s} not horizontal");
            Assert.True(DegBetween(lowerDir, lateralSide) < 2f, $"forearm {s} not horizontal");
        }
    }

    private static BoneRole R(string name) => Enum.Parse<BoneRole>(name);
}
