using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Mapping;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

public class FingerSolverTests
{
    private const float RadToDeg = GeometricSolverTests.RadToDeg;

    private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
    private static readonly string[] Segments = { "Meta", "Prox", "Mid", "Dist" };

    private static IEnumerable<BoneRole> AllFingerRoles()
    {
        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in Fingers)
            {
                foreach (var segment in Segments)
                    yield return Enum.Parse<BoneRole>(finger + segment + side);
            }
        }
    }

    // ================================================================ D1. round-trip fingers

    [Fact]
    public void CitizenRoundTrip_FingerWorldRotations_Within1Degree()
    {
        var scene = GeometricSolverTests.Citizen();
        var rig = GeometricSolverTests.RigOverSkeleton(scene.Skeleton, "citizen_roundtrip");
        var srcMap = GeometricSolverTests.RoleMapByName(scene.Skeleton, rig);
        var clip = scene.Clips[0];

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var mapped = AllFingerRoles()
            .Where(r => srcMap.RoleToBone.ContainsKey(r) && rig.BoneForRole(r) is not null)
            .Select(r => (Src: srcMap.RoleToBone[r], Tgt: rig.BoneForRole(r)!.Value, Role: r))
            .ToList();
        Assert.True(mapped.Count >= 20, $"only {mapped.Count} finger bones mapped in the round-trip");

        var max = 0f;
        var worst = "";
        for (var f = 0; f < clip.FrameCount; f++)
        {
            var srcWorld = GeometricSolverTests.Fk(scene.Skeleton, clip.Frames[f]);
            var outWorld = GeometricSolverTests.Fk(rig.Skeleton, solved.Frames[f]);
            foreach (var (s, t, role) in mapped)
            {
                var deg = GeometricSolverTests.DegBetween(srcWorld[s].Rot, outWorld[t].Rot);
                if (deg > max)
                {
                    max = deg;
                    worst = $"{role} @ frame {f}";
                }
            }
        }
        Assert.True(max <= 1.0f, $"finger round-trip error {max:F3} deg (worst: {worst})");
    }

    // ================================================================ D2. synthetic fist (redistribution path)

    [Fact]
    public void SyntheticFist_CurlsTargetFingers_WithoutTwistDrift()
    {
        // Source: Zombie Crawl rest with every finger phalanx curled +60 deg about ITS
        // canonical hinge (hierarchically, prox -> mid -> dist, hinge transported with the
        // accumulated rotation). Mixamo fingers are Prox/Mid/Dist; the s&box target adds a
        // metacarpal, so this exercises the FingerSolver redistribution path.
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        var srcMap = detected!.Value.Result;

        var canon = CanonicalFrames.Build(skeleton, srcMap); // bind rest (animation space)
        var world = skeleton.RestWorld.ToArray();
        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in Fingers)
            {
                foreach (var segment in new[] { "Prox", "Mid", "Dist" })
                {
                    var role = Enum.Parse<BoneRole>(finger + segment + side);
                    if (!srcMap.RoleToBone.TryGetValue(role, out var bone) || !canon.Has(role))
                        continue;

                    var restHinge = Vector3.Transform(Vector3.UnitY, canon.WorldFrameOf(role));
                    var deltaSoFar = MathQ.Normalize(
                        world[bone].Rot * Quaternion.Conjugate(skeleton.RestWorld[bone].Rot));
                    var hinge = Vector3.Transform(restHinge, deltaSoFar);
                    GeometricSolverTests.RotateWorldSubtree(
                        skeleton, world, bone,
                        Quaternion.CreateFromAxisAngle(hinge, 60f / RadToDeg), world[bone].Pos);
                }
            }
        }
        // Frame 0 = source rest (the drift baseline), frame 1 = the fist.
        var scene = GeometricSolverTests.SceneFromWorldFrame(skeleton, world, "fist");
        var restLocals = new XForm[skeleton.Count];
        for (var i = 0; i < skeleton.Count; i++)
            restLocals[i] = skeleton[i].RestLocal;
        scene.Clips[0].Frames.Insert(0, restLocals);

        var rig = GeometricSolverTests.SboxRig();
        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var tgtRest = rig.Skeleton.RestWorld.ToArray();
        var tgtBase = GeometricSolverTests.Fk(rig.Skeleton, solved.Frames[0]);
        var tgtPose = GeometricSolverTests.Fk(rig.Skeleton, solved.Frames[1]);

        // Fingertip-to-palm-center distance must shrink by >= 35% on both hands
        // (averaged over the four non-thumb fingers). The fingertip is the distal head
        // extrapolated by 0.8x the mid->dist rest segment (carried in distal bone space, so
        // the distal phalanx's own curl moves it).
        foreach (var side in new[] { "L", "R" })
        {
            Vector3 PalmCenter(IReadOnlyList<XForm> w)
            {
                var hand = w[rig.BoneForRole(GeometricSolverTests.R("Hand", side))!.Value].Pos;
                var knuckles = Vector3.Zero;
                var count = 0;
                foreach (var finger in Fingers)
                {
                    if (rig.BoneForRole(GeometricSolverTests.R(finger + "Prox", side)) is int b)
                    {
                        knuckles += w[b].Pos;
                        count++;
                    }
                }
                return (hand + knuckles / count) * 0.5f;
            }

            var restPalm = PalmCenter(tgtRest);
            var posePalm = PalmCenter(tgtPose);
            var shrinkSum = 0f;
            var fingerCount = 0;
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                var mid = rig.BoneForRole(GeometricSolverTests.R(finger + "Mid", side))!.Value;
                var dist = rig.BoneForRole(GeometricSolverTests.R(finger + "Dist", side))!.Value;
                var segment = tgtRest[dist].Pos - tgtRest[mid].Pos;
                var tipLocal = tgtRest[dist].Inverse().TransformPoint(tgtRest[dist].Pos + 0.8f * segment);

                var restLen = (tgtRest[dist].TransformPoint(tipLocal) - restPalm).Length();
                var poseLen = (tgtPose[dist].TransformPoint(tipLocal) - posePalm).Length();
                shrinkSum += 1f - poseLen / restLen;
                fingerCount++;
            }
            var shrink = shrinkSum / fingerCount;
            Assert.True(shrink >= 0.35f,
                $"hand {side}: fingertips only moved {shrink:P0} closer to the palm");
        }

        // Twist drift: curling must not introduce axial twist. Measured as the change of the
        // finger-axis (canonical X) twist of each phalanx's local rotation (relative to its
        // chain predecessor) between the rest-baseline frame and the fist frame — the static
        // cross-rig roll re-pose (e.g. the thumb's canonical roll differs ~12 deg between
        // Mixamo and s&box anatomy) is a constant and must not count as drift.
        var tgtMap = rig.ToMappingResult();
        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, tgtMap);
        var tgtCanon = CanonicalFrames.Build(rig.Skeleton, tgtMap, tgtNorm.WorldRest);

        float TwistOf(IReadOnlyList<XForm> pose, BoneRole prevRole, BoneRole role)
        {
            Quaternion DeltaOf(BoneRole r)
            {
                var bone = rig.BoneForRole(r)!.Value;
                return MathQ.Normalize(pose[bone].Rot * Quaternion.Conjugate(tgtNorm.WorldRest[bone].Rot));
            }

            var local = MathQ.Normalize(Quaternion.Conjugate(DeltaOf(prevRole)) * DeltaOf(role));
            var c = tgtCanon.WorldFrameOf(role);
            var canonLocal = MathQ.Normalize(Quaternion.Conjugate(c) * local * c);
            MathQ.SwingTwist(canonLocal, Vector3.UnitX, out _, out var twist);
            var s = twist.X; // axis = UnitX
            var angle = 2f * MathF.Atan2(s, twist.W);
            if (angle > MathF.PI)
                angle -= 2f * MathF.PI;
            else if (angle < -MathF.PI)
                angle += 2f * MathF.PI;
            return angle * RadToDeg;
        }

        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in Fingers)
            {
                var prevRole = GeometricSolverTests.R("Hand", side);
                foreach (var segment in new[] { "Prox", "Mid", "Dist" })
                {
                    var role = GeometricSolverTests.R(finger + segment, side);
                    if (rig.BoneForRole(role) is null || !tgtCanon.Has(role)
                        || !srcMap.RoleToBone.ContainsKey(role))
                    {
                        continue;
                    }

                    var drift = MathF.Abs(TwistOf(tgtPose, prevRole, role) - TwistOf(tgtBase, prevRole, role));
                    Assert.True(drift < 8f, $"{role}: twist drift {drift:F2} deg");
                    prevRole = role;
                }
            }
        }
    }

    // ================================================================ D3. proportional redistribution (count mismatch)

    [Fact]
    public void SyntheticFist_TwoPhalanxSource_RedistributesCurlOverThreeTargetPhalanges()
    {
        // Strip the distal phalanges from the source mapping: a 2-phalanx source finger onto
        // the 3-phalanx target exercises the proportional total-curl redistribution.
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var detected = ProfileDetector.Detect(skeleton);
        Assert.NotNull(detected);
        var full = detected!.Value.Result;
        var srcMap = new MappingResult(full.ProfileName, full.Source) { Confidence = full.Confidence };
        foreach (var (role, bone) in full.RoleToBone)
        {
            if (!role.ToString().Contains("Dist"))
                srcMap.RoleToBone[role] = bone;
        }

        // Curl the remaining prox+mid phalanges +60 deg about their canonical hinges.
        var canon = CanonicalFrames.Build(skeleton, srcMap);
        var world = skeleton.RestWorld.ToArray();
        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in Fingers)
            {
                foreach (var segment in new[] { "Prox", "Mid" })
                {
                    var role = GeometricSolverTests.R(finger + segment, side);
                    if (!srcMap.RoleToBone.TryGetValue(role, out var bone) || !canon.Has(role))
                        continue;
                    var restHinge = Vector3.Transform(Vector3.UnitY, canon.WorldFrameOf(role));
                    var deltaSoFar = MathQ.Normalize(
                        world[bone].Rot * Quaternion.Conjugate(skeleton.RestWorld[bone].Rot));
                    GeometricSolverTests.RotateWorldSubtree(
                        skeleton, world, bone,
                        Quaternion.CreateFromAxisAngle(Vector3.Transform(restHinge, deltaSoFar), 60f / RadToDeg),
                        world[bone].Pos);
                }
            }
        }
        var scene = GeometricSolverTests.SceneFromWorldFrame(skeleton, world, "fist2");

        var rig = GeometricSolverTests.SboxRig();
        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        // The 120 deg of chain curl must spread over all THREE target phalanges: every
        // phalanx receives a meaningful share (local rotation well away from rest), and the
        // fingertips close toward the palm.
        var tgtRest = rig.Skeleton.RestWorld.ToArray();
        var tgtPose = GeometricSolverTests.Fk(rig.Skeleton, solved.Frames[0]);
        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                foreach (var segment in new[] { "Prox", "Mid", "Dist" })
                {
                    var bone = rig.BoneForRole(GeometricSolverTests.R(finger + segment, side))!.Value;
                    var localDeg = GeometricSolverTests.DegBetween(
                        solved.Frames[0][bone].Rot, rig.Skeleton[bone].RestLocal.Rot);
                    Assert.True(localDeg > 10f,
                        $"{finger}{segment}{side}: only {localDeg:F1} deg of local curl received");
                }

                var hand = tgtPose[rig.BoneForRole(GeometricSolverTests.R("Hand", side))!.Value].Pos;
                var handRest = tgtRest[rig.BoneForRole(GeometricSolverTests.R("Hand", side))!.Value].Pos;
                var dist = rig.BoneForRole(GeometricSolverTests.R(finger + "Dist", side))!.Value;
                var restLen = (tgtRest[dist].Pos - handRest).Length();
                var poseLen = (tgtPose[dist].Pos - hand).Length();
                Assert.True(poseLen < restLen * 0.85f,
                    $"{finger}{side}: distal did not close toward the hand ({poseLen:F2} vs rest {restLen:F2} cm)");
            }
        }
    }

    // ================================================================ missing fingers + opt-out

    [Fact]
    public void SourceWithoutFingerRoles_LeavesTargetFingersAtRest()
    {
        var scene = GeometricSolverTests.Zombie();
        var full = GeometricSolverTests.DetectMap(scene, "mixamo");
        var rig = GeometricSolverTests.SboxRig();

        // Strip every finger role from the source mapping (a fingerless mocap rig).
        var srcMap = new MappingResult(full.ProfileName, full.Source) { Confidence = full.Confidence };
        var fingerRoles = AllFingerRoles().ToHashSet();
        foreach (var (role, bone) in full.RoleToBone)
        {
            if (!fingerRoles.Contains(role))
                srcMap.RoleToBone[role] = bone;
        }

        var solved = new GeometricSolver().Solve(scene, srcMap, rig, new SolveOptions());

        var fingerBones = AllFingerRoles()
            .Select(rig.BoneForRole)
            .Where(b => b is not null)
            .Select(b => b!.Value)
            .ToList();
        Assert.True(fingerBones.Count >= 30);

        foreach (var frame in solved.Frames)
        {
            foreach (var bone in fingerBones)
                Assert.Equal(rig.Skeleton[bone].RestLocal, frame[bone]);
        }
    }

    [Fact]
    public void TransferFingersFalse_LeavesTargetFingersAtRest()
    {
        var scene = GeometricSolverTests.Zombie();
        var srcMap = GeometricSolverTests.DetectMap(scene, "mixamo");
        var rig = GeometricSolverTests.SboxRig();

        var solved = new GeometricSolver().Solve(
            scene, srcMap, rig, new SolveOptions { TransferFingers = false });

        foreach (var frame in solved.Frames)
        {
            foreach (var role in AllFingerRoles())
            {
                if (rig.BoneForRole(role) is int bone)
                    Assert.Equal(rig.Skeleton[bone].RestLocal, frame[bone]);
            }
        }
    }
}
