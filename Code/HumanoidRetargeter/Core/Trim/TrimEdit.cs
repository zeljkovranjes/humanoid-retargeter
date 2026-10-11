#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Trim;

/// <summary>
/// The trim editor's state: which frames of a clip are kept, with undo and redo. Positions are indices
/// into the kept frames (what the timeline shows); <see cref="Kept"/> holds the original frame numbers.
/// </summary>
public sealed class TrimEdit
{
    const int MinimumFrames = 2;

    readonly Stack<int[]> _undo = new();
    readonly Stack<int[]> _redo = new();
    int[] _kept;

    /// <summary>Starts from <paramref name="kept"/>, or every frame when null.</summary>
    public TrimEdit(int frameCount, IReadOnlyList<int>? kept = null)
    {
        if (frameCount < 1)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        FrameCount = frameCount;
        _kept = kept is { Count: > 0 } && kept.All(f => f >= 0 && f < frameCount)
            ? kept.ToArray()
            : Enumerable.Range(0, frameCount).ToArray();
    }

    /// <summary>Frames in the untrimmed clip.</summary>
    public int FrameCount { get; }

    /// <summary>Original frame numbers still in the clip, in order.</summary>
    public IReadOnlyList<int> Kept => _kept;

    /// <summary>True when any frame has been cut.</summary>
    public bool IsTrimmed => _kept.Length != FrameCount;

    /// <summary>True when there is an edit to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is an undone edit to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when frames were cut out between <paramref name="position"/> and the next one.</summary>
    public bool IsCutAfter(int position)
        => position >= 0 && position + 1 < _kept.Length && _kept[position + 1] != _kept[position] + 1;

    /// <summary>Removes positions <paramref name="start"/>..<paramref name="end"/> (inclusive).</summary>
    public bool Delete(int start, int end)
    {
        (start, end) = Clamp(start, end);
        if (_kept.Length - (end - start + 1) < MinimumFrames)
            return false;
        return Set(_kept.Take(start).Concat(_kept.Skip(end + 1)).ToArray());
    }

    /// <summary>Keeps only positions <paramref name="start"/>..<paramref name="end"/> (inclusive).</summary>
    public bool Keep(int start, int end)
    {
        (start, end) = Clamp(start, end);
        if (end - start + 1 < MinimumFrames)
            return false;
        return Set(_kept[start..(end + 1)]);
    }

    /// <summary>Brings back every frame (undoable).</summary>
    public bool Restore() => Set(Enumerable.Range(0, FrameCount).ToArray());

    /// <summary>Steps back one edit.</summary>
    public bool Undo() => Swap(_undo, _redo);

    /// <summary>Re-applies the last undone edit.</summary>
    public bool Redo() => Swap(_redo, _undo);

    (int, int) Clamp(int start, int end)
    {
        if (start > end)
            (start, end) = (end, start);
        return (Math.Clamp(start, 0, _kept.Length - 1), Math.Clamp(end, 0, _kept.Length - 1));
    }

    bool Set(int[] next)
    {
        if (next.AsSpan().SequenceEqual(_kept))
            return false;
        _undo.Push(_kept);
        _redo.Clear();
        _kept = next;
        return true;
    }

    bool Swap(Stack<int[]> from, Stack<int[]> to)
    {
        if (from.Count == 0)
            return false;
        to.Push(_kept);
        _kept = from.Pop();
        return true;
    }
}
