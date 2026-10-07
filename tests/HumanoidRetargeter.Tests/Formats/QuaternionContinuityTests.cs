using System.Numerics;
using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class QuaternionContinuityTests
{
    private static Quaternion Rz(float radians)
        => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);

    private static void AssertAligned(IReadOnlyList<Quaternion> track)
    {
        for (var i = 1; i < track.Count; i++)
            Assert.True(Quaternion.Dot(track[i - 1], track[i]) >= 0f,
                $"negative dot between samples {i - 1} and {i}: {track[i - 1]} -> {track[i]}");
    }

    [Fact]
    public void Align_FlippedSample_NegatedBackOntoSameHemisphere()
    {
        var track = new[] { Quaternion.Identity, Quaternion.Negate(Rz(0.1f)) };
        QuaternionContinuity.Align(track);

        AssertAligned(track);
        // Semantically the same rotation: only the sign changed.
        Assert.Equal(Rz(0.1f).X, track[1].X, 6);
        Assert.Equal(Rz(0.1f).W, track[1].W, 6);
    }

    [Fact]
    public void Align_AlternatingFlips_PropagatesCumulatively()
    {
        // Flips must be judged against the ALIGNED predecessor, not the raw one: after the
        // second sample is negated, the third (raw-consistent with the second) must flip too.
        var track = new[]
        {
            Rz(0.0f),
            Quaternion.Negate(Rz(0.1f)),
            Quaternion.Negate(Rz(0.2f)),
            Rz(0.3f),
        };
        QuaternionContinuity.Align(track);
        AssertAligned(track);
    }

    [Fact]
    public void Align_AlreadyContinuous_Unchanged()
    {
        var track = new[] { Rz(0.0f), Rz(0.1f), Rz(0.2f) };
        var expected = (Quaternion[])track.Clone();
        QuaternionContinuity.Align(track);
        Assert.Equal(expected, track);
    }

    [Fact]
    public void AlignFrames_PerBoneTracks_AlignedIndependently()
    {
        var frames = new List<XForm[]>
        {
            new[]
            {
                new XForm(Vector3.Zero, Rz(0.0f)),
                new XForm(Vector3.Zero, Rz(1.0f)),
            },
            new[]
            {
                new XForm(Vector3.Zero, Quaternion.Negate(Rz(0.1f))), // bone 0 flips
                new XForm(Vector3.Zero, Rz(1.1f)),                    // bone 1 stays
            },
            new[]
            {
                new XForm(Vector3.Zero, Quaternion.Negate(Rz(0.2f))),
                new XForm(Vector3.Zero, Quaternion.Negate(Rz(1.2f))), // bone 1 flips
            },
        };

        QuaternionContinuity.AlignFrames(frames);

        for (var bone = 0; bone < 2; bone++)
            AssertAligned(frames.Select(f => f[bone].Rot).ToList());

        // Positions untouched.
        foreach (var frame in frames)
            foreach (var x in frame)
                Assert.Equal(Vector3.Zero, x.Pos);
    }

    [Fact]
    public void AlignFrames_SingleFrame_NoOp()
    {
        var frames = new List<XForm[]> { new[] { new XForm(Vector3.Zero, Rz(0.4f)) } };
        QuaternionContinuity.AlignFrames(frames);
        Assert.Equal(Rz(0.4f), frames[0][0].Rot);
    }
}
