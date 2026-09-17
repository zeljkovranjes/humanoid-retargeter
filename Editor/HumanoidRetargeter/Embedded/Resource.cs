// Adapted from ValveResourceFormat 17.0 Resource.cs (MIT); see ValveResourceFormat/LICENSE.
// Only the resource blocks needed for model and animgraph recovery are dispatched here.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HumanoidRetargeterVrf.Blocks;
using HumanoidRetargeterVrf.ResourceTypes;

namespace HumanoidRetargeterVrf;

public sealed class Resource : IDisposable
{
    public BinaryReader Reader { get; private set; } = null!;
    public string FileName { get; set; } = "";
    public ushort Version { get; private set; }
    public List<Block> Blocks { get; } = new();
    public ResourceEditInfo? EditInfo => Blocks.OfType<ResourceEditInfo>().FirstOrDefault();
    public ResourceExtRefList? ExternalReferences => GetBlockByType(BlockType.RERL) as ResourceExtRefList;
    public Block? DataBlock => GetBlockByType(BlockType.DATA);
    public Block? GetBlockByType(BlockType type) => Blocks.FirstOrDefault(b => b.Type == type);
    public Block GetBlockByIndex(int index) => Blocks[index];
    public bool ContainsBlockType(BlockType type) => Blocks.Any(b => b.Type == type);

    public void Read(string file)
    {
        if (string.IsNullOrEmpty(FileName)) FileName = file;
        Reader = new BinaryReader(File.OpenRead(file));
        var size = Reader.ReadUInt32();
        if (size > Reader.BaseStream.Length || size < 16 || Reader.ReadUInt16() != 12)
            throw new InvalidDataException("Not a supported Source 2 resource header.");
        Version = Reader.ReadUInt16();
        var offset = Reader.ReadUInt32();
        var count = Reader.ReadUInt32();
        if (count > 4096 || offset + 8L + count * 12L > size) throw new InvalidDataException("Invalid resource block table.");
        Reader.BaseStream.Position = offset + 8;
        for (var i = 0; i < count; i++)
        {
            var name = System.Text.Encoding.ASCII.GetString(Reader.ReadBytes(4));
            var start = Reader.BaseStream.Position;
            var relative = Reader.ReadUInt32();
            var length = Reader.ReadUInt32();
            if (start + relative + length > size) throw new InvalidDataException("Resource block is out of bounds.");
            if (!Enum.TryParse<BlockType>(name, out var type)) type = BlockType.Undefined;
            Block block = type switch
            {
                BlockType.RERL => new ResourceExtRefList(),
                BlockType.NTRO => new ResourceIntrospectionManifest(),
                BlockType.REDI => new ResourceEditInfo(),
                BlockType.RED2 => new ResourceEditInfo2(),
                BlockType.VBIB => new VBIB(),
                BlockType.MBUF => new MBUF(),
                BlockType.MDAT => new Mesh(BlockType.MDAT),
                BlockType.PHYS => new PhysAggregateData(BlockType.PHYS),
                BlockType.MRPH => new Morph(BlockType.MRPH),
                BlockType.CTRL or BlockType.INSG => new BinaryKV3(type),
                BlockType.ANIM => new KeyValuesOrNTRO(type, "AnimationResourceData_t"),
                BlockType.AGRP => new KeyValuesOrNTRO(type, "AnimationGroupResourceData_t"),
                BlockType.ASEQ => new KeyValuesOrNTRO(type, "SequenceGroupResourceData_t"),
                BlockType.DATA when FileName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase) => new Model(),
                BlockType.DATA when FileName.EndsWith(".vmesh", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".vmesh_c", StringComparison.OrdinalIgnoreCase) => new Mesh(BlockType.DATA),
                BlockType.DATA when FileName.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase) => new Material(),
                BlockType.DATA when FileName.EndsWith(".vphys", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".vphys_c", StringComparison.OrdinalIgnoreCase) => new PhysAggregateData(),
                BlockType.DATA => new KeyValuesOrNTRO(),
                _ => new OpaqueBlock(type)
            };
            block.Offset = checked((uint)(start + relative)); block.Size = length; block.Resource = this;
            Blocks.Add(block);
        }
        // Introspection and dependencies must be available before interpreting DATA.
        foreach (var block in Blocks.Where(b => b.Type is BlockType.NTRO or BlockType.RERL or BlockType.REDI or BlockType.RED2)) block.Read(Reader);
        foreach (var block in Blocks.Where(b => b.Type is not (BlockType.NTRO or BlockType.RERL or BlockType.REDI or BlockType.RED2 or BlockType.DATA))) block.Read(Reader);
        DataBlock?.Read(Reader);
    }

    public void Dispose() => Reader?.Dispose();

    sealed class OpaqueBlock(BlockType type) : Block
    {
        public override BlockType Type => type;
        public override void Read(BinaryReader reader) { }
        public override void WriteText(IndentedTextWriter writer) => throw new NotSupportedException();
        public override void Serialize(Stream stream) => throw new NotSupportedException();
    }
}
