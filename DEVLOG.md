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
- `Assets/humanoid_retargeter/` moved to `Assets/data/humanoid_retargeter/` (coordinator decision, same as puppeteer):
  the 40 literal `"Assets", "humanoid_retargeter"` path strings in tests got `"data"` inserted, nothing else on
  those lines changed. `UserPresets` writes to the new folder and still reads presets saved in the old one.
- Not moved, on purpose: `dev/corpus`, `dev/m0`, `dev/HumanoidRetargeter.Tests/fixtures`. Tests open these by
  literal repo path in test code.
- Baseline and final results: `dev/out/baseline.txt`, `dev/out/final.txt`.

## Log

- 2026-10-06: brought under the workspace standard (layout, namespaces, docs); no behaviour change, all tests pass
- 2026-10-06: user report "body deforms in Citizen animations, fine in Mixamo" (Humanoid Rigger, Human Citizen Male
  Complete, then Create Citizen animation model). Reproduced with humanMesh6.fbx in the rigger's owned editor:
  every clip, bindPose included, kept the arms ~50 degrees off the stock pose. Cause: FittedCitizenPose mapped rest to
  rest, so a T-pose bind kept its offset from the Citizen A-pose in every frame. Fix: each bone is first swung onto the
  stock rest direction (roll kept); limb directions now match stock within 0-2 degrees. Mixamo works because it
  matches directions, not bind deltas.
- 2026-10-06: merged origin/main (e8f7f1b headless Smart Port, roles, attachment influences, graph helpers; 4ed723f
  stock locomotion on every speed ring and through subgraphs) into the new layout: their files were run through the
  same move/rename/split script as the restructure and 3-way merged; every added line is present. More [Alias]es:
  types other packages bind by name (mocap, weapon-importer, rigger, Source 1 Migrator via SmartPortHeadless).
- 2026-10-07: bodynychu report (Humanoid Rigger + Create Citizen animation model). Pinkies were 120-150 degrees off in fists:
  the rigger disables CopyPinky on fitted rigs, but stock Citizen sources have no pinky motion (the stock model copies the ring).
  FittedCitizenPose gained copyRingToPinky (used only by FittedCitizenAnimations): pinky joints take the matching ring joint's
  delta, re-expressed in the pinky's parent frame. The arm twist and knee/elbow problems were fixed in the rigger (frames,
  helper weights); after both, every bone and helper of the fitted model moves within 1 degree of the stock Human male.
