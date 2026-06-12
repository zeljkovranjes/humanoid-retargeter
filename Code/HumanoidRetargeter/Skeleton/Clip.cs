using System;
using System.Collections.Generic;
using HumanoidRetargeter.Maths;

namespace HumanoidRetargeter.Skeleton;

/// <summary>
/// A sampled animation clip: a fixed-rate sequence of frames, each holding one
/// parent-relative local transform per bone (skeleton bone order). Clips are always
/// resampled at ingest — no key data is preserved.
/// </summary>
public sealed class Clip
{
    /// <summary>Clip (sequence) name.</summary>
    public string Name { get; }

    /// <summary>Sample rate in frames per second.</summary>
    public float Fps { get; }

    /// <summary>Whether the clip is authored to loop.</summary>
    public bool Looping { get; }

    /// <summary>Frames in playback order; each entry is one local transform per bone.</summary>
    public List<XForm[]> Frames { get; }

    /// <summary>Number of frames currently in the clip.</summary>
    public int FrameCount => Frames.Count;

    /// <summary>
    /// Clip duration in seconds at <see cref="Fps"/>: the time span between the first and the
    /// last sample, <c>(FrameCount - 1) / Fps</c> (frames are fence posts, intervals are the
    /// spans between them — matching the DMX timeFrame this clip serializes to). Zero for
    /// empty and single-frame clips.
    /// </summary>
    public float Duration => FrameCount <= 1 ? 0f : (FrameCount - 1) / Fps;

    /// <summary>Creates an empty clip.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="fps"/> is not positive.</exception>
    public Clip(string name, float fps, bool looping)
        : this(name, fps, looping, new List<XForm[]>())
    {
    }

    /// <summary>Creates a clip wrapping an existing frame list (not copied).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="fps"/> is not positive.</exception>
    public Clip(string name, float fps, bool looping, List<XForm[]> frames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(frames);
        if (!(fps > 0f) || !float.IsFinite(fps))
            throw new ArgumentOutOfRangeException(nameof(fps), fps, "Fps must be a positive finite number.");

        Name = name;
        Fps = fps;
        Looping = looping;
        Frames = frames;
    }
}
