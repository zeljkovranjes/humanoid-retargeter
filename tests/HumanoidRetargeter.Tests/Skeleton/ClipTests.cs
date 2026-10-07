using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Skeleton;

public class ClipTests
{
    private static Clip WithFrames(int frameCount, float fps)
    {
        var frames = new List<XForm[]>(frameCount);
        for (var i = 0; i < frameCount; i++)
            frames.Add(new[] { XForm.Identity });
        return new Clip("clip", fps, looping: false, frames);
    }

    [Theory]
    [InlineData(0, 30f, 0f)]      // empty
    [InlineData(1, 30f, 0f)]      // a single sample spans no time
    [InlineData(2, 30f, 1f / 30f)]
    [InlineData(31, 30f, 1f)]     // 31 fence posts = 30 intervals = 1 s
    [InlineData(3, 30f, 2f / 30f)] // matches the DMX timeFrame duration (FrameCount-1)/Fps
    public void Duration_IsSpanBetweenFirstAndLastSample(int frameCount, float fps, float expected)
    {
        Assert.Equal(expected, WithFrames(frameCount, fps).Duration, 6);
    }

    [Fact]
    public void Duration_NonPositiveFpsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Clip("clip", 0f, false));
    }
}
