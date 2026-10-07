// Minimal extraction-only adapters for the vendored VRF model exporter (MIT).
#nullable enable
using System;
using System.Collections.Generic;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO;

    public sealed record SubFile(string FileName, Func<byte[]> Extract);
