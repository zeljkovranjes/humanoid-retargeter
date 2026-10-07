using System.Numerics;
using System.Text;
using System.Text.Json;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Mapping;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class GeometricSolverTests
{
    internal const float RadToDeg = 180f / MathF.PI;

    // ---------------------------------------------------------------- fixtures (shared with FingerSolverTests)

    private static readonly Lazy<SourceScene> CitizenWalk =
        new(() => ImportFbx("Citizen@Walk_N.fbx"));

    private static readonly Lazy<SourceScene> ZombieCrawl =
        new(() => ImportFbx("Zombie Crawl.fbx"));

    private static readonly Lazy<SourceScene> ActorCoreCatwalk =
        new(() => ImportFbx("catwalk-loop-378982.fbx"));

    internal static SourceScene ImportFbx(string name)
        => FbxImporter.Import(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fbx", name)),
            new FbxImportOptions());

    internal static SourceScene Citizen() => CitizenWalk.Value;
    internal static SourceScene Zombie() => ZombieCrawl.Value;
    internal static SourceScene ActorCore() => ActorCoreCatwalk.Value;

    /// <summary>The shipped s&amp;box target rig (same generated definition as the committed
    /// Assets/humanoid_retargeter/target_rig_sbox.json).</summary>
    internal static TargetRig SboxRig()
        => TargetRig.Load(TargetRigGenerator.Generate(
            File.ReadAllText(MappingFixtures.FixturePath("rig_human_male.json"))));

    /// <summary>
    /// Builds a TargetRig over an arbitrary (imported) skeleton using the s&amp;box name
    /// classifier — the round-trip target: same skeleton, same rest, role/class annotations
    /// from <see cref="SboxBoneClassifier"/>. Animated bones the classifier does not know
    /// simply get no role (they stay at rest).
    /// </summary>
    internal static TargetRig RigOverSkeleton(Skel skeleton, string name)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteStartArray("bones");
            foreach (var bone in skeleton.Bones)
            {
                writer.WriteStartObject();
                writer.WriteString("name", bone.Name);
                if (bone.ParentIndex < 0)
                    writer.WriteNull("parent");
                else
                    writer.WriteString("parent", skeleton[bone.ParentIndex].Name);

                var boneClass = SboxBoneClassifier.Classify(bone.Name);
                writer.WriteString("class", boneClass.ToString());
                if (boneClass == BoneClass.Animated && SboxBoneClassifier.RoleFor(bone.Name) is BoneRole role)
                    writer.WriteString("role", role.ToString());

                writer.WriteStartArray("local_pos");
                writer.WriteNumberValue(bone.RestLocal.Pos.X);
                writer.WriteNumberValue(bone.RestLocal.Pos.Y);
                writer.WriteNumberValue(bone.RestLocal.Pos.Z);
                writer.WriteEndArray();
                writer.WriteStartArray("local_rot_xyzw");
                writer.WriteNumberValue(bone.RestLocal.Rot.X);
                writer.WriteNumberValue(bone.RestLocal.Rot.Y);
                writer.WriteNumberValue(bone.RestLocal.Rot.Z);
                writer.WriteNumberValue(bone.RestLocal.Rot.W);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return TargetRig.Load(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Identity source mapping for the round-trip: role → source bone index resolved
    /// by the rig's bone names against the source skeleton.</summary>
    internal static MappingResult RoleMapByName(Skel source, TargetRig rig)
    {
        var map = new MappingResult("identity", MappingSource.Manual) { Confidence = 1f };
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.RoleOf(i) is not BoneRole role)
                continue;
            var srcIndex = source.IndexOf(rig.Skeleton[i].Name);
            Assert.True(srcIndex >= 0, $"source skeleton lacks bone {rig.Skeleton[i].Name}");
            map.RoleToBone[role] = srcIndex;
        }
        return map;
    }

    internal static MappingResult DetectMap(SourceScene scene, string expectedProfile)
    {
        var detected = ProfileDetector.Detect(scene.Skeleton);
        Assert.NotNull(detected);
        Assert.Equal(expectedProfile, detected!.Value.Profile.Name);
        return detected.Value.Result;
    }

    // ---------------------------------------------------------------- helpers

    internal static XForm[] Fk(Skel skeleton, XForm[] locals)
    {
        var world = new XForm[locals.Length];
        for (var i = 0; i < locals.Length; i++)
        {
            var parent = skeleton[i].ParentIndex;
            world[i] = parent < 0 ? locals[i] : XForm.Compose(world[parent], locals[i]);
        }
        return world;
    }

    internal static float DegBetween(Quaternion a, Quaternion b) => MathQ.AngleBetween(a, b) * RadToDeg;

    internal static float DegBetween(Vector3 a, Vector3 b) => MathQ.AngleBetween(a, b) * RadToDeg;

    /// <summary>Normalized rest + canonical frames + character-frame basis, exactly like the
    /// solver builds them.</summary>
    internal static (XForm[] NormRest, CanonicalFrames Canon, Quaternion ChrBasis) NormCanon(
        Skel skeleton, MappingResult map)
    {
        var (norm, _) = RestNormalizer.Normalize(skeleton, map);
        var canon = CanonicalFrames.Build(skeleton, map, norm.WorldRest);
        return (norm.WorldRest, canon, MathQ.BasisFromForwardUp(canon.CharacterForward, canon.CharacterUp));
    }

    /// <summary>All chains of consecutive anatomical roles (pinned independently of the
    /// production tables).</summary>
    internal static IEnumerable<BoneRole[]> ChainDefinitions()
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

    internal static BoneRole R(string baseName, string side) => Enum.Parse<BoneRole>(baseName + side);

    /// <summary>Consecutive role pairs mapped on BOTH the source map and the target rig.</summary>
    internal static List<(BoneRole A, BoneRole B)> MappedChainPairs(MappingResult srcMap, TargetRig rig)
    {
        var pairs = new List<(BoneRole, BoneRole)>();
        foreach (var chain in ChainDefinitions())
        {
            var mapped = chain
                .Where(r => srcMap.RoleToBone.ContainsKey(r) && rig.BoneForRole(r) is not null)
                .ToArray();
            for (var i = 0; i + 1 < mapped.Length; i++)
                pairs.Add((mapped[i], mapped[i + 1]));
        }
        return pairs;
    }

    /// <summary>5 frame indices spread over a clip.</summary>
    internal static int[] SampleFrames(int frameCount)
        => new[] { 0, frameCount / 4, frameCount / 2, 3 * frameCount / 4, frameCount - 1 }
            .Distinct().ToArray();

    /// <summary>
    /// The cross-rig direction metric: worldspace anatomical direction (chain child head −
    /// bone head), each expressed in its own rig's character-frame coordinates; returns the
    /// per-pair angles in degrees.
    /// </summary>
    internal static IEnumerable<(BoneRole A, BoneRole B, float Deg)> DirectionErrors(
        Skel src, MappingResult srcMap, XForm[] srcWorld, Quaternion srcChr,
        TargetRig rig, XForm[] tgtWorld, Quaternion tgtChr)
    {
        var srcChrInv = Quaternion.Conjugate(srcChr);
        var tgtChrInv = Quaternion.Conjugate(tgtChr);
        foreach (var (a, b) in MappedChainPairs(srcMap, rig))
        {
            var ds = srcWorld[srcMap.RoleToBone[b]].Pos - srcWorld[srcMap.RoleToBone[a]].Pos;
            var dt = tgtWorld[rig.BoneForRole(b)!.Value].Pos - tgtWorld[rig.BoneForRole(a)!.Value].Pos;
            if (ds.LengthSquared() < 1e-8f || dt.LengthSquared() < 1e-8f)
                continue;
            var dsc = Vector3.Transform(Vector3.Normalize(ds), srcChrInv);
            var dtc = Vector3.Transform(Vector3.Normalize(dt), tgtChrInv);
            yield return (a, b, DegBetween(dsc, dtc));
        }
    }

    // ================================================================ A. citizen round-trip (identity gate)

    [Fact]
    public void CitizenRoundTrip_IsIdentity_ForAllMappedBones()
    {
        var scene = Citizen();
        var rig = RigOverSkeleton(scene.Skeleton, "citizen_roundtrip");
        var srcMap = RoleMapByName(scene.Skeleton, rig);
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        Assert.Equal(clip.FrameCount, solved.FrameCount);
        Assert.Equal(clip.Fps, solved.Fps);
        Assert.Equal(clip.Looping, solved.Looping);

        // (source bone, target bone, name) for every mapped role.
        var mapped = new List<(int Src, int Tgt, string Name)>();
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.RoleOf(i) is not BoneRole role)
                continue;
            mapped.Add((srcMap.RoleToBone[role], i, $"{rig.Skeleton[i].Name} ({role})"));
        }
        Assert.True(mapped.Count >= 50, $"only {mapped.Count} mapped bones in the round-trip");

        float maxRot = 0f, maxPos = 0f;
        var worst = "";
        for (var f = 0; f < clip.FrameCount; f++)
        {
            var srcWorld = Fk(scene.Skeleton, clip.Frames[f]);
            var outWorld = Fk(rig.Skeleton, solved.Frames[f]);
            foreach (var (s, t, name) in mapped)
            {
                var rotDeg = DegBetween(srcWorld[s].Rot, outWorld[t].Rot);
                var posCm = (srcWorld[s].Pos - outWorld[t].Pos).Length();
                if (rotDeg > maxRot)
                {
                    maxRot = rotDeg;
                    worst = $"{name} @ frame {f}";
                }
                maxPos = MathF.Max(maxPos, posCm);
            }
        }

        Assert.True(maxRot <= 0.5f, $"round-trip rotation error {maxRot:F3} deg (worst: {worst})");
        Assert.True(maxPos <= 0.2f, $"round-trip position error {maxPos:F3} cm");
    }

    [Fact]
    public void CitizenRoundTrip_PelvisPosition_IsIdentity()
    {
        var scene = Citizen();
        var rig = RigOverSkeleton(scene.Skeleton, "citizen_roundtrip");
        var srcMap = RoleMapByName(scene.Skeleton, rig);
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var srcPelvis = srcMap.RoleToBone[BoneRole.Hips];
        var tgtPelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        var maxPos = 0f;
        for (var f = 0; f < clip.FrameCount; f++)
        {
            var srcWorld = Fk(scene.Skeleton, clip.Frames[f]);
            var outWorld = Fk(rig.Skeleton, solved.Frames[f]);
            maxPos = MathF.Max(maxPos, (srcWorld[srcPelvis].Pos - outWorld[tgtPelvis].Pos).Length());
        }
        Assert.True(maxPos <= 0.2f, $"pelvis round-trip error {maxPos:F3} cm");
    }

    /// <summary>
    /// Per-role canonical rest-direction divergence between the two rigs (angle between each
    /// rig's rest chain direction in its own character frame) — the constant offset that
    /// <see cref="RoleTransferMode.DeltaFromRest"/> roles intentionally keep instead of
    /// adopting the source's rest carriage.
    /// </summary>
    internal static float RestDivergenceDeg(
        BoneRole role, Skel src, MappingResult srcMap, TargetRig rig)
    {
        var (_, srcCanon, srcChr) = NormCanon(src, srcMap);
        var (_, tgtCanon, tgtChr) = NormCanon(rig.Skeleton, rig.ToMappingResult());
        var ds = Vector3.Transform(
            Vector3.UnitX, Quaternion.Conjugate(srcChr) * srcCanon.WorldFrameOf(role));
        var dt = Vector3.Transform(
            Vector3.UnitX, Quaternion.Conjugate(tgtChr) * tgtCanon.WorldFrameOf(role));
        return DegBetween(ds, dt);
    }

    /// <summary>
    /// Asserts the cross-rig direction contract on 5 sample frames: absolute-mode roles match
    /// the source direction within 3°, while delta-mode roles (clavicles, neck — they keep the
    /// target's own rest carriage by design) stay within their rest divergence + 5°.
    /// </summary>
    private static void AssertDirectionContract(SourceScene scene, MappingResult srcMap)
    {
        var rig = SboxRig();
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var (_, _, srcChr) = NormCanon(scene.Skeleton, srcMap);
        var (_, _, tgtChr) = NormCanon(rig.Skeleton, rig.ToMappingResult());

        var maxStrict = 0f;
        var maxDeltaOver = float.MinValue; // worst (error − per-role bound); ≤ 0 passes
        string worstStrict = "", worstDelta = "";
        var bounds = new Dictionary<BoneRole, float>();
        foreach (var f in SampleFrames(clip.FrameCount))
        {
            var srcWorld = Fk(scene.Skeleton, clip.Frames[f]);
            var tgtWorld = Fk(rig.Skeleton, solved.Frames[f]);
            foreach (var (a, b, deg) in DirectionErrors(
                scene.Skeleton, srcMap, srcWorld, srcChr, rig, tgtWorld, tgtChr))
            {
                if (SolveOptions.DefaultTransferModes.ContainsKey(a))
                {
                    if (!bounds.TryGetValue(a, out var bound))
                        bounds[a] = bound = RestDivergenceDeg(a, scene.Skeleton, srcMap, rig) + 5f;
                    if (deg - bound > maxDeltaOver)
                    {
                        maxDeltaOver = deg - bound;
                        worstDelta = $"{a}->{b} @ frame {f}: {deg:F2} deg (bound {bound:F2})";
                    }
                }
                else if (deg > maxStrict)
                {
                    maxStrict = deg;
                    worstStrict = $"{a}->{b} @ frame {f}";
                }
            }
        }
        Assert.True(maxStrict <= 3.0f, $"direction error {maxStrict:F2} deg (worst: {worstStrict})");
        Assert.True(maxDeltaOver <= 0f,
            $"delta-mode direction sanity exceeded rest divergence + 5 deg: {worstDelta}");
    }

    // ================================================================ B. cross-rig direction gate (Mixamo)

    [Fact]
    public void ZombieCrawl_ToSbox_DirectionsMatchWithin3Degrees()
        => AssertDirectionContract(Zombie(), DetectMap(Zombie(), "mixamo"));

    [Fact]
    public void ZombieCrawl_ToSbox_PelvisTravelScalesWithHipRatio()
    {
        var scene = Zombie();
        var srcMap = DetectMap(scene, "mixamo");
        var rig = SboxRig();
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var (_, srcCanon, srcChr) = NormCanon(scene.Skeleton, srcMap);
        var (_, tgtCanon, tgtChr) = NormCanon(rig.Skeleton, rig.ToMappingResult());

        var srcPelvis = srcMap.RoleToBone[BoneRole.Hips];
        var tgtPelvis = rig.BoneForRole(BoneRole.Hips)!.Value;

        Vector3 Horizontal(Vector3 worldTravel, Quaternion chr)
        {
            var v = Vector3.Transform(worldTravel, Quaternion.Conjugate(chr));
            return new Vector3(v.X, v.Y, 0f); // character basis: Z = up
        }

        var srcTravel = Horizontal(
            Fk(scene.Skeleton, clip.Frames[^1])[srcPelvis].Pos
            - Fk(scene.Skeleton, clip.Frames[0])[srcPelvis].Pos, srcChr);
        var tgtTravel = Horizontal(
            Fk(rig.Skeleton, solved.Frames[^1])[tgtPelvis].Pos
            - Fk(rig.Skeleton, solved.Frames[0])[tgtPelvis].Pos, tgtChr);

        Assert.True(srcTravel.Length() > 5f,
            $"fixture sanity: source pelvis should travel (got {srcTravel.Length():F1} cm)");

        var expected = tgtCanon.HipHeight / srcCanon.HipHeight;
        var ratio = tgtTravel.Length() / srcTravel.Length();
        Assert.InRange(ratio / expected, 0.85f, 1.15f);
    }

    // ================================================================ C. ActorCore cross-axis gate (Z-up source)

    [Fact]
    public void ActorCoreCatwalk_ToSbox_DirectionsMatchWithin3Degrees()
        => AssertDirectionContract(ActorCore(), DetectMap(ActorCore(), "actorcore_cc"));

    // ================================================================ D. per-role transfer modes

    [Fact]
    public void DefaultTransferModes_AreClavicleNeckDelta_AndHeadFeetCharacterDelta()
    {
        var modes = SolveOptions.DefaultTransferModes;
        Assert.Equal(RoleTransferMode.DeltaFromRest, modes[BoneRole.ClavicleL]);
        Assert.Equal(RoleTransferMode.DeltaFromRest, modes[BoneRole.ClavicleR]);
        Assert.Equal(RoleTransferMode.DeltaFromRest, modes[BoneRole.Neck]);
        Assert.Equal(RoleTransferMode.CharacterDeltaFromRest, modes[BoneRole.Head]);
        Assert.Equal(RoleTransferMode.CharacterDeltaFromRest, modes[BoneRole.FootL]);
        Assert.Equal(RoleTransferMode.CharacterDeltaFromRest, modes[BoneRole.FootR]);
        // Spine/limbs/toes stay absolute. The head keeps the target's neutral skull
        // attitude (rest neck→head lean is head-joint-placement anatomy, 0–27° across
        // neutral rigs); on a POSED-rest source the solver falls back to absolute gaze
        // matching instead — see TransferModeContractTests.
        Assert.Equal(6, modes.Count);
    }

    /// <summary>
    /// With the source posed at its own rest, a delta-mode role's source delta is identity, so
    /// the target bone must sit at its own normalized rest orientation (its natural carriage)
    /// even though the rigs' rest directions diverge — the defining property of
    /// <see cref="RoleTransferMode.DeltaFromRest"/>. An all-absolute override must instead
    /// adopt the source's rest direction (the legacy behavior).
    /// </summary>
    [Fact]
    public void SourceAtRest_DeltaRoles_KeepTargetRestCarriage()
    {
        var skeleton = Zombie().Skeleton;
        var srcMap = DetectMap(Zombie(), "mixamo");
        var rig = SboxRig();
        var scene = SceneFromWorldFrame(skeleton, skeleton.RestWorld.ToArray(), "rest_frame");

        // Fixture sanity: this source's clavicle line genuinely diverges from the target's.
        var clavDiv = RestDivergenceDeg(BoneRole.ClavicleL, skeleton, srcMap, rig);
        Assert.True(clavDiv > 5f, $"fixture sanity: clavicle rest divergence {clavDiv:F1} deg");

        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());

        // Mixamo rests are unnormalized T-poses for the body (only arms could be swung, and
        // Mixamo is already a T-pose), so the source delta at this frame is identity for the
        // delta roles. RestNormalizer never touches clavicles or the neck regardless.
        var deltaSolved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());
        var absSolved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions
        {
            TransferModes = new Dictionary<BoneRole, RoleTransferMode>(),
        });

        var deltaWorld = Fk(rig.Skeleton, deltaSolved.Frames[0]);
        var absWorld = Fk(rig.Skeleton, absSolved.Frames[0]);
        // Head included: its CharacterDeltaFromRest default shares the defining property
        // (source at rest → target keeps its own neutral skull attitude). Zombie's rest head
        // is neutral, so the posed-rest gaze fallback stays dormant here.
        foreach (var role in new[]
                 {
                     BoneRole.ClavicleL, BoneRole.ClavicleR, BoneRole.Neck, BoneRole.Head,
                 })
        {
            var bone = rig.BoneForRole(role)!.Value;
            var restRot = tgtNorm.WorldRest[bone].Rot;
            var deltaDeg = DegBetween(deltaWorld[bone].Rot, restRot);
            Assert.True(deltaDeg <= 0.5f,
                $"{role}: delta mode should keep the target rest orientation, off {deltaDeg:F2} deg");

            var absDeg = DegBetween(absWorld[bone].Rot, restRot);
            Assert.True(absDeg > 2f,
                $"{role}: all-absolute override should adopt the source rest carriage "
                + $"(only {absDeg:F2} deg from target rest — override not effective?)");
        }

        // Absolute roles are unaffected by the mode map: same output on both solves.
        foreach (var role in new[] { BoneRole.HandR, BoneRole.UpperArmL, BoneRole.Spine0 })
        {
            var bone = rig.BoneForRole(role)!.Value;
            Assert.True(DegBetween(deltaWorld[bone].Rot, absWorld[bone].Rot) <= 1e-3f,
                $"{role} should be identical across mode maps");
        }
    }

    /// <summary>
    /// A toe-less source's foot direction is a virtual character-forward extension; any
    /// direction-matching against it is meaningless (measured: constant ~41° heel-standing
    /// on the makehuman/daz rig under absolute matching). The solver must fall back to
    /// canonical delta for feet in that case: at source rest the target foot keeps its own
    /// rest orientation. The toed default (CharacterDeltaFromRest) shares that property —
    /// the contrast is the explicit all-absolute map, which adopts the source direction.
    /// </summary>
    [Fact]
    public void ToelessSource_FootFallsBackToDelta()
    {
        var skeleton = Zombie().Skeleton;
        var rig = SboxRig();
        var scene = SceneFromWorldFrame(skeleton, skeleton.RestWorld.ToArray(), "rest_frame");

        var toeless = DetectMap(Zombie(), "mixamo");
        toeless.RoleToBone.Remove(BoneRole.ToeL);
        toeless.RoleToBone.Remove(BoneRole.ToeR);

        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        var solved = new GeometricSolver().Solve(scene, toeless, rig, new SolveOptions());
        var world = Fk(rig.Skeleton, solved.Frames[0]);
        foreach (var role in new[] { BoneRole.FootL, BoneRole.FootR })
        {
            var bone = rig.BoneForRole(role)!.Value;
            var deg = DegBetween(world[bone].Rot, tgtNorm.WorldRest[bone].Rot);
            Assert.True(deg <= 0.5f,
                $"{role}: toe-less source should keep the target foot rest carriage, off {deg:F2} deg");
        }

        // Control: under an explicit all-absolute map, the same rest frame drives the foot
        // to the SOURCE's direction, away from the target rest (rigs' foot pitches differ).
        var withToes = DetectMap(Zombie(), "mixamo");
        var absSolved = new GeometricSolver().Solve(scene, withToes, rig, new SolveOptions
        {
            TransferModes = new Dictionary<BoneRole, RoleTransferMode>(),
        });
        var absWorld = Fk(rig.Skeleton, absSolved.Frames[0]);
        var footBone = rig.BoneForRole(BoneRole.FootL)!.Value;
        Assert.True(DegBetween(absWorld[footBone].Rot, tgtNorm.WorldRest[footBone].Rot) > 1.5f,
            "control: explicit absolute mode should adopt the source foot direction");
    }

    /// <summary>
    /// The TransferModes contract (xmldoc on <see cref="SolveOptions.TransferModes"/>): a
    /// NON-NULL map replaces the defaults entirely AND disables every fallback heuristic —
    /// an explicit (or implicit-absent = absolute) foot entry must win over the virtual-foot
    /// delta fallback that null-mode solves apply on toe-less sources
    /// (<see cref="ToelessSource_FootFallsBackToDelta"/> pins the null behavior).
    /// </summary>
    [Fact]
    public void ExplicitTransferModes_DisableVirtualFootFallback()
    {
        var skeleton = Zombie().Skeleton;
        var rig = SboxRig();
        var scene = SceneFromWorldFrame(skeleton, skeleton.RestWorld.ToArray(), "rest_frame");

        var toeless = DetectMap(Zombie(), "mixamo");
        toeless.RoleToBone.Remove(BoneRole.ToeL);
        toeless.RoleToBone.Remove(BoneRole.ToeR);

        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());

        // Explicit EMPTY map = all-absolute, no heuristics: the toe-less source's virtual
        // (character-forward) foot direction is imposed on the target — the foot must leave
        // its target rest carriage, unlike the null-mode fallback which keeps it there.
        var absSolved = new GeometricSolver().Solve(scene, toeless, rig, new SolveOptions
        {
            TransferModes = new Dictionary<BoneRole, RoleTransferMode>(),
        });
        var absWorld2 = Fk(rig.Skeleton, absSolved.Frames[0]);
        foreach (var role in new[] { BoneRole.FootL, BoneRole.FootR })
        {
            var bone = rig.BoneForRole(role)!.Value;
            var deg = DegBetween(absWorld2[bone].Rot, tgtNorm.WorldRest[bone].Rot);
            Assert.True(deg > 1.5f,
                $"{role}: explicit all-absolute map must be honored on a toe-less source "
                + $"(virtual-foot fallback must not override it) — only {deg:F2} deg from target rest");
        }

        // Explicit per-role DELTA entries are honored exactly too (same outcome as the
        // null-mode fallback, but by the caller's explicit choice).
        var deltaSolved = new GeometricSolver().Solve(scene, toeless, rig, new SolveOptions
        {
            TransferModes = new Dictionary<BoneRole, RoleTransferMode>
            {
                [BoneRole.FootL] = RoleTransferMode.DeltaFromRest,
                [BoneRole.FootR] = RoleTransferMode.DeltaFromRest,
            },
        });
        var deltaWorld = Fk(rig.Skeleton, deltaSolved.Frames[0]);
        foreach (var role in new[] { BoneRole.FootL, BoneRole.FootR })
        {
            var bone = rig.BoneForRole(role)!.Value;
            var deg = DegBetween(deltaWorld[bone].Rot, tgtNorm.WorldRest[bone].Rot);
            Assert.True(deg <= 0.5f,
                $"{role}: explicit DeltaFromRest entry should keep the target rest carriage "
                + $"at source rest, off {deg:F2} deg");
        }
    }

    /// <summary>Delta-mode roles still move 1:1 with the source: rotating the source neck by
    /// 20° about the character lateral axis must rotate the target neck by 20° too (the delta
    /// is replayed, only the rest carriage stays the target's own).</summary>
    [Fact]
    public void DeltaMode_ReplaysSourceRotationDelta()
    {
        var skeleton = Zombie().Skeleton;
        var srcMap = DetectMap(Zombie(), "mixamo");
        var rig = SboxRig();

        var (_, srcCanon, _) = NormCanon(skeleton, srcMap);
        var lateral = Vector3.Cross(srcCanon.CharacterUp, srcCanon.CharacterForward);
        var bent = skeleton.RestWorld.ToArray();
        var srcNeck = srcMap.RoleToBone[BoneRole.Neck];
        RotateWorldSubtree(skeleton, bent, srcNeck,
            Quaternion.CreateFromAxisAngle(lateral, 20f / RadToDeg), bent[srcNeck].Pos);

        var restScene = SceneFromWorldFrame(skeleton, skeleton.RestWorld.ToArray(), "rest");
        var bentScene = SceneFromWorldFrame(skeleton, bent, "neck_bend");

        var solver = new GeometricSolver();
        var restOut = Fk(rig.Skeleton, solver.Solve(restScene, srcMap, rig, new SolveOptions()).Frames[0]);
        var bentOut = Fk(rig.Skeleton, solver.Solve(bentScene, srcMap, rig, new SolveOptions()).Frames[0]);

        var tgtNeck = rig.BoneForRole(BoneRole.Neck)!.Value;
        var moved = DegBetween(restOut[tgtNeck].Rot, bentOut[tgtNeck].Rot);
        Assert.True(MathF.Abs(moved - 20f) <= 1.0f,
            $"target neck rotated {moved:F2} deg for a 20 deg source neck bend");
    }

    [Fact]
    public void ActorCoreCatwalk_ToSbox_HandsEndOnCorrectSides()
    {
        var scene = ActorCore();
        var srcMap = DetectMap(scene, "actorcore_cc");
        var rig = SboxRig();
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var (_, srcCanon, _) = NormCanon(scene.Skeleton, srcMap);
        var (_, tgtCanon, _) = NormCanon(rig.Skeleton, rig.ToMappingResult());
        var srcLateral = Vector3.Cross(srcCanon.CharacterUp, srcCanon.CharacterForward);
        var tgtLateral = Vector3.Cross(tgtCanon.CharacterUp, tgtCanon.CharacterForward);

        var srcPelvis = srcMap.RoleToBone[BoneRole.Hips];
        var tgtPelvis = rig.BoneForRole(BoneRole.Hips)!.Value;
        foreach (var f in SampleFrames(clip.FrameCount))
        {
            var srcWorld = Fk(scene.Skeleton, clip.Frames[f]);
            var tgtWorld = Fk(rig.Skeleton, solved.Frames[f]);
            foreach (var (role, name) in new[] { (BoneRole.HandL, "left"), (BoneRole.HandR, "right") })
            {
                var srcSide = Vector3.Dot(
                    srcWorld[srcMap.RoleToBone[role]].Pos - srcWorld[srcPelvis].Pos, srcLateral);
                var tgtSide = Vector3.Dot(
                    tgtWorld[rig.BoneForRole(role)!.Value].Pos - tgtWorld[tgtPelvis].Pos, tgtLateral);
                Assert.True(srcSide * tgtSide > 0f,
                    $"{name} hand crossed sides at frame {f} (src {srcSide:F1}, tgt {tgtSide:F1})");
            }
        }
    }

    // ================================================================ spine interpolation (5 → 3 bones)

    [Fact]
    public void UeMannequinSpine_FiveToThree_InterpolatesBend()
    {
        var skeleton = MappingFixtures.BuildUeMannequin();
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        Assert.Equal("ue_mannequin", detected!.Value.Profile.Name);
        var srcMap = detected.Value.Result;

        // 5 source spine bones must actually be mapped for this to exercise interpolation.
        var srcSpines = new[]
        {
            BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3, BoneRole.Spine4,
        }.Count(r => srcMap.RoleToBone.ContainsKey(r));
        Assert.Equal(5, srcSpines);

        // Build a 1-frame clip bending each spine bone 10 deg forward (about character lateral).
        var (_, canon, _) = NormCanon(skeleton, srcMap);
        var lateral = Vector3.Cross(canon.CharacterUp, canon.CharacterForward);
        var world = skeleton.RestWorld.ToArray();
        foreach (var role in new[]
        {
            BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3, BoneRole.Spine4,
        })
        {
            var bone = srcMap.RoleToBone[role];
            RotateWorldSubtree(skeleton, world, bone,
                Quaternion.CreateFromAxisAngle(lateral, 10f / RadToDeg), world[bone].Pos);
        }
        var scene = SceneFromWorldFrame(skeleton, world, "spine_bend");

        var rig = SboxRig();
        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        // The target spine must bend by the same amount as the source: compare each rig's
        // chord (Spine0 head -> Neck head) bend angle between rest and the solved pose.
        // (The chord's absolute direction is proportion-sensitive — different spine station
        // spacing shifts it a few degrees even when every segment direction interpolates
        // correctly — so the invariant is the bend angle, not the chord itself.)
        var tgtWorld = Fk(rig.Skeleton, solved.Frames[0]);

        Vector3 Chord(IReadOnlyList<XForm> w, int from, int to) => w[to].Pos - w[from].Pos;
        var srcBend = DegBetween(
            Chord(skeleton.RestWorld, srcMap.RoleToBone[BoneRole.Spine0], srcMap.RoleToBone[BoneRole.Neck]),
            Chord(world, srcMap.RoleToBone[BoneRole.Spine0], srcMap.RoleToBone[BoneRole.Neck]));
        var tgtBend = DegBetween(
            Chord(rig.Skeleton.RestWorld, rig.BoneForRole(BoneRole.Spine0)!.Value, rig.BoneForRole(BoneRole.Neck)!.Value),
            Chord(tgtWorld, rig.BoneForRole(BoneRole.Spine0)!.Value, rig.BoneForRole(BoneRole.Neck)!.Value));
        Assert.True(srcBend > 25f, $"fixture sanity: source spine should bend (got {srcBend:F1} deg)");
        Assert.True(MathF.Abs(srcBend - tgtBend) <= 3.0f,
            $"spine bend {tgtBend:F2} deg vs source {srcBend:F2} deg");

        // And every target spine bone actually moved (no rest freeze on the interpolated path).
        foreach (var role in new[] { BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2 })
        {
            var bone = rig.BoneForRole(role)!.Value;
            Assert.True(
                DegBetween(solved.Frames[0][bone].Rot, rig.Skeleton[bone].RestLocal.Rot) > 1f,
                $"{role} did not move");
        }
    }

    // ================================================================ E. determinism + rest preservation

    [Fact]
    public void Solve_IsDeterministic()
    {
        var scene = Zombie();
        var srcMap = DetectMap(scene, "mixamo");
        var rig = SboxRig();

        var a = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());
        var b = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        Assert.Equal(a.FrameCount, b.FrameCount);
        for (var f = 0; f < a.FrameCount; f++)
        {
            for (var i = 0; i < a.Frames[f].Length; i++)
                Assert.Equal(a.Frames[f][i], b.Frames[f][i]);
        }
    }

    [Fact]
    public void Solve_PreservesRest_ForUnmappedAndNonAnimatedBones()
    {
        var scene = Zombie();
        var srcMap = DetectMap(scene, "mixamo");
        var rig = SboxRig();

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var restBones = new List<int>();
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            var role = rig.RoleOf(i);
            // ConstraintDriven/IkBaked, role-less animated bones, and animated bones whose
            // role the Mixamo source does not map (e.g. finger metacarpals) must stay at rest.
            if (rig.ClassOf(i) != BoneClass.Animated
                || role is null
                || !srcMap.RoleToBone.ContainsKey(role.Value))
            {
                restBones.Add(i);
            }
        }
        Assert.True(restBones.Count >= 30, $"only {restBones.Count} rest-pose bones — fixture changed?");

        foreach (var frame in solved.Frames)
        {
            foreach (var i in restBones)
                Assert.Equal(rig.Skeleton[i].RestLocal, frame[i]);
        }
    }

    // ---------------------------------------------------------------- frame-construction helpers (shared)

    /// <summary>Hierarchically rotates the world rest of <paramref name="root"/> and its
    /// descendants about a pivot (positions orbit, orientations premultiplied).</summary>
    internal static void RotateWorldSubtree(Skel skeleton, XForm[] world, int root, Quaternion q, Vector3 pivot)
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

    /// <summary>Wraps a world-space pose as a 1-frame SourceScene clip (locals derived
    /// against the skeleton hierarchy).</summary>
    internal static SourceScene SceneFromWorldFrame(Skel skeleton, XForm[] world, string clipName)
    {
        var locals = new XForm[skeleton.Count];
        for (var i = 0; i < skeleton.Count; i++)
        {
            var parent = skeleton[i].ParentIndex;
            locals[i] = parent < 0 ? world[i] : XForm.ToLocal(world[parent], world[i]);
        }
        var clip = new Clip(clipName, 30f, false);
        clip.Frames.Add(locals);
        return new SourceScene(skeleton, new[] { clip }, 1f);
    }
}
