#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Target;

/// <summary>An entry graph with its bone references renamed, the subgraph copies it now points at (keyed by
/// asset path) and the subgraphs that could not be read (still referenced as they were).</summary>
public sealed record SmartPortGraphCopy(string Graph, IReadOnlyDictionary<string, string> Subgraphs, IReadOnlyList<string> Unread);
