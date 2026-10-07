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
/// Local-only quality gate against the REAL user-reported repro in <c>dev/corpus/todo/</c>
/// (not committed; every test silently passes when the file is absent). User report:
/// "nvidia soma fingers are not correct. i dont think they get animated."
/// <para><b>Root cause:</b> SOMA fingers have FOUR segments per finger where segment 1 is a
/// metacarpal (<c>LeftHandIndex1</c> sits 3.2 cm from the wrist at the palm base, then a
/// 6.4 cm metacarpal to the <c>Index2</c> knuckle, then 3.7 / 2.3 cm phalanges). The
/// soma_bvh preset mapped segments 1..3 as Prox/Mid/Dist (mixamo's 3-phalanx segmentation),
/// so every curl was shifted one joint outward — the citizen proximal tracked the (nearly
/// straight) metacarpal — and segment 4's curl was dropped entirely. Fixed by mapping
/// 1→Meta, 2→Prox, 3→Mid, 4→Dist for the four fingers (thumb stays 1..3).</para>
/// <para><b>Data note (measured; verified independently with a Blender BVH import):</b> in
/// this clip every finger channel is CONSTANT across all frames — the source hands hold a
/// fixed relaxed grip while the arms throw. "Fingers get animated" therefore means: the
/// citizen phalanges must track the source phalanx directions frame by frame (they swing
/// with the hands in world space and hold the source's curl), NOT that their local
/// rotations vary over time — a local-rotation-variance assertion would be dishonest here,
/// and <see cref="SomaThrow_Fingers_DoNotWiggle_WhenSourceHoldsStill"/> pins the opposite:
/// static source fingers must produce static output finger locals.</para>
/// </summary>
public class SomaFingerReproTests
{
    private const string TodoFile = "Neutral_throw_ball_001__A057.bvh";
    private const float Rad2Deg = 180f / MathF.PI;

    private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaPreset_MapsFourSegmentFingers_MetaPlusPhalanges()
    {
        if (Load() is not var (scene, map, _))
            return;

        Assert.Equal("soma_bvh", map.ProfileName);
        var skel = scene.Skeleton;
        foreach (var (roleSide, nameSide) in new[] { ("L", "Left"), ("R", "Right") })
        {
            // Thumb: three segments, mapped 1..3 (SOMA's Thumb1 is the thumb metacarpal —
            // the conventional Prox for 3-segment thumbs, same policy as mixamo).
            AssertRole(skel, map, Role("ThumbProx", roleSide), $"{nameSide}HandThumb1");
            AssertRole(skel, map, Role("ThumbMid", roleSide), $"{nameSide}HandThumb2");
            AssertRole(skel, map, Role("ThumbDist", roleSide), $"{nameSide}HandThumb3");

            // Four fingers: segment 1 is the metacarpal, 2/3/4 the phalanges. Mapping 1..3
            // as the phalanges (the old bug) froze the fingers.
            foreach (var finger in Fingers)
            {
                AssertRole(skel, map, Role($"{finger}Meta", roleSide), $"{nameSide}Hand{finger}1");
                AssertRole(skel, map, Role($"{finger}Prox", roleSide), $"{nameSide}Hand{finger}2");
                AssertRole(skel, map, Role($"{finger}Mid", roleSide), $"{nameSide}Hand{finger}3");
                AssertRole(skel, map, Role($"{finger}Dist", roleSide), $"{nameSide}Hand{finger}4");
            }
        }
    }

    /// <summary>End-to-end (full <see cref="Retargeter.Convert"/> pipeline): every citizen
    /// proximal and middle phalanx segment must match the source's TRUE anatomical phalanx
    /// direction — resolved by source joint NAME, independent of the mapping under test —
    /// in character space, on every frame. With the old off-by-one mapping the citizen
    /// proximal tracked the metacarpal instead of the proximal phalanx (mean error = the
    /// knuckle bend, 6–25° on this clip); the direction matcher is exact once the
    /// segmentation is right.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaThrow_FingerPhalanxDirections_MatchSource_Within3Degrees()
    {
        if (Load() is not var (scene, map, target))
            return;

        var (rig, frames, srcChrInv, tgtChrInv) = Solve(scene, map, target);
        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;

        foreach (var (srcSide, tgtSide) in new[] { ("Left", "L"), ("Right", "R") })
        {
            // (source segment head/tail joint, citizen segment head/tail bone) per phalanx.
            var segments = new List<(string Label, int Sa, int Sb, int Ta, int Tb)>
            {
                ("thumb prox", SrcBone(srcSkel, $"{srcSide}HandThumb1"), SrcBone(srcSkel, $"{srcSide}HandThumb2"),
                    TgtBone(tgtSkel, $"finger_thumb_0_{tgtSide}"), TgtBone(tgtSkel, $"finger_thumb_1_{tgtSide}")),
                ("thumb mid", SrcBone(srcSkel, $"{srcSide}HandThumb2"), SrcBone(srcSkel, $"{srcSide}HandThumb3"),
                    TgtBone(tgtSkel, $"finger_thumb_1_{tgtSide}"), TgtBone(tgtSkel, $"finger_thumb_2_{tgtSide}")),
            };
            foreach (var finger in Fingers)
            {
                var f = finger.ToLowerInvariant();
                segments.Add(($"{f} prox",
                    SrcBone(srcSkel, $"{srcSide}Hand{finger}2"), SrcBone(srcSkel, $"{srcSide}Hand{finger}3"),
                    TgtBone(tgtSkel, $"finger_{f}_0_{tgtSide}"), TgtBone(tgtSkel, $"finger_{f}_1_{tgtSide}")));
                segments.Add(($"{f} mid",
                    SrcBone(srcSkel, $"{srcSide}Hand{finger}3"), SrcBone(srcSkel, $"{srcSide}Hand{finger}4"),
                    TgtBone(tgtSkel, $"finger_{f}_1_{tgtSide}"), TgtBone(tgtSkel, $"finger_{f}_2_{tgtSide}")));
            }

            foreach (var (label, sa, sb, ta, tb) in segments)
            {
                var sum = 0f;
                var max = 0f;
                for (var f = 0; f < frames.Count; f++)
                {
                    var srcWorld = Fk(srcSkel, scene.Clips[0].Frames[f]);
                    var tgtWorld = Fk(tgtSkel, frames[f]);
                    var ds = Vector3.Transform(
                        Vector3.Normalize(srcWorld[sb].Pos - srcWorld[sa].Pos), srcChrInv);
                    var dt = Vector3.Transform(
                        Vector3.Normalize(tgtWorld[tb].Pos - tgtWorld[ta].Pos), tgtChrInv);
                    var deg = MathQ.AngleBetween(ds, dt) * Rad2Deg;
                    sum += deg;
                    max = MathF.Max(max, deg);
                }
                var mean = sum / frames.Count;
                Assert.True(mean <= 3f,
                    $"{tgtSide} {label}: mean direction error {mean:F2} deg (max {max:F2})");
            }
        }
    }

    /// <summary>The source hands hold a curled grip (constant, non-zero finger channels);
    /// the citizen fingers must adopt that pose instead of staying frozen at the citizen
    /// rest (the reported symptom). Every finger chain must have at least one phalanx
    /// clearly off its rest local (measured 6.3–31.6° per chain after the fix; the frozen
    /// bug read 0° on the distal chain links and near-0 curl overall).</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaThrow_FingersAdoptHeldCurl_NotTargetRest()
    {
        if (Load() is not var (scene, map, target))
            return;

        var (rig, frames, _, _) = Solve(scene, map, target);
        var tgtSkel = rig.Skeleton;

        foreach (var side in new[] { "L", "R" })
        {
            foreach (var finger in new[] { "thumb", "index", "middle", "ring", "pinky" })
            {
                var chainMax = 0f;
                foreach (var bone in Enumerable.Range(0, tgtSkel.Count)
                    .Where(b => tgtSkel[b].Name.StartsWith($"finger_{finger}_")
                        && tgtSkel[b].Name.EndsWith($"_{side}")
                        && !tgtSkel[b].Name.Contains("meta")))
                {
                    var rest = RestLocalRot(tgtSkel, bone);
                    foreach (var frame in frames)
                        chainMax = MathF.Max(chainMax, MathQ.AngleBetween(frame[bone].Rot, rest) * Rad2Deg);
                }
                Assert.True(chainMax >= 5f,
                    $"finger_{finger}_{side}: max phalanx deviation from rest {chainMax:F2} deg — "
                    + "the source's held grip did not transfer");
            }
        }
    }

    /// <summary>Faithfulness pin for the static-finger source: the source finger locals
    /// never change in this clip (verified against a Blender import), so the citizen finger
    /// locals must not wiggle over time either.</summary>
    [Fact] // skipped (silently green) when the local corpus file is not present
    public void SomaThrow_Fingers_DoNotWiggle_WhenSourceHoldsStill()
    {
        if (Load() is not var (scene, map, target))
            return;

        var (rig, frames, _, _) = Solve(scene, map, target);
        var tgtSkel = rig.Skeleton;

        for (var b = 0; b < tgtSkel.Count; b++)
        {
            if (!tgtSkel[b].Name.Contains("finger"))
                continue;
            var first = frames[0][b].Rot;
            foreach (var frame in frames)
            {
                var deg = MathQ.AngleBetween(frame[b].Rot, first) * Rad2Deg;
                Assert.True(deg <= 1.5f,
                    $"{tgtSkel[b].Name}: local rotation moved {deg:F2} deg over time on a "
                    + "static-finger source clip");
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static (SourceScene Scene, MappingResult Map, RetargetTargetSpec Target)? Load()
    {
        var path = Path();
        if (!File.Exists(path))
            return null;
        var scene = Retargeter.ImportSource(File.ReadAllBytes(path), TodoFile);
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));
        return (scene, map, target);
    }

    /// <summary>Runs the full production pipeline and returns the rig, solved local frames
    /// and the character-frame inverse rotations for direction comparisons.</summary>
    private static (TargetRig Rig, IReadOnlyList<XForm[]> Frames, Quaternion SrcChrInv, Quaternion TgtChrInv)
        Solve(SourceScene scene, MappingResult map, RetargetTargetSpec target)
    {
        var rig = target.Rig;
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = File.ReadAllBytes(Path()),
            SourceFileName = TodoFile,
            MappingOverride = map,
        }, target);
        var clip = result.Clips.First(c => c.Success);
        var frames = clip.SolvedFrames!;

        var (srcNorm, _) = RestNormalizer.Normalize(scene.Skeleton, map, scene.Clips[0].Frames[0]);
        var (tgtNorm, _) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        var srcCanon = CanonicalFrames.Build(scene.Skeleton, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(rig.Skeleton, rig.ToMappingResult(), tgtNorm.WorldRest);
        var srcChrInv = Quaternion.Conjugate(
            MathQ.BasisFromForwardUp(srcCanon.CharacterForward, srcCanon.CharacterUp));
        var tgtChrInv = Quaternion.Conjugate(
            MathQ.BasisFromForwardUp(tgtCanon.CharacterForward, tgtCanon.CharacterUp));
        return (rig, frames, srcChrInv, tgtChrInv);
    }

    private static string Path() => RepoFile("dev", "corpus", "todo", TodoFile);

    private static BoneRole Role(string baseName, string side) => Enum.Parse<BoneRole>(baseName + side);

    private static void AssertRole(SkeletonModel skeleton, MappingResult map, BoneRole role, string expected)
    {
        Assert.True(map.RoleToBone.TryGetValue(role, out var index), $"Role {role} unmapped");
        Assert.Equal(expected, skeleton[index].Name);
    }

    private static int SrcBone(SkeletonModel skel, string name)
        => Enumerable.Range(0, skel.Count).First(i => skel[i].Name == name);

    private static int TgtBone(SkeletonModel skel, string name)
        => Enumerable.Range(0, skel.Count).First(i => skel[i].Name == name);

    private static Quaternion RestLocalRot(SkeletonModel skel, int bone)
    {
        var p = skel[bone].ParentIndex;
        return p < 0
            ? skel.RestWorld[bone].Rot
            : MathQ.Normalize(Quaternion.Conjugate(skel.RestWorld[p].Rot) * skel.RestWorld[bone].Rot);
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
