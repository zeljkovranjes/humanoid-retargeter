# Changelog

## 2026-10-06
### Breaking
- Runtime namespaces moved under `HumanoidRetargeter.Core`. Code that calls the library directly needs its `using` lines updated:
  - `HumanoidRetargeter` (`Retargeter`, `RetargetRequest`, `RetargetResult`, `RetargetBatchResult`, `RetargetTargetSpec`, `ClipResult`, `ResolvedSource`, `InspectResult`, `MappingReportInfo`, `BatchOptions`, `SolverKind`, `TargetUpAxis`, `CoreInfo`) -> `HumanoidRetargeter.Core`
  - `HumanoidRetargeter.Cleanup` -> `HumanoidRetargeter.Core.Cleanup`
  - `HumanoidRetargeter.Dl` -> `HumanoidRetargeter.Core.Dl`
  - `HumanoidRetargeter.Formats` (and `.Ant`, `.Bvh`, `.Dmx`, `.Fbx`, `.Gltf`, `.Renderware`) -> `HumanoidRetargeter.Core.Formats` (same sub-namespaces)
  - `HumanoidRetargeter.Mapping` -> `HumanoidRetargeter.Core.Mapping`
  - `HumanoidRetargeter.Maths` -> `HumanoidRetargeter.Core.Maths`
  - `HumanoidRetargeter.Skeleton` -> `HumanoidRetargeter.Core.Skeleton`
  - `HumanoidRetargeter.Solve` -> `HumanoidRetargeter.Core.Solve`
  - `HumanoidRetargeter.Target` -> `HumanoidRetargeter.Core.Target`
- Editor namespaces renamed: `HumanoidRetargeter.Editor` and `HumanoidRetargeter.M0` -> `HumanoidRetargeter.EditorTools`.
- Vendored editor code moved to `HumanoidRetargeter.EditorTools.Embedded.*`: `HumanoidRetargeterVrf` -> `.ValveResourceFormat`, `HumanoidRetargeterZstd` -> `.ZstdSharp`, `HumanoidRetargeterDmx` -> `.Datamodel` (its `HumanoidRetargeterDmx` class is `Datamodel` again), `HumanoidRetargeterKeyValue` -> `.ValveKeyValue`, `HumanoidRetargeterCompression` -> `.Compression`.
- Shipped data moved from `Assets/humanoid_retargeter/` to `Assets/data/humanoid_retargeter/` (target rigs, profiles, DL weights). Code that read these files by path must use the new folder. New user presets are saved to `Assets/data/humanoid_retargeter/profiles/user/`; presets saved earlier in `Assets/humanoid_retargeter/profiles/user/` are still found.
- Libraries that bind `Skeleton`, `BoneDefinition`, `XForm`, `AutoMapper` or `MappingResult` by name through TypeLibrary keep working through aliases for the old names; switch to the new `HumanoidRetargeter.Core.*` names when convenient.
### Changed
- The package now follows the workspace layout: all runtime code is in `Code/HumanoidRetargeter/Core` (engine-free, builds as plain .NET), editor code sits directly in `Editor/`, one type per file. Retargeting behaviour is unchanged and every existing test passes unmodified.
- The main test project moved from `dev/` to `tests/HumanoidRetargeter.Tests`; research notes moved to `docs/research`, verification renders and dumps to `dev/data/verification`.
- Package summary, description and tags rewritten for the library manager; README rewritten in the standard format.
