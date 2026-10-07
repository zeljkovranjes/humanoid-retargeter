using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// Regression gate for the heading-reflected head-pitch defect found by the 2026-07-01
/// head-alignment audit (dev/verification/quality_head_audit/AUDIT.md).
/// <para><b>The defect:</b> the Head default (<see cref="RoleTransferMode.CharacterDeltaFromRest"/>)
/// replayed the source head's world ΔR with the constant source↔target head-carriage
/// divergence (the rigs' differing neutral neck→head lean, ~13.4° on rokoko-class BVH rigs
/// vs the citizen) baked in the REST heading's frame. When a clip holds a pitch while the
/// character faces away from its rest heading, the divergence counter-rotates and reflects
/// into a real pitch error of up to 2× the divergence (measured −26.6° chin-up plateaus on
/// cmu_01_14; −12.7° on turned figure-8 walk segments). Fixed in GeometricSolver.TryAddDirect
/// by transporting the divergence with the body (conjugation by the hips ΔR mapped to the
/// target side) instead of leaving it constant.</para>
/// <para><b>The metric</b> (ported from the audit's HeadAudit tool): the carried head
/// direction (rest neck→head direction rotated by the head's world ΔR) decomposed against
/// the PER-FRAME hips heading — the fixed-axes lean of a properly carried head flips sign
/// at a 180° turn and fabricates errors on correct output, so only this heading-relative
/// residual separates artifact from defect.</para>
/// Local-only (corpus files are gitignored): every test silently passes when absent.
/// </summary>
public class HeadHeadingRegressionTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    /// <summary>Pre-fix heading-relative residual: |mean| 24.3°, max 50.9° (turned figure-8
    /// walk — the character walks segments facing ~180° from its rest heading with a level
    /// head). Post-fix measured: |mean| 0.34°, max 1.36°.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Pymo8Walk_HeadPitch_NoHeadingReflection()
        => AssertHeadingResidual("pymo_AV_8Walk_Meredith_HVHA_Rep1.bvh", meanTolDeg: 2f, maxTolDeg: 5f);

    /// <summary>Pre-fix: |mean| 8.9°, max 26.8° (−11.4° transient at a turned segment).
    /// Post-fix measured: |mean| 0.24°, max 1.99°.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Cmu_01_01_HeadPitch_NoHeadingReflection()
        => AssertHeadingResidual("cmu_01_01.bvh", meanTolDeg: 2f, maxTolDeg: 5f);

    private static void AssertHeadingResidual(string fileName, float meanTolDeg, float maxTolDeg)
    {
        var path = RepoFile("dev", "corpus", "bvh", fileName);
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var scene = Retargeter.ImportSource(bytes, fileName);
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));
        var rig = target.Rig;

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = fileName,
            MappingOverride = map,
        }, target);
        var clip = result.Clips.First(c => c.Success);
        var tgtFrames = clip.SolvedFrames!;
        var srcFrames = scene.Clips[0].Frames;
        var n = Math.Min(srcFrames.Count, tgtFrames.Count);
        Assert.True(n > 10, $"{fileName}: too few frames ({n}) for a meaningful series");

        var tgtMap = rig.ToMappingResult();
        var (srcNorm, _) = RestNormalizer.Normalize(scene.Skeleton, map, srcFrames[0]);
        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, tgtMap);
        var srcCanon = CanonicalFrames.Build(scene.Skeleton, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(rig.Skeleton, tgtMap, tgtNorm.WorldRest);

        var src = new HeadSeries(scene.Skeleton, map, srcNorm.WorldRest,
            srcCanon.CharacterForward, srcCanon.CharacterUp);
        var tgt = new HeadSeries(rig.Skeleton, tgtMap, tgtNorm.WorldRest,
            tgtCanon.CharacterForward, tgtCanon.CharacterUp);
        var expectedOffset = tgt.RestLean - src.RestLean;

        var sumAbs = 0f;
        var maxAbs = 0f;
        for (var f = 0; f < n; f++)
        {
            var resid = NormDeg(tgt.HeadingLean(tgtFrames[f]) - src.HeadingLean(srcFrames[f]) - expectedOffset);
            sumAbs += MathF.Abs(resid);
            maxAbs = MathF.Max(maxAbs, MathF.Abs(resid));
        }
        var meanAbs = sumAbs / n;
        Assert.True(meanAbs <= meanTolDeg,
            $"{fileName}: heading-relative head-lean residual |mean| {meanAbs:F2} deg "
            + $"(max {maxAbs:F2}) exceeds {meanTolDeg} deg — the carriage divergence is "
            + "reflecting at turned facings again");
        Assert.True(maxAbs <= maxTolDeg,
            $"{fileName}: heading-relative head-lean residual max {maxAbs:F2} deg exceeds {maxTolDeg} deg");
    }

    /// <summary>Heading-relative head-lean evaluator for one skeleton (audit-tool metric).</summary>
    private sealed class HeadSeries
    {
        private readonly SkeletonModel _skel;
        private readonly int _head;
        private readonly int _hips;
        private readonly Vector3 _restDir;
        private readonly Quaternion _restHeadRotInv;
        private readonly Quaternion _restHipsRotInv;
        private readonly Vector3 _fwd;
        private readonly Vector3 _up;

        public float RestLean { get; }

        public HeadSeries(
            SkeletonModel skel, MappingResult map, IReadOnlyList<XForm> normRest,
            Vector3 fwd, Vector3 up)
        {
            _skel = skel;
            _fwd = fwd;
            _up = up;
            Assert.True(map.RoleToBone.TryGetValue(BoneRole.Head, out _head), "Head unmapped");
            Assert.True(map.RoleToBone.TryGetValue(BoneRole.Hips, out _hips), "Hips unmapped");
            // Neck→head rest direction; neck-less rigs fall back to the head's parent
            // (the audit's zero-length-neck / neck-less edge cases resolve the same way).
            var neck = map.RoleToBone.TryGetValue(BoneRole.Neck, out var neckBone)
                ? neckBone
                : _skel[_head].ParentIndex;
            var restDir = normRest[_head].Pos - normRest[neck].Pos;
            if (restDir.LengthSquared() < 1e-10f && _skel[neck].ParentIndex >= 0)
                restDir = normRest[_head].Pos - normRest[_skel[neck].ParentIndex].Pos;
            _restDir = restDir;
            _restHeadRotInv = Quaternion.Conjugate(normRest[_head].Rot);
            _restHipsRotInv = Quaternion.Conjugate(normRest[_hips].Rot);
            RestLean = MathF.Atan2(Vector3.Dot(restDir, fwd), Vector3.Dot(restDir, up)) * Rad2Deg;
        }

        /// <summary>Forward lean of the carried head direction against the per-frame hips
        /// heading (flattened to the horizontal plane), in degrees.</summary>
        public float HeadingLean(XForm[] locals)
        {
            var world = Fk(_skel, locals);
            var dir = Vector3.Transform(
                _restDir, MathQ.Normalize(world[_head].Rot * _restHeadRotInv));
            var hdg = Vector3.Transform(
                _fwd, MathQ.Normalize(world[_hips].Rot * _restHipsRotInv));
            hdg -= _up * Vector3.Dot(hdg, _up);
            hdg = hdg.LengthSquared() < 1e-8f ? _fwd : Vector3.Normalize(hdg);
            return MathF.Atan2(Vector3.Dot(dir, hdg), Vector3.Dot(dir, _up)) * Rad2Deg;
        }
    }

    private static float NormDeg(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }

    private static XForm[] Fk(SkeletonModel skel, XForm[] locals)
    {
        var world = new XForm[skel.Count];
        for (var i = 0; i < skel.Count; i++)
        {
            var p = skel[i].ParentIndex;
            world[i] = p < 0 ? locals[i] : XForm.Compose(world[p], locals[i]);
        }
        return world;
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return System.IO.Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
