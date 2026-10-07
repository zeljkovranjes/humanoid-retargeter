#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Target;

/// <summary>The rewritten entry graph plus every subgraph it now references by a project-owned
/// copy, keyed by that copy's asset path.</summary>
public sealed record StockGraphReplacement(string Graph, IReadOnlyDictionary<string, string> Subgraphs, int References);
