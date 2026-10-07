using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests;

/// <summary>Shared deterministic random generators and assertion helpers.</summary>
internal static class TestUtil
{
    /// <summary>Random vector with components in [-range, range].</summary>
    public static Vector3 RandomVector(Random rng, float range = 10f)
        => new(
            (float)(rng.NextDouble() * 2.0 - 1.0) * range,
            (float)(rng.NextDouble() * 2.0 - 1.0) * range,
            (float)(rng.NextDouble() * 2.0 - 1.0) * range);

    /// <summary>Random unit vector (rejection-sampled, deterministic for a seeded rng).</summary>
    public static Vector3 RandomUnitVector(Random rng)
    {
        while (true)
        {
            var v = RandomVector(rng, 1f);
            var lsq = v.LengthSquared();
            if (lsq > 0.01f && lsq <= 1f)
                return Vector3.Normalize(v);
        }
    }

    /// <summary>Random unit quaternion from a random axis and angle in (-pi, pi).</summary>
    public static Quaternion RandomQuaternion(Random rng)
    {
        var axis = RandomUnitVector(rng);
        var angle = (float)((rng.NextDouble() * 2.0 - 1.0) * Math.PI);
        return Quaternion.CreateFromAxisAngle(axis, angle);
    }

    /// <summary>Random rigid transform with positions in [-100, 100] (cm scale).</summary>
    public static XForm RandomXForm(Random rng)
        => new(RandomVector(rng, 100f), RandomQuaternion(rng));

    /// <summary>Asserts two vectors are equal within an absolute per-component tolerance.</summary>
    public static void AssertVectorEqual(Vector3 expected, Vector3 actual, float eps)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= eps &&
            MathF.Abs(expected.Y - actual.Y) <= eps &&
            MathF.Abs(expected.Z - actual.Z) <= eps,
            $"Vectors differ beyond eps={eps}: expected {expected}, actual {actual}");
    }

    /// <summary>
    /// Asserts two quaternions represent the same rotation within a per-component tolerance,
    /// accounting for the q / -q double cover.
    /// </summary>
    public static void AssertQuaternionEqual(Quaternion expected, Quaternion actual, float eps)
    {
        var same = MaxComponentDelta(expected, actual);
        var flipped = MaxComponentDelta(expected, Quaternion.Negate(actual));
        Assert.True(
            MathF.Min(same, flipped) <= eps,
            $"Quaternions differ beyond eps={eps}: expected {expected}, actual {actual}");
    }

    private static float MaxComponentDelta(Quaternion a, Quaternion b)
        => MathF.Max(
            MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y)),
            MathF.Max(MathF.Abs(a.Z - b.Z), MathF.Abs(a.W - b.W)));

    /// <summary>Resolves a repo-relative path by walking up to the .sbproj directory
    /// (shared by the local-corpus repro tests).</summary>
    public static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
