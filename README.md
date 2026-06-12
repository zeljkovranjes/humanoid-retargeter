# Humanoid Retargeter

An [s&box](https://sbox.game) library that retargets skeletal animations from **any
humanoid rig onto any humanoid rig** — body, hands, feet, fingers, bone rolls, and root
motion — entirely inside the editor, in pure managed C#.

Drop in Mixamo, ActorCore/Character Creator, UE Mannequin, BVH mocap, or glTF animations
(or a rig the library has never seen) and get compiled, animgraph-ready s&box animations.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **s&box** (editor) | A current s&box install with the editor (`sbox-dev`). The library is editor-side; nothing runs at game runtime. |
| **A game project** | Conversions write into your project's `Assets/` folder. Any project works — including the shipped samples. The library refuses to modify engine-owned content (`addons/`, `core/`). |
| **This library installed** | Copy or clone this repository into your project's `Libraries/` folder (e.g. `Libraries/local.humanoid_retargeter/`). Only `Code/`, `Editor/`, `Assets/` and the `.sbproj` are needed. |
| Source animations | `.fbx` (binary or ASCII, FBX 7.x), `.bvh`, `.glb`, or `.gltf` files. FBX 6.x is rejected with a clear message — re-export from your DCC. |

No NuGet packages, no native DLLs, no Python, no external tools.

---

## Features

### Input
- **FBX** — own managed parser (binary + ASCII, v7000–7700): full pivot/PreRotation
  transform evaluation, all rotation orders, multi-take files, zlib-compressed curves.
- **BVH** — mocap files with any channel ordering; unit heuristics for meter/cm exports.
- **glTF / GLB** — node hierarchies, skins, animation samplers (linear/step/cubic-spline).
- **Multi-take unpacking** — a file containing many animations expands into one list entry
  per take, each independently previewable, removable, and convertible.

### Rig understanding (automatic, per file)
- **Built-in profiles**: Mixamo, ActorCore / Character Creator (`CC_Base_*`),
  UE Mannequin (UE4/UE5 naming), Rokoko/Xsens-style BVH.
- **Your saved presets** — confirm a mapping once and that skeleton is recognized
  instantly forever (keyed by skeleton signature).
- **Auto-mapper** — token-based name matching for unlisted rigs (DAZ/Poser, 3ds Max
  Biped, CMU, …) with hierarchy validation.
- **Topology fallback** — maps rigs with meaningless bone names from the skeleton's
  shape alone (verified: recovers every role including all 30 finger phalanges on a
  fully renamed skeleton).
- **No-profile dialog** — when nothing matches confidently: auto-map blindly, use the
  deep-learning solver, or map manually. Batches may mix profiles freely.

### Retargeting
- **Geometric solver** (default): canonical anatomical frames built from rest geometry
  (immune to bone-roll conventions), A/T-pose rest normalization on both rigs, exact
  identity on same-rig round-trips (≤ 0.00025°), spine chain interpolation (3–5 source
  spine bones → target), finger curl/splay transfer, hip-height-scaled root translation.
- **Natural shoulder/neck carriage** (option, on by default): clavicles and neck keep the
  target body's own posture and receive only the source's motion — fixes the slumped
  shoulders / hunched neck look that exact direction-copying produces on
  differently-proportioned rigs. Toe-less sources automatically get the same treatment
  for feet (no more heel-standing).
- **Deep-learning solver** (experimental): a pure-C# implementation of SAME
  (skeleton-agnostic motion embedding) running the pretrained checkpoint — no mapping
  needed at all. Offered in the no-profile dialog; after previewing, it can derive and
  save a regular profile from its own output so the rig switches to the deterministic
  geometric path. *(Weights ship in `Assets/humanoid_retargeter/dl/`; see ATTRIBUTION —
  CC BY-NC 4.0, non-commercial.)*
- **Cleanup passes**: Kovar foot-plant correction (anti foot-skate with plant detection,
  blending, and knee-pop-free stretch), optional arm effector IK, root motion
  keep / strip-in-place / extract-to-root.
- **s&box-exact IK helper bones**: `root_IK`, hand/foot IK targets, and `ikrule` bones are
  baked with the exact relationships Facepunch's own clips use (reverse-engineered to
  ~0.0001 cm) — retargeted clips drive the citizen animgraph like official animations.
  Twist/helper bones are correctly left to the model's own constraints.

### Targets
- **s&box Human** (default) — the 5-finger `citizen_human` rig.
- **s&box Citizen (classic)** — the 4-finger citizen; missing pinky roles are skipped
  cleanly.
- **Any custom humanoid** — pick any model/.vmdl or FBX as the target; its skeleton goes
  through the same detection/mapping machinery (best-effort even on imperfect matches).
  Engine-unit targets are handled with the correct axis/unit conventions automatically.

### Output
- **DMX animation files** (byte-compatible with s&box's own fbx2dmx output) plus either:
  - a **new animation vmdl** (s&box Base Model: your model keeps the mesh, the new vmdl
    holds the sequences), or
  - **augmentation of an existing vmdl** — non-destructive splice with a `.bak` backup,
    collision protection for your hand-authored entries, idempotent re-runs (re-converting
    updates entries in place), and automatic CopyPinky constraint neutralization when real
    pinky animation is present.
- **Batch conversion** — N files × all takes in one click, per-clip failure isolation,
  name-collision auto-suffixing, one combined vmdl.

### Editor experience
- Dockable **Humanoid Retargeter** window (View menu): colored profile/status chips with
  confidence badges, per-row Mapping / Preview / Remove, options panel (root motion,
  looping, foot-plant, carriage, arm IK, hip scales, sample fps), progress + per-clip
  compile status with real compiler errors surfaced.
- **Live preview** before anything is written — the actual skinned s&box model playing the
  retargeted clip, with play/pause/scrub. Confirming offers **"Save as profile"**.
- **Asset browser integration** — right-click animation files → *Retarget Animation*.
- Code API: `Retargeter.Convert` / `ConvertBatch` / `Inspect` (engine-agnostic, bytes in,
  strings out).

---

## Quick start

1. Install the library (see prerequisites), open your project in the editor.
2. *View → Humanoid Retargeter*.
3. **Add Files…** (or right-click animation assets → *Retarget Animation*).
4. Check the profile chips — green is good to go; amber/red rows offer auto-map, DL, or
   manual mapping.
5. Optional: **Preview** any row on the real model.
6. Pick the target and output mode, hit **Convert All**.
7. The compiled sequences appear on the output model, ready for animgraph/`Sequence` use.

## Verification

The repo ships its own evidence (`dev/`): 391 unit/integration tests; an independent
headless-Blender harness that re-measures the full corpus (Mixamo, ActorCore, UE, CMU/DAZ
BVH — 10/10 passing: anatomical direction error, end-effector paths, foot contacts,
jitter) with side-by-side renders; and unattended editor-process gates that compile real
output through `sbox-dev` and assert the sequences, preview pose, and preset round-trip.

## Limitations

- Humanoid bipeds only (no quadrupeds/tails); facial/morph animation is not transferred.
- glTF with external `.bin` URIs isn't supported — use `.glb` (embedded) instead.
- FBX 6.x (2010-era) files are rejected; re-export as FBX 7.x.
- The DL solver ignores fingers (checkpoint limitation) and is weakest on hands — the
  geometric path remains the quality reference wherever a mapping exists.

## Attribution

The deep-learning mode implements **SAME** (Lee et al., *SAME: Skeleton-Agnostic Motion
Embedding for Character Animation*, SIGGRAPH Asia 2023), weights derived from the authors'
checkpoint — [github.com/sunny-Codes/SAME](https://github.com/sunny-Codes/SAME),
**CC BY-NC 4.0** (non-commercial). See `Assets/humanoid_retargeter/dl/ATTRIBUTION.md`.
