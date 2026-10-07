using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Mapping;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class CanonicalFramesTests
{
    // ---------------------------------------------------------------- fixture rigs

    internal static (Skel Skeleton, MappingResult Map) LoadZombie()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        Assert.Equal("mixamo", detected!.Value.Profile.Name);
        return (skeleton, detected.Value.Result);
    }

    internal static (Skel Skeleton, MappingResult Map) LoadActorCore()
    {
        var skeleton = MappingFixtures.LoadActorCore();
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        Assert.Equal("actorcore_cc", detected!.Value.Profile.Name);
        return (skeleton, detected.Value.Result);
    }

    /// <summary>The s&amp;box target rig: skeleton + role mapping taken from the generated
    /// target-rig definition (roles live on the rig, not on a source profile).</summary>
    internal static (Skel Skeleton, MappingResult Map) LoadSbox()
    {
        var rigJson = File.ReadAllText(MappingFixtures.FixturePath("rig_human_male.json"));
        var rig = TargetRig.Load(TargetRigGenerator.Generate(rigJson));
        var map = new MappingResult("sbox_target", MappingSource.Manual) { Confidence = 1f };
        foreach (var role in Enum.GetValues<BoneRole>())
        {
            if (rig.BoneForRole(role) is int index)
                map.RoleToBone[role] = index;
        }
        return (rig.Skeleton, map);
    }

    public static IEnumerable<object[]> RigNames()
    {
        yield return new object[] { "sbox" };
        yield return new object[] { "zombie" };
        yield return new object[] { "actorcore" };
    }

    private static (Skel Skeleton, MappingResult Map) LoadRig(string name) => name switch
    {
        "sbox" => LoadSbox(),
        "zombie" => LoadZombie(),
        "actorcore" => LoadActorCore(),
        _ => throw new ArgumentException(name),
    };

    // ---------------------------------------------------------------- helpers

    private static float DegBetween(Vector3 a, Vector3 b)
        => MathQ.AngleBetween(a, b) * (180f / MathF.PI);

    private static Vector3 Pos(Skel s, MappingResult map, BoneRole role)
        => s.RestWorld[map.RoleToBone[role]].Pos;

    /// <summary>All chains of consecutive roles whose primary axis must point at the mapped
    /// chain child (mirrors the production chain tables on purpose — the test pins them).</summary>
    private static IEnumerable<BoneRole[]> ChainDefinitions()
    {
        yield return new[]
        {
            BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3,
            BoneRole.Spine4, BoneRole.Neck, BoneRole.Head,
        };
        foreach (var s in new[] { "L", "R" })
        {
            yield return new[] { R("Clavicle", s), R("UpperArm", s), R("LowerArm", s), R("Hand", s) };
            yield return new[] { R("UpperLeg", s), R("LowerLeg", s), R("Foot", s), R("Toe", s) };
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                yield return new[] { R(f + "Meta", s), R(f + "Prox", s), R(f + "Mid", s), R(f + "Dist", s) };
        }
    }

    private static BoneRole R(string baseName, string side) => Enum.Parse<BoneRole>(baseName + side);

    // ---------------------------------------------------------------- (a) primary axis = chain direction

    [Theory]
    [MemberData(nameof(RigNames))]
    public void PrimaryAxis_PointsAtMappedChainChild_ForAllChainBones(string rigName)
    {
        var (skeleton, map) = LoadRig(rigName);
        var frames = CanonicalFrames.Build(skeleton, map);

        var checkedPairs = 0;
        foreach (var chain in ChainDefinitions())
        {
            var mapped = chain.Where(r => map.RoleToBone.ContainsKey(r)).ToArray();
            for (var i = 0; i + 1 < mapped.Length; i++)
            {
                var role = mapped[i];
                Assert.True(frames.Has(role), $"{rigName}: no frame for {role}");

                var expected = Pos(skeleton, map, mapped[i + 1]) - Pos(skeleton, map, role);
                var actual = Vector3.Transform(Vector3.UnitX, frames.WorldFrameOf(role));
                var deg = DegBetween(actual, expected);
                Assert.True(deg < 0.5f, $"{rigName}: {role} frame X is {deg:F3} deg off its chain child direction");
                checkedPairs++;
            }
        }

        // Sanity: the sweep actually covered the body, both arms, both legs and fingers.
        Assert.True(checkedPairs >= 30, $"{rigName}: only {checkedPairs} chain pairs checked");
    }

    [Fact]
    public void Sbox_UpperArmFrame_PointsFromShoulderTowardElbow()
    {
        var (skeleton, map) = LoadSbox();
        var frames = CanonicalFrames.Build(skeleton, map);

        var expected = Pos(skeleton, map, BoneRole.LowerArmL) - Pos(skeleton, map, BoneRole.UpperArmL);
        var actual = Vector3.Transform(Vector3.UnitX, frames.WorldFrameOf(BoneRole.UpperArmL));
        Assert.True(DegBetween(actual, expected) < 0.5f);
    }

    // ---------------------------------------------------------------- (b) orthonormality

    [Theory]
    [MemberData(nameof(RigNames))]
    public void Frames_AreOrthonormalRightHandedUnitQuaternions(string rigName)
    {
        var (skeleton, map) = LoadRig(rigName);
        var frames = CanonicalFrames.Build(skeleton, map);

        foreach (var role in Enum.GetValues<BoneRole>())
        {
            if (!frames.Has(role))
                continue;
            var q = frames.WorldFrameOf(role);
            Assert.True(MathF.Abs(q.Length() - 1f) < 1e-4f, $"{rigName}: {role} frame is not unit length");

            var x = Vector3.Transform(Vector3.UnitX, q);
            var y = Vector3.Transform(Vector3.UnitY, q);
            var z = Vector3.Transform(Vector3.UnitZ, q);
            Assert.True(MathF.Abs(Vector3.Dot(x, y)) < 1e-4f, $"{rigName}: {role} X·Y != 0");
            Assert.True(MathF.Abs(Vector3.Dot(y, z)) < 1e-4f, $"{rigName}: {role} Y·Z != 0");
            Assert.True(MathF.Abs(Vector3.Dot(z, x)) < 1e-4f, $"{rigName}: {role} Z·X != 0");
            Assert.True(Vector3.Dot(Vector3.Cross(x, y), z) > 0.999f, $"{rigName}: {role} basis not right-handed");
        }
    }

    // ---------------------------------------------------------------- (c) forward = toe direction

    [Theory]
    [MemberData(nameof(RigNames))]
    public void CharacterForward_PointsTheWayTheToesPoint(string rigName)
    {
        var (skeleton, map) = LoadRig(rigName);
        var frames = CanonicalFrames.Build(skeleton, map);

        foreach (var (foot, toe) in new[]
        {
            (BoneRole.FootL, BoneRole.ToeL), (BoneRole.FootR, BoneRole.ToeR),
        })
        {
            var toeDir = Vector3.Normalize(Pos(skeleton, map, toe) - Pos(skeleton, map, foot));
            var dot = Vector3.Dot(frames.CharacterForward, toeDir);
            Assert.True(dot > 0f, $"{rigName}: forward·toeDir = {dot:F3} for {foot}->{toe}");
        }

        // Character up and forward are unit and orthogonal; hip height is human-plausible.
        Assert.True(MathF.Abs(frames.CharacterUp.Length() - 1f) < 1e-4f);
        Assert.True(MathF.Abs(frames.CharacterForward.Length() - 1f) < 1e-4f);
        Assert.True(MathF.Abs(Vector3.Dot(frames.CharacterUp, frames.CharacterForward)) < 1e-4f);
        Assert.InRange(frames.HipHeight, 60f, 110f);
    }

    // ---------------------------------------------------------------- (d) finger curl mirror consistency

    [Fact]
    public void FingerCurl_AboutCanonicalHinge_MovesTipsTowardPalm_OnBothHands()
    {
        // The canonical hinge is the finger frame's Y axis. A positive rotation about it must
        // curl the fingertip toward the palm on BOTH hands. The palm side is grounded
        // anatomically and independently of the dorsal formula: the thumb base sits on the
        // palmar side, so a correct curl brings fingertips closer to the thumb proximal.
        var (skeleton, map) = LoadActorCore();
        var frames = CanonicalFrames.Build(skeleton, map);

        foreach (var side in new[] { "L", "R" })
        {
            var thumb = Pos(skeleton, map, R("ThumbProx", side));
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                var proxRole = R(finger + "Prox", side);
                Assert.True(frames.Has(proxRole), $"no frame for {proxRole}");

                var prox = Pos(skeleton, map, proxRole);
                var tip = Pos(skeleton, map, R(finger + "Dist", side));
                var hinge = Vector3.Transform(Vector3.UnitY, frames.WorldFrameOf(proxRole));

                var curl = Quaternion.CreateFromAxisAngle(hinge, 60f * MathF.PI / 180f);
                var newTip = prox + Vector3.Transform(tip - prox, curl);

                var before = (tip - thumb).Length();
                var after = (newTip - thumb).Length();
                Assert.True(after < before,
                    $"{finger}{side}: +60 deg curl moved tip away from the palm ({before:F2} -> {after:F2} cm to thumb)");
            }
        }
    }

    // ---------------------------------------------------------------- (e) determinism

    [Fact]
    public void Build_IsDeterministic()
    {
        var (skeleton, map) = LoadZombie();
        var a = CanonicalFrames.Build(skeleton, map);
        var b = CanonicalFrames.Build(skeleton, map);

        Assert.Equal(a.CharacterForward, b.CharacterForward);
        Assert.Equal(a.CharacterUp, b.CharacterUp);
        Assert.Equal(a.HipHeight, b.HipHeight);
        foreach (var role in Enum.GetValues<BoneRole>())
        {
            Assert.Equal(a.Has(role), b.Has(role));
            if (a.Has(role))
                Assert.Equal(a.WorldFrameOf(role), b.WorldFrameOf(role));
        }
    }

    [Fact]
    public void WorldFrameOf_UnmappedRole_Throws()
    {
        var (skeleton, map) = LoadSbox();
        var frames = CanonicalFrames.Build(skeleton, map);

        // The s&box rig has a 3-bone spine: Spine3/4 are unmapped.
        Assert.False(frames.Has(BoneRole.Spine4));
        Assert.Throws<InvalidOperationException>(() => frames.WorldFrameOf(BoneRole.Spine4));
    }
}
