#if SANDBOX
namespace HumanoidRetargeter.Engine;

/// <summary>
/// The animation graph system that drives a model: s&amp;box's original AnimGraph (<c>.vanmgrph</c>) or AnimGraph2
/// (<c>.ag</c>), which newer engines add. AnimGraph2 is reached only through the type library, so this compiles and runs
/// on engines without it, where everything is <see cref="Legacy"/>.
/// </summary>
public enum AnimGraphVersion
{
	/// <summary>AnimGraph1, the original <c>.vanmgrph</c> graphs.</summary>
	Legacy,

	/// <summary>AnimGraph2, <c>.ag</c> graphs.</summary>
	AnimGraph2,
}
#endif
