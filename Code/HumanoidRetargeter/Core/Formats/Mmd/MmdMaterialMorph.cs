using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

internal sealed class MmdMaterialMorph
{
	internal int Material;
	internal bool Add;
	internal Vector4 Diffuse;
	internal Vector3 Specular;
	internal float Power;
	internal Vector3 Ambient;
	internal Vector4 EdgeColor;
	internal float EdgeSize;
	internal Vector4 Texture;
	internal Vector4 Sphere;
	internal Vector4 Toon;

	internal void Apply( MmdMaterial target, float weight )
	{
		float Scalar( float value, float offset ) => Add ? value + offset * weight : value * (1 + (offset - 1) * weight);
		Vector3 Three( Vector3 value, Vector3 offset ) => Add ? value + offset * weight : value * (Vector3.One + (offset - Vector3.One) * weight);
		Vector4 Four( Vector4 value, Vector4 offset ) => Add ? value + offset * weight : value * (Vector4.One + (offset - Vector4.One) * weight);
		target.Diffuse = Four( target.Diffuse, Diffuse );
		target.Specular = Three( target.Specular, Specular );
		target.SpecularPower = Scalar( target.SpecularPower, Power );
		target.Ambient = Three( target.Ambient, Ambient );
		target.EdgeColor = Four( target.EdgeColor, EdgeColor );
		target.EdgeSize = Scalar( target.EdgeSize, EdgeSize );
		target.TextureTint = Four( target.TextureTint, Texture );
		target.SphereTint = Four( target.SphereTint, Sphere );
		target.ToonTint = Four( target.ToonTint, Toon );
	}
}
