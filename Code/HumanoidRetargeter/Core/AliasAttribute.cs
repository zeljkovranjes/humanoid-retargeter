#if !SANDBOX
namespace HumanoidRetargeter.Core;

/// <summary>
/// Plain-.NET stand-in for the engine's global <c>AliasAttribute</c>, so Core types can carry their
/// pre-restructure full names (other libraries bind them by name through TypeLibrary) and still
/// build in the dev harness and tests. Compiled out of every s&amp;box build (SANDBOX is defined there),
/// where the engine's own attribute is used.
/// </summary>
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )]
internal sealed class AliasAttribute : Attribute
{
	public AliasAttribute( params string[] value ) => Value = value;

	public string[] Value { get; }
}
#endif
