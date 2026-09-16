#nullable enable annotations
using System;
using System.Linq;
using System.Text.RegularExpressions;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Solve;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Target;

using Vector3 = System.Numerics.Vector3;

/// <summary>A suggestion only: never renames a clip or changes a graph without confirmation.</summary>
public sealed record LocomotionSuggestion(string Family, string Direction, string Basis)
{
    public string SlotId => Family + "_" + Direction.ToLowerInvariant();

    public static LocomotionSuggestion? Detect(string name, SkeletonModel? skeleton = null, MappingResult? map = null, Clip? clip = null)
    {
        // Split separators and camelCase without treating letters inside ordinary words as directions.
        var tokens = Regex.Split(Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(), "[^a-z0-9]+");
        var family = tokens.Contains("crouchwalk") || (tokens.Contains("crouch") && tokens.Contains("walk")) ? "crouchwalk"
            : tokens.Contains("run") || tokens.Contains("jog") || tokens.Contains("sprint") ? "run"
            : tokens.Contains("walk") ? "walk" : null;
        if (family is null) return null;
        string? direction = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            var paired = i + 1 < tokens.Length ? LocomotionSetDetector.CanonicalDirection(tokens[i] + tokens[i + 1]) : null;
            var found = paired ?? LocomotionSetDetector.CanonicalDirection(tokens[i]);
            if (found is null) continue;
            if (direction is not null && direction != found) return null; // conflicting names: ask the user
            direction = found;
            if (paired is not null) i++;
        }
        if (direction is not null) return new(family, direction, "clip name");
        if (skeleton is null || map is null || clip is null || clip.FrameCount < 3 || !map.RoleToBone.TryGetValue(BoneRole.Hips, out var hips)) return null;
        try
        {
            var first = new Pose(clip.Frames[0]).ToWorld(skeleton);
            var frame = CharacterFrame.Compute(skeleton, map, first);
            var last = new Pose(clip.Frames[^1]).ToWorld(skeleton);
            var delta = last[hips].Pos - first[hips].Pos;
            delta -= frame.Up * Vector3.Dot(delta, frame.Up);
            var distance = delta.Length();
            if (!float.IsFinite(distance) || distance < MathF.Max(frame.HipHeight * 0.3f, 1e-4f)) return null;
            var previous = first[hips].Pos;
            var travel = 0f;
            for (var i = 1; i <= 8; i++)
            {
                var world = new Pose(clip.Frames[i * (clip.FrameCount - 1) / 8]).ToWorld(skeleton);
                var facing = CharacterFrame.Compute(skeleton, map, world, frame.Up);
                if (Vector3.Dot(frame.Forward, facing.Forward) < 0.94f) return null; // turning clip
                var step = world[hips].Pos - previous;
                step -= frame.Up * Vector3.Dot(step, frame.Up);
                travel += step.Length();
                previous = world[hips].Pos;
            }
            if (distance < travel * 0.9f) return null; // not straight travel
            var angle = MathF.Atan2(-Vector3.Dot(delta, frame.Lateral), Vector3.Dot(delta, frame.Forward)) / (MathF.PI / 4);
            var sector = (int)MathF.Round(angle);
            if (MathF.Abs(angle - sector) > 0.4f) return null; // near a direction boundary
            direction = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(sector + 8) % 8];
            return new(family, direction, "straight travel relative to character facing");
        }
        catch (ArgumentException) { return null; } // incomplete/degenerate mapping: no guess
    }
}
