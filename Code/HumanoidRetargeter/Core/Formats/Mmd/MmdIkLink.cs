using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed record MmdIkLink( int Bone, bool Limited, Vector3 Minimum, Vector3 Maximum );
