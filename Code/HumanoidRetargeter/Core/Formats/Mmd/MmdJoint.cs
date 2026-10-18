namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdJoint
{
	internal int BodyA;
	internal int BodyB;
	internal Vector3 Position;
	internal Vector3 Rotation;
	internal Vector3 LinearMin;
	internal Vector3 LinearMax;
	internal Vector3 AngularMin;
	internal Vector3 AngularMax;
	internal Vector3 LinearSpring;
	internal Vector3 AngularSpring;
}
