using HumanoidRetargeterDmx;
using System.Numerics;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.Blocks;
using HumanoidRetargeterVrf.IO;
using HumanoidRetargeterVrf.ResourceTypes;
using HumanoidRetargeterVrf.Serialization.KeyValues;
using Xunit;
using Dmx = HumanoidRetargeterDmx.HumanoidRetargeterDmx;

namespace SmartPort.Parser.Tests;

public class MeshSkinningTests
{
    sealed class NoMaterials : IFileLoader
    {
        public Resource? LoadFile(string file) => null;
        public Resource? LoadFileCompiled(string file) => null;
    }

    [CompiledFixtureFact]
    public void RecoveredMeshesBindEveryInfluenceToItsOriginalNamedJoint()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c");
        using var resource = new Resource();
        resource.Read(path);
        var model = Assert.IsType<Model>(resource.DataBlock);
        var extract = new ModelExtract(resource, new NoMaterials());
        using var content = extract.ToContentFile();
        var checkedInfluences = 0;
        foreach (var mesh in extract.RenderMeshesToExtract)
        {
            using var dmx = Dmx.Load(content.SubFiles.Single(f => f.FileName == Path.GetFileName(mesh.FileName)).Extract());
            var root = Assert.IsType<Element>(dmx.Root!["model"]);
            var joints = Assert.IsAssignableFrom<IList<Element>>(root["jointList"]);
            Assert.Same(root, joints[0]);
            foreach (var bone in model.Skeleton.Bones)
            {
                Assert.Equal(bone.Name, joints[bone.Index + 1].Name);
                var transform = Assert.IsType<Element>(joints[bone.Index + 1]["transform"]);
                Assert.Equal(bone.Position, transform["position"]);
                Assert.Equal(bone.Angle, transform["orientation"]);
            }
            var vertices = dmx.AllElements.Where(e => e.ClassName == "DmeVertexData" && e.ContainsKey("blendindices$0")).ToArray();
            var count = mesh.Mesh.Data.GetSubCollection("m_skeleton").GetInt32Property("m_nBoneWeightCount");
            foreach (var vb in mesh.Mesh.VBIB.VertexBuffers)
            {
                var field = vb.InputLayoutFields.FirstOrDefault(f => f.SemanticName == "BLENDINDICES");
                if (field.SemanticName is null) continue;
                var original = VBIB.GetBlendIndicesArray(vb, field, mesh.BoneRemapTable);
                var stride = original.Length / (int)vb.ElementCount;
                var positions = VBIB.GetVector3AttributeArray(vb, vb.InputLayoutFields.First(f => f.SemanticName == "POSITION"));
                var vertexData = Assert.Single(vertices.Where(e => e["position$0"] is IEnumerable<Vector3> p && p.SequenceEqual(positions)));
                var exported = Assert.IsAssignableFrom<IList<int>>(vertexData["blendindices$0"]);
                Assert.Equal((int)vb.ElementCount * count, exported.Count);
                for (var i = 0; i < vb.ElementCount; i++)
                    for (var j = 0; j < count; j++)
                    {
                        Assert.Equal(model.Skeleton.Bones[original[i * stride + j]].Name, joints[exported[i * count + j]].Name);
                        checkedInfluences++;
                    }
            }
        }
        Assert.True(checkedInfluences > 1000);
    }

    [SkinFixtureFact]
    public void NativeCompiledPortPreservesTargetSkinWeights()
    {
        using var target = new Resource();
        using var port = new Resource();
        target.Read(Environment.GetEnvironmentVariable("HR_SMART_PORT_SKIN_TARGET")!);
        port.Read(Environment.GetEnvironmentVariable("HR_SMART_PORT_SKIN_RESULT")!);
        var original = ReadSkin(Assert.IsType<Model>(target.DataBlock)).ToArray();
        var compiled = ReadSkin(Assert.IsType<Model>(port.DataBlock)).ToArray();
        Assert.NotEmpty(original);
        Assert.NotEmpty(compiled);
        // ModelDoc may reorder/deduplicate vertices. Match by bind position and named weights,
        // never by vertex number or the combined skeleton's new bone indices.
        foreach (var vertex in compiled)
            Assert.True(original.Any(v => Vector3.DistanceSquared(v.Position, vertex.Position) < .000001f
                && v.Weights.Keys.Union(vertex.Weights.Keys).All(n =>
                    MathF.Abs(v.Weights.GetValueOrDefault(n) - vertex.Weights.GetValueOrDefault(n)) < .01f)),
                $"Skin assignment changed at {vertex.Position}: {string.Join(", ", vertex.Weights)}");
    }

    static IEnumerable<(Vector3 Position, Dictionary<string, float> Weights)> ReadSkin(Model model)
    {
        foreach (var mesh in model.GetEmbeddedMeshes())
            foreach (var vb in mesh.Mesh.VBIB.VertexBuffers)
            {
                var field = vb.InputLayoutFields.FirstOrDefault(f => f.SemanticName == "BLENDINDICES");
                if (field.SemanticName is null) continue;
                var indices = VBIB.GetBlendIndicesArray(vb, field, model.GetRemapTable(mesh.MeshIndex));
                var positions = VBIB.GetVector3AttributeArray(vb, vb.InputLayoutFields.First(f => f.SemanticName == "POSITION"));
                var weightField = vb.InputLayoutFields.FirstOrDefault(f => f.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS");
                var weights = weightField.SemanticName is null ? null : VBIB.GetBlendWeightsArray(vb, weightField);
                var stride = indices.Length / positions.Length;
                var count = mesh.Mesh.Data.GetSubCollection("m_skeleton").GetInt32Property("m_nBoneWeightCount");
                for (var i = 0; i < positions.Length; i++)
                {
                    var named = new Dictionary<string, float>();
                    for (var j = 0; j < count; j++)
                    {
                        var weight = weights is null ? 1f : weights[i * stride / 4 + j / 4][j % 4];
                        if (weight == 0) continue;
                        var name = model.Skeleton.Bones[indices[i * stride + j]].Name;
                        named[name] = named.GetValueOrDefault(name) + weight;
                    }
                    yield return (positions[i], named);
                }
            }
    }
}

public sealed class SkinFixtureFactAttribute : FactAttribute
{
    public SkinFixtureFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HR_SMART_PORT_SKIN_TARGET"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HR_SMART_PORT_SKIN_RESULT")))
            Skip = "Set HR_SMART_PORT_SKIN_TARGET and HR_SMART_PORT_SKIN_RESULT to the original and natively compiled Smart Port models.";
    }
}
