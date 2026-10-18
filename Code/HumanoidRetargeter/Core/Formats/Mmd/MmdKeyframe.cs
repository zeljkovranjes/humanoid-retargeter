using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed record MmdKeyframe( int Frame, Vector3 Position, Quaternion Rotation, byte[] Interpolation );
