using System.Numerics;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector4 = System.Numerics.Vector4;

internal sealed class MmdMaterial
{
	internal string Name = "";
	internal Vector4 Diffuse;
	internal System.Numerics.Vector3 Specular;
	internal System.Numerics.Vector3 Ambient;
	internal float SpecularPower;
	internal Vector4 TextureTint = Vector4.One;
	internal Vector4 SphereTint = Vector4.One;
	internal Vector4 ToonTint = Vector4.One;
	internal string Texture = "";
	internal bool DoubleSided;
	internal int IndexCount;
	internal string SphereTexture = "";
	internal int SphereMode;
	internal string ToonTexture = "";
	internal Vector4 EdgeColor;
	internal float EdgeSize;
	internal int ToonIndex = -1;
	internal MmdMaterial Copy()
	{
		var copy = new MmdMaterial
		{
			Name = Name,
			Texture = Texture,
			DoubleSided = DoubleSided,
			IndexCount = IndexCount,
			SphereTexture = SphereTexture,
			SphereMode = SphereMode,
			ToonTexture = ToonTexture,
			ToonIndex = ToonIndex,
		};
		copy.Reset( this );
		return copy;
	}

	internal void Reset( MmdMaterial original )
	{
		Diffuse = original.Diffuse;
		Specular = original.Specular;
		SpecularPower = original.SpecularPower;
		Ambient = original.Ambient;
		EdgeColor = original.EdgeColor;
		EdgeSize = original.EdgeSize;
		TextureTint = original.TextureTint;
		SphereTint = original.SphereTint;
		ToonTint = original.ToonTint;
	}
}
