<!-- sbox-standard: v1 | type: library | root: HumanoidRetargeter -->
# Humanoid Retargeter

**Standard: sbox-standard v1** (library, root namespace `HumanoidRetargeter`). This package follows the package layout and
code standard in E:\.sbox\CLAUDE.md (loaded automatically), enforced by `sbox-check`. Run it before
calling work done.

s&box library `notpointless.chomnr_humanoid_retargeter`.

## Package facts

Only what the code can't tell you: design decisions, engine gotchas found here, where test assets live.

- All runtime code is engine-free and lives in `Code/HumanoidRetargeter/Core`; the runtime assembly has no
  Sandbox references at all. Everything that touches the engine (file IO, asset compile, preview, UI) is in `Editor/`.
- s&box declares `Vector3` in the global namespace: Core files use the namespace-scoped alias
  `using Vector3 = System.Numerics.Vector3;` after the namespace line (see `Code/HumanoidRetargeter/Assembly.cs`).
- Other packages bind types by name (fitter, mocap, weapon-importer, rigger tools, Source 1 Migrator via
  `SmartPortHeadless.PortJson`). Every such type carries `[Alias]` with its pre-2026-10-06 name; `Core/AliasAttribute.cs` is a stand-in
  compiled only outside s&box (`#if !SANDBOX`). Renaming any of these breaks those packages; Editor files that use `[Alias]` and also compile in the SmartPort parser tests need `using HumanoidRetargeter.Core;`.
- `dev/HumanoidRetargeter.Dev.csproj` (net8.0) is referenced by `tests/HumanoidRetargeter.Tests`, the `dev/tools`
  probes and sbox-humanoid-fitter's tests (`..\..\..\humanoid-retargeter\dev\HumanoidRetargeter.Dev.csproj`): keep its path.
- Shipped data (target rigs, profiles, DL weights) is in `Assets/data/humanoid_retargeter/` (moved from
  `Assets/humanoid_retargeter/` on 2026-10-06). `EditorPipeline` and `DlAssets` find it by that Assets-relative path
  in the open project or under `Libraries/*/Assets`. User presets are written to the user's
  `Assets/data/humanoid_retargeter/profiles/user/` and still read from the old `Assets/humanoid_retargeter/profiles/user/`.
- Test data the tests open by literal repo path (move it only together with those path strings):
  `Assets/data/humanoid_retargeter/*`, `dev/HumanoidRetargeter.Tests/fixtures/` (also copied into the test output),
  `dev/corpus/` (local-only; those tests pass silently when it is absent), `dev/m0/ref_idlepose.dmx`.
- `TargetRigGenerator` writes a description string that names `research/rig_human_male.json`; it is baked into
  the shipped `target_rig_sbox*.json` and checked by a regenerate-and-diff test, so leave it as is.
- `Editor/Embedded/` is vendored, trimmed third-party code (VRF 17.0, Datamodel.NET, ZstdSharp, ValveKeyValue);
  read its README before touching it. VRF 17 is intentional.
- The editor gates (`M0Gate`, `UiSmokeGate`, `*Gate`) only arm with their env var plus a one-shot marker file
  written by the `dev/editor-rig` scripts; normal users never trigger them.
- `dev/data/verification` holds the Blender verification harness, its reports and renders (`run_gates.ps1`).
