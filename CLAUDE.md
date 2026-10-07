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
- Other libraries bind Core types by name (sbox-humanoid-fitter: `Skeleton`, `BoneDefinition`, `XForm`, `AutoMapper`,
  `MappingResult`). They carry `[Alias]` with their pre-2026-10-06 names; `Core/AliasAttribute.cs` is a stand-in
  compiled only outside s&box (`#if !SANDBOX`). Renaming any of these breaks the fitter.
- `dev/HumanoidRetargeter.Dev.csproj` (net8.0) is referenced by `tests/HumanoidRetargeter.Tests`, the `dev/tools`
  probes and sbox-humanoid-fitter's tests (`..\..\..\humanoid-retargeter\dev\HumanoidRetargeter.Dev.csproj`): keep its path.
- Test data the tests open by literal repo path (so it must not move): `Assets/humanoid_retargeter/*`,
  `dev/HumanoidRetargeter.Tests/fixtures/` (also copied into the test output), `dev/corpus/` (local-only; those
  tests pass silently when it is absent), `dev/m0/ref_idlepose.dmx`. This is why `Assets/humanoid_retargeter/`
  is not under `Assets/data/` yet (sbox-check reports it): moving it needs test call-site edits and changes the
  mounted asset paths (`humanoid_retargeter/...`) that `EditorPipeline`, `DlAssets` and `UserPresets` use.
- `TargetRigGenerator` writes a description string that names `research/rig_human_male.json`; it is baked into
  the shipped `target_rig_sbox*.json` and checked by a regenerate-and-diff test, so leave it as is.
- `Editor/Embedded/` is vendored, trimmed third-party code (VRF 17.0, Datamodel.NET, ZstdSharp, ValveKeyValue);
  read its README before touching it. VRF 17 is intentional.
- The editor gates (`M0Gate`, `UiSmokeGate`, `*Gate`) only arm with their env var plus a one-shot marker file
  written by the `dev/editor-rig` scripts; normal users never trigger them.
- `dev/data/verification` holds the Blender verification harness, its reports and renders (`run_gates.ps1`).
