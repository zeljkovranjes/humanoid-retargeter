// Minimal extraction-only adapters for the vendored VRF model exporter (MIT).
#nullable enable
using System;
using System.Collections.Generic;
using HumanoidRetargeterVrf.ResourceTypes;

namespace HumanoidRetargeterVrf.IO
{
    public interface IFileLoader
    {
        Resource? LoadFile(string file);
        Resource? LoadFileCompiled(string file);
    }
    public sealed class ContentFile : IDisposable
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = "";
        public List<SubFile> SubFiles { get; } = new();
        public void AddSubFile(string name, Func<byte[]> extract) => SubFiles.Add(new SubFile(name, extract));
        public void Dispose() { }
    }
    public sealed record SubFile(string FileName, Func<byte[]> Extract);
    internal static class FileExtract
    {
        internal static void EnsurePopulatedStringToken(IFileLoader loader) { }
    }
}

namespace HumanoidRetargeterVrf.ResourceTypes
{
    // Model/animation export only needs controller definitions from MRPH. The upstream
    // DMX mesh exporter does not export vertex morph deltas. Keep source targets when possible.
    public sealed class Morph(BlockType type) : KeyValuesOrNTRO(type, "MorphSetData_t")
    {
        public ModelFlex.FlexController[] FlexControllers => Array.Empty<ModelFlex.FlexController>();
    }
}
