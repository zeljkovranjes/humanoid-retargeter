#if SANDBOX
using System.Runtime.InteropServices;
using Sandbox;

namespace HumanoidRetargeter.Engine;

[StructLayout( LayoutKind.Sequential )]
internal struct MmdRenderVertex
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector4 Tangent;
	public Vector2 Uv;
	public Vector2 SphereUv;

	internal static VertexAttribute[] Layout => new VertexAttribute[]
	{
		new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3, 0 ),
		new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3, 0 ),
		new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4, 0 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
		new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
	};
}
#endif
