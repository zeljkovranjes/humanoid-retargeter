using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repro (2026-07-04, local-only): Sonic.fbx (Sonic Dream Team mobile rip)
/// as a custom target — reported black/red (texture matching). Import/map/solve audit.</summary>
public class UserSonicDiagnostics
{
    private readonly ITestOutputHelper _out;
    public UserSonicDiagnostics(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Sonic_ImportMapSolveAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "sonic", "source",
            "Mobile - Sonic Dream Team - Sonic (1)", "Sonic.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        _out.WriteLine($"bones={scene.Skeleton.Count} unitScaleCm={scene.UnitScaleCm} upAxis={scene.UpAxis} clips={scene.Clips.Count}");
        foreach (var b in scene.Skeleton.Bones.Take(40))
            _out.WriteLine($"  [{b.Index}] {b.Name} parent={b.ParentIndex}");
        var (map, report) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        _out.WriteLine($"profile={map.ProfileName} conf={map.Confidence:0.00} roles={map.RoleToBone.Count} needsUser={report.NeedsUserDecision}");
        foreach (var (role, index) in map.RoleToBone.OrderBy(kv => kv.Key.ToString()).Take(40))
            _out.WriteLine($"  {role} -> {scene.Skeleton[index].Name}");

        // The AdvancedSkeleton preset must recognize this rig — before it existed the
        // topology fallback mapped arms/legs onto the head quills ("played backwards").
        int Bone(string name) => Enumerable.Range(0, scene.Skeleton.Count)
            .Single(i => scene.Skeleton[i].Name == name);
        Assert.Equal("advanced_skeleton", map.ProfileName);
        Assert.Equal(Bone("Root_M"), map.RoleToBone[BoneRole.Hips]);
        Assert.Equal(Bone("Head_M"), map.RoleToBone[BoneRole.Head]);
        Assert.Equal(Bone("Wrist_L"), map.RoleToBone[BoneRole.HandL]);
        Assert.Equal(Bone("Ankle_R"), map.RoleToBone[BoneRole.FootR]);
        Assert.Equal(Bone("Hip_L"), map.RoleToBone[BoneRole.UpperLegL]);
        Assert.Equal(Bone("IndexFinger1_L"), map.RoleToBone[BoneRole.IndexProxL]);

        if (!map.RoleToBone.ContainsKey(BoneRole.Hips)) { _out.WriteLine("NO HIPS - stops here"); return; }
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = "Surprised.fbx" } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        _out.WriteLine($"solve success={clip.Success} err={clip.Error} frames={clip.SolvedFrames?.Count}");
        if (!clip.Success) return;

        // Head attitude: solved world delta from the normalized rest vs the source's.
        var srcScene = HumanoidRetargeter.Core.Retargeter.ImportSource(File.ReadAllBytes(clipPath), "Surprised.fbx");
        var (srcMap, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(srcScene.Skeleton);
        var srcRef = srcScene.Clips[0].Frames.Count > 0 ? srcScene.Clips[0].Frames[0] : null;
        var (srcNorm, _) = HumanoidRetargeter.Core.Solve.RestNormalizer.Normalize(srcScene.Skeleton, srcMap, srcRef);
        var (tgtNorm, _) = HumanoidRetargeter.Core.Solve.RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        var tgtHead = rig.BoneForRole(BoneRole.Head)!.Value;
        var srcHead = srcMap.RoleToBone[BoneRole.Head];
        float Swing(System.Numerics.Quaternion a, System.Numerics.Quaternion b)
            => 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(
                System.Numerics.Quaternion.Normalize(a), System.Numerics.Quaternion.Normalize(b))))) * 180f / MathF.PI;
        foreach (var f in new[] { 0, 30, 60 })
        {
            var tf = Math.Min(f, clip.SolvedFrames!.Count - 1);
            var sf = Math.Min(f, srcScene.Clips[0].Frames.Count - 1);
            var tgtWorld = new HumanoidRetargeter.Core.Skeleton.Pose(clip.SolvedFrames[tf]).ToWorld(rig.Skeleton);
            var srcWorld = new HumanoidRetargeter.Core.Skeleton.Pose(srcScene.Clips[0].Frames[sf]).ToWorld(srcScene.Skeleton);
            _out.WriteLine($"f{f}: tgt head WORLD delta {Swing(tgtWorld[tgtHead].Rot, tgtNorm.WorldRest[tgtHead].Rot):0.#} deg  "
                + $"src head WORLD delta {Swing(srcWorld[srcHead].Rot, srcNorm.WorldRest[srcHead].Rot):0.#} deg  "
                + $"tgt head LOCAL swing {Swing(clip.SolvedFrames[tf][tgtHead].Rot, rig.Skeleton[tgtHead].RestLocal.Rot):0.#}");
        }
    }
}
