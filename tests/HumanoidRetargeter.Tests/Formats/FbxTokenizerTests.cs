using HumanoidRetargeter.Core.Formats.Fbx;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

public class FbxTokenizerTests
{
    private static byte[] Fixture(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fbx", name));

    // ---- 1. Binary (version 7700, u64 offsets) ----

    [Fact]
    public void Binary_ZombieCrawl_HasExpectedTopLevelNodes()
    {
        var root = FbxTokenizer.Parse(Fixture("Zombie Crawl.fbx"));

        var names = root.Children.Select(c => c.Name).ToHashSet();
        Assert.Contains("FBXHeaderExtension", names);
        Assert.Contains("GlobalSettings", names);
        Assert.Contains("Objects", names);
        Assert.Contains("Connections", names);

        var header = root.Child("FBXHeaderExtension");
        Assert.NotNull(header);
        var version = header!.Child("FBXVersion");
        Assert.NotNull(version);
        Assert.True(version!.Prop<long>(0) >= 7400, $"FBXVersion was {version.Prop<long>(0)}");
    }

    // ---- 2. Compressed long-array property (zlib) ----

    [Fact]
    public void Binary_ZombieCrawl_AnimationCurveKeyTimes_DecompressAndIncrease()
    {
        var root = FbxTokenizer.Parse(Fixture("Zombie Crawl.fbx"));
        var objects = root.Child("Objects");
        Assert.NotNull(objects);

        var keyTimes = objects!.ChildrenNamed("AnimationCurve")
            .Select(c => c.Child("KeyTime"))
            .FirstOrDefault(kt => kt is not null);
        Assert.NotNull(keyTimes);

        long[] times = keyTimes!.AsLongArray(0);
        Assert.True(times.Length > 0);
        for (int i = 1; i < times.Length; i++)
            Assert.True(times[i] > times[i - 1], $"KeyTime not strictly increasing at index {i}");
    }

    // ---- 3. String properties + name/class split ----

    [Fact]
    public void Binary_ZombieCrawl_ModelNames_SplitIntoNameAndClass()
    {
        var root = FbxTokenizer.Parse(Fixture("Zombie Crawl.fbx"));
        var models = root.Child("Objects")!.ChildrenNamed("Model").ToList();
        Assert.NotEmpty(models);

        // Binary FBX 'S' props store "Name\x00\x01Class"; SplitName decodes that.
        var model = models[0];
        string raw = model.AsString(1);
        var (name, cls) = FbxNode.SplitName(raw);
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Equal("Model", cls);
    }

    // ---- 4. ASCII fallback ----

    [Fact]
    public void Ascii_ProbeFile_ParsesObjectsAndArrays()
    {
        var root = FbxTokenizer.Parse(Fixture("probe_ascii.fbx"));

        var objects = root.Child("Objects");
        Assert.NotNull(objects);

        // Some AnimationCurve has a KeyTime *N { a: ... } array parsed as long[].
        var keyTimes = objects!.ChildrenNamed("AnimationCurve")
            .Select(c => c.Child("KeyTime"))
            .FirstOrDefault(kt => kt is not null);
        Assert.NotNull(keyTimes);
        long[] times = keyTimes!.AsLongArray(0);
        Assert.True(times.Length > 0);

        // Float-ish array content comes back as double[] (tolerantly readable as float[]).
        var keyValues = objects.ChildrenNamed("AnimationCurve")
            .Select(c => c.Child("KeyValueFloat"))
            .FirstOrDefault(kv => kv is not null);
        Assert.NotNull(keyValues);
        Assert.True(keyValues!.AsFloatArray(0).Length > 0);
    }

    // ---- 5. Older binary (version 7400, u32 offsets) ----

    [Fact]
    public void Binary_7400_ParsesWithU32Offsets()
    {
        var root = FbxTokenizer.Parse(Fixture("prod_fix7400.fbx"));
        var names = root.Children.Select(c => c.Name).ToHashSet();
        Assert.Contains("Objects", names);
        Assert.Contains("FBXHeaderExtension", names);
        Assert.True(root.Children.Count >= 3);
    }

    // ---- 6. Malformed input ----

    [Fact]
    public void Binary_Truncated_ThrowsFormatException()
    {
        var truncated = Fixture("Zombie Crawl.fbx").AsSpan(0, 200).ToArray();
        Assert.Throws<FormatException>(() => FbxTokenizer.Parse(truncated));
    }

    [Fact]
    public void Binary_HugeDeclaredStringLength_ThrowsFormatException()
    {
        // A crafted header that declares a near-int.MaxValue string length: the bounds check
        // `pos + count` must not overflow int (which would slip past the check and surface
        // as ArgumentOutOfRangeException from AsSpan instead of FormatException).
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("Kaydara FBX Binary  "u8);
        w.Write((byte)0x00); w.Write((byte)0x1A); w.Write((byte)0x00);
        w.Write(7400u);          // version (< 7500: u32 node headers)
        w.Write(64u);            // endOffset (file is padded to 64 bytes)
        w.Write(1u);             // numProperties
        w.Write(16u);            // propertyListLen
        w.Write((byte)1);        // nameLen
        w.Write((byte)'N');      // name
        w.Write((byte)'S');      // string property type code
        w.Write(0x7FFFFFF0u);    // declared string length: pos + len overflows int
        while (ms.Length < 64)
            w.Write((byte)0);

        var ex = Assert.Throws<FormatException>(() => FbxTokenizer.Parse(ms.ToArray()));
        Assert.Contains("unexpected end of file", ex.Message);
    }

    // ---- 7. Unsupported FBX 6.x (Properties60) ----

    [Fact]
    public void Binary_Version6100_ThrowsFormatExceptionNamingFbx6()
    {
        // Doctor the header's version field of a real 7400 fixture down to 6100: FBX 6.x
        // stores transforms in Properties60, which would otherwise parse to identity
        // transforms silently.
        var data = Fixture("prod_fix7400.fbx").ToArray();
        BitConverter.GetBytes(6100u).CopyTo(data, 23); // version sits right after the 23-byte magic

        var ex = Assert.Throws<FormatException>(() => FbxTokenizer.Parse(data));
        Assert.Contains("6.x", ex.Message);
        Assert.Contains("6100", ex.Message);
    }

    // ---- helpers: tolerant typed accessors ----

    [Fact]
    public void Binary_ZombieCrawl_FloatArrayRequestedFromDoubleArray_Converts()
    {
        var root = FbxTokenizer.Parse(Fixture("Zombie Crawl.fbx"));
        // Lcl Translation defaults etc. live in Properties70, but array-bearing nodes
        // (e.g. AnimationCurve KeyValueFloat 'f' or geometry 'd' arrays) must be readable
        // as either float[] or double[] regardless of stored width.
        var curve = root.Child("Objects")!.ChildrenNamed("AnimationCurve")
            .First(c => c.Child("KeyValueFloat") is not null);
        var kv = curve.Child("KeyValueFloat")!;
        float[] f = kv.AsFloatArray(0);
        double[] d = kv.AsDoubleArray(0);
        Assert.Equal(f.Length, d.Length);
        if (f.Length > 0)
            Assert.Equal(d[0], f[0], 3);
    }
}
