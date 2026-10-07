using HumanoidRetargeter.Core.Target;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>
/// RenderMeshList embedding in standalone vmdls (user report 2026-07-04): a custom FBX
/// target has no compiled base model, so its generated vmdl used to compile into an EMPTY
/// model — 0 bones, 0 sequences — and playing any converted animation did nothing. With
/// <see cref="RetargetTargetSpec.MeshFilePath"/> set, the vmdl must embed the mesh source
/// so the compiled model carries the skeleton and skin the sequences play on.
/// </summary>
public class VmdlMeshEmbedTests
{
    private static readonly AnimEntry[] OneAnim =
    {
        new() { Name = "clip", SourceFilename = "animations/clip.dmx" },
    };

    [Fact]
    public void GenerateStandalone_WithMeshFile_EmbedsRenderMeshNode()
    {
        var vmdl = VmdlWriter.GenerateStandalone(
            "", OneAnim, 0.3937f, "pelvis",
            meshFilePath: "animations/retargeted/fox.fbx", meshImportScale: 100f);

        Assert.Contains("\"RenderMeshList\"", vmdl);
        Assert.Contains("\"RenderMeshFile\"", vmdl);
        Assert.Contains("animations/retargeted/fox.fbx", vmdl);
        Assert.Contains("import_scale = 100", vmdl);
        Assert.Contains("name = \"fox\"", vmdl);
        // The cm→inch modifier and the animation list still ride along.
        Assert.Contains("ModelModifier_ScaleAndMirror", vmdl);
        Assert.Contains("\"AnimationList\"", vmdl);
        // Round-trips through the kv3 parser (augmentation reads these files back).
        Kv3.Parse(vmdl);
    }

    [Fact]
    public void GenerateStandalone_WithoutMeshFile_OmitsRenderMeshNode()
    {
        var vmdl = VmdlWriter.GenerateStandalone(
            RetargetTargetSpec.SboxHumanMalePath, OneAnim, 0.3937f, "pelvis");

        Assert.DoesNotContain("RenderMeshList", vmdl);
        Assert.DoesNotContain("RenderMeshFile", vmdl);
    }

    /// <summary>The accumulate path augments an existing standalone vmdl IN PLACE, so a
    /// pipeline-owned output generated before mesh embedding existed (empty
    /// base_model_name, no mesh) would stay an empty unplayable model forever - user
    /// report: "convert onto the custom FBX, open the new vmdl, the animation doesn't
    /// play". EnsureMeshFile heals it.</summary>
    [Fact]
    public void EnsureMeshFile_HealsPreMeshEmbedVmdl_AndIsIdempotent()
    {
        // A pre-fix standalone output: no base model, no mesh, one accumulated animation.
        var old = VmdlWriter.GenerateStandalone("", OneAnim, 0.3937f, "pelvis");
        Assert.DoesNotContain("RenderMeshFile", old);

        var healed = VmdlAugmenter.EnsureMeshFile(old, "animations/retargeted/die.fbx", 2.54f);
        Assert.Contains("\"RenderMeshFile\"", healed);
        Assert.Contains("animations/retargeted/die.fbx", healed);
        Assert.Contains("import_scale = 2.54", healed);
        // The accumulated animation survives.
        Assert.Contains("\"clip\"", healed);
        Kv3.Parse(healed);

        // Same mesh again: no change at all.
        Assert.Equal(healed, VmdlAugmenter.EnsureMeshFile(healed, "animations/retargeted/die.fbx", 2.54f));

        // Switched target FBX: the pipeline-owned mesh node is replaced, not duplicated.
        var switched = VmdlAugmenter.EnsureMeshFile(healed, "animations/retargeted/fox.fbx", 100f);
        Assert.Contains("animations/retargeted/fox.fbx", switched);
        Assert.DoesNotContain("animations/retargeted/die.fbx", switched);
        Kv3.Parse(switched);
    }

    [Fact]
    public void EnsureMeshFile_EmptyMeshPath_IsNoOp()
    {
        var vmdl = VmdlWriter.GenerateStandalone("", OneAnim, 0.3937f, "pelvis");
        Assert.Same(vmdl, VmdlAugmenter.EnsureMeshFile(vmdl, "", 1f));
    }

    /// <summary>FBX materials are bare names the compiler cannot resolve as resource paths
    /// ("Trying to load an illegal resource name X.vmat") - generated vmdls must remap them
    /// to the real vmat files via MaterialGroupList (the shipped citizen mechanism).</summary>
    [Fact]
    public void GenerateStandalone_WithMaterialRemaps_EmitsMaterialGroup()
    {
        var remaps = new Dictionary<string, string>
        {
            ["mi_dante_head.vmat"] = "animations/retargeted/mi_dante_head.vmat",
        };
        var vmdl = VmdlWriter.GenerateStandalone(
            "", OneAnim, 0.3937f, "pelvis",
            meshFilePath: "animations/retargeted/die.fbx", meshImportScale: 2.54f,
            materialRemaps: remaps);

        Assert.Contains("\"MaterialGroupList\"", vmdl);
        Assert.Contains("\"DefaultMaterialGroup\"", vmdl);
        Assert.Contains("from = \"mi_dante_head.vmat\"", vmdl);
        Assert.Contains("to = \"animations/retargeted/mi_dante_head.vmat\"", vmdl);
        Kv3.Parse(vmdl);

        // And the healing path adds the same node to pre-remap outputs (idempotent).
        var old = VmdlWriter.GenerateStandalone("", OneAnim, 0.3937f, "pelvis");
        var healed = VmdlAugmenter.EnsureMeshFile(
            old, "animations/retargeted/die.fbx", 2.54f, remaps);
        Assert.Contains("\"MaterialGroupList\"", healed);
        Assert.Contains("from = \"mi_dante_head.vmat\"", healed);
        Assert.Equal(healed, VmdlAugmenter.EnsureMeshFile(
            healed, "animations/retargeted/die.fbx", 2.54f, remaps));
    }

    /// <summary>An output vmdl whose remap table predates a texture-matching improvement
    /// must LEARN new entries on the next conversion (observed: textured preview but
    /// untextured ModelDoc forever) - while existing entries stay untouched.</summary>
    [Fact]
    public void EnsureMeshFile_MergesNewMaterialRemaps()
    {
        var oldRemaps = new Dictionary<string, string> { ["a.vmat"] = "out/a.vmat" };
        var old = VmdlWriter.GenerateStandalone("", OneAnim, 0.3937f, "pelvis",
            meshFilePath: "out/m.fbx", meshImportScale: 1f, materialRemaps: oldRemaps);

        var merged = VmdlAugmenter.EnsureMeshFile(old, "out/m.fbx", 1f,
            new Dictionary<string, string>
            {
                ["a.vmat"] = "IGNORED/other.vmat",
                ["homer.vmat"] = "out/homer.vmat",
            });
        Assert.Contains("to = \"out/a.vmat\"", merged);       // existing entry untouched
        Assert.DoesNotContain("IGNORED", merged);
        Assert.Contains("from = \"homer.vmat\"", merged);     // new entry learned
        Assert.Contains("to = \"out/homer.vmat\"", merged);
        Kv3.Parse(merged);
        Assert.Equal(merged, VmdlAugmenter.EnsureMeshFile(merged, "out/m.fbx", 1f,
            new Dictionary<string, string> { ["homer.vmat"] = "out/homer.vmat" }));
    }
}
