using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repro (2026-07-04, local-only): Catgirl_2.fbx "completely messed up /
/// stretched weird" with Neutral_throw, and "one part of the body doesn't move at all".
/// Import/map/solve audit with stretch + immobility metrics.</summary>
public class UserCatgirlDiagnostics
{
    private readonly ITestOutputHelper _out;
    public UserCatgirlDiagnostics(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Catgirl_ImportMapSolveAudit()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "todo", "Neutral_throw_ball_001__A057.bvh");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        _out.WriteLine($"bones={scene.Skeleton.Count} unitScaleCm={scene.UnitScaleCm} upAxis={scene.UpAxis} clips={scene.Clips.Count}");
        var rest = scene.Skeleton.RestWorld;
        for (var i = 0; i < Math.Min(scene.Skeleton.Count, 80); i++)
        {
            var b = scene.Skeleton[i];
            var depth = 0;
            for (var p = b.ParentIndex; p >= 0; p = scene.Skeleton[p].ParentIndex) depth++;
            _out.WriteLine($"{new string(' ', depth * 2)}[{i}] {b.Name} world={rest[i].Pos.X:0.#},{rest[i].Pos.Y:0.#},{rest[i].Pos.Z:0.#}");
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
                SourceData = File.ReadAllBytes(clipPath),
                SourceFileName = Path.GetFileName(clipPath), FootPlantCleanup = true } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        _out.WriteLine($"solve success={clip.Success} err={clip.Error} frames={clip.SolvedFrames?.Count}");
        if (!clip.Success) return;

        // Stretch: per-bone hips-relative distance vs rest across frames (FK world) and
        // immobility: which mapped roles never move.
        var skeleton = rig.Skeleton;
        var hips = rig.BoneForRole(BoneRole.Hips)!.Value;
        var restWorld = skeleton.RestWorld;
        var maxStretch = new float[skeleton.Count];
        var maxSwing = new float[skeleton.Count];
        foreach (var frame in clip.SolvedFrames!)
        {
            var world = new HumanoidRetargeter.Core.Skeleton.Pose(frame).ToWorld(skeleton);
            for (var i = 0; i < skeleton.Count; i++)
            {
                if (skeleton[i].ParentIndex is int p and >= 0)
                {
                    var restLen = (restWorld[i].Pos - restWorld[p].Pos).Length();
                    var frameLen = (world[i].Pos - world[p].Pos).Length();
                    if (restLen > 0.5f)
                        maxStretch[i] = MathF.Max(maxStretch[i], MathF.Abs(frameLen - restLen) / restLen);
                }
                var swing = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(
                    System.Numerics.Quaternion.Normalize(frame[i].Rot),
                    System.Numerics.Quaternion.Normalize(skeleton[i].RestLocal.Rot))))) * 180f / MathF.PI;
                maxSwing[i] = MathF.Max(maxSwing[i], swing);
            }
        }
        _out.WriteLine("worst bone-length stretch (should be ~0 - rotations preserve length):");
        foreach (var i in Enumerable.Range(0, skeleton.Count).OrderByDescending(i => maxStretch[i]).Take(8))
            _out.WriteLine($"  {skeleton[i].Name,-30} stretch {maxStretch[i]:P0}  swing {maxSwing[i]:0.#}");
        _out.WriteLine("mapped roles that never move (>3deg):");
        foreach (var (role, index) in map.RoleToBone.OrderBy(kv => kv.Key.ToString()))
            if (maxSwing[index] < 3f && role != BoneRole.Hips)
                _out.WriteLine($"  {role} '{skeleton[index].Name}' maxSwing {maxSwing[index]:0.#}");

        // The kilt: 48 skirt-physics bones parented to the SCENE ROOT, outside the body
        // chain. Unmapped bones hold rest locals, so the whole skirt froze at the bind
        // spot while the body animated away ("one part doesn't move at all", the mesh
        // between skirt and legs "stretched weird"). They must ride the hips rigidly:
        // hips-relative offset stays the rest offset on every frame.
        var kiltTop = Enumerable.Range(0, skeleton.Count).Single(i => skeleton[i].Name == "c_kilt_01_01.l");
        var restOffset = HumanoidRetargeter.Core.Maths.XForm.ToLocal(restWorld[hips], restWorld[kiltTop]).Pos;
        var worstDrift = 0f;
        foreach (var frame in clip.SolvedFrames)
        {
            var world = new HumanoidRetargeter.Core.Skeleton.Pose(frame).ToWorld(skeleton);
            var offset = HumanoidRetargeter.Core.Maths.XForm.ToLocal(world[hips], world[kiltTop]).Pos;
            worstDrift = MathF.Max(worstDrift, (offset - restOffset).Length());
        }
        _out.WriteLine($"kilt hips-frame drift: worst {worstDrift:0.##} cm");
        Assert.True(worstDrift < 2f,
            $"kilt bone drifts {worstDrift:0.#} cm from the hips - the skirt is frozen in place");
    }
}
