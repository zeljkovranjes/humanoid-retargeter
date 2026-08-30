using System;
using System.Text;
using HumanoidRetargeter.Formats.Ant;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public sealed class AntSkeletonJsonTests
{
    [Fact]
    public void ParsesBareOrderedJointArray()
    {
        var data = Encoding.UTF8.GetBytes(
            """[{"name":"Reference","parent":-1},{"name":"Hips","parent":0}]""");

        Assert.True(AntSkeletonJson.Looks(data));
        var joints = AntSkeletonJson.Parse(data);

        Assert.Equal(2, joints.Count);
        Assert.Equal(new AntSkeletonJson.Joint("Reference", -1), joints[0]);
        Assert.Equal(new AntSkeletonJson.Joint("Hips", 0), joints[1]);
    }

    [Fact]
    public void ParsesExtractorMetadataWrapper()
    {
        var data = Encoding.UTF8.GetBytes(
            """{"name":"skeleton_boxer","joints":[{"name":"Reference","parent":-1},{"name":"AITrajectory","parent":0}]}""");

        Assert.True(AntSkeletonJson.Looks(data));
        var joints = AntSkeletonJson.Parse(data);

        Assert.Equal(2, joints.Count);
        Assert.Equal("AITrajectory", joints[1].Name);
        Assert.Equal(0, joints[1].Parent);
    }

    [Fact]
    public void RejectsInvalidParentIndex()
    {
        var data = Encoding.UTF8.GetBytes(
            """[{"name":"Reference","parent":2}]""");

        Assert.Throws<FormatException>(() => AntSkeletonJson.Parse(data));
    }
}
