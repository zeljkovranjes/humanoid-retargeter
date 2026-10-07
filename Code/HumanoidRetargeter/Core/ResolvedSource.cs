#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core;

/// <summary>Imported source after the public mapping and bind-rest preparation cascade.</summary>
public sealed record ResolvedSource( SourceScene Scene, MappingResult Mapping, MappingReportInfo Report );
