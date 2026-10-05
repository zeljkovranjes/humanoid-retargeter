using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Editor;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeterDmx;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace SmartPort.Parser.Tests;

/// <summary>Skips unless the s&amp;box install (its stock Citizen) is present.</summary>
public sealed class SboxInstallFactAttribute : FactAttribute
{
    public SboxInstallFactAttribute()
    {
        if (SmartPortHeadlessTests.CitizenRoot is null)
            Skip = "Needs the s&box install (addons/citizen/Assets); set HR_SBOX_ROOT.";
    }
}

/// <summary>
/// Smart Port without the editor: the stock Citizen ported onto a differently named, larger humanoid given as
/// ModelDoc source plus skeleton (how a model converter hands its output over).
/// </summary>
public class SmartPortHeadlessTests
{
    public static string? CitizenRoot => new[] { Environment.GetEnvironmentVariable("HR_SBOX_ROOT"), @"C:\Program Files (x86)\Steam\steamapps\common\sbox", @"D:\SteamLibrary\steamapps\common\sbox" }
        .Where(r => !string.IsNullOrEmpty(r)).Select(r => Path.Combine(r!, "addons", "citizen", "Assets"))
        .FirstOrDefault(r => File.Exists(Path.Combine(r, "models", "citizen", "citizen.vmdl_c")));

    /// <summary>Citizen joints renamed the ValveBiped way (a Source 1 rig); its helper bones left out.</summary>
    static readonly Dictionary<string, string> ValveBiped = BuildNames();

    static Dictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>
        {
            ["pelvis"] = "Pelvis", ["spine_0"] = "Spine", ["spine_1"] = "Spine1", ["spine_2"] = "Spine2", ["neck_0"] = "Neck1", ["head"] = "Head1",
        };
        foreach (var s in new[] { "L", "R" })
        {
            names[$"clavicle_{s}"] = $"{s}_Clavicle"; names[$"arm_upper_{s}"] = $"{s}_UpperArm"; names[$"arm_lower_{s}"] = $"{s}_Forearm";
            names[$"hand_{s}"] = $"{s}_Hand"; names[$"leg_upper_{s}"] = $"{s}_Thigh"; names[$"leg_lower_{s}"] = $"{s}_Calf";
            names[$"ankle_{s}"] = $"{s}_Foot"; names[$"ball_{s}"] = $"{s}_Toe0";
            foreach (var (finger, n) in new[] { ("thumb", 0), ("index", 1), ("middle", 2), ("ring", 3) })
                for (var j = 0; j < 3; j++)
                    names[$"finger_{finger}_{j}_{s}"] = j == 0 ? $"{s}_Finger{n}" : $"{s}_Finger{n}{j}";
        }
        return names.ToDictionary(p => p.Key, p => "ValveBiped_Bip01_" + p.Value);
    }

    /// <summary>The Citizen's mapped joints, 15% larger, under their ValveBiped names.</summary>
    static SmartPortBone[] Target(string citizen)
    {
        using var resource = new Resource { FileName = "models/citizen/citizen.vmdl" };
        resource.Read(citizen);
        var model = (Model)resource.DataBlock!;
        var source = SkeletonModel.Create(model.Skeleton.Bones.Select(b => new BoneDefinition(b.Name, b.Parent?.Name, new XForm(b.Position, b.Angle))).ToArray());
        var bones = new List<SmartPortBone>();
        var world = new Dictionary<string, XForm>();
        foreach (var bone in source.Bones)
        {
            if (!ValveBiped.TryGetValue(bone.Name, out var name)) continue;
            var parent = bone.ParentIndex;
            while (parent >= 0 && !ValveBiped.ContainsKey(source[parent].Name)) parent = source[parent].ParentIndex;
            var at = source.RestWorld[bone.Index];
            at = new XForm(at.Pos * 1.15f, at.Rot);
            world[name] = at;
            var local = parent < 0 ? at : XForm.ToLocal(world[ValveBiped[source[parent].Name]], at);
            bones.Add(new SmartPortBone
            {
                Name = name, Parent = parent < 0 ? null : ValveBiped[source[parent].Name],
                Position = new[] { local.Pos.X, local.Pos.Y, local.Pos.Z }, Rotation = new[] { local.Rot.X, local.Rot.Y, local.Rot.Z, local.Rot.W },
            });
        }
        return bones.ToArray();
    }

    const string TargetVmdl = VmdlWriter.Kv3Header + """
        { rootNode = { _class = "RootNode" children = [
            { _class = "RenderMeshList" children = [ { _class = "RenderMeshFile" name = "body" filename = "models/biped/body.dmx" } ] },
            { _class = "GameDataList" children = [ { _class = "GenericGameData" name = "eye_right" game_class = "eye" game_keys = { bonename = "ValveBiped_Bip01_Head1" } } ] },
            { _class = "PhysicsShapeList" children = [ ] }
          ] model_archetype = "" primary_associated_entity = "" anim_graph_name = "" base_model_name = "" } }
        """;

    static (SmartPortHeadlessResult Result, string Staging) Port(string seed)
    {
        var root = CitizenRoot!;
        var staging = Path.Combine(Path.GetTempPath(), "hr-smart-port-headless-" + Guid.NewGuid().ToString("N"));
        var request = new SmartPortHeadlessRequest
        {
            ContentRoots = new[] { root },
            TargetVmdl = TargetVmdl,
            TargetBones = Target(Path.Combine(root, "models", "citizen", "citizen.vmdl_c")),
            ModelPath = "models/biped/biped.vmdl",
            OutputFolder = "models/biped/biped_src/player",
            StagingDirectory = staging,
            KeepTargetLists = new[] { "GameDataList" },
            Seed = seed,
        };
        return (SmartPortHeadless.Port(request), staging);
    }

    static string Staged(string staging, string asset) => Path.Combine(staging, asset["models/biped/biped_src/player/".Length..]);

    [SboxInstallFact]
    public void CitizenSetupIsPortedOntoARenamedLargerSkeleton()
    {
        var (result, staging) = Port("biped");
        try
        {
            Assert.True(result.Retargeted);
            Assert.InRange(result.MotionScale, 1.14f, 1.16f);
            Assert.Equal("ValveBiped_Bip01_Pelvis", result.BoneMap["pelvis"]);
            Assert.Equal("foot_L_IK_target", result.BoneMap["foot_L_IK_target"]); // graph helpers keep their names
            Assert.Contains("IdlePose_Default", result.Sequences);
            Assert.True(result.Sequences.Length > 300);

            var vmdl = Kv3.Parse(result.Vmdl);
            var root = (KvObject)((KvObject)vmdl.Root)["rootNode"];
            Assert.Equal("models/biped/biped_src/player/biped.vanmgrph", root.GetString("anim_graph_name"));
            Assert.Equal(result.GraphPath, root.GetString("anim_graph_name"));
            Assert.Contains("\"models/biped/body.dmx\"", result.Vmdl); // the target's mesh, not the Citizen's
            Assert.DoesNotContain("citizen_torso", result.Vmdl);
            Assert.Contains("\"eye_right\"", result.Vmdl); // a kept target entry
            Assert.Contains("name = \"ValveBiped_Bip01_L_Hand\"", result.Vmdl); // the skeleton node made from the bones
            Assert.Contains("name = \"hold_R\"", result.Vmdl);

            // every file the model and graphs name exists; the Citizen's own meshes are not reported
            Assert.All(result.Files, f => Assert.True(File.Exists(Staged(staging, f)), f));
            Assert.DoesNotContain(result.Files, f => f.Contains("citizen_torso", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GraphPath, result.Files);

            // subgraphs that name renamed bones are copied and renamed; the graph points at them
            var copies = result.Files.Where(f => f.Contains("/subgraphs/")).ToList();
            Assert.NotEmpty(copies);
            var graph = File.ReadAllText(Staged(staging, result.GraphPath));
            var texts = copies.Select(c => File.ReadAllText(Staged(staging, c))).ToList();
            Assert.All(copies, c => Assert.True(graph.Contains(c) || texts.Any(t => t.Contains(c)), c));
            Assert.DoesNotContain(texts, t => t.Contains("m_hipBoneName = \"pelvis\""));
            Assert.Contains("models/biped/biped.vmdl", graph);
        }
        finally
        {
            Directory.Delete(staging, true);
        }
    }

    [SboxInstallFact]
    public void ASeededPortWritesTheSameBytes()
    {
        var (a, stagingA) = Port("same");
        var (b, stagingB) = Port("same");
        try
        {
            Assert.Equal(a.Vmdl, b.Vmdl);
            Assert.Equal(a.Files, b.Files);
            foreach (var file in a.Files.Where((_, i) => i % 25 == 0).Append(a.GraphPath))
                Assert.Equal(File.ReadAllBytes(Staged(stagingA, file)), File.ReadAllBytes(Staged(stagingB, file)));
        }
        finally
        {
            Directory.Delete(stagingA, true);
            Directory.Delete(stagingB, true);
        }
    }

    [SboxInstallFact]
    public void TheJsonEntryPointReportsFailuresAsErrors()
    {
        var bad = SmartPortHeadless.PortJson(JsonSerializer.Serialize(new SmartPortHeadlessRequest { ContentRoots = new[] { CitizenRoot! } }));
        Assert.Contains("\"Error\"", bad);
        var blob = new SmartPortHeadlessRequest
        {
            ContentRoots = new[] { CitizenRoot! }, TargetVmdl = TargetVmdl, ModelPath = "models/blob.vmdl", OutputFolder = "models/blob_src",
            StagingDirectory = Path.Combine(Path.GetTempPath(), "hr-smart-port-blob-" + Guid.NewGuid().ToString("N")),
            TargetBones = new[] { new SmartPortBone { Name = "root" }, new SmartPortBone { Name = "jiggle", Parent = "root", Position = new[] { 0f, 0, 10 } } },
        };
        var result = JsonDocument.Parse(SmartPortHeadless.PortJson(JsonSerializer.Serialize(blob))).RootElement;
        Assert.Contains("humanoid role", result.GetProperty("Error").GetString());
    }

    [Fact]
    public void ElementIdsAreRandomUnlessADeterministicScopeIsOpen()
    {
        Assert.NotEqual(ElementIds.Next(), ElementIds.Next());
        Guid[] Take(string seed)
        {
            using var scope = ElementIds.Deterministic(seed);
            return new[] { ElementIds.Next(), ElementIds.Next() };
        }
        Assert.Equal(Take("a"), Take("a"));
        Assert.NotEqual(Take("a"), Take("b"));
        using (ElementIds.Deterministic("outer"))
        {
            var first = ElementIds.Next();
            using (ElementIds.Deterministic("inner")) ElementIds.Next();
            var afterInner = ElementIds.Next();
            using var again = ElementIds.Deterministic("outer");
            Assert.Equal(first, ElementIds.Next());
            Assert.NotEqual(first, afterInner);
        }
    }
}
