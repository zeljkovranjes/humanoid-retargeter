using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Solve;

// TEMPORARY diagnostic - delete before commit. Mixamo neck "oddly straight / slightly
// longer" + possible clavicle issue on the s&box human male target (user report 2026-07-07).
public class MixamoNeckDiagTemp
{
    private const float Rad2Deg = 180f / MathF.PI;

    [Theory]
    [InlineData("mixamo", "Surprised.fbx")]
    [InlineData("actorcore", "catwalk-loop-378982.fbx")]
    public void Dump(string folder, string file)
    {
        var path = RepoFile("dev", "corpus", folder, file);
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var scene = Retargeter.ImportSource(bytes, file);
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "humanoid_retargeter", "target_rig_sbox.json")));

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = file,
            MappingOverride = map,
        }, target);
        var tgtFrames = result.Clips.First(c => c.Success).SolvedFrames!;
        var rig = target.Rig;

        var srcSkel = scene.Skeleton;
        var tgtSkel = rig.Skeleton;
        var tgtMap = rig.ToMappingResult();
        var (srcNorm, _) = RestNormalizer.Normalize(srcSkel, map);
        var (tgtNorm, _) = RestNormalizer.Normalize(tgtSkel, tgtMap);
        var srcCanon = CanonicalFrames.Build(srcSkel, map, srcNorm.WorldRest);
        var tgtCanon = CanonicalFrames.Build(tgtSkel, tgtMap, tgtNorm.WorldRest);
        var clip = scene.Clips[0];

        var sb = new StringBuilder();
        sb.AppendLine($"==== {file}: srcFrames={clip.FrameCount} tgtFrames={tgtFrames.Count}");
        foreach (var note in result.Clips.First(c => c.Success).Mapping?.Notes ?? new List<string>())
            sb.AppendLine($"  NOTE: {note}");

        // Rest carriage: lean of the neck->head rest segment from character up (deg).
        float RestLean(IReadOnlyList<XForm> rest, int a, int b, Vector3 fwd, Vector3 up)
        {
            var d = Vector3.Normalize(rest[b].Pos - rest[a].Pos);
            return MathF.Atan2(Vector3.Dot(d, Vector3.Normalize(fwd)), Vector3.Dot(d, Vector3.Normalize(up))) * Rad2Deg;
        }
        {
            int? s0 = map.RoleToBone.TryGetValue(BoneRole.Neck, out var sn0) ? sn0 : null;
            int? s1 = map.RoleToBone.TryGetValue(BoneRole.Head, out var sh0) ? sh0 : null;
            var t0 = rig.BoneForRole(BoneRole.Neck);
            var t1 = rig.BoneForRole(BoneRole.Head);
            if (s0 is int sa0 && s1 is int sb0 && t0 is int ta0 && t1 is int tb0)
                sb.AppendLine($"  REST neck->head lean: src={RestLean(srcNorm.WorldRest, sa0, sb0, srcCanon.CharacterForward, srcCanon.CharacterUp):F1} "
                    + $"tgt={RestLean(tgtNorm.WorldRest, ta0, tb0, tgtCanon.CharacterForward, tgtCanon.CharacterUp):F1}");
            // Also spine2->neck (the visible nape) rest lean.
            int? s2 = map.RoleToBone.TryGetValue(BoneRole.Spine2, out var ss2) ? ss2 : null;
            var t2 = rig.BoneForRole(BoneRole.Spine2);
            if (s2 is int sa2 && s0 is int sb2 && t2 is int ta2 && t0 is int tb2x)
                sb.AppendLine($"  REST spine2->neck lean: src={RestLean(srcNorm.WorldRest, sa2, sb2, srcCanon.CharacterForward, srcCanon.CharacterUp):F1} "
                    + $"tgt={RestLean(tgtNorm.WorldRest, ta2, tb2x, tgtCanon.CharacterForward, tgtCanon.CharacterUp):F1}");
        }
        int? SB(BoneRole r) => map.RoleToBone.TryGetValue(r, out var b) ? b : null;
        int? TB(BoneRole r) => rig.BoneForRole(r);
        foreach (var r in new[] { BoneRole.Neck, BoneRole.Head, BoneRole.ClavicleL, BoneRole.ClavicleR, BoneRole.Spine2, BoneRole.UpperArmL, BoneRole.UpperArmR })
            sb.AppendLine($"  {r}: src={(SB(r) is int s ? srcSkel[s].Name : "-")} tgt={(TB(r) is int t ? tgtSkel[t].Name : "-")}");

        // ---- neck / head lean series (forward lean of rest segment carried by bone delta)
        void LeanStats(string label, BoneRole seg0, BoneRole seg1, BoneRole carrier)
        {
            if (SB(seg0) is not int sa || SB(seg1) is not int sbn || SB(carrier) is not int sc
                || TB(seg0) is not int ta || TB(seg1) is not int tb2 || TB(carrier) is not int tc)
            { sb.AppendLine($"  {label}: unmapped"); return; }
            var src = Leans(clip.Frames, srcSkel, sa, sbn, sc, srcNorm.WorldRest, srcCanon.CharacterForward, srcCanon.CharacterUp);
            var tgt = Leans(tgtFrames, tgtSkel, ta, tb2, tc, tgtNorm.WorldRest, tgtCanon.CharacterForward, tgtCanon.CharacterUp);
            int n = Math.Min(src.Length, tgt.Length);
            var err = Enumerable.Range(0, n).Select(f => tgt[f] - src[f]).ToArray();
            sb.AppendLine($"  {label}: srcLean mean={src.Take(n).Average():F1} range=[{src.Take(n).Min():F1},{src.Take(n).Max():F1}]  "
                + $"tgtLean mean={tgt.Take(n).Average():F1} range=[{tgt.Take(n).Min():F1},{tgt.Take(n).Max():F1}]  "
                + $"err mean={err.Average():F1} max|={err.Max(MathF.Abs):F1}");
        }
        LeanStats("neck seg (carried by NECK)", BoneRole.Neck, BoneRole.Head, BoneRole.Neck);
        LeanStats("head seg (carried by HEAD)", BoneRole.Neck, BoneRole.Head, BoneRole.Head);

        // ---- rotation-from-rest magnitude (is the target neck frozen/straight?)
        void DeltaMag(string label, BoneRole role)
        {
            if (SB(role) is not int s || TB(role) is not int t)
            { sb.AppendLine($"  {label}: unmapped"); return; }
            var src = RotDeltaDeg(clip.Frames, srcSkel, s, srcNorm.WorldRest);
            var tgt = RotDeltaDeg(tgtFrames, tgtSkel, t, tgtNorm.WorldRest);
            int n = Math.Min(src.Length, tgt.Length);
            sb.AppendLine($"  {label} world-rot delta deg: src mean={src.Take(n).Average():F1} max={src.Take(n).Max():F1}  "
                + $"tgt mean={tgt.Take(n).Average():F1} max={tgt.Take(n).Max():F1}");
        }
        DeltaMag("Neck", BoneRole.Neck);
        DeltaMag("Head", BoneRole.Head);
        DeltaMag("ClavicleL", BoneRole.ClavicleL);
        DeltaMag("ClavicleR", BoneRole.ClavicleR);

        // ---- clavicle direction (clavicle->upperarm) elevation vs rest
        void ClavDir(string label, BoneRole clav, BoneRole arm)
        {
            if (SB(clav) is not int sc || SB(arm) is not int sa2 || TB(clav) is not int tc || TB(arm) is not int ta2)
            { sb.AppendLine($"  {label}: unmapped"); return; }
            var src = SegElev(clip.Frames, srcSkel, sc, sa2, srcNorm.WorldRest, srcCanon.CharacterUp);
            var tgt = SegElev(tgtFrames, tgtSkel, tc, ta2, tgtNorm.WorldRest, tgtCanon.CharacterUp);
            int n = Math.Min(src.Length, tgt.Length);
            var err = Enumerable.Range(0, n).Select(f => tgt[f] - src[f]).ToArray();
            sb.AppendLine($"  {label} elevation-vs-rest deg: src range=[{src.Take(n).Min():F1},{src.Take(n).Max():F1}]  "
                + $"tgt range=[{tgt.Take(n).Min():F1},{tgt.Take(n).Max():F1}]  err mean={err.Average():F1} max|={err.Max(MathF.Abs):F1}");
        }
        ClavDir("ClavL", BoneRole.ClavicleL, BoneRole.UpperArmL);
        ClavDir("ClavR", BoneRole.ClavicleR, BoneRole.UpperArmR);

        // ---- held forward offset: neck/head delta from OWN rest, per-frame stats
        void HeldOffset(string label, BoneRole seg0, BoneRole seg1, BoneRole carrier)
        {
            if (SB(seg0) is not int sa || SB(seg1) is not int sbn || SB(carrier) is not int sc
                || TB(seg0) is not int ta || TB(seg1) is not int tb2 || TB(carrier) is not int tc)
                return;
            var src = Leans(clip.Frames, srcSkel, sa, sbn, sc, srcNorm.WorldRest, srcCanon.CharacterForward, srcCanon.CharacterUp);
            var tgt = Leans(tgtFrames, tgtSkel, ta, tb2, tc, tgtNorm.WorldRest, tgtCanon.CharacterForward, tgtCanon.CharacterUp);
            float RestLean(IReadOnlyList<XForm> rest, int a, int b, Vector3 fwd, Vector3 up)
            {
                var d = Vector3.Normalize(rest[b].Pos - rest[a].Pos);
                return MathF.Atan2(Vector3.Dot(d, Vector3.Normalize(fwd)), Vector3.Dot(d, Vector3.Normalize(up))) * Rad2Deg;
            }
            var srcRest = RestLean(srcNorm.WorldRest, sa, sbn, srcCanon.CharacterForward, srcCanon.CharacterUp);
            var tgtRest = RestLean(tgtNorm.WorldRest, ta, tb2, tgtCanon.CharacterForward, tgtCanon.CharacterUp);
            var srcDelta = src.Select(v => v - srcRest).ToArray();
            var tgtDelta = tgt.Select(v => v - tgtRest).ToArray();
            var sorted = srcDelta.OrderBy(v => v).ToArray();
            var median = sorted[sorted.Length / 2];
            var iqr = sorted[(int)(sorted.Length * 0.75)] - sorted[(int)(sorted.Length * 0.25)];
            sb.AppendLine($"  {label} HELD delta-from-own-rest: src median={median:F1} iqr={iqr:F1} "
                + $"range=[{srcDelta.Min():F1},{srcDelta.Max():F1}]  tgt mean={tgtDelta.Average():F1} "
                + $"(+ = forward of the rig's own rest carriage)");
        }
        HeldOffset("neck", BoneRole.Neck, BoneRole.Head, BoneRole.Neck);
        HeldOffset("head", BoneRole.Neck, BoneRole.Head, BoneRole.Head);

        // ---- finger curls: joint angle (angle between consecutive segment directions)
        void FingerCurl(string label, BoneRole a, BoneRole b, BoneRole c)
        {
            if (SB(a) is not int sa || SB(b) is not int sbn || SB(c) is not int sc2
                || TB(a) is not int ta || TB(b) is not int tb2 || TB(c) is not int tc)
            { sb.AppendLine($"  {label}: unmapped"); return; }
            float[] Curls(List<XForm[]> frames, SkeletonModel skel, int i, int j, int k)
            {
                var r = new float[frames.Count];
                for (int f = 0; f < frames.Count; f++)
                {
                    var p0 = WorldPos(frames[f], skel, i);
                    var p1 = WorldPos(frames[f], skel, j);
                    var p2 = WorldPos(frames[f], skel, k);
                    var d0 = Vector3.Normalize(p1 - p0);
                    var d1 = Vector3.Normalize(p2 - p1);
                    r[f] = MathF.Acos(Math.Clamp(Vector3.Dot(d0, d1), -1f, 1f)) * Rad2Deg;
                }
                return r;
            }
            var src = Curls(clip.Frames, srcSkel, sa, sbn, sc2);
            var tgt = Curls(tgtFrames, tgtSkel, ta, tb2, tc);
            int n = Math.Min(src.Length, tgt.Length);
            var err = Enumerable.Range(0, n).Select(f => tgt[f] - src[f]).ToArray();
            sb.AppendLine($"  {label} joint-angle deg: src range=[{src.Take(n).Min():F0},{src.Take(n).Max():F0}] "
                + $"tgt range=[{tgt.Take(n).Min():F0},{tgt.Take(n).Max():F0}] err mean={err.Average():F1} max|={err.Max(MathF.Abs):F1}");
        }
        foreach (var side in new[] { "L", "R" })
        {
            FingerCurl($"index{side} knuckle", Role($"Index", "Prox", side), Role($"Index", "Mid", side), Role($"Index", "Dist", side));
            FingerCurl($"middle{side} knuckle", Role($"Middle", "Prox", side), Role($"Middle", "Mid", side), Role($"Middle", "Dist", side));
            FingerCurl($"ring{side} knuckle", Role($"Ring", "Prox", side), Role($"Ring", "Mid", side), Role($"Ring", "Dist", side));
            FingerCurl($"pinky{side} knuckle", Role($"Pinky", "Prox", side), Role($"Pinky", "Mid", side), Role($"Pinky", "Dist", side));
            FingerCurl($"thumb{side}", Role($"Thumb", "Prox", side), Role($"Thumb", "Mid", side), Role($"Thumb", "Dist", side));
        }

        // pinky-vs-ring distinctness in the SOLVED output: if these two series were equal,
        // the solver itself would be copying the ring onto the pinky (it must not - that is
        // the ENGINE's CopyPinky constraint on standalone vmdls).
        if (TB(BoneRole.PinkyProxL) is int pxl && TB(BoneRole.RingProxL) is int rgl)
        {
            float sum = 0, max = 0;
            for (int f = 0; f < tgtFrames.Count; f++)
            {
                var a = tgtFrames[f][pxl].Rot;
                var b = tgtFrames[f][rgl].Rot;
                var ang = MathQ.AngleBetween(MathQ.Normalize(a), MathQ.Normalize(b)) * Rad2Deg;
                sum += ang; max = MathF.Max(max, ang);
            }
            sb.AppendLine($"  solved pinkyProxL vs ringProxL LOCAL rot difference: mean={sum / tgtFrames.Count:F1}deg max={max:F1}deg (0 would mean the solver copies)");
        }

        // ---- "longer neck": head height above the neck head, along up, vs rest (ratio)
        if (SB(BoneRole.Neck) is int sn && SB(BoneRole.Head) is int sh
            && TB(BoneRole.Neck) is int tn && TB(BoneRole.Head) is int th)
        {
            var srcH = SegLenRatio(clip.Frames, srcSkel, sn, sh, srcNorm.WorldRest);
            var tgtH = SegLenRatio(tgtFrames, tgtSkel, tn, th, tgtNorm.WorldRest);
            int n = Math.Min(srcH.Length, tgtH.Length);
            sb.AppendLine($"  neck->head distance / rest: src range=[{srcH.Take(n).Min():F3},{srcH.Take(n).Max():F3}]  "
                + $"tgt range=[{tgtH.Take(n).Min():F3},{tgtH.Take(n).Max():F3}]");
        }

        File.AppendAllText(@"C:\Users\zeljk\AppData\Local\Temp\claude\P--5-11-2026-Organization-s--Zeljko-Vranjes-humanoid-retargeter\cb12e4b4-4e03-4aba-ad84-d1589dce2694\scratchpad\mixamo_neck_diag.txt", sb.ToString());
    }

    private static BoneRole Role(string finger, string part, string side)
        => Enum.Parse<BoneRole>(finger + part + side);

    // ------------------------------------------------------------------ metric helpers

    private static float[] Leans(
        List<XForm[]> frames, SkeletonModel skeleton, int seg0, int seg1, int carrier,
        IReadOnlyList<XForm> normRest, Vector3 fwd, Vector3 up)
    {
        fwd = Vector3.Normalize(fwd);
        up = Vector3.Normalize(up);
        var restDir = normRest[seg1].Pos - normRest[seg0].Pos;
        var restRotInv = Quaternion.Conjugate(normRest[carrier].Rot);
        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
        {
            var world = WorldRot(frames[f], skeleton, carrier);
            var dir = Vector3.Normalize(Vector3.Transform(restDir, MathQ.Normalize(world * restRotInv)));
            result[f] = MathF.Atan2(Vector3.Dot(dir, fwd), Vector3.Dot(dir, up)) * Rad2Deg;
        }
        return result;
    }

    private static float[] RotDeltaDeg(
        List<XForm[]> frames, SkeletonModel skeleton, int bone, IReadOnlyList<XForm> normRest)
    {
        var restRotInv = Quaternion.Conjugate(normRest[bone].Rot);
        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
        {
            var delta = MathQ.Normalize(WorldRot(frames[f], skeleton, bone) * restRotInv);
            result[f] = MathQ.AngleBetween(delta, Quaternion.Identity) * Rad2Deg;
        }
        return result;
    }

    private static float[] SegElev(
        List<XForm[]> frames, SkeletonModel skeleton, int a, int b,
        IReadOnlyList<XForm> normRest, Vector3 up)
    {
        up = Vector3.Normalize(up);
        float Elev(Vector3 d) => MathF.Asin(Math.Clamp(Vector3.Dot(Vector3.Normalize(d), up), -1f, 1f)) * Rad2Deg;
        var rest = Elev(normRest[b].Pos - normRest[a].Pos);
        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
            result[f] = Elev(WorldPos(frames[f], skeleton, b) - WorldPos(frames[f], skeleton, a)) - rest;
        return result;
    }

    private static float[] SegLenRatio(
        List<XForm[]> frames, SkeletonModel skeleton, int a, int b, IReadOnlyList<XForm> normRest)
    {
        var rest = (normRest[b].Pos - normRest[a].Pos).Length();
        var result = new float[frames.Count];
        for (int f = 0; f < frames.Count; f++)
            result[f] = (WorldPos(frames[f], skeleton, b) - WorldPos(frames[f], skeleton, a)).Length() / MathF.Max(rest, 1e-4f);
        return result;
    }

    private static Quaternion WorldRot(XForm[] locals, SkeletonModel skeleton, int bone)
    {
        var rot = locals[bone].Rot;
        for (var b = skeleton[bone].ParentIndex; b >= 0; b = skeleton[b].ParentIndex)
            rot = locals[b].Rot * rot;
        return MathQ.Normalize(rot);
    }

    private static Vector3 WorldPos(XForm[] locals, SkeletonModel skeleton, int bone)
    {
        var x = locals[bone];
        for (var b = skeleton[bone].ParentIndex; b >= 0; b = skeleton[b].ParentIndex)
            x = XForm.Compose(locals[b], x);
        return x.Pos;
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root not found");
    }
}
