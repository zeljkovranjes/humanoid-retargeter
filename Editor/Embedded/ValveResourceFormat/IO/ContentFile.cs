// Minimal extraction-only adapters for the vendored VRF model exporter (MIT).
#nullable enable
using System;
using System.Collections.Generic;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO;

    public sealed class ContentFile : IDisposable
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = "";
        public List<SubFile> SubFiles { get; } = new();
        public void AddSubFile(string name, Func<byte[]> extract) => SubFiles.Add(new SubFile(name, extract));
        public void Dispose() { }
    }
