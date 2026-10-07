using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repro (2026-07-04, local-only): DieExported.fbx picked as a custom FBX
/// target — skinned character, inch units (UnitScaleFactor 2.54), Y-up, textures folder
/// NEXT TO the source folder. Must import, be recognized (or at least accepted
/// best-effort), and convert cleanly onto its own skeleton.</summary>
public class UserDieFbxTargetTests
{
    private static readonly string FbxPath =
        TestUtil.RepoFile("dev", "corpus", "user_rigs", "die", "source", "DieExported.fbx");

    private readonly ITestOutputHelper _out;

    public UserDieFbxTargetTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DieExported_ImportsAndResolvesAsTarget()
    {
        if (!File.Exists(FbxPath))
            return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(FbxPath));
        _out.WriteLine($"bones={scene.Skeleton.Count} unitScaleCm={scene.UnitScaleCm} upAxis={scene.UpAxis}");
        foreach (var bone in scene.Skeleton.Bones.Take(24))
            _out.WriteLine($"  {bone.Name}");

        var (map, report) = Retargeter.ResolveMapping(scene.Skeleton);
        _out.WriteLine($"profile={map.ProfileName} source={map.Source} conf={map.Confidence:0.00} "
            + $"needsUser={report.NeedsUserDecision} roles={map.RoleToBone.Count}");
        foreach (var (role, index) in map.RoleToBone.OrderBy(kv => kv.Key.ToString()))
            _out.WriteLine($"  {role} -> {scene.Skeleton[index].Name}");

        // The structural minimum TargetPickers accepts best-effort maps on.
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.Hips), "no hips");
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.UpperLegL), "no upper leg L");
        Assert.True(map.RoleToBone.ContainsKey(BoneRole.UpperLegR), "no upper leg R");

        // Converting a citizen clip onto it must solve.
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        {
            Rig = rig,
            VmdlScale = HumanoidRetargeter.Core.RetargetTargetSpec.SboxSourceScale,
            DefaultRootBone = scene.Skeleton[0].Name,
        };
        var bvh = TestUtil.RepoFile("dev", "corpus", "bvh", "cmu_01_01.bvh");
        if (!File.Exists(bvh))
            return;
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[]
            {
                new HumanoidRetargeter.Core.RetargetRequest
                {
                    SourceData = File.ReadAllBytes(bvh),
                    SourceFileName = "cmu_01_01.bvh",
                },
            },
            target,
            new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        foreach (var clip in batch.Clips.Where(c => !c.Success))
            _out.WriteLine($"FAIL {clip.ClipName}: {clip.Error}");
        Assert.True(batch.Clips.All(c => c.Success));
        Assert.Contains("cmu_01_01", batch.StandaloneVmdl);
    }

    /// <summary>User request: "apply the Neutral throw ball animation to that model, see
    /// if the animation plays correctly" (and "try the surprised.fbx animation as well,
    /// which is mixamo"). Solves each clip onto DieExported and requires real motion: the
    /// hands must travel, the character must stay upright (pelvis height near rest, no
    /// axis flip), and the pelvis must not sink.</summary>
    [Theory]
    [InlineData("todo/Neutral_throw_ball_001__A057.bvh", 0.5f)]
    [InlineData("mixamo/Surprised.fbx", 0.15f)] // startled recoil - hands travel less than a throw
    public void DieExported_UserClips_SolveWithRealMotion(string corpusRelative, float travelFactor)
    {
        var throwPath = TestUtil.RepoFile(
            ("dev/corpus/" + corpusRelative).Split('/'));
        if (!File.Exists(FbxPath) || !File.Exists(throwPath))
            return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(FbxPath));
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        {
            Rig = rig,
            VmdlScale = HumanoidRetargeter.Core.RetargetTargetSpec.SboxSourceScale,
            DefaultRootBone = scene.Skeleton[0].Name,
        };

        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[]
            {
                new HumanoidRetargeter.Core.RetargetRequest
                {
                    SourceData = File.ReadAllBytes(throwPath),
                    SourceFileName = Path.GetFileName(throwPath),
                    FootPlantCleanup = true,
                },
            },
            target,
            new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });

        var clip = Assert.Single(batch.Clips);
        _out.WriteLine($"clip={clip.ClipName} success={clip.Success} err={clip.Error} "
            + $"frames={clip.SolvedFrames?.Count}");
        Assert.True(clip.Success, clip.Error);
        Assert.True(clip.SolvedFrames!.Count > 30, "suspiciously short solve");

        var skeleton = rig.Skeleton;
        var hips = rig.BoneForRole(BoneRole.Hips)!.Value;
        var handR = rig.BoneForRole(BoneRole.HandR)!.Value;

        var restPelvisY = skeleton.RestWorld[hips].Pos.Y;
        var minPelvis = float.MaxValue;
        var maxPelvis = float.MinValue;
        var handMin = new System.Numerics.Vector3(float.MaxValue);
        var handMax = new System.Numerics.Vector3(float.MinValue);
        foreach (var frame in clip.SolvedFrames)
        {
            var world = new HumanoidRetargeter.Core.Skeleton.Pose(frame).ToWorld(skeleton);
            minPelvis = MathF.Min(minPelvis, world[hips].Pos.Y);
            maxPelvis = MathF.Max(maxPelvis, world[hips].Pos.Y);
            handMin = System.Numerics.Vector3.Min(handMin, world[handR].Pos);
            handMax = System.Numerics.Vector3.Max(handMax, world[handR].Pos);
        }
        var handTravel = (handMax - handMin).Length();
        _out.WriteLine($"restPelvisY={restPelvisY:0.#} pelvisY=[{minPelvis:0.#}..{maxPelvis:0.#}] "
            + $"handTravel={handTravel:0.#}cm (rig units)");

        // Upright: pelvis stays within a sane band around its rest height (a lying/flipped
        // or buried character violates this immediately).
        Assert.InRange(minPelvis, restPelvisY * 0.5f, restPelvisY * 1.5f);
        Assert.InRange(maxPelvis, restPelvisY * 0.5f, restPelvisY * 1.5f);
        // The clip must move the hand substantially (relative to character size).
        Assert.True(handTravel > restPelvisY * travelFactor,
            $"hand barely moves ({handTravel:0.#} vs pelvis height {restPelvisY:0.#})");
    }
}

public class UserDieFbxHandDiagnostics
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public UserDieFbxHandDiagnostics(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    [Xunit.Fact]
    public void Surprised_OnDieExported_HandTransferAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "die", "source", "DieExported.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return;

        var tgtScene = HumanoidRetargeter.Core.Formats.Fbx.FbxImporter.Import(File.ReadAllBytes(fbx));
        var (tgtMap, tgtReport) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(tgtScene.Skeleton);
        var rig = HumanoidRetargeter.Core.Target.TargetRig.FromSkeleton(tgtScene.Skeleton, tgtMap);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        {
            Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = tgtScene.Skeleton[0].Name,
        };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = "Surprised.fbx" } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Xunit.Assert.True(clip.Success, clip.Error);

        foreach (var note in clip.Mapping!.Notes.Where(n => n.Contains("and") || n.Contains("roll")))
            _out.WriteLine("NOTE: " + note);

        // Local-rotation swing per bone at the surprise peak vs source counterparts.
        var srcScene = HumanoidRetargeter.Core.Retargeter.ImportSource(File.ReadAllBytes(clipPath), "Surprised.fbx");
        var (srcMap, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(srcScene.Skeleton);
        var f = Math.Min(60, clip.SolvedFrames!.Count - 1);
        var srcF = Math.Min(60, srcScene.Clips[0].Frames.Count - 1);
        float Angle(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
        {
            var d = MathF.Min(MathF.Abs(System.Numerics.Quaternion.Dot(
                System.Numerics.Quaternion.Normalize(a), System.Numerics.Quaternion.Normalize(b))), 1f);
            return 2f * MathF.Acos(d) * 180f / MathF.PI;
        }
        foreach (var role in new[] {
            HumanoidRetargeter.Core.Mapping.BoneRole.LowerArmR, HumanoidRetargeter.Core.Mapping.BoneRole.HandR,
            HumanoidRetargeter.Core.Mapping.BoneRole.IndexProxR, HumanoidRetargeter.Core.Mapping.BoneRole.MiddleProxR,
            HumanoidRetargeter.Core.Mapping.BoneRole.ThumbProxR,
            HumanoidRetargeter.Core.Mapping.BoneRole.HandL, HumanoidRetargeter.Core.Mapping.BoneRole.IndexProxL })
        {
            var t = rig.BoneForRole(role);
            srcMap.RoleToBone.TryGetValue(role, out var s);
            if (t is null) { _out.WriteLine($"{role}: unmapped on target"); continue; }
            var tgtSwing = Angle(clip.SolvedFrames[f][t.Value].Rot, rig.Skeleton[t.Value].RestLocal.Rot);
            var srcSwing = srcMap.RoleToBone.ContainsKey(role)
                ? Angle(srcScene.Clips[0].Frames[srcF][s].Rot, srcScene.Skeleton[s].RestLocal.Rot)
                : -1f;
            _out.WriteLine($"{role}: target swing {tgtSwing:0.#} deg  source swing {srcSwing:0.#} deg");
        }
    }
}

public class UserDieFbxFingerLengths
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public UserDieFbxFingerLengths(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    [Xunit.Fact]
    public void Surprised_OnDieExported_FingerLengthAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "die", "source", "DieExported.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return;

        var tgtScene = HumanoidRetargeter.Core.Formats.Fbx.FbxImporter.Import(File.ReadAllBytes(fbx));
        var (tgtMap, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(tgtScene.Skeleton);
        foreach (var b in tgtScene.Skeleton.Bones)
            if (b.Name.Contains("Finger") || b.Name.Contains("Hand") || b.Name.Contains("Forearm"))
                _out.WriteLine($"bone: {b.Name} mapped={tgtMap.RoleToBone.ContainsValue(b.Index)}");

        var rig = HumanoidRetargeter.Core.Target.TargetRig.FromSkeleton(tgtScene.Skeleton, tgtMap);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = tgtScene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = "Surprised.fbx" } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        var f = Math.Min(60, clip.SolvedFrames!.Count - 1);
        var world = new HumanoidRetargeter.Core.Skeleton.Pose(clip.SolvedFrames[f]).ToWorld(rig.Skeleton);
        var rest = rig.Skeleton.RestWorld;
        var hand = rig.BoneForRole(HumanoidRetargeter.Core.Mapping.BoneRole.HandR)!.Value;
        foreach (var role in new[] {
            HumanoidRetargeter.Core.Mapping.BoneRole.IndexDistR, HumanoidRetargeter.Core.Mapping.BoneRole.MiddleDistR,
            HumanoidRetargeter.Core.Mapping.BoneRole.ThumbDistR, HumanoidRetargeter.Core.Mapping.BoneRole.PinkyDistR })
        {
            if (rig.BoneForRole(role) is not { } tip) { _out.WriteLine($"{role}: unmapped"); continue; }
            var restD = (rest[tip].Pos - rest[hand].Pos).Length();
            var frameD = (world[tip].Pos - world[hand].Pos).Length();
            _out.WriteLine($"{role}: rest {restD:0.##}  frame {frameD:0.##}  ratio {frameD / restD:0.###}");
        }
    }
}

public class UserSimpsonRigDiagnostics
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public UserSimpsonRigDiagnostics(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    [Xunit.Fact]
    public void Simpson_ImportAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        if (!File.Exists(fbx)) return;
        var scene = HumanoidRetargeter.Core.Formats.Fbx.FbxImporter.Import(File.ReadAllBytes(fbx));
        _out.WriteLine($"bones={scene.Skeleton.Count} unitScaleCm={scene.UnitScaleCm} upAxis={scene.UpAxis} clips={scene.Clips.Count}");
        var (map, report) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        _out.WriteLine($"profile={map.ProfileName} conf={map.Confidence:0.00} roles={map.RoleToBone.Count} needsUser={report.NeedsUserDecision}");
        // Character extents + suspicious rest locals
        var rest = scene.Skeleton.RestWorld;
        float maxY = float.MinValue, minY = float.MaxValue;
        foreach (var w in rest) { maxY = MathF.Max(maxY, w.Pos.Y); minY = MathF.Min(minY, w.Pos.Y); }
        _out.WriteLine($"rest height span Y: {minY:0.#}..{maxY:0.#} cm");
        foreach (var role in new[] { HumanoidRetargeter.Core.Mapping.BoneRole.Hips, HumanoidRetargeter.Core.Mapping.BoneRole.Head,
            HumanoidRetargeter.Core.Mapping.BoneRole.HandR, HumanoidRetargeter.Core.Mapping.BoneRole.FootL })
        {
            if (map.RoleToBone.TryGetValue(role, out var i))
                _out.WriteLine($"{role} -> {scene.Skeleton[i].Name} world {rest[i].Pos}");
            else _out.WriteLine($"{role}: UNMAPPED");
        }
        foreach (var b in scene.Skeleton.Bones.Take(12))
            _out.WriteLine($"  [{b.Index}] {b.Name} parent={b.ParentIndex} localPos={b.RestLocal.Pos}");
    }
}
