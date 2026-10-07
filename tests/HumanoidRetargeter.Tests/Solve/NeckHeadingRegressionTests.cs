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
/// Regression gate for the heading-reflected NECK lateral tilt found on Armchair1.bvh
/// ("the neck is slightly pointed towards the left, sticking out").
/// <para><b>The defect:</b> the Neck default (<see cref="RoleTransferMode.DeltaFromRest"/>)
/// constant <c>ct·cs⁻¹</c> factors exactly into the pure-pitch carriage divergence D between
/// the rigs' rest neck→head leans (24.90° on the rokoko-class Armchair1 rig vs the citizen,
/// 0.00° frame-construction residue) times a residue K, and the old product re-applied D
/// about the REST heading's lateral axis. A clip whose body heading is yawed λ away from the
/// rest heading reads that pitch as heading-relative LATERAL tilt ≈ D·sin λ — Armchair1 sits
/// facing ~125° off its rest heading, measured mean +13.3° (worst +85.7°) lateral
/// neck-segment residual, correlation 0.87 with D·sin λ. Pre-existing before the 2026-07
/// head-carriage wave (identical numbers at 55b6223). Fixed in GeometricSolver.TryAddDirect
/// by transporting D with the source HIPS' carried yaw (body heading — the neck's rest lean
/// is torso anatomy; the neck's own yaw over-rotates the tilt axis when the head looks
/// off-body), the same machinery as the head's carriage-divergence transport.</para>
/// <para><b>The metric</b> (HeadAudit's "LAT seg resid"): the world neck→head segment
/// direction decomposed laterally against the PER-FRAME hips heading, target minus source.
/// A fixed-axes lateral lean would fabricate errors whenever the character faces away from
/// rest; only the heading-relative residual separates artifact from defect.</para>
/// Local-only (corpus files are gitignored): every test silently passes when absent.
/// </summary>
public class NeckHeadingRegressionTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    /// <summary>Pre-fix: signed mean +13.34°, |mean| 18.30° (seated facing ~125° off the
    /// rest heading — the persistent leftward neck tilt). Post-fix measured: signed mean
    /// +0.52°, |mean| 7.74° (the |mean| is dominated by a ~35-frame full-forward-bend
    /// cluster where the segment drops below horizontal and the lateral angle degenerates).</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Armchair1_NeckSegment_NoHeadingLateralTilt()
        => AssertNeckLateralResidual(
            RepoFile("dev", "corpus", "todo", "Armchair1.bvh"), signedMeanTolDeg: 3f, absMeanTolDeg: 10f);

    /// <summary>Pre-fix: signed mean +7.73°, |mean| 9.24° (tumbling clip with turned
    /// segments). Post-fix measured: signed mean +1.98°, |mean| 3.63°.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Cmu_01_14_NeckSegment_NoHeadingLateralTilt()
        => AssertNeckLateralResidual(
            RepoFile("dev", "corpus", "bvh", "cmu_01_14.bvh"), signedMeanTolDeg: 4f, absMeanTolDeg: 6f);

    private static void AssertNeckLateralResidual(string path, float signedMeanTolDeg, float absMeanTolDeg)
    {
        if (!File.Exists(path))
            return;
        var fileName = System.IO.Path.GetFileName(path);

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

        var src = new NeckSeries(scene.Skeleton, map, srcNorm.WorldRest,
            srcCanon.CharacterForward, srcCanon.CharacterUp);
        var tgt = new NeckSeries(rig.Skeleton, tgtMap, tgtNorm.WorldRest,
            tgtCanon.CharacterForward, tgtCanon.CharacterUp);

        var sum = 0f;
        var sumAbs = 0f;
        for (var f = 0; f < n; f++)
        {
            var resid = NormDeg(tgt.SegmentLateral(tgtFrames[f]) - src.SegmentLateral(srcFrames[f]));
            sum += resid;
            sumAbs += MathF.Abs(resid);
        }
        var mean = sum / n;
        var meanAbs = sumAbs / n;
        Assert.True(MathF.Abs(mean) <= signedMeanTolDeg,
            $"{fileName}: heading-relative neck-segment lateral residual mean {mean:F2} deg "
            + $"(|mean| {meanAbs:F2}) exceeds ±{signedMeanTolDeg} deg — the neck carriage "
            + "divergence is reading as a persistent sideways tilt at turned facings again");
        Assert.True(meanAbs <= absMeanTolDeg,
            $"{fileName}: heading-relative neck-segment lateral residual |mean| {meanAbs:F2} deg "
            + $"exceeds {absMeanTolDeg} deg");
    }

    /// <summary>Heading-relative neck→head segment lateral evaluator for one skeleton
    /// (HeadAudit's "LAT seg resid" metric).</summary>
    private sealed class NeckSeries
    {
        private readonly SkeletonModel _skel;
        private readonly int _neck;
        private readonly int _head;
        private readonly int _hips;
        private readonly Quaternion _restHipsRotInv;
        private readonly Vector3 _fwd;
        private readonly Vector3 _up;

        public NeckSeries(
            SkeletonModel skel, MappingResult map, IReadOnlyList<XForm> normRest,
            Vector3 fwd, Vector3 up)
        {
            _skel = skel;
            _fwd = fwd;
            _up = up;
            Assert.True(map.RoleToBone.TryGetValue(BoneRole.Neck, out _neck), "Neck unmapped");
            Assert.True(map.RoleToBone.TryGetValue(BoneRole.Head, out _head), "Head unmapped");
            Assert.True(map.RoleToBone.TryGetValue(BoneRole.Hips, out _hips), "Hips unmapped");
            _restHipsRotInv = Quaternion.Conjugate(normRest[_hips].Rot);
        }

        /// <summary>Lateral lean of the world neck→head segment against the per-frame hips
        /// heading (flattened to the horizontal plane), in degrees.</summary>
        public float SegmentLateral(XForm[] locals)
        {
            var world = Fk(_skel, locals);
            var seg = world[_head].Pos - world[_neck].Pos;
            var hdg = Vector3.Transform(
                _fwd, MathQ.Normalize(world[_hips].Rot * _restHipsRotInv));
            hdg -= _up * Vector3.Dot(hdg, _up);
            hdg = hdg.LengthSquared() < 1e-8f ? _fwd : Vector3.Normalize(hdg);
            var lat = Vector3.Normalize(Vector3.Cross(_up, hdg));
            return MathF.Atan2(Vector3.Dot(seg, lat), Vector3.Dot(seg, _up)) * Rad2Deg;
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
