using System.Text;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// .vrm input tests: a VRM is a glTF 2.0 GLB container plus a VRM extension whose
/// <c>humanoid.humanBones</c> block is an EXPLICIT, authored bone map. The importer must
/// surface it as <see cref="HumanoidRetargeter.Core.Skeleton.SourceScene.AuthoredMapping"/>
/// (<see cref="MappingSource.Authored"/>, confidence 1.0) and the mapping cascade must
/// prefer it over presets/auto detection. Both extension layouts are covered: VRM 0.x
/// (<c>extensions.VRM.humanoid.humanBones</c> as an array of <c>{bone, node}</c>) and
/// VRM 1.0 (<c>extensions.VRMC_vrm.humanoid.humanBones</c> as a name-keyed object).
/// </summary>
public class VrmInputTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    // ---------------------------------------------------------------- synthetic .vrm

    /// <summary>
    /// Tiny humanoid: deliberately OPAQUE node names (j00…j20) so no preset and no
    /// name-based auto-map could resolve it — proving the roles come from the VRM extension.
    /// Meter-scale T-pose (glTF units), Y-up. Index = position in this array.
    /// </summary>
    private static readonly (string Vrm, int Parent, float X, float Y, float Z)[] Rig =
    {
        ("hips", -1, 0f, 0.95f, 0f),
        ("spine", 0, 0f, 0.10f, 0f),
        ("chest", 1, 0f, 0.12f, 0f),
        ("neck", 2, 0f, 0.14f, 0f),
        ("head", 3, 0f, 0.06f, 0f),
        ("leftShoulder", 2, 0.06f, 0.08f, 0f),
        ("leftUpperArm", 5, 0.12f, 0f, 0f),
        ("leftLowerArm", 6, 0.26f, 0f, 0f),
        ("leftHand", 7, 0.25f, 0f, 0f),
        ("rightShoulder", 2, -0.06f, 0.08f, 0f),
        ("rightUpperArm", 9, -0.12f, 0f, 0f),
        ("rightLowerArm", 10, -0.26f, 0f, 0f),
        ("rightHand", 11, -0.25f, 0f, 0f),
        ("leftUpperLeg", 0, 0.09f, -0.05f, 0f),
        ("leftLowerLeg", 13, 0f, -0.40f, 0f),
        ("leftFoot", 14, 0f, -0.40f, 0f),
        ("leftToes", 15, 0f, -0.08f, 0.12f),
        ("rightUpperLeg", 0, -0.09f, -0.05f, 0f),
        ("rightLowerLeg", 17, 0f, -0.40f, 0f),
        ("rightFoot", 18, 0f, -0.40f, 0f),
        ("rightToes", 19, 0f, -0.08f, 0.12f),
    };

    private static string NodeName(int index) => $"j{index:00}";

    /// <summary>Builds the glTF JSON: nodes + skin + a 1 s hips-sway animation (data: URI
    /// buffers) + the requested VRM extension block.</summary>
    private static string VrmJson(int vrmVersion)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"asset\": { \"version\": \"2.0\" }, \"nodes\": [");
        for (var i = 0; i < Rig.Length; i++)
        {
            var children = Enumerable.Range(0, Rig.Length)
                .Where(c => Rig[c].Parent == i).ToList();
            sb.Append(i > 0 ? ", {" : " {");
            sb.Append($" \"name\": \"{NodeName(i)}\"");
            sb.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $", \"translation\": [{Rig[i].X}, {Rig[i].Y}, {Rig[i].Z}]"));
            if (children.Count > 0)
                sb.Append($", \"children\": [{string.Join(", ", children)}]");
            sb.Append(" }");
        }
        sb.Append(" ], \"skins\": [ { \"joints\": [");
        sb.Append(string.Join(", ", Enumerable.Range(0, Rig.Length)));
        sb.Append("] } ]");

        // One rotation channel on the hips (node 0): ±10° about Y over 1 s.
        var sin5 = MathF.Sin(MathF.PI / 36f);
        var cos5 = MathF.Cos(MathF.PI / 36f);
        var times = Base64Floats(0f, 1f);
        var rotations = Base64Floats(0f, -sin5, 0f, cos5, 0f, sin5, 0f, cos5);
        sb.Append(", \"animations\": [ { \"name\": \"sway\", \"channels\": [ " +
            "{ \"sampler\": 0, \"target\": { \"node\": 0, \"path\": \"rotation\" } } ], " +
            "\"samplers\": [ { \"input\": 0, \"output\": 1, \"interpolation\": \"LINEAR\" } ] } ]");
        sb.Append(", \"buffers\": [" +
            $" {{ \"byteLength\": 8, \"uri\": \"data:application/octet-stream;base64,{times}\" }}," +
            $" {{ \"byteLength\": 32, \"uri\": \"data:application/octet-stream;base64,{rotations}\" }} ]");
        sb.Append(", \"bufferViews\": [" +
            " { \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 8 }," +
            " { \"buffer\": 1, \"byteOffset\": 0, \"byteLength\": 32 } ]");
        sb.Append(", \"accessors\": [" +
            " { \"bufferView\": 0, \"componentType\": 5126, \"count\": 2, \"type\": \"SCALAR\" }," +
            " { \"bufferView\": 1, \"componentType\": 5126, \"count\": 2, \"type\": \"VEC4\" } ]");

        // ---- the VRM extension (authored humanoid bone map) ----
        if (vrmVersion == 0)
        {
            var entries = string.Join(", ", Rig.Select(
                (bone, i) => $"{{ \"bone\": \"{bone.Vrm}\", \"node\": {i} }}"));
            sb.Append($", \"extensions\": {{ \"VRM\": {{ \"exporterVersion\": \"test\", " +
                $"\"humanoid\": {{ \"humanBones\": [ {entries} ] }} }} }}");
        }
        else
        {
            var entries = string.Join(", ", Rig.Select(
                (bone, i) => $"\"{bone.Vrm}\": {{ \"node\": {i} }}"));
            sb.Append($", \"extensions\": {{ \"VRMC_vrm\": {{ \"specVersion\": \"1.0\", " +
                $"\"humanoid\": {{ \"humanBones\": {{ {entries} }} }} }} }}");
        }

        sb.Append(" }");
        return sb.ToString();
    }

    private static string Base64Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Wraps JSON into a GLB container (header + space-padded JSON chunk) — the
    /// physical layout of real .vrm files.</summary>
    private static byte[] Glb(string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var pad = (4 - jsonBytes.Length % 4) % 4;
        var chunkLength = jsonBytes.Length + pad;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(0x46546C67u);                  // 'glTF'
        bw.Write(2u);                           // container version
        bw.Write((uint)(12 + 8 + chunkLength)); // total length
        bw.Write((uint)chunkLength);
        bw.Write(0x4E4F534Au);                  // 'JSON'
        bw.Write(jsonBytes);
        for (var i = 0; i < pad; i++)
            bw.Write((byte)0x20);               // spaces, per spec
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] TinyVrm(int vrmVersion) => Glb(VrmJson(vrmVersion));

    // ---------------------------------------------------------------- import + mapping

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ImportSource_Vrm_SurfacesAuthoredMapping(int vrmVersion)
    {
        var scene = Retargeter.ImportSource(TinyVrm(vrmVersion), "avatar.vrm");

        Assert.NotNull(scene.AuthoredMapping);
        var map = scene.AuthoredMapping!;
        Assert.Equal(MappingSource.Authored, map.Source);
        Assert.Equal(1f, map.Confidence);
        Assert.Equal("vrm", map.ProfileName);
        Assert.Equal(Rig.Length, map.RoleToBone.Count); // all 21 humanoid bones resolved

        // Every VRM bone landed on the node it names (verified through the skeleton's
        // OPAQUE bone names — nothing here is name-detectable).
        var expectedRoles = new Dictionary<string, BoneRole>
        {
            ["hips"] = BoneRole.Hips,
            ["spine"] = BoneRole.Spine0,
            ["chest"] = BoneRole.Spine1,
            ["neck"] = BoneRole.Neck,
            ["head"] = BoneRole.Head,
            ["leftShoulder"] = BoneRole.ClavicleL,
            ["leftUpperArm"] = BoneRole.UpperArmL,
            ["leftLowerArm"] = BoneRole.LowerArmL,
            ["leftHand"] = BoneRole.HandL,
            ["rightUpperLeg"] = BoneRole.UpperLegR,
            ["rightLowerLeg"] = BoneRole.LowerLegR,
            ["rightFoot"] = BoneRole.FootR,
            ["rightToes"] = BoneRole.ToeR,
        };
        foreach (var (vrmName, role) in expectedRoles)
        {
            var nodeIndex = Array.FindIndex(Rig, b => b.Vrm == vrmName);
            var bone = Assert.Contains(role, (IDictionary<BoneRole, int>)map.RoleToBone);
            Assert.Equal(NodeName(nodeIndex), scene.Skeleton[bone].Name);
        }

        Assert.Contains(map.Notes, n => n.Contains(vrmVersion == 1 ? "VRM 1.0" : "VRM 0.x"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Inspect_Vrm_ReportsAuthoredProfileAtFullConfidence(int vrmVersion)
    {
        var inspected = Retargeter.Inspect(TinyVrm(vrmVersion), "avatar.vrm");

        Assert.Equal("vrm", inspected.Mapping.ProfileName);
        Assert.Equal(MappingSource.Authored, inspected.Mapping.Source);
        Assert.Equal(1f, inspected.Mapping.Confidence);
        Assert.False(inspected.Mapping.NeedsUserDecision);
        Assert.Equal(Rig.Length, inspected.Mapping.MappedRoleCount);
        Assert.Equal(1, inspected.TakeCount); // the sway animation
    }

    [Fact]
    public void ResolveMapping_AuthoredBeatsUserPreset_ButOverrideBeatsAuthored()
    {
        var scene = Retargeter.ImportSource(TinyVrm(0), "avatar.vrm");
        var authored = scene.AuthoredMapping!;

        // Authored wins over a user preset — the lookup is never even consulted.
        var lookupCalls = 0;
        var (map, report) = Retargeter.ResolveMapping(scene.Skeleton, null,
            _ => { lookupCalls++; return new MappingResult("user", MappingSource.UserPreset); },
            authored);
        Assert.Same(authored, map);
        Assert.Equal(MappingSource.Authored, report.Source);
        Assert.Equal(0, lookupCalls);

        // An explicit override (deliberate user decision) still beats the file's own map.
        var manual = new MappingResult("manual", MappingSource.Manual) { Confidence = 1f };
        manual.RoleToBone[BoneRole.Hips] = 0;
        var (overridden, overriddenReport) = Retargeter.ResolveMapping(
            scene.Skeleton, manual, null, authored);
        Assert.Same(manual, overridden);
        Assert.Equal(MappingSource.Manual, overriddenReport.Source);
    }

    [Fact]
    public void PlainGlb_WithoutVrmExtension_IsUnchanged()
    {
        // The existing mixamo-named GLB fixture: no VRM extension → no authored mapping,
        // and the cascade still lands on the shipped preset like before.
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "gltf", "freebvh.glb"));
        var scene = Retargeter.ImportSource(bytes, "freebvh.glb");
        Assert.Null(scene.AuthoredMapping);

        var inspected = Retargeter.Inspect(bytes, "freebvh.glb");
        Assert.Equal("mixamo", inspected.Mapping.ProfileName);
        Assert.Equal(MappingSource.Preset, inspected.Mapping.Source);
    }

    // ---------------------------------------------------------------- end-to-end facade

    [Fact]
    public void Convert_Vrm_EndToEnd_UsesAuthoredMapping()
    {
        // Convert the .vrm's animation onto a target built from its own authored rig — the
        // full pipeline (import → authored mapping → solve → cleanup → DMX → vmdl) must
        // succeed even though NO name-based detection could ever map the j00…j20 skeleton.
        var scene = Retargeter.ImportSource(TinyVrm(0), "avatar.vrm");
        var spec = new RetargetTargetSpec
        {
            Rig = TargetRig.FromSkeleton(scene.Skeleton, scene.AuthoredMapping!),
            VmdlScale = 1.0f,
            BaseModelPath = "",
            DefaultRootBone = scene.Skeleton[scene.AuthoredMapping!.RoleToBone[BoneRole.Hips]].Name,
        };

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = TinyVrm(0),
            SourceFileName = "avatar.vrm",
        }, spec);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.Equal("sway", clip.ClipName);
        Assert.False(string.IsNullOrEmpty(clip.DmxContent));

        Assert.NotNull(clip.Mapping);
        Assert.Equal("vrm", clip.Mapping!.ProfileName);
        Assert.Equal(MappingSource.Authored, clip.Mapping.Source);
        Assert.Equal(1f, clip.Mapping.Confidence);
        Assert.False(clip.Mapping.NeedsUserDecision);
    }
}
