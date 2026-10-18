namespace HumanoidRetargeter.Core.Formats.Mmd;

internal sealed class MmdSoftBody
{
	internal string Name = "";
	internal int Shape;
	internal int Material;
	internal int Group;
	internal int ExcludedGroups;
	internal int Flags;
	internal int LinkDistance;
	internal int Clusters;
	internal float Mass;
	internal float Margin;
	internal int AeroModel;
	internal float[] Config = new float[ 12 ];
	internal float[] Cluster = new float[ 6 ];
	internal int[] Iterations = new int[ 4 ];
	internal float[] Stiffness = new float[ 3 ];
	internal List<(int Body, int Vertex, bool Near)> Anchors = new();
	internal HashSet<int> Pins = new();
}
