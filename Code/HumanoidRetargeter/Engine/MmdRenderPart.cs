#if SANDBOX
using Sandbox;

namespace HumanoidRetargeter.Engine;

internal sealed class MmdRenderPart
{
	internal int Material;
	internal bool Outline;
	internal int[] Vertices;
	internal MmdRenderVertex[] Buffer;
	internal Mesh Mesh;
	internal SceneObject Object;
}
#endif
