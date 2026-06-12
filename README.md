# Humanoid Retargeter

An [s&box](https://sbox.game) **library** that retargets skeletal animations from any
humanoid rig onto the s&box armature (or any other humanoid rig) — body, hands, feet,
fingers, bone rolls, and root motion — entirely in managed C#, inside the editor.

Drop in a Mixamo, ActorCore/Character Creator, UE Mannequin, or BVH mocap file (or a rig
the library has never seen), and get compiled, animgraph-ready s&box animations.

## How it works

```
source (.fbx / .bvh)
  → managed importers (binary+ASCII FBX incl. PreRotation/pivots; BVH)
  → automatic profile detection
      presets: Mixamo · ActorCore/CC · UE Mannequin · Rokoko/Xsens BVH
      → your saved presets (auto-learned, keyed by skeleton signature)
      → token auto-mapper (DAZ/Poser, Biped, CMU, … naming)
      → pure-topology fallback (works on rigs with meaningless bone names)
  → geometric retarget solver
      canonical anatomical frames from rest GEOMETRY (never local axes —
      handles the citizen rig's chest-forward bone rolls),
      A/T-pose rest normalization on both rigs, absolute canonical-orientation
      matching, spine chain interpolation (3–5 source spine bones → 3),
      finger curl/splay transfer, hip-height-scaled pelvis translation
  → cleanup: Kovar foot-plant correction (anti foot-skate), optional limb IK,
      root-motion extract / in-place
  → s&box IK helper bones baked exactly like shipped clips drive them
      (root_IK, hand/foot IK targets, ikrule bones — relationships reverse-
      engineered from Facepunch's own animations to ~1e-4 cm accuracy)
  → DMX animation files + vmdl (new anim-only model via Base Model, or
      non-destructive augmentation of your existing vmdl) → compiled
```

## Install

Copy/clone this library into your project's `Libraries/` folder (or install via the
Library Manager once published). No native DLLs, no external tools.

## Use

**Window:** *View → Humanoid Retargeter*
1. **Add Files** (or right-click FBX/BVH assets in the Asset Browser → *Retarget to s&box rig…*).
2. Each file is profile-detected independently — batches can mix Mixamo + ActorCore + BVH
   freely. Status chips: green = recognized, amber = auto-mapped (review recommended),
   red = failed.
3. If no profile is found you'll be offered: **Auto-map blindly** (recommended),
   **Deep learning** (experimental — ships in a later release), or **Manual mapping**.
4. **Preview** plays the retargeted clip on the actual citizen model before anything is
   written. Confirming a manual/auto mapping offers **"Save as profile"** — that rig is
   then recognized instantly forever after.
5. Pick the **target**: s&box rig (default) or any custom humanoid model/vmdl/FBX
   (its skeleton is detected and mapped the same way sources are).
6. Pick the **output**: a new animation vmdl (uses s&box's Base Model feature) or
   **augment an existing vmdl** (splices `AnimFile` entries non-destructively; re-running
   with the same names updates them in place).
7. **Convert All** — per-clip results, compile status, and errors are shown inline.

**Code:** everything the window does is on the engine-agnostic facade:

```csharp
var result = Retargeter.ConvertBatch(requests, RetargetTargetSpec.SboxDefault(rigJson));
```

Options per request: root motion (off / extract / in-place), foot-plant cleanup, arm IK,
hip translation scales, looping, clip names.

## Quality

Every stage is gated by tests (CI suite: `dotnet test dev/HumanoidRetargeter.Tests`):

- FBX/BVH importers validated against headless-Blender ground truth at float precision
  (max 0.0004° across full clips).
- Retargeting a shipped citizen clip onto the citizen rig reproduces it to **0.00025°**
  (identity proof of the solver math).
- An **independent** Blender harness re-measures the full corpus (Mixamo, ActorCore, UE,
  CMU/DAZ BVH) from raw data: anatomical direction error, end-effector paths, foot-contact
  agreement, jitter — results in `dev/verification/RESULTS.md`, with side-by-side renders.
- An unattended editor-process gate compiles real output through `sbox-dev` and asserts
  the sequences appear on the model.

## Limitations

- Sources must be humanoid bipeds (no quadrupeds/tails-as-spines); facial/morph animation
  is not transferred.
- s&box twist/helper bones are intentionally not exported — they're constraint-driven by
  the model (exporting channels for them is ignored by Source 2 anyway).
- GLB input isn't supported yet (s&box itself doesn't consume GLB; FBX/BVH cover the
  major sources).
- CMU-converted BVH uses a non-physical unit scale; hip-height normalization absorbs it,
  but absolute world travel from such files reflects the source's odd scale.

## Deep-learning mode (roadmap)

A SAME-style skeleton-agnostic solver (ONNX Runtime, pure C# inference) is planned as the
fallback for rigs the auto-mapper can't handle, and as a mapping-discovery tool (DL result
→ preview → save derived profile → deterministic geometric path thereafter).
SAME (Lee et al., SIGGRAPH Asia 2023) is licensed CC BY-NC 4.0; any shipped weights or
adaptations will carry attribution, and this library is non-commercial.

## Repository layout

- `Code/HumanoidRetargeter/` — engine-agnostic core (also compiles under net8.0:
  `dev/HumanoidRetargeter.Dev.csproj`)
- `Editor/HumanoidRetargeter/` — window, dialogs, preview, asset actions, compile pipeline
- `Assets/humanoid_retargeter/` — target rig definition + built-in profiles (+ your saved
  `profiles/user/` presets)
- `dev/` — test suite, fixtures, corpus, verification harness, editor-process gates
- `docs/superpowers/` — design spec and implementation plan
