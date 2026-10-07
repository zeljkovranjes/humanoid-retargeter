#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumanoidRetargeter.Core.Target;

public sealed record SmartPortClip(string Name, bool Looping, bool Additive, bool Hidden);
