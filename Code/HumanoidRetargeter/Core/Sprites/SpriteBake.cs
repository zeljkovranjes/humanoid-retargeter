#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;

namespace HumanoidRetargeter.Core.Sprites;

using Vector2 = System.Numerics.Vector2; // s&box compat: shadow engine's global-namespace Vector2 (see Code/HumanoidRetargeter/Assembly.cs)
using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Redraws a solved 2D clip frame by frame: the posed armature gives each
/// <see cref="SpriteCharacter"/> bone its place on the canvas, and <see cref="SpriteSkin"/>
/// draws the skin there, as Bonebase does. The result is pixels, not a model bent into shape.
/// </summary>
public static class SpriteBake
{
    /// <summary>Bonebase's clip rate.</summary>
    public const float FramesPerSecond = 12f;

    /// <summary>Joints this close on the canvas (px) are one joint: the child ends its parent.</summary>
    private const float ConnectedDistance = 1.5f;

    /// <summary>Depth from the hips that puts a part over or under the body, as a share of the
    /// character's height, and how much further it must go to change sides (retarget.lua).</summary>
    private const float DepthMargin = 0.03f, DepthHold = 1.5f;

    private sealed record BoneLink(int RigBone, int TipBone, Vector3 RestTail);

    /// <summary>
    /// Draws the clip at <see cref="FramesPerSecond"/>, each frame RGBA row by row.
    /// <paramref name="solved"/> are the local transforms of a clip solved through
    /// <paramref name="view"/> onto <paramref name="rig"/>, which must be the armature baked
    /// from <paramref name="character"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A character bone has no bone on the rig.</exception>
    public static List<byte[]> Frames(
        SpriteCharacter character, TargetRig rig, IReadOnlyList<XForm[]> solved, float fps, bool isLooping, PlanarView view)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(solved);

        var (up, toward) = PlanarSolver.CameraAxes(rig, view);
        var right = Vector3.Cross(up, toward);
        var rest = rig.Skeleton.RestWorld;
        var bones = character.Bones;
        var rigBones = new int[bones.Count];
        for (var i = 0; i < bones.Count; i++)
            rigBones[i] = FindBone(rig, bones[i].Name)
                ?? throw new ArgumentException($"The rig has no bone for the 2D character's '{bones[i].Name}'.", nameof(rig));

        var origin = bones[0].Head;
        var restOrigin = rest[rigBones[0]].Pos;
        var scale = PixelsPerUnit(character, rest, rigBones, origin, restOrigin, right, up);
        Vector2 ToCanvas(Vector3 position)
        {
            var offset = position - restOrigin;
            return origin + new Vector2(Vector3.Dot(offset, right), -Vector3.Dot(offset, up)) * scale;
        }

        var links = new BoneLink[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var line = (bones[i].Tail - bones[i].Head) / scale;
            links[i] = new BoneLink(rigBones[i], TipOf(character, rig, rigBones, i), right * line.X - up * line.Y);
        }

        var margin = DepthMargin * character.Height / scale;
        var sides = new int[character.Parts.Count];
        var firstBones = FirstBones(character);
        var heads = new Vector2[bones.Count];
        var tails = new Vector2[bones.Count];
        var frames = new List<byte[]>();
        foreach (var index in SampleFrames(solved.Count, fps, isLooping))
        {
            var world = new Pose(solved[index]).ToWorld(rig.Skeleton);
            for (var i = 0; i < bones.Count; i++)
            {
                var link = links[i];
                var head = world[link.RigBone].Pos;
                heads[i] = ToCanvas(head);
                if (link.TipBone >= 0)
                {
                    tails[i] = ToCanvas(world[link.TipBone].Pos);
                }
                else
                {
                    var turn = MathQ.Normalize(world[link.RigBone].Rot * Quaternion.Conjugate(rest[link.RigBone].Rot));
                    tails[i] = ToCanvas(head + Vector3.Transform(link.RestTail, turn));
                }
            }
            frames.Add(SpriteSkin.Draw(character, heads, tails,
                Stacking(character, firstBones, rigBones, world, toward, margin, sides)));
        }
        return frames;
    }

    /// <summary>The solved frames that land on each output frame.</summary>
    private static IEnumerable<int> SampleFrames(int count, float fps, bool isLooping)
    {
        var duration = Math.Max(0, count - 1) / fps;
        var frames = Math.Max(1, (int)MathF.Round(duration * FramesPerSecond) + (isLooping ? 0 : 1));
        for (var i = 0; i < frames; i++)
            yield return Math.Min(count - 1, (int)MathF.Round(i / FramesPerSecond * fps));
    }

    private static int? FindBone(TargetRig rig, string name)
    {
        var wanted = Plain(name);
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (Plain(rig.Skeleton[i].Name) == wanted)
                return i;
        }
        return null;
    }

    private static string Plain(string name) => name.Replace("_", "").Replace(".", "").ToLowerInvariant();

    private static float PixelsPerUnit(
        SpriteCharacter character, IReadOnlyList<XForm> rest, int[] rigBones, Vector2 origin, Vector3 restOrigin, Vector3 right, Vector3 up)
    {
        float canvas = 0f, rig = 0f;
        for (var i = 1; i < rigBones.Length; i++)
        {
            canvas += Vector2.Distance(character.Bones[i].Head, origin);
            var offset = rest[rigBones[i]].Pos - restOrigin;
            rig += new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up)).Length();
        }
        if (rig <= 0f)
            throw new ArgumentException("The rig's 2D character bones are all on one spot.", nameof(rest));
        return canvas / rig;
    }

    /// <summary>The joint a bone's tail follows, so a bone that shortens is drawn shorter: a
    /// connected child, or the toe at the end of a foot. -1 when the tail just turns with it.</summary>
    private static int TipOf(SpriteCharacter character, TargetRig rig, int[] rigBones, int bone)
    {
        var tail = character.Bones[bone].Tail;
        for (var i = 0; i < character.Bones.Count; i++)
        {
            if (i != bone && character.Bones[i].Part == character.Bones[bone].Part
                && Vector2.Distance(character.Bones[i].Head, tail) < ConnectedDistance)
                return rigBones[i];
        }
        var name = character.Bones[bone].Name;
        return name.StartsWith("foot", StringComparison.Ordinal)
            ? FindBone(rig, "toe" + name["foot".Length..]) ?? -1
            : -1;
    }

    private static int[] FirstBones(SpriteCharacter character)
    {
        var first = new int[character.Parts.Count];
        for (var p = 0; p < first.Length; p++)
        {
            first[p] = -1;
            for (var i = 0; i < character.Bones.Count && first[p] < 0; i++)
            {
                if (character.Bones[i].Part == character.Parts[p].Id)
                    first[p] = i;
            }
        }
        return first;
    }

    /// <summary>retarget.lua's depth order: a part clearly in front of the hips goes over what
    /// is level with them, one clearly behind goes under, and inside each side the rig's own
    /// order holds. A part already on a side stays until it is well past.</summary>
    private static List<int> Stacking(
        SpriteCharacter character, int[] firstBones, int[] rigBones, XForm[] world, Vector3 toward, float margin, int[] sides)
    {
        var hips = Vector3.Dot(world[rigBones[0]].Pos, toward);
        var order = new List<int>(sides.Length);
        for (var p = 0; p < sides.Length; p++)
        {
            order.Add(p);
            if (firstBones[p] < 0)
                continue;
            var depth = Vector3.Dot(world[rigBones[firstBones[p]]].Pos, toward) - hips;
            var limit = sides[p] != 0 && depth > 0f == sides[p] > 0 ? margin / DepthHold : margin;
            sides[p] = depth > limit ? 1 : depth < -limit ? -1 : 0;
        }
        var stacking = sides;
        order.Sort((a, b) =>
        {
            if (stacking[a] != stacking[b])
                return stacking[a].CompareTo(stacking[b]);
            var layers = character.Parts[a].Layer.CompareTo(character.Parts[b].Layer);
            return layers != 0 ? layers : a.CompareTo(b);
        });
        return order;
    }
}
