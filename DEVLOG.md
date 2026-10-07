# Humanoid Retargeter

Journal for this library. Keep it current: decisions, engine gotchas, what failed and why, the next step.

## Package

| | |
|---|---|
| Ident | `notpointless.chomnr_humanoid_retargeter` |
| Type | library |
| Root namespace | `HumanoidRetargeter` |
| Depends on | none |
| Used by | `sbox-humanoid-fitter` (TypeLibrary binding at runtime; its tests compile against `dev/HumanoidRetargeter.Dev.csproj`) |
| Published | last publish artifacts (`.bin`, `.version` 1.0.307486) kept in `dev/` |

## Restructure into sbox-standard v1 (2026-10-06)

- Everything under `Code/` was already engine-free (it compiled in the plain-.NET harness), so all of it went to
  `Core/`; there is no Engine/Components layer. Feature folders kept (Cleanup, Dl, Formats, Mapping, Maths,
  Skeleton, Solve, Target); multi-type files split one type per file with a Roslyn script (text moved verbatim).
- `[Alias]` on the five types sbox-humanoid-fitter binds by name. `AliasAttribute` is an engine global type, so
  Core carries an internal stand-in compiled only when `SANDBOX` is not defined (plain .NET builds).
  Unverified in the editor: that `TypeLibrary.GetType( "old name" )` resolves through `[Alias]`.
- `Editor/HumanoidRetargeter/*` flattened to `Editor/` (`HumanoidRetargeter.EditorTools`). Vendored code moved
  to `Editor/Embedded/<Component>/`, folder = namespace, block namespaces made file-scoped; the
  `HumanoidRetargeterDmx` class got its upstream name `Datamodel` back because it collided with the namespace.
- Not moved, on purpose: `Assets/humanoid_retargeter/` (sbox-check wants `Assets/data/...`), `dev/corpus`,
  `dev/m0`, `dev/HumanoidRetargeter.Tests/fixtures`. Tests open these by literal repo path in test code, and
  test code may not change. Moving `Assets/` also changes the mounted asset paths the editor and installed users rely on.
- Baseline and final results: `dev/out/baseline.txt`, `dev/out/final.txt`.

## Log

- 2026-10-06: brought under the workspace standard (layout, namespaces, docs); no behaviour change, all tests pass
