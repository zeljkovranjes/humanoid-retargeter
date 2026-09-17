using HumanoidRetargeter.Editor;
using HumanoidRetargeterCompression;
using HumanoidRetargeterVrf;
using Xunit;

namespace SmartPort.Parser.Tests;

public class RecoveryTests
{
    [Fact]
    public void EmbeddedZstdDecodesFrameWithoutNativeLibrary()
    {
        using var decoder = new HumanoidRetargeterZstd.Decompressor();
        byte[] frame = [0x28, 0xB5, 0x2F, 0xFD, 0x20, 4, 0x21, 0, 0, 116, 101, 115, 116];
        var output = new byte[4];
        Assert.True(decoder.TryUnwrap(frame, output, out var count));
        Assert.Equal(4, count);
        Assert.Equal("test", System.Text.Encoding.ASCII.GetString(output));
    }

    [Theory]
    [InlineData(0x2FD82220u, .068791926f, .017197981f, .99748284f)]
    [InlineData(0x30080230u, .104684785f, 0f, .99450547f)]
    [InlineData(0x000FFFFFu, 0f, 0f, -1f)]
    public void SboxPackedNormalsMatchNativeRecompiledFixture(uint packed, float x, float y, float z)
    {
        var buffer = new HumanoidRetargeterVrf.Blocks.VBIB.OnDiskBufferData
        {
            ElementCount = 1, ElementSizeInBytes = 4, Data = BitConverter.GetBytes(packed)
        };
        var attribute = new HumanoidRetargeterVrf.Blocks.VBIB.RenderInputLayoutField
        {
            SemanticName = "NORMAL", Format = HumanoidRetargeterVrf.DXGI_FORMAT.R10G10B10A2_UNORM
        };
        var (normals, tangents) = HumanoidRetargeterVrf.Blocks.VBIB.GetNormalTangentArray(buffer, attribute);
        Assert.True(System.Numerics.Vector3.Distance(normals[0], new(x, y, z)) < .002f);
        Assert.Empty(tangents); // ModelDoc regenerates tangents from UVs.
    }

    [Theory]
    [InlineData(90, 123, 26)]
    [InlineData(-90, -123, 26)]
    [InlineData(89.99999, -50, 80)]
    [InlineData(-30, 55, -110)]
    public void RecoveredEulerAnglesPreserveRotationAtPoles(float pitch, float yaw, float roll)
    {
        static System.Numerics.Quaternion Rotation(float p, float y, float r) =>
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, y * MathF.PI / 180)
            * System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, p * MathF.PI / 180)
            * System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, r * MathF.PI / 180);
        var original = Rotation(pitch, yaw, roll);
        var angles = HumanoidRetargeterVrf.IO.ModelExtract.ToEulerAngles(original);
        var actual = Rotation(angles.X, angles.Y, angles.Z);
        Assert.True(MathF.Abs(System.Numerics.Quaternion.Dot(original, actual)) > .999999f);
    }

    [Fact]
    public void Lz4DecodesLiteralsOverlapsAndChainedHistory()
    {
        byte[] output = new byte[8];
        Assert.Equal(8, Lz4.Decode(new byte[] { 0x40, 97, 98, 99, 100, 4, 0 }, output));
        Assert.Equal("abcdabcd", System.Text.Encoding.ASCII.GetString(output));
        Assert.Equal(8, Lz4.Decode(new byte[] { 0x13, 97, 1, 0 }, output));
        Assert.Equal("aaaaaaaa", System.Text.Encoding.ASCII.GetString(output));
        using var chain = new Lz4Chain(65536, 0);
        Assert.True(chain.DecodeAndDrain(new byte[] { 0x40, 97, 98, 99, 100 }, output, out var count));
        Assert.Equal(4, count);
        Assert.True(chain.DecodeAndDrain(new byte[] { 0, 4, 0 }, output, out count));
        Assert.Equal(4, count);
        Assert.Equal("abcd", System.Text.Encoding.ASCII.GetString(output, 0, count));
    }

    [Theory]
    [InlineData(new byte[] { 0xF0 })]
    [InlineData(new byte[] { 0, 0, 0 })]
    [InlineData(new byte[] { 0, 1, 0 })]
    [InlineData(new byte[] { 0x40, 1 })]
    public void Lz4RejectsTruncatedAndInvalidInput(byte[] input)
        => Assert.Throws<InvalidDataException>(() => Lz4.Decode(input, new byte[16]));

    [Fact]
    public void Lz4RejectsOutputOverflow()
        => Assert.Throws<InvalidDataException>(() => Lz4.Decode(new byte[] { 0x20, 1, 2 }, new byte[1]));

    [Theory]
    [InlineData("../escape.dmx")]
    [InlineData("nested/../../escape.dmx")]
    [InlineData("C:/escape.dmx")]
    [InlineData("file.dmx:alternate")]
    public void RecoveredPathsCannotEscapeOutput(string relative)
        => Assert.Throws<InvalidDataException>(() => CompiledAssetRecovery.ConfinedPath(Path.Combine(Path.GetTempPath(), "smart-port-test"), relative));

    [Fact]
    public void InvalidResourceHeaderIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[32]);
            using var resource = new Resource();
            Assert.Throws<InvalidDataException>(() => resource.Read(path));
        }
        finally { File.Delete(path); }
    }
}
