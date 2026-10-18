#if SANDBOX
using Sandbox;

namespace HumanoidRetargeter.Resources;

/// <summary>An exported MMD character and animation, including deformation, morphs and physics.</summary>
[AssetType( Name = "MMD Animation", Extension = "mmdanim", Category = "Animation" )]
public sealed class MmdAnimation : GameResource
{
	/// <summary>Versioned payload written by the retargeter.</summary>
	[Hide]
	public string Payload { get; set; } = "";

	/// <summary>Surface materials, in the original PMX/PMD material order.</summary>
	public Material[] Materials { get; set; } = Array.Empty<Material>();

	/// <summary>Outline materials in the same order; null entries disable outlines.</summary>
	public Material[] Outlines { get; set; } = Array.Empty<Material>();
}
#endif
