#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Mapping;

namespace HumanoidRetargeter.Core.Sprites;

using Vector2 = System.Numerics.Vector2; // s&box compat: shadow engine's global-namespace Vector2 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// A humanoid skeleton placed over a 2D sprite, in image pixels (y down). <see cref="Fit"/>
/// reads the joints off the sprite's silhouette; a caller may then move them before
/// <see cref="SpriteGlb.Write"/> turns the sprite into a rigged model.
/// </summary>
/// <remarks>
/// The proportions come from Bonebase's Humanoid and Profile templates, drawn on a 64 px
/// canvas. A sprite in profile shows one arm and one leg, so its left and right joints start
/// on the same spot and only the left ones are edited: <see cref="Joints"/> keeps them level.
/// The character's left is screen right in a front view, as in Bonebase.
/// </remarks>
public sealed class SpriteRig
{
    private const float TemplateCentre = 32f;
    private const float TemplateTop = 6.7f;
    private const float TemplateNeck = 16f;
    private const float TemplateBottom = 61.5f;

    /// <summary>The template's width across the shoulders, arms included.</summary>
    private const float TemplateBodyWidth = 24f;

    /// <summary>Where a neck can be, as shares of the figure's height.</summary>
    private const float NeckFrom = 0.1f, NeckTo = 0.55f;

    /// <summary>Toes must reach this share of the height past the legs to say which way a profile faces.</summary>
    private const float FacingReach = 0.03f;

    /// <summary>Alpha at or above this is the sprite; below it is background.</summary>
    public const byte OpaqueAlpha = 128;

    /// <summary>Limbs in a row this far down the figure show as two runs on a front view.</summary>
    private const float LegRow = 0.85f;

    /// <summary>A figure narrower than this share of its height is taken for a profile.</summary>
    private const float ProfileAspect = 0.55f;

    /// <summary>The share of the figure's height read as its feet.</summary>
    private const float FootRows = 0.1f;

    private static readonly (BoneRole Role, float X, float Y)[] FrontTemplate =
    {
        (BoneRole.Hips, 32f, 37f), (BoneRole.Spine0, 32f, 31f), (BoneRole.Spine1, 32f, 25.5f),
        (BoneRole.Spine2, 32f, 20f), (BoneRole.Neck, 32f, 16f), (BoneRole.Head, 32f, 12f),
        (BoneRole.UpperArmL, 38.5f, 21f), (BoneRole.LowerArmL, 41f, 31f), (BoneRole.HandL, 43f, 40f),
        (BoneRole.UpperLegL, 35f, 37f), (BoneRole.LowerLegL, 36f, 48f), (BoneRole.FootL, 36f, 58f),
        (BoneRole.ToeL, 39f, 60f),
    };

    private static readonly (BoneRole Role, float X, float Y)[] ProfileTemplate =
    {
        (BoneRole.Hips, 31f, 33f), (BoneRole.Spine0, 31.5f, 28f), (BoneRole.Spine1, 32f, 24f),
        (BoneRole.Spine2, 32.5f, 19.5f), (BoneRole.Neck, 33.5f, 15f), (BoneRole.Head, 34f, 11.5f),
        (BoneRole.UpperArmL, 32.5f, 20.5f), (BoneRole.LowerArmL, 33f, 29f), (BoneRole.HandL, 34f, 36f),
        (BoneRole.UpperLegL, 32f, 34f), (BoneRole.LowerLegL, 32f, 45.5f), (BoneRole.FootL, 32f, 57f),
        (BoneRole.ToeL, 37f, 59f),
    };

    private static readonly (BoneRole Left, BoneRole Right)[] Sides =
    {
        (BoneRole.UpperArmL, BoneRole.UpperArmR), (BoneRole.LowerArmL, BoneRole.LowerArmR),
        (BoneRole.HandL, BoneRole.HandR), (BoneRole.UpperLegL, BoneRole.UpperLegR),
        (BoneRole.LowerLegL, BoneRole.LowerLegR), (BoneRole.FootL, BoneRole.FootR),
        (BoneRole.ToeL, BoneRole.ToeR),
    };

    private SpriteRig(int width, int height, bool isProfile, bool facesLeft, (int Left, int Top, int Right, int Bottom) figure)
    {
        Width = width;
        Height = height;
        IsProfile = isProfile;
        FacesLeft = facesLeft;
        Figure = figure;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>True when the sprite is drawn from the side, false from the front.</summary>
    public bool IsProfile { get; }

    /// <summary>True when a profile sprite faces screen-left.</summary>
    public bool FacesLeft { get; }

    /// <summary>The opaque bounds of the figure, in pixels (inclusive).</summary>
    public (int Left, int Top, int Right, int Bottom) Figure { get; }

    /// <summary>Joint positions in image pixels, y down.</summary>
    public Dictionary<BoneRole, Vector2> Joints { get; } = new();

    /// <summary>The joints a person moves: the right side follows the left on a profile.</summary>
    public IEnumerable<BoneRole> EditableJoints
    {
        get
        {
            foreach (var role in Joints.Keys)
            {
                if (!IsProfile || !IsRightSide(role))
                    yield return role;
            }
        }
    }

    private static bool IsRightSide(BoneRole role)
    {
        foreach (var (_, right) in Sides)
        {
            if (right == role)
                return true;
        }
        return false;
    }

    /// <summary>Moves a joint (image pixels); on a profile a left joint carries its right twin along.</summary>
    public void Move(BoneRole role, Vector2 position)
    {
        Joints[role] = position;
        if (!IsProfile)
            return;
        foreach (var (left, right) in Sides)
        {
            if (left == role)
                Joints[right] = position;
        }
    }

    /// <summary>
    /// Reads a skeleton off a sprite. <paramref name="alpha"/> holds one byte per pixel, row by
    /// row. Null <paramref name="isProfile"/> or <paramref name="facesLeft"/> are worked out
    /// from the silhouette.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the alpha size does not match, or the
    /// sprite has no background to read a silhouette from.</exception>
    public static SpriteRig Fit(byte[] alpha, int width, int height, bool? isProfile = null, bool? facesLeft = null)
    {
        ArgumentNullException.ThrowIfNull(alpha);
        if (width <= 0 || height <= 0 || alpha.Length != width * height)
            throw new ArgumentException($"Sprite alpha holds {alpha.Length} pixels, expected {width} x {height}.", nameof(alpha));

        var figure = OpaqueBounds(alpha, width, height);
        var profile = isProfile ?? LooksLikeProfile(alpha, width, figure);
        var left = profile && (facesLeft ?? LooksLeft(alpha, width, figure));
        var rig = new SpriteRig(width, height, profile, left, figure);

        var neck = NeckRow(alpha, width, figure);
        var centre = TrunkCentre(alpha, width, figure);
        var mirror = left ? -1f : 1f;
        // Head and body fit apart: a chibi's head is half of it, a realistic figure's an eighth.
        var headScale = (neck - figure.Top) / (TemplateNeck - TemplateTop);
        var bodyScale = (figure.Bottom + 1 - neck) / (TemplateBottom - TemplateNeck);
        var widthScale = MathF.Min(bodyScale, RowWidth(alpha, width, figure, neck + (figure.Bottom - neck) / 6) / TemplateBodyWidth);
        foreach (var (role, x, y) in profile ? ProfileTemplate : FrontTemplate)
        {
            var rowY = y <= TemplateNeck
                ? figure.Top + (y - TemplateTop) * headScale
                : neck + (y - TemplateNeck) * bodyScale;
            rig.Joints[role] = new Vector2(centre + (x - TemplateCentre) * widthScale * mirror, rowY);
        }
        foreach (var (leftRole, rightRole) in Sides)
        {
            var joint = rig.Joints[leftRole];
            rig.Joints[rightRole] = profile ? joint : new Vector2(2f * centre - joint.X, joint.Y);
        }
        if (!profile)
            rig.FitFrontLimbs(alpha, centre);
        return rig;
    }

    /// <summary>The narrowest row between head and shoulders: where the head stops.</summary>
    private static int NeckRow(byte[] alpha, int width, (int Left, int Top, int Right, int Bottom) figure)
    {
        var size = figure.Bottom - figure.Top + 1;
        var first = figure.Top + (int)(size * NeckFrom);
        var last = figure.Top + (int)(size * NeckTo);
        var neck = first;
        var narrowest = int.MaxValue;
        for (var y = first; y <= last; y++)
        {
            var rowWidth = RowWidth(alpha, width, figure, y);
            if (rowWidth > 0 && rowWidth < narrowest)
            {
                narrowest = rowWidth;
                neck = y;
            }
        }
        return neck;
    }

    private static int RowWidth(byte[] alpha, int width, (int Left, int Top, int Right, int Bottom) figure, int row)
    {
        var count = 0;
        for (var x = figure.Left; x <= figure.Right; x++)
        {
            if (alpha[row * width + x] >= OpaqueAlpha)
                count++;
        }
        return count;
    }

    private static (int Left, int Top, int Right, int Bottom) OpaqueBounds(byte[] alpha, int width, int height)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[y * width + x] < OpaqueAlpha)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0)
            throw new ArgumentException("The sprite has no visible pixels.", nameof(alpha));
        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1 && IsSolid(alpha))
            throw new ArgumentException("The sprite has no transparent background: its silhouette cannot be read. Export it with transparency.", nameof(alpha));
        return (left, top, right, bottom);
    }

    private static bool IsSolid(byte[] alpha)
    {
        foreach (var value in alpha)
        {
            if (value < OpaqueAlpha)
                return false;
        }
        return true;
    }

    private static float TrunkCentre(byte[] alpha, int width, (int Left, int Top, int Right, int Bottom) figure)
    {
        var size = figure.Bottom - figure.Top + 1;
        return MeanX(alpha, width, figure.Top + (int)(size * 0.2f), figure.Top + (int)(size * 0.5f), figure)
            ?? (figure.Left + figure.Right) / 2f;
    }

    private static float? MeanX(byte[] alpha, int width, int fromRow, int toRow, (int Left, int Top, int Right, int Bottom) figure)
    {
        long sum = 0, count = 0;
        for (var y = fromRow; y <= toRow; y++)
        {
            for (var x = figure.Left; x <= figure.Right; x++)
            {
                if (alpha[y * width + x] < OpaqueAlpha)
                    continue;
                sum += x;
                count++;
            }
        }
        return count > 0 ? (float)sum / count : null;
    }

    private static bool LooksLikeProfile(byte[] alpha, int width, (int Left, int Top, int Right, int Bottom) figure)
    {
        var size = figure.Bottom - figure.Top + 1;
        var row = figure.Top + (int)(size * LegRow);
        var runs = 0;
        var inside = false;
        for (var x = figure.Left; x <= figure.Right; x++)
        {
            var opaque = alpha[row * width + x] >= OpaqueAlpha;
            if (opaque && !inside)
                runs++;
            inside = opaque;
        }
        return runs < 2 && (figure.Right - figure.Left + 1f) / size < ProfileAspect;
    }

    /// <summary>Toes reach past the legs on the side a profile faces.</summary>
    private static bool LooksLeft(byte[] alpha, int width, (int Left, int Top, int Right, int Bottom) figure)
    {
        var size = figure.Bottom - figure.Top + 1;
        var feet = figure.Bottom - (int)(size * FootRows);
        var legs = MeanX(alpha, width, figure.Top + (int)(size * 0.6f), feet, figure);
        var toes = MeanX(alpha, width, feet, figure.Bottom, figure);
        return legs is { } l && toes is { } t && t < l - size * FacingReach;
    }

    /// <summary>A front sprite's arms may be raised or held out, and its stance wide: the
    /// hands and feet go where the silhouette actually reaches.</summary>
    private void FitFrontLimbs(byte[] alpha, float centre)
    {
        var size = Figure.Bottom - Figure.Top + 1;
        var armTop = Figure.Top + (int)(size * 0.15f);
        var armBottom = Figure.Top + (int)(size * 0.7f);
        var footRow = Figure.Bottom - (int)(size * FootRows);

        foreach (var (side, shoulder, elbow, hand, hip, knee, ankle, toe) in new[]
        {
            (1f, BoneRole.UpperArmL, BoneRole.LowerArmL, BoneRole.HandL, BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL),
            (-1f, BoneRole.UpperArmR, BoneRole.LowerArmR, BoneRole.HandR, BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR),
        })
        {
            var reach = Reach(alpha, centre, side, armTop, armBottom);
            if (reach is { } tip && MathF.Abs(tip.X - centre) > MathF.Abs(Joints[hand].X - centre) * 1.25f)
            {
                Joints[hand] = tip;
                Joints[elbow] = (Joints[shoulder] + tip) * 0.5f;
            }

            if (SideMeanX(alpha, centre, side, footRow, Figure.Bottom) is { } foot)
            {
                var y = Joints[ankle].Y;
                Joints[ankle] = new Vector2(foot, y);
                Joints[toe] = new Vector2(foot, Joints[toe].Y);
                Joints[knee] = new Vector2((Joints[hip].X + foot) * 0.5f, Joints[knee].Y);
            }
        }
    }

    private Vector2? Reach(byte[] alpha, float centre, float side, int fromRow, int toRow)
    {
        Vector2? best = null;
        var furthest = 0f;
        for (var y = fromRow; y <= toRow; y++)
        {
            for (var x = Figure.Left; x <= Figure.Right; x++)
            {
                if (alpha[y * Width + x] < OpaqueAlpha)
                    continue;
                var distance = (x - centre) * side;
                if (distance > furthest)
                {
                    furthest = distance;
                    best = new Vector2(x, y);
                }
            }
        }
        return best;
    }

    private float? SideMeanX(byte[] alpha, float centre, float side, int fromRow, int toRow)
    {
        long sum = 0, count = 0;
        for (var y = fromRow; y <= toRow; y++)
        {
            for (var x = Figure.Left; x <= Figure.Right; x++)
            {
                if (alpha[y * Width + x] < OpaqueAlpha || (x - centre) * side <= 0f)
                    continue;
                sum += x;
                count++;
            }
        }
        return count > 0 ? (float)sum / count : null;
    }
}
