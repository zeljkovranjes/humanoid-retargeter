using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repros (2026-07-04, local-only): humanMesh6.fbx converts to a T-pose
/// (only the location moves); Sonic.fbx renders black/red (texture matching). Import +
/// mapping + solve audits naming the failing stage.</summary>
public class UserHumanModelDiagnostics
{
    private readonly ITestOutputHelper _out;
    public UserHumanModelDiagnostics(ITestOutputHelper o) => _out = o;

    [Fact]
    public void HumanMesh6_ImportMapSolveAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "human", "source", "humanMesh6.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "mixamo", "Surprised.fbx");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        _out.WriteLine($"bones={scene.Skeleton.Count} unitScaleCm={scene.UnitScaleCm} upAxis={scene.UpAxis} clips={scene.Clips.Count}");
        var rest = scene.Skeleton.RestWorld;
        for (var i = 0; i < scene.Skeleton.Count; i++)
        {
            var b = scene.Skeleton[i];
            var depth = 0;
            for (var p = b.ParentIndex; p >= 0; p = scene.Skeleton[p].ParentIndex) depth++;
            _out.WriteLine($"{new string(' ', depth * 2)}[{i}] {b.Name} world={rest[i].Pos.X:0.#},{rest[i].Pos.Y:0.#},{rest[i].Pos.Z:0.#}");
            if (i > 90) { _out.WriteLine("..."); break; }
        }

        var (map, report) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        _out.WriteLine($"profile={map.ProfileName} source={map.Source} conf={map.Confidence:0.00} roles={map.RoleToBone.Count} needsUser={report.NeedsUserDecision}");
        foreach (var (role, index) in map.RoleToBone.OrderBy(kv => kv.Key.ToString()))
            _out.WriteLine($"  {role} -> {scene.Skeleton[index].Name}");
        foreach (var note in map.Notes)
            _out.WriteLine($"NOTE: {note}");

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

        // Do bones actually rotate?
        var moved = 0;
        var maxSwing = 0f;
        string maxBone = "";
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            var restRot = rig.Skeleton[i].RestLocal.Rot;
            foreach (var frame in clip.SolvedFrames!)
            {
                var swing = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(
                    System.Numerics.Quaternion.Normalize(frame[i].Rot),
                    System.Numerics.Quaternion.Normalize(restRot))))) * 180f / MathF.PI;
                if (swing > maxSwing) { maxSwing = swing; maxBone = rig.Skeleton[i].Name; }
                if (swing > 3f) { moved++; break; }
            }
        }
        _out.WriteLine($"bones with >3deg local swing: {moved}/{rig.Skeleton.Count}  max={maxSwing:0.#} on '{maxBone}'");
    }
}
