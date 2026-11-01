#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;

namespace HumanoidRetargeter.Core.Sprites;

using Vector2 = System.Numerics.Vector2; // s&box compat: shadow engine's global-namespace Vector2 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Draws a <see cref="SpriteCharacter"/> in a pose, ported from Bonebase's skin.lua: each
/// bone a signed distance field (an uneven capsule, plus bulges), the bones of a part merged
/// with a smooth minimum, and a one-pixel outline around the whole part. Being redrawn, the
/// skin keeps its radii however a bone turns or shortens, with no seam at a joint.
/// </summary>
public static class SpriteSkin
{
    /// <summary>The outline's brightness against the part's colour.</summary>
    private const float LineShade = 0.55f;

    private const double NearZero = 1e-9;

    private static readonly (int Ax, int Ay, int Bx, int By)[] CornerPairs =
    {
        (0, -1, 1, 0), (1, 0, 0, 1), (0, 1, -1, 0), (-1, 0, 0, -1),
    };

    /// <summary>
    /// Draws one frame as RGBA bytes, row by row. <paramref name="heads"/> and
    /// <paramref name="tails"/> place each bone on the canvas, indexed like
    /// <see cref="SpriteCharacter.Bones"/>; <paramref name="stacking"/> lists part indices
    /// bottom first.
    /// </summary>
    public static byte[] Draw(SpriteCharacter character, Vector2[] heads, Vector2[] tails, IReadOnlyList<int> stacking)
    {
        ArgumentNullException.ThrowIfNull(character);
        var pixels = new byte[character.Width * character.Height * 4];
        foreach (var index in stacking)
            DrawPart(character, character.Parts[index], heads, tails, pixels);
        return pixels;
    }

    private static void DrawPart(SpriteCharacter character, SpritePart part, Vector2[] heads, Vector2[] tails, byte[] pixels)
    {
        var bones = new List<int>();
        for (var i = 0; i < character.Bones.Count; i++)
        {
            if (character.Bones[i].Part == part.Id)
                bones.Add(i);
        }
        if (bones.Count == 0)
            return;

        var (x0, y0, x1, y1) = Bounds(character, part, bones, heads, tails);
        if (x1 < x0 || y1 < y0)
            return;
        var width = x1 - x0 + 1;
        var height = y1 - y0 + 1;

        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                mask[y * width + x] = PartDistance(character, part, bones, heads, tails, new Vector2(x0 + x + 0.5f, y0 + y + 0.5f)) <= 0f;
        }

        var line = Outline(mask, width, height);
        var lineRed = (byte)(part.Red * LineShade);
        var lineGreen = (byte)(part.Green * LineShade);
        var lineBlue = (byte)(part.Blue * LineShade);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var k = y * width + x;
                if (!mask[k])
                    continue;
                var i = ((y0 + y) * character.Width + x0 + x) * 4;
                var isLine = line[k];
                pixels[i] = isLine ? lineRed : part.Red;
                pixels[i + 1] = isLine ? lineGreen : part.Green;
                pixels[i + 2] = isLine ? lineBlue : part.Blue;
                pixels[i + 3] = 255;
            }
        }
    }

    private static (int X0, int Y0, int X1, int Y1) Bounds(
        SpriteCharacter character, SpritePart part, List<int> bones, Vector2[] heads, Vector2[] tails)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var i in bones)
        {
            var bone = character.Bones[i];
            var margin = new Vector2(MathF.Max(bone.HeadRadius, bone.TailRadius) + MaxBulge(bone) + part.Blend + 1f);
            min = Vector2.Min(min, Vector2.Min(heads[i], tails[i]) - margin);
            max = Vector2.Max(max, Vector2.Max(heads[i], tails[i]) + margin);
        }
        return (Math.Max(0, (int)MathF.Floor(min.X)), Math.Max(0, (int)MathF.Floor(min.Y)),
            Math.Min(character.Width - 1, (int)MathF.Ceiling(max.X)),
            Math.Min(character.Height - 1, (int)MathF.Ceiling(max.Y)));
    }

    private static float MaxBulge(SpriteBone bone)
    {
        var most = 0f;
        foreach (var bulge in bone.BulgeLeft)
            most = MathF.Max(most, bulge.Radius);
        foreach (var bulge in bone.BulgeRight)
            most = MathF.Max(most, bulge.Radius);
        return most;
    }

    private static double PartDistance(
        SpriteCharacter character, SpritePart part, List<int> bones, Vector2[] heads, Vector2[] tails, Vector2 point)
    {
        var distance = double.PositiveInfinity;
        foreach (var i in bones)
        {
            var bone = BoneDistance(character.Bones[i], heads[i], tails[i], point);
            distance = double.IsPositiveInfinity(distance) ? bone : SmoothMin(distance, bone, part.Blend);
        }
        return distance;
    }

    private static double SmoothMin(double a, double b, double radius)
    {
        if (radius <= 0)
            return Math.Min(a, b);
        var h = Math.Max(radius - Math.Abs(a - b), 0) / radius;
        return Math.Min(a, b) - h * h * radius * 0.25;
    }

    /// <summary>Signed distance to a bone's skin: Inigo Quilez's uneven capsule. The length is
    /// the posed one, so a shortened bone is a shorter capsule of the same radii.</summary>
    private static double BoneDistance(SpriteBone bone, Vector2 head, Vector2 tail, Vector2 point)
    {
        double dx = tail.X - head.X, dy = tail.Y - head.Y;
        double px = point.X - head.X, py = point.Y - head.Y;
        double headRadius = bone.HeadRadius, tailRadius = bone.TailRadius;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!(length > NearZero))
            return Math.Sqrt(px * px + py * py) - Math.Max(headRadius, tailRadius);

        var along = (px * dx + py * dy) / length;
        var side = (-px * dy + py * dx) / length;
        var across = Math.Abs(side);
        var slope = (headRadius - tailRadius) / length;

        double distance;
        if (slope * slope >= 1)
        {
            distance = Math.Min(
                Math.Sqrt(along * along + side * side) - headRadius,
                Math.Sqrt((along - length) * (along - length) + side * side) - tailRadius);
        }
        else
        {
            var a = Math.Sqrt(1 - slope * slope);
            var k = -slope * across + a * along;
            if (k < 0)
                distance = Math.Sqrt(across * across + along * along) - headRadius;
            else if (k > a * length)
                distance = Math.Sqrt(across * across + (along - length) * (along - length)) - tailRadius;
            else
                distance = across * a + along * slope - headRadius;
        }

        var bulges = side < 0 ? bone.BulgeLeft : bone.BulgeRight;
        return bulges.Length > 0 ? distance - BulgeAt(bulges, along / length) : distance;
    }

    private static double BulgeAt(SpriteBulge[] bulges, double t)
    {
        if (t <= 0 || t >= 1)
            return 0;
        double previousAt = 0, previousRadius = 0;
        foreach (var bulge in bulges)
        {
            if (t <= bulge.At)
                return Ease(previousAt, previousRadius, bulge.At, bulge.Radius, t);
            (previousAt, previousRadius) = (bulge.At, bulge.Radius);
        }
        return Ease(previousAt, previousRadius, 1, 0, t);
    }

    private static double Ease(double t0, double d0, double t1, double d1, double t)
    {
        if (t1 <= t0)
            return d1;
        var u = (t - t0) / (t1 - t0);
        return d0 + (d1 - d0) * (1 - Math.Cos(Math.PI * u)) * 0.5;
    }

    /// <summary>Ink with an empty 4-neighbour, without the L corners a pixel artist would cut.</summary>
    private static bool[] Outline(bool[] mask, int width, int height)
    {
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && mask[y * width + x];

        var line = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                line[y * width + x] = mask[y * width + x]
                    && !(Inside(x - 1, y) && Inside(x + 1, y) && Inside(x, y - 1) && Inside(x, y + 1));
        }

        bool IsLine(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && line[y * width + x];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!line[y * width + x])
                    continue;
                foreach (var (ax, ay, bx, by) in CornerPairs)
                {
                    if (IsLine(x + ax, y + ay) && IsLine(x + bx, y + by) && !IsLine(x - ax, y - ay)
                        && !IsLine(x - bx, y - by) && !IsLine(x + ax + bx, y + ay + by))
                    {
                        line[y * width + x] = false;
                        break;
                    }
                }
            }
        }
        return line;
    }
}
