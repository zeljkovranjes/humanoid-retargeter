using System.Numerics;
using System.Text;
using HumanoidRetargeter.Formats.Gltf;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class GltfTargetBindTests
{
    [Theory]
    [InlineData(false, 1f, 2f)]
    [InlineData(true, 1f, 0f)]
    [InlineData(true, .5f, 2f)]
    public void TargetUsesAuthoredBindWithoutChangingAnimation(bool useBind, float inverseScale, float expectedX)
    {
        var values = new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-1,0,1,
            0,1, 2,1,0, 3,1,0 };
        values[0] = values[5] = values[10] = inverseScale;
        var buffer = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, buffer, 0, buffer.Length);
        var json = $$$"""
        {
          "asset":{"version":"2.0"},
          "buffers":[{"byteLength":96,"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(buffer)}}}"}],
          "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":64},
            {"buffer":0,"byteOffset":64,"byteLength":8},
            {"buffer":0,"byteOffset":72,"byteLength":24}],
          "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"MAT4"},
            {"bufferView":1,"componentType":5126,"count":2,"type":"SCALAR"},
            {"bufferView":2,"componentType":5126,"count":2,"type":"VEC3"}],
          "nodes":[{"name":"root","children":[1]},
            {"name":"joint","translation":[2,1,0],"rotation":[0,0,0.70710678,0.70710678],"children":[2]},
            {"name":"helper","translation":[0,1,0]}],
          "skins":[{"joints":[1],"inverseBindMatrices":0}],
          "animations":[{"samplers":[{"input":1,"output":2}],
            "channels":[{"sampler":0,"target":{"node":1,"path":"translation"}},
              {"sampler":0,"target":{"node":2,"path":"translation"}}]}]
        }
        """;
        var scene = GltfImporter.Import(Encoding.UTF8.GetBytes(json),
            new GltfImportOptions { UseSkinBindPose = useBind });
        var joint = scene.Skeleton.IndexOf("joint");
        var helper = scene.Skeleton.IndexOf("helper");
        Assert.Equal(new Vector3(expectedX * 100, 100, 0), scene.Skeleton.RestWorld[joint].Pos);
        var corrected = useBind && inverseScale == 1f;
        var expectedHelper = corrected ? new Vector3(0, 200, 0) : new Vector3(100, 100, 0);
        Assert.True(Vector3.Distance(expectedHelper, scene.Skeleton.RestWorld[helper].Pos) < .001f);
        var posedRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var expectedRotation = corrected ? Quaternion.Identity : posedRotation;
        Assert.True(MathF.Abs(Quaternion.Dot(expectedRotation, scene.Skeleton.RestWorld[joint].Rot)) > .99999f);
        Assert.True(MathF.Abs(Quaternion.Dot(posedRotation, scene.Clips[0].Frames[0][joint].Rot)) > .99999f);
        Assert.Equal(new Vector3(200, 100, 0), scene.Clips[0].Frames[0][joint].Pos);
        Assert.Equal(new Vector3(300, 100, 0), scene.Clips[0].Frames[^1][joint].Pos);
    }
}
