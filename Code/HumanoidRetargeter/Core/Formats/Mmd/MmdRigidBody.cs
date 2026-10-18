namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdRigidBody
{
	internal int Bone;
	internal int Group;
	internal int ExcludedGroups;
	internal int Shape;
	internal Vector3 Size;
	internal Vector3 Position;
	internal Vector3 Rotation;
	internal float Mass;
	internal float LinearDamping;
	internal float AngularDamping;
	internal float Restitution;
	internal float Friction;
	internal int Mode;
}
