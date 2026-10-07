using System.Text;
using HumanoidRetargeter.Core.Formats.Gltf;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

/// <summary>
/// Malformed/hostile glTF inputs must fail with <see cref="FormatException"/> (the importer's
/// malformed-file contract) instead of hanging, overflowing, or OOM-allocating: node graphs
/// whose children lists form a cycle (unbounded DFS / parent walks), attacker-controlled
/// accessor counts (negative → <see cref="OverflowException"/> from the array allocation,
/// huge → OOM), and unbounded animation key-time spans (resampled frame count = span × fps —
/// an allocation bomb). All documents are tiny in-test JSON with base64 data: URI buffers.
/// </summary>
public class GltfMalformedInputTests
{
    private static FormatException ImportThrows(string json)
        => Assert.Throws<FormatException>(() => GltfImporter.Import(Encoding.UTF8.GetBytes(json)));

    private static string Base64Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    // ---------------------------------------------------------------- node-graph cycle

    [Fact]
    public void NodeGraphCycle_ThrowsFormatException()
    {
        // A → B → C → B: per spec each node has at most one parent, so ANY revisit during the
        // skeleton-selection DFS means the children lists are cyclic. Unguarded recursion
        // would overflow the stack (taking the host process down uncatchably); the importer
        // must fail cleanly instead.
        var json = """
            {
                "asset": { "version": "2.0" },
                "nodes": [
                    { "name": "A", "children": [1] },
                    { "name": "B", "children": [2] },
                    { "name": "C", "children": [1] }
                ],
                "skins": [ { "joints": [1] } ]
            }
            """;
        var e = ImportThrows(json);
        Assert.Contains("cycle", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- accessor count validation

    /// <summary>One animated node whose sampler INPUT accessor declares the given count; the
    /// backing data: URI buffer really holds only 2 floats (8 bytes).</summary>
    private static string GltfWithInputAccessorCount(string count)
    {
        var times = Base64Floats(0f, 1f);
        var rotations = Base64Floats(0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);
        return $$"""
            {
                "asset": { "version": "2.0" },
                "nodes": [ { "name": "root" } ],
                "animations": [ {
                    "channels": [ { "sampler": 0, "target": { "node": 0, "path": "rotation" } } ],
                    "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ]
                } ],
                "buffers": [
                    { "byteLength": 8, "uri": "data:application/octet-stream;base64,{{times}}" },
                    { "byteLength": 32, "uri": "data:application/octet-stream;base64,{{rotations}}" }
                ],
                "bufferViews": [
                    { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
                    { "buffer": 1, "byteOffset": 0, "byteLength": 32 }
                ],
                "accessors": [
                    { "bufferView": 0, "componentType": 5126, "count": {{count}}, "type": "SCALAR" },
                    { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC4" }
                ]
            }
            """;
    }

    [Fact]
    public void AccessorNegativeCount_ThrowsFormatException_NotOverflow()
    {
        // A negative count would reach `new float[count * comps]` and surface as
        // OverflowException — the count must be validated BEFORE any allocation sized by it.
        var e = ImportThrows(GltfWithInputAccessorCount("-1"));
        Assert.Contains("negative count", e.Message);
    }

    [Fact]
    public void AccessorHugeCount_ThrowsFormatException_NotOutOfMemory()
    {
        // 400M SCALAR floats (~1.6 GB) "backed" by an 8-byte buffer: the long-arithmetic
        // bounds check must reject the accessor before allocating anything count-sized.
        var e = ImportThrows(GltfWithInputAccessorCount("400000000"));
        Assert.Contains("reads past the end", e.Message);
    }

    // ---------------------------------------------------------------- clip duration cap

    [Fact]
    public void KeyTimeSpanBeyondCap_ThrowsFormatException()
    {
        // Two keys spanning 1e7 s would resample to 300M frames at 30 fps (an allocation
        // bomb); the importer caps the clip key-time span at one hour and treats anything
        // larger as malformed data.
        var times = Base64Floats(0f, 1e7f);
        var rotations = Base64Floats(0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);
        var json = $$"""
            {
                "asset": { "version": "2.0" },
                "nodes": [ { "name": "root" } ],
                "animations": [ {
                    "channels": [ { "sampler": 0, "target": { "node": 0, "path": "rotation" } } ],
                    "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ]
                } ],
                "buffers": [
                    { "byteLength": 8, "uri": "data:application/octet-stream;base64,{{times}}" },
                    { "byteLength": 32, "uri": "data:application/octet-stream;base64,{{rotations}}" }
                ],
                "bufferViews": [
                    { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
                    { "buffer": 1, "byteOffset": 0, "byteLength": 32 }
                ],
                "accessors": [
                    { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR" },
                    { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC4" }
                ]
            }
            """;
        var e = ImportThrows(json);
        Assert.Contains("key-time span", e.Message);
        Assert.Contains("3600", e.Message);
    }
}
