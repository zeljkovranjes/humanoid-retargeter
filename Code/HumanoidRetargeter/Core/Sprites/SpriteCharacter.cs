#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace HumanoidRetargeter.Core.Sprites;

using Vector2 = System.Numerics.Vector2; // s&box compat: shadow engine's global-namespace Vector2 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>A bulge on one side of a bone: extra radius at a share of its length.</summary>
public readonly record struct SpriteBulge(float At, float Radius);

/// <summary>One bone of a 2D character: where it rests on the canvas and how thick it is.</summary>
public sealed record SpriteBone(
    string Name, int Part, Vector2 Head, Vector2 Tail, float HeadRadius, float TailRadius,
    SpriteBulge[] BulgeLeft, SpriteBulge[] BulgeRight);

/// <summary>One body part: its bones merge into one shape in one colour, on one layer.</summary>
public sealed record SpritePart(int Id, string Name, int Layer, float Blend, byte Red, byte Green, byte Blue);

/// <summary>
/// A 2D character as Bonebase defines it: bones with radii on a pixel canvas, grouped into
/// coloured parts. Read from the <c>.rig.json</c> files <c>dev/sprite-templates/bake.lua</c>
/// dumps from Bonebase's own templates.
/// </summary>
public sealed class SpriteCharacter
{
    private SpriteCharacter(int width, int height, SpriteBone[] bones, SpritePart[] parts)
    {
        Width = width;
        Height = height;
        Bones = bones;
        Parts = parts;
    }

    /// <summary>Canvas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>The bones, parents first.</summary>
    public IReadOnlyList<SpriteBone> Bones { get; }

    /// <summary>The parts, in stacking order at rest.</summary>
    public IReadOnlyList<SpritePart> Parts { get; }

    /// <summary>Reads a character.</summary>
    /// <exception cref="FormatException">The text is not a character definition.</exception>
    public static SpriteCharacter Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var canvas = root.GetProperty("canvas");
            var bones = new List<SpriteBone>();
            foreach (var bone in root.GetProperty("bones").EnumerateArray())
            {
                bones.Add(new SpriteBone(
                    bone.GetProperty("name").GetString() ?? "",
                    bone.GetProperty("part").GetInt32(),
                    new Vector2(bone.GetProperty("hx").GetSingle(), bone.GetProperty("hy").GetSingle()),
                    new Vector2(bone.GetProperty("tx").GetSingle(), bone.GetProperty("ty").GetSingle()),
                    bone.GetProperty("r0").GetSingle(),
                    bone.GetProperty("r1").GetSingle(),
                    Bulges(bone.GetProperty("bulgeL")),
                    Bulges(bone.GetProperty("bulgeR"))));
            }
            var parts = new List<SpritePart>();
            foreach (var part in root.GetProperty("parts").EnumerateArray())
            {
                var color = part.GetProperty("color");
                parts.Add(new SpritePart(
                    part.GetProperty("id").GetInt32(),
                    part.GetProperty("name").GetString() ?? "",
                    part.GetProperty("z").GetInt32(),
                    part.GetProperty("blend").GetSingle(),
                    color[0].GetByte(), color[1].GetByte(), color[2].GetByte()));
            }
            if (bones.Count == 0 || parts.Count == 0)
                throw new FormatException("the character has no bones or no parts.");
            return new SpriteCharacter(canvas[0].GetInt32(), canvas[1].GetInt32(), bones.ToArray(), parts.ToArray());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new FormatException($"Not a 2D character definition: {e.Message}", e);
        }
    }

    private static SpriteBulge[] Bulges(JsonElement list)
    {
        var bulges = new SpriteBulge[list.GetArrayLength()];
        for (var i = 0; i < bulges.Length; i++)
            bulges[i] = new SpriteBulge(list[i][0].GetSingle(), list[i][1].GetSingle());
        return bulges;
    }
}
