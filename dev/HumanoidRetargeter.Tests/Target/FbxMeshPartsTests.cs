using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class FbxMeshPartsTests
{
    [Fact]
    public void NamesComeFromConnectedMeshInstancesNotGeometryOrBones()
    {
        var root = Node("");
        var objects = Node("Objects");
        objects.Children.AddRange(new[]
        {
            Node("Geometry", 1L, "Geometry::arbitrary_geometry"),
            Node("Model", 2L, "Model::surface.one"),
            Node("Model", 3L, "Model::surface.two"),
            Node("Model", 4L, "Model::Head"),
            Node("Model", 5L, "Model::unused"),
        });
        var connections = Node("Connections");
        connections.Children.AddRange(new[]
        {
            Node("C", "OO", 1L, 2L),
            Node("C", "OO", 1L, 3L), // instanced geometry must keep both instances
            Node("C", "OO", 2L, 4L),
            Node("C", "OO", 1L, 2L), // repeated connection must not duplicate a mesh
            Node("C", "OP", 1L, 5L),
            Node("C", "OO"),
        });
        root.Children.AddRange(new[] { objects, connections });

        Assert.Equal(new[] { "surface.one", "surface.two" },
            FbxMeshParts.ReadNames(FbxBinaryWriter.Write(root)));
    }

    [Fact]
    public void MissingConnectionsDoNotFilterOutTheWholeModel()
    {
        var root = Node("");
        root.Children.Add(Node("Objects"));
        Assert.Empty(FbxMeshParts.ReadNames(FbxBinaryWriter.Write(root)));
    }

    [Fact]
    public void SeparateImportsKeepNamesScaleAndMaterialRemaps()
    {
        var remaps = new Dictionary<string, string> { ["eyes.vmat"] = "models/eyes.vmat" };
        var text = VmdlWriter.GenerateStandalone("", Array.Empty<AnimEntry>(), 1, "Hips",
            meshFilePath: "models/person.fbx", meshImportScale: 100, materialRemaps: remaps,
            meshImportNames: new[] { "surface.one", "surface.two" });
        var meshes = Meshes(text);
        Assert.Equal(2, meshes.Count);
        for (var i = 0; i < meshes.Count; i++)
        {
            Assert.Equal("models/person.fbx", meshes[i].GetString("filename"));
            Assert.Equal(100, Assert.IsType<KvDouble>(meshes[i]["import_scale"]).Value);
            Assert.Equal($"person_part_{i}", meshes[i].GetString("name"));
            var filter = Assert.IsType<KvObject>(meshes[i]["import_filter"]);
            Assert.True(Assert.IsType<KvBool>(filter["exclude_by_default"]).Value);
            var names = Assert.IsType<KvArray>(filter["exception_list"]);
            Assert.Equal(i == 0 ? "surface.one" : "surface.two",
                Assert.IsType<KvString>(Assert.Single(names.Items)).Value);
        }
        Assert.Contains("models/eyes.vmat", text);
    }

    [Fact]
    public void LegacyOutputIsUpgradedWithoutDroppingAnimationsAndIsIdempotent()
    {
        var text = VmdlWriter.GenerateStandalone("", new[]
        {
            new AnimEntry { Name = "walk", SourceFilename = "models/walk.dmx" },
        }, 1, "Hips", meshFilePath: "models/person.fbx");
        var names = new[] { "body", "eyes" };
        var updated = VmdlAugmenter.EnsureMeshFile(text, "models/person.fbx", 1,
            meshImportNames: names);
        Assert.Equal(2, Meshes(updated).Count);
        Assert.Contains("models/walk.dmx", updated);
        Assert.Equal(updated, VmdlAugmenter.EnsureMeshFile(updated, "models/person.fbx", 1,
            meshImportNames: names));
    }

    static List<KvObject> Meshes(string text)
    {
        var root = (KvObject)((KvObject)Kv3.Parse(text).Root)["rootNode"];
        var list = ((KvArray)root["children"]).Items.OfType<KvObject>()
            .Single(n => n.GetString("_class") == "RenderMeshList");
        return ((KvArray)list["children"]).Items.Cast<KvObject>().ToList();
    }

    static FbxNode Node(string name, params object[] properties)
    {
        var node = new FbxNode(name);
        node.Properties.AddRange(properties);
        return node;
    }
}
