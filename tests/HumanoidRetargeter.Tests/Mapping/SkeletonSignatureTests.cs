using System.Text.RegularExpressions;
using HumanoidRetargeter.Core.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class SkeletonSignatureTests
{
    [Fact]
    public void SameSkeletonLoadedTwice_ProducesEqualSignatures()
    {
        var a = SkeletonSignature.Compute(MappingFixtures.LoadZombieCrawl());
        var b = SkeletonSignature.Compute(MappingFixtures.LoadZombieCrawl());
        Assert.Equal(a, b);
    }

    [Fact]
    public void RenamingOneBone_ChangesSignature()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var original = SkeletonSignature.Compute(skeleton);

        var renamed = MappingFixtures.RenameAll(
            skeleton,
            i => skeleton[i].Name == "mixamorig1:LeftHand" ? "mixamorig1:LeftPaw" : skeleton[i].Name);

        Assert.NotEqual(original, SkeletonSignature.Compute(renamed));
    }

    [Fact]
    public void Signature_IsLowercaseSha256Hex()
    {
        var signature = SkeletonSignature.Compute(MappingFixtures.LoadActorCore());
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), signature);
    }

    [Fact]
    public void DifferentHierarchySameNames_ChangesSignature()
    {
        var flat = MappingFixtures.FromWorldPositions(new List<(string, string?, System.Numerics.Vector3)>
        {
            ("a", null, new(0, 0, 0)),
            ("b", "a", new(0, 1, 0)),
            ("c", "a", new(0, 2, 0)),
        });
        var chain = MappingFixtures.FromWorldPositions(new List<(string, string?, System.Numerics.Vector3)>
        {
            ("a", null, new(0, 0, 0)),
            ("b", "a", new(0, 1, 0)),
            ("c", "b", new(0, 2, 0)),
        });
        Assert.NotEqual(SkeletonSignature.Compute(flat), SkeletonSignature.Compute(chain));
    }
}
