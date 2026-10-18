using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

internal sealed class MmdVertex
{
	internal Vector3 Position;
	internal Vector3 Normal;
	internal Vector2 Uv;
	internal Vector4[] AdditionalUv = Array.Empty<Vector4>();
	internal byte Deform;
	internal Vector3 SdefC;
	internal Vector3 SdefR0;
	internal Vector3 SdefR1;
	internal float EdgeScale = 1f;
	internal int[] Bones = new int[ 4 ];
	internal float[] Weights = new float[ 4 ];
}
