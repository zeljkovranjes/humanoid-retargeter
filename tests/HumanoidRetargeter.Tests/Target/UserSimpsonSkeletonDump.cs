using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>Temporary instrumentation (2026-07-04): the simpson rig maps Head to a bone
/// whose rest sits 90cm below-behind the neck. Dump the full hierarchy + bind vs normalized
/// rest so the real skull bone and the mis-assignment are visible.</summary>
public class UserSimpsonSkeletonDump
{
    private readonly ITestOutputHelper _out;
    public UserSimpsonSkeletonDump(ITestOutputHelper o) => _out = o;

    [Fact]
    public void DumpHierarchyAndMapping()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        if (!File.Exists(fbx)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, report) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        _out.WriteLine($"bones={scene.Skeleton.Count} profile={map.ProfileName} conf={map.Confidence:0.00}");

        var roleOf = map.RoleToBone.ToDictionary(kv => kv.Value, kv => kv.Key.ToString());
        var rest = scene.Skeleton.RestWorld;
        for (var i = 0; i < scene.Skeleton.Count; i++)
        {
            var b = scene.Skeleton[i];
            var depth = 0;
            for (var p = b.ParentIndex; p >= 0; p = scene.Skeleton[p].ParentIndex) depth++;
            _out.WriteLine($"{new string(' ', depth * 2)}[{i}] {b.Name}  world={rest[i].Pos.X:0.#},{rest[i].Pos.Y:0.#},{rest[i].Pos.Z:0.#}"
                + (roleOf.TryGetValue(i, out var role) ? $"  <== {role}" : ""));
        }

        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var (norm, normReport) = RestNormalizer.Normalize(rig.Skeleton, rig.ToMappingResult());
        foreach (var note in normReport.Notes)
            _out.WriteLine($"NORM NOTE: {note}");
        // Bind vs normalized world rot difference on the neck/head chain.
        foreach (var role in new[] { BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2,
            BoneRole.Neck, BoneRole.Head })
        {
            if (rig.BoneForRole(role) is not { } bi) { _out.WriteLine($"{role}: unmapped"); continue; }
            var d = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(
                System.Numerics.Quaternion.Normalize(rest[bi].Rot),
                System.Numerics.Quaternion.Normalize(norm.WorldRest[bi].Rot))))) * 180f / MathF.PI;
            _out.WriteLine($"{role} '{rig.Skeleton[bi].Name}': bind→norm world-rot delta {d:0.#} deg "
                + $"bindPos={rest[bi].Pos} normPos={norm.WorldRest[bi].Pos}");
        }
    }
}
