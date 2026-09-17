# Compiled model recovery

Editor-only source integration; no CLI executable, binary package, native DLL, runtime download or subprocess. These files compile with the library's editor assembly, not the shipped game code.

Vendored sources (licenses retained alongside each component):

- [ValveResourceFormat 17.0](https://github.com/ValveResourceFormat/ValveResourceFormat/tree/17.0), MIT: resource headers, binary KV3/NTRO, mesh buffers, skeletal animations, ModelDoc/DMX export and associated types. The meshoptimizer decoder is also MIT; see `Compression/MESHOPTIMIZER-LICENSE`.
- [Datamodel.NET d06da8dd6bf351171dd448288f7ff945754054c9](https://github.com/ValveResourceFormat/Datamodel.NET/tree/d06da8dd6bf351171dd448288f7ff945754054c9), MIT: DMX model serialization.
- [ZstdSharp 0.8.6](https://github.com/oleg-st/ZstdSharp/tree/0.8.6), MIT: decompressor and its reachable dependencies only. Compression, dictionary training, streams and thread pools are excluded. Underlying Zstandard 1.5.7 attribution is in `ZSTANDARD-LICENSE` (BSD).
- [ValveKeyValue b0f2917f8c09697e9fd41c4f23647a34c5803f0d](https://github.com/ValveResourceFormat/ValveKeyValue/tree/b0f2917f8c09697e9fd41c4f23647a34c5803f0d), MIT: `KVValueType` enum only.

## Local adaptations

Namespaces are prefixed `HumanoidRetargeter` and numerics types are explicitly qualified to avoid engine/global-type conflicts. Global usings are made explicit. Optional Fody inlining attributes are removed. Zstd's generated `Methods` members are mechanically trimmed to those reachable from `Decompressor` and framework conditionals are specialized for s&box's .NET 10 runtime. The assembly-wide `SkipLocalsInit` attribute is omitted so it cannot change unrelated editor code; the obsolete volatile read uses `Volatile.Read`. Decompression algorithms are otherwise unchanged.

`Resource.cs` dispatches only the blocks used for model/graph recovery. `ExtractionTypes.cs` supplies the small file-loader/content abstractions. `Lz4.cs` provides bounded block and chained-dictionary decoding. Viewer, shader, texture, glTF, native entry point and unrelated resource export code is excluded. Mesh index decoding is brought over without the glTF exporter. Unused material shader evaluation and diagnostic REDI serialization are omitted.

The ModelDoc exporter fixes quaternion-to-Euler pole handling. Current s&box's packed octahedral normals are decoded; ModelDoc regenerates their tangents from UVs. The host serializes ModelDoc numbers without exponents and confines every recovered file to the chosen output directory.

Version 17 is intentional: later exporter versions omit legacy ANIM-only sequences present in s&box models. Do not upgrade based only on parsing/compiling successfully: run the optional compiled fixture test and native play-mode test in `tests/SmartPort`.

## Limits

Recovery is not a lossless reconstruction of the original authoring files. The upstream DMX mesh exporter does not recover vertex morph deltas or cloth authoring. Unsupported encodings fail explicitly. Editable source is preferred; use only assets you have permission to reuse. Smart Port also checks sequence coverage and the compiled target bind transforms after compilation.
