using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdBone
{
	internal string Name = "";
	internal int Parent = -1;
	internal Vector3 Position;
	internal int Flags;
	internal int Layer;
	internal int AppendParent = -1;
	internal float AppendWeight;
	internal int IkTarget = -1;
	internal int Iterations;
	internal float AngleLimit;
	internal List<MmdIkLink> Links = new();
}
