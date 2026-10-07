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
- Code that looks types up by their old full name through TypeLibrary keeps working through aliases: `Skeleton`, `BoneDefinition`, `XForm`, `AutoMapper`, `MappingResult`, `MappingSource`, `BoneRole`, `TargetRig`, `RuntimePoseRetargeter`, `Retargeter`, `RetargetRequest`, `BatchOptions`, `EditorPipeline`, `RetargetWindow`, `SmartPortHeadless` (and its `SmartPortHeadlessRequest`, `SmartPortHeadlessResult`, `SmartPortBone`). Switch to the new names when convenient.
### Added
- Headless Smart Port (`HumanoidRetargeter.EditorTools.SmartPortHeadless`, JSON entry point `PortJson`) runs Smart Port without the editor window, for tools that convert characters in bulk; DMX element ids can be made deterministic.
- Smart Port takes explicit target bones for roles the mapper cannot find, and re-expresses attachments that ignore their bones' rotation or blend several bones in the ported skeleton's frames, so the body aim no longer bends a ValveBiped spine over.
### Changed
- The package now follows the workspace layout: all runtime code is in `Code/HumanoidRetargeter/Core` (engine-free, builds as plain .NET), editor code sits directly in `Editor/`, one type per file. Retargeting behaviour is unchanged and every existing test passes unmodified.
- The main test project moved from `dev/` to `tests/HumanoidRetargeter.Tests`; research notes moved to `docs/research`, verification renders and dumps to `dev/data/verification`.
- Package summary, description and tags rewritten for the library manager; README rewritten in the standard format.
- Stock locomotion replacement now replaces each direction on every speed ring (run, fast run, sprint; walk, fast walk; crouch, low crouch), so a replaced forward run shows at the default player speed, and it follows the Citizen graph's subgraphs into project-owned copies instead of failing.
### Fixed
- Characters bound in a T-pose (most Blender characters, including ones rigged with Humanoid Rigger) no longer play stock Citizen animations with the arms held about 50° too high. "Create Citizen animation model" now points every limb where the Citizen's limb points, so arms, shoulders and biceps no longer stretch.
