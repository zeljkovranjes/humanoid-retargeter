using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

internal sealed class MmdMorph
{
	internal string Name = "";
	internal Dictionary<int, Vector3> Vertices = new();
	internal List<(int Bone, Vector3 Position, Quaternion Rotation)> Bones = new();
	internal List<(int Morph, float Weight)> Groups = new();
	internal List<(int Vertex, int Channel, Vector4 Offset)> Uvs = new();
	internal List<MmdMaterialMorph> Materials = new();
	internal List<(int Body, bool Local, Vector3 Velocity, Vector3 Torque)> Impulses = new();
}
