using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;

namespace HumanoidRetargeter.Formats.Bvh;

/// <summary>Options for <see cref="BvhImporter.Import"/>.</summary>
public sealed class BvhImportOptions
{
    /// <summary>Fixed resampling rate for the motion data, frames per second.</summary>
    public float SampleFps { get; init; } = 30f;
}

/// <summary>
/// BVH (Biovision Hierarchy) → <see cref="SourceScene"/> importer.
/// </summary>
/// <remarks>
/// <para><b>Format conventions implemented</b> (verified against Blender's
/// <c>io_anim_bvh</c> importer, which is the project's ground-truth extractor):</para>
/// <list type="bullet">
/// <item><b>Rest pose:</b> each joint's rest local translation is its <c>OFFSET</c>; rest
/// rotation is identity (BVH stores no rest orientation).</item>
/// <item><b>Rotation channels:</b> the channel list order IS the rotation order. The listed
/// rotations apply left-to-right as intrinsic rotations, which in this library's
/// column-vector convention (<c>a * b</c> applies <c>b</c> first) is the product
/// <c>R = R_chan1 * R_chan2 * R_chan3</c> — e.g. <c>Zrotation Yrotation Xrotation</c> gives
/// <c>R = Rz * Ry * Rx</c>. This matches Blender, which builds
/// <c>Euler((x,y,z), reversed(channelOrder))</c> for the same matrix. Angles are degrees.</item>
/// <item><b>Position channels:</b> when a joint has any position channel, the channel values
/// REPLACE the joint's local translation (missing components are 0) — they are not added to
/// the <c>OFFSET</c>. This is Blender's behavior; in practice roots have OFFSET 0 so the two
/// readings only diverge on non-root position channels (e.g. Bandai-Namco exports).</item>
/// <item><b>End Sites:</b> synthesized as a channel-less leaf bone named
/// <c>"&lt;parent&gt;_end"</c> so chain tips keep their direction information (Blender instead
/// folds them into the parent bone's tail).</item>
/// </list>
/// <para><b>Units</b>: BVH files carry no unit declaration. Heuristic: compute the rest
/// skeleton height (max−min world Y over all joints); if it is &lt; 10 the file is assumed
/// to be in meters and all translations (offsets AND position channels, root included) are
/// scaled ×100 to centimeters, otherwise it is assumed to already be centimeters (×1).
/// Millimeter-scale files (height &gt; 400) are not special-cased — they are rare and
/// ambiguous against cm mocap of long ranges; <see cref="SourceScene.UnitScaleCm"/> records
/// whichever factor was applied for diagnostics.</para>
/// <para><b>Resampling</b>: motion frames are resampled from the file's <c>Frame Time</c>
/// grid onto <see cref="BvhImportOptions.SampleFps"/>. Each native frame's euler channels are
/// converted to a quaternion FIRST and bracketing frames are then slerped (positions lerped).
/// Interpolating raw euler angles across frames would mostly work at mocap densities
/// (30–120 fps, small per-frame deltas) but breaks down when an angle wraps ±180° between
/// frames; per-frame quaternion + slerp has no such failure mode, so that is what we do.</para>
/// <para><b>Axes</b>: BVH is conventionally Y-up / Z-forward / X-right. Native axes are
/// preserved (no conversion), matching the FBX importer's policy; the conventional axes are
/// recorded on the <see cref="SourceScene"/> (up = Y, front = Z, coord = X).</para>
/// </remarks>
public static class BvhImporter
{
    private const float MeterHeightThreshold = 10f;

    /// <summary>Parses BVH bytes and builds the source scene.</summary>
    /// <exception cref="FormatException">Malformed or truncated BVH.</exception>
    public static SourceScene Import(byte[] data, BvhImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new BvhImportOptions();
        if (!(options.SampleFps > 0f) || !float.IsFinite(options.SampleFps))
            throw new ArgumentOutOfRangeException(nameof(options), "SampleFps must be positive.");

        var cursor = new TokenCursor(Encoding.UTF8.GetString(data));

        // ---- HIERARCHY -----------------------------------------------------------------
        cursor.ExpectKeyword("HIERARCHY");
        var joints = new List<Joint>();
        int channelCount = 0;
        if (!cursor.PeekIs("ROOT"))
            throw new FormatException("BVH: expected ROOT after HIERARCHY.");
        while (cursor.PeekIs("ROOT")) // multiple roots are out of spec but harmless to accept
        {
            cursor.Next();
            ParseJoint(cursor, joints, parent: -1, ref channelCount);
        }

        // ---- MOTION ---------------------------------------------------------------------
        cursor.ExpectKeyword("MOTION");
        cursor.ExpectKeyword("FRAMES:");
        int frameCount = cursor.NextInt();
        if (frameCount < 0)
            throw new FormatException($"BVH: negative frame count {frameCount}.");
        cursor.ExpectKeyword("FRAME");
        cursor.ExpectKeyword("TIME:");
        float frameTime = cursor.NextFloat();
        if (!(frameTime > 0f) || !float.IsFinite(frameTime))
            throw new FormatException($"BVH: invalid Frame Time {frameTime}.");

        var motion = new float[frameCount][];
        for (int f = 0; f < frameCount; f++)
        {
            var row = new float[channelCount];
            for (int c = 0; c < channelCount; c++)
                row[c] = cursor.NextFloat();
            motion[f] = row;
        }

        // ---- units heuristic --------------------------------------------------------------
        float unitScale = HeuristicUnitScale(joints);

        // ---- skeleton ----------------------------------------------------------------------
        var defs = new List<BoneDefinition>(joints.Count);
        foreach (var j in joints)
        {
            defs.Add(new BoneDefinition(
                j.Name,
                j.Parent < 0 ? null : joints[j.Parent].Name,
                new XForm(j.Offset * unitScale, Quaternion.Identity)));
        }
        var skeleton = Skeleton.Skeleton.Create(defs);

        // ---- clip ----------------------------------------------------------------------------
        var clips = new List<Clip>();
        if (frameCount > 0)
            clips.Add(ResampleClip(joints, skeleton, motion, frameTime, unitScale, options.SampleFps));

        // BVH conventional axes: Y-up (1), Z-front (2), X-coord (0) — recorded, not converted.
        return new SourceScene(
            skeleton, clips, unitScale,
            upAxis: 1, upAxisSign: 1,
            frontAxis: 2, frontAxisSign: 1,
            coordAxis: 0, coordAxisSign: 1,
            originalUpAxis: -1);
    }

    // =====================================================================================
    // hierarchy parsing
    // =====================================================================================

    private sealed class Joint
    {
        public required string Name;
        public required int Parent;          // index into the joint list, -1 for roots
        public Vector3 Offset;               // raw file units
        public int PosX = -1, PosY = -1, PosZ = -1;            // motion column per position axis
        public List<(int Axis, int Column)> Rot = new();        // rotation channels in file order
        public bool HasPos => PosX >= 0 || PosY >= 0 || PosZ >= 0;
    }

    private static void ParseJoint(TokenCursor cursor, List<Joint> joints, int parent, ref int channelCount)
    {
        // Joint name: tokens up to '{', joined with '_' (mirrors Blender's handling of
        // names containing spaces).
        var nameParts = new List<string>();
        while (!cursor.PeekIs("{"))
        {
            if (cursor.AtEnd)
                throw new FormatException("BVH: unexpected end of file in joint name.");
            nameParts.Add(cursor.Next());
        }
        if (nameParts.Count == 0)
            throw new FormatException("BVH: joint with no name.");
        string name = UniqueName(string.Join('_', nameParts), joints);

        cursor.ExpectKeyword("{");
        cursor.ExpectKeyword("OFFSET");
        var joint = new Joint { Name = name, Parent = parent };
        joint.Offset = new Vector3(cursor.NextFloat(), cursor.NextFloat(), cursor.NextFloat());
        int index = joints.Count;
        joints.Add(joint);

        if (cursor.PeekIs("CHANNELS"))
        {
            cursor.Next();
            int n = cursor.NextInt();
            if (n < 0 || n > 6)
                throw new FormatException($"BVH: joint '{name}' has invalid channel count {n}.");
            for (int i = 0; i < n; i++)
            {
                string channel = cursor.Next();
                int column = channelCount++;
                switch (channel.ToUpperInvariant())
                {
                    case "XPOSITION": joint.PosX = column; break;
                    case "YPOSITION": joint.PosY = column; break;
                    case "ZPOSITION": joint.PosZ = column; break;
                    case "XROTATION": joint.Rot.Add((0, column)); break;
                    case "YROTATION": joint.Rot.Add((1, column)); break;
                    case "ZROTATION": joint.Rot.Add((2, column)); break;
                    default:
                        throw new FormatException($"BVH: unknown channel '{channel}' on joint '{name}'.");
                }
            }
        }

        while (!cursor.PeekIs("}"))
        {
            if (cursor.AtEnd)
                throw new FormatException($"BVH: unexpected end of file inside joint '{name}'.");
            if (cursor.PeekIs("JOINT"))
            {
                cursor.Next();
                ParseJoint(cursor, joints, index, ref channelCount);
            }
            else if (cursor.PeekIs("END"))
            {
                cursor.Next();
                cursor.ExpectKeyword("SITE");
                while (!cursor.PeekIs("{")) // a name after "End Site" is out of spec; skip it
                {
                    if (cursor.AtEnd)
                        throw new FormatException("BVH: unexpected end of file in End Site.");
                    cursor.Next();
                }
                cursor.ExpectKeyword("{");
                cursor.ExpectKeyword("OFFSET");
                var endOffset = new Vector3(cursor.NextFloat(), cursor.NextFloat(), cursor.NextFloat());
                cursor.ExpectKeyword("}");

                // Synthesize a channel-less leaf so the chain tip's direction is kept.
                joints.Add(new Joint
                {
                    Name = UniqueName(name + "_end", joints),
                    Parent = index,
                    Offset = endOffset,
                });
            }
            else
            {
                throw new FormatException(
                    $"BVH: unexpected token '{cursor.Next()}' inside joint '{name}'.");
            }
        }
        cursor.ExpectKeyword("}");
    }

    private static string UniqueName(string name, List<Joint> joints)
    {
        bool Taken(string candidate)
        {
            foreach (var j in joints)
                if (string.Equals(j.Name, candidate, StringComparison.Ordinal))
                    return true;
            return false;
        }

        if (!Taken(name))
            return name;
        for (int i = 1; ; i++)
        {
            string candidate = $"{name}#{i}";
            if (!Taken(candidate))
                return candidate;
        }
    }

    // =====================================================================================
    // units
    // =====================================================================================

    /// <summary>
    /// Meters-vs-centimeters heuristic: rest skeleton height (max−min world Y over all
    /// joints, end sites included) &lt; 10 → meters → ×100; otherwise centimeters → ×1.
    /// </summary>
    private static float HeuristicUnitScale(List<Joint> joints)
    {
        Span<float> worldY = joints.Count <= 256 ? stackalloc float[joints.Count] : new float[joints.Count];
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < joints.Count; i++)
        {
            worldY[i] = (joints[i].Parent < 0 ? 0f : worldY[joints[i].Parent]) + joints[i].Offset.Y;
            min = MathF.Min(min, worldY[i]);
            max = MathF.Max(max, worldY[i]);
        }
        float height = max - min;
        return height > 0f && height < MeterHeightThreshold ? 100f : 1f;
    }

    // =====================================================================================
    // motion sampling
    // =====================================================================================

    /// <summary>
    /// Decodes every native frame to per-joint local transforms (quaternions built per frame
    /// from the joint's channel order), then resamples onto the <paramref name="fps"/> grid —
    /// positions lerped, rotations slerped between the bracketing native frames.
    /// </summary>
    private static Clip ResampleClip(
        List<Joint> joints, Skeleton.Skeleton skeleton, float[][] motion,
        float frameTime, float unitScale, float fps)
    {
        int jointCount = joints.Count;
        int nativeCount = motion.Length;

        // Joint order may differ from skeleton bone order (topological sort) — map.
        var toSkeleton = new int[jointCount];
        for (int i = 0; i < jointCount; i++)
            toSkeleton[i] = skeleton.IndexOf(joints[i].Name);

        // Native-frame locals.
        var native = new XForm[nativeCount][];
        for (int f = 0; f < nativeCount; f++)
        {
            var row = motion[f];
            var locals = new XForm[jointCount];
            for (int i = 0; i < jointCount; i++)
                locals[i] = EvaluateLocal(joints[i], row, unitScale);
            native[f] = locals;
        }

        double duration = (nativeCount - 1) * (double)frameTime;
        int outCount = Math.Max(1, (int)Math.Round(duration * fps) + 1);

        var frames = new List<XForm[]>(outCount);
        for (int f = 0; f < outCount; f++)
        {
            double s = f / (double)fps / frameTime; // position on the native frame grid
            int i0 = Math.Clamp((int)Math.Floor(s), 0, nativeCount - 1);
            int i1 = Math.Min(i0 + 1, nativeCount - 1);
            float u = Math.Clamp((float)(s - i0), 0f, 1f);

            var frame = new XForm[skeleton.Count];
            var a = native[i0];
            var b = native[i1];
            for (int i = 0; i < jointCount; i++)
            {
                frame[toSkeleton[i]] = new XForm(
                    Vector3.Lerp(a[i].Pos, b[i].Pos, u),
                    MathQ.Normalize(Quaternion.Slerp(a[i].Rot, b[i].Rot, u)));
            }
            frames.Add(frame);
        }

        return new Clip("motion", fps, looping: false, frames);
    }

    /// <summary>One joint's local transform from one motion row (see class remarks).</summary>
    private static XForm EvaluateLocal(Joint joint, float[] row, float unitScale)
    {
        // Position channels replace the OFFSET; absent channels (or no position channels at
        // all) fall back per Blender's semantics described in the class remarks.
        Vector3 pos = joint.HasPos
            ? new Vector3(
                joint.PosX >= 0 ? row[joint.PosX] : 0f,
                joint.PosY >= 0 ? row[joint.PosY] : 0f,
                joint.PosZ >= 0 ? row[joint.PosZ] : 0f)
            : joint.Offset;

        // R = R_chan1 * R_chan2 * R_chan3 (column-vector convention; degrees in the file).
        var rot = Quaternion.Identity;
        foreach (var (axis, column) in joint.Rot)
        {
            float radians = row[column] * (MathF.PI / 180f);
            var axisVector = axis switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                _ => Vector3.UnitZ,
            };
            rot *= Quaternion.CreateFromAxisAngle(axisVector, radians);
        }

        return new XForm(pos * unitScale, MathQ.Normalize(rot));
    }

    // =====================================================================================
    // tokenizer
    // =====================================================================================

    /// <summary>Whitespace token stream over the BVH text (BVH is line-format agnostic).</summary>
    private sealed class TokenCursor
    {
        private readonly string[] _tokens;
        private int _pos;

        public TokenCursor(string text)
            => _tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        public bool AtEnd => _pos >= _tokens.Length;

        public bool PeekIs(string keywordUpper)
            => _pos < _tokens.Length &&
               string.Equals(_tokens[_pos], keywordUpper, StringComparison.OrdinalIgnoreCase);

        public string Next()
        {
            if (AtEnd)
                throw new FormatException("BVH: unexpected end of file.");
            return _tokens[_pos++];
        }

        public void ExpectKeyword(string keywordUpper)
        {
            string token = Next();
            if (!string.Equals(token, keywordUpper, StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"BVH: expected '{keywordUpper}', found '{token}'.");
        }

        public int NextInt()
        {
            string token = Next();
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new FormatException($"BVH: expected an integer, found '{token}'.");
            return value;
        }

        public float NextFloat()
        {
            string token = Next();
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                !float.IsFinite(value))
                throw new FormatException($"BVH: expected a number, found '{token}'.");
            return value;
        }
    }
}
