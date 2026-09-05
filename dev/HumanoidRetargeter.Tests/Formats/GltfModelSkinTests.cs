using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HumanoidRetargeter.Formats.Gltf;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class GltfModelSkinTests
{
    [Theory]
    [InlineData(true, true, .5f, 700f)]
    [InlineData(true, false, .5f, 1000f)]
    [InlineData(false, false, .5f, 10100f)]
    [InlineData(true, true, 0f, 600f)]
    public void MeshUsesSkinBindTransformsInsteadOfMeshNode(bool skinned, bool inverseBinds, float weight, float expectedX)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        // Three vertices, two equally weighted joints. Parent scale must be baked once.
        foreach (var y in new[] { 0f, 1f, 2f })
            foreach (var value in new[] { 1f, y, 0f }) writer.Write(value);
        for (var i = 0; i < 3; i++) writer.Write(new byte[] { 0, 1, 0, 0 });
        for (var i = 0; i < 3; i++)
            foreach (var value in new[] { weight, weight, 0f, 0f }) writer.Write(value);
        foreach (var x in new[] { -1f, -2f })
            foreach (var value in new[] { 1f,0,0,0, 0,1,0,0, 0,0,1,0, x,0,0,1 }) writer.Write(value);
        var json = $$$"""
        {
          "asset":{"version":"2.0"},
          "buffers":[{"byteLength":224,"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(stream.ToArray())}}}"}],
          "bufferViews":[
            {"buffer":0,"byteOffset":0,"byteLength":36},
            {"buffer":0,"byteOffset":36,"byteLength":12},
            {"buffer":0,"byteOffset":48,"byteLength":48},
            {"buffer":0,"byteOffset":96,"byteLength":128}],
          "accessors":[
            {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
            {"bufferView":1,"componentType":5121,"count":3,"type":"VEC4"},
            {"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"},
            {"bufferView":3,"componentType":5126,"count":2,"type":"MAT4"}],
          "nodes":[
            {"name":"root","scale":[2,2,2],"children":[1,2]},
            {"name":"jointA","translation":[3,0,0]},
            {"name":"jointB","translation":[5,0,0]},
            {"name":"mesh","mesh":0,"translation":[100,0,0]{{{(skinned ? ",\"skin\":0" : "")}}}}],
          "skins":[{"joints":[1,2]{{{(inverseBinds ? ",\"inverseBindMatrices\":3" : "")}}}}],
          "meshes":[{"primitives":[{"attributes":{"POSITION":0,"JOINTS_0":1,"WEIGHTS_0":2}}]}]
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(json);
        var skeleton = GltfImporter.Import(bytes).Skeleton;
        var dmx = GltfModelDmxWriter.Write(bytes, skeleton, "test");
        var match = Regex.Match(dmx, """"position\$0" "vector3_array"\s*\[\s*"([^"]+)"""");
        Assert.True(match.Success);
        var xPosition = float.Parse(match.Groups[1].Value.Split(' ')[0], CultureInfo.InvariantCulture);
        Assert.Equal(expectedX, xPosition, 3);
    }
}
