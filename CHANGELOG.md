# Changelog

## 2026-11-01
Title: 2D Mode
### Added
# Solve
2D view - a new option (Side, Front, Back, 3/4) retargets a 3D clip through an orthographic camera onto a plane of the target: each bone takes the direction of its projected source segment and the character keeps its own proportions, on the s&box Human, Citizen and custom armatures, including ones drawn flat, with the usual DMX and VMDL output
2D foreshortening - a limb pointing at the camera shortens as the projection does (down to 10%); on by default, untick it to keep every bone full length
2D depth order - shoulders and hips sit at the source's own depth, scaled to the target, so the near limbs stay in front of the far ones and swap as the character turns
# Editor
2D preview - a 2D clip on a flat character shows through a locked orthographic camera, square-on to its plane, in Preview and Trim; dragging pans and the wheel zooms instead of orbiting, and the camera angle list is hidden. A 3D character keeps its orbit camera
2D badge - every animation in the list shows a 2D badge while a 2D view is picked, and so does the preview of a 2D clip
2D sprites - a new target, 2D sprite (.png/.sprite), reads a humanoid off a sprite's silhouette, front or profile, lets you drag its joints, and builds a rigged model any 3D animation can be retargeted onto; output is the usual VMDL
Automatic 2D view - picking a flat humanoid (a sprite, or a flat VMDL, FBX or GLB) sets the 2D view to Front or Side as it was drawn, and going back to a 3D character turns it off
Sprite preview - a flat character plays on open the way the editor previews a Sprite: lit from the camera, filling the view, with no ground

## 2026-10-24
Title: Runtime Facing
### Added
# Runtime
Facing alignment - RuntimePoseRetargeter and TryCreateAutoMapped take alignFacingToSource, which turns a live target to face the way the source does; a rig bound facing sideways no longer turns sideways when it is driven by a character in game. Off by default, so existing callers are unchanged
### Fixed
# Mapping
Busy namespaced rigs - a body named with a shared prefix (bip01_pelvis, bip01_l_thigh...) among many more hair, cloth and tail bones now maps by name; such rigs used to fall back to topology, which could take long cloth strings for the legs and leave the real legs standing still
Backward-facing rigs - a character modelled facing the other way, with bone names the mapper can't read (Elsa's SHJnt rig), is no longer driven mirrored and turned round, head to toe: which way its toes point now sets its left and right
# Solve
Crumpled chests - a spine joint placed behind or below the one before it (some models on the citizen skeleton put spine_2 that way) no longer turns the chest round; the spine aims at the next joint above instead
# Runtime
Live twist bones - RuntimePoseRetargeter now turns a target's unmapped forearm, upper arm and leg twist helpers with the joint they follow, as baked clips already did; they used to stay at rest, so a hand turned over (first-person arms gripping a rifle) wrung the forearm skin
# Editor
Waiting on new models on Linux - Smart Port and "Create Citizen animation model" no longer sit on a model that has already compiled until they give up (five minutes); they carry on as soon as it is written
Citizen clips on Linux - "Create Citizen animation model" no longer turns every stock animation into a single frame (261 clips); the reference model it samples them from was being recompiled under it halfway through
Smart Port graphs on Linux - a port no longer fails with "its animation graph did not load correctly"; the graph is compiled before the new model is loaded to check it
Citizen model crash - the editor no longer crashes as a model made with "Create Citizen animation model" loads; the stock animation folders it copies in had no names

## 2026-10-18
Title: MMD Characters and Motion
### Added
# Import
MMD models - select PMX or PMD characters as custom targets, including skin weights and texture folders
MMD motion - import VMD animations and VPD poses with their character model, Japanese bone mapping, interpolation and IK
Standalone MMD motion - add VMD/VPD directly onto Human, Citizen or custom humanoid targets using a built-in source skeleton; PMX/PMD selection is optional
MMD physics - bake rigid bodies, collision masks and spring joints with a managed C# Bullet solver inside the retargeter
MMD faces - preserve vertex/group morphs and bone morphs, including facial weights in preview and exported animation
# Playback
MMD Playback - export a character animation resource with native SDEF/QDEF deformation, UV/material morphs and sphere subtextures
MMD soft bodies - bake cloth/rope links, pins, anchors and rigid collisions into seekable vertex animation
MMD impulse morphs - apply local and world-space impulses through the managed Bullet solver
# Materials
MMD appearance - import diffuse textures, toon ramps, sphere maps and skinned outline shells
### Improved
# Import
Shared mesh buffers - export only the vertices each material uses, reducing MMD model file sizes
### Fixed
# Import
Japanese decoding - read CP932 without the code-page provider blocked by the s&box whitelist
Duplicate helper bones - preserve models with repeated helper names without breaking their indexed hierarchy
Japanese morph names - use stable ASCII flex identifiers accepted by ModelDoc
Sandbox compatibility - remove blocked diagnostic APIs and replace cloning calls with explicit copies
Trimmed morphs - keep expression samples aligned with trimmed preview frames
MMD custom targets - preserve Japanese bone identities through model compilation so Alicia and other PMX/PMD characters accept VMD and FBX clips without false anatomical bind-pose errors
MMD playback orientation - undo the compiled bind's coordinate rotation when exporting native playback
### Known Issues
# MMD
Standard VMDL - advanced MMD deformation needs the exported MMD Playback component; standard VMDL and skeletal preview use linear skinning
Soft-body fidelity - cluster, pressure/volume, aerodynamic and soft-to-soft collision behavior is not reproduced by the adapted cloth/rope solver
Physics - the managed Bullet version and model-space outlines can differ from MikuMikuDance

## 2026-10-13
Title: Hand IK and Wheel Fixes
### Fixed
# Retargeting
Hand and foot IK targets - stay on the hands and feet again; they had started following a foot, so in game the arms were pulled behind the back and the wrists looked flat
# Editor
Trim timeline - use the current mouse-wheel callback to retain wheel zooming without obsolete API warnings

## 2026-10-11
Title: Trim Update
### Added
# Editor
Trim editor - a scissors button on each animation opens the character preview with a timeline: cut sections out, keep only a selection, or trim before or after any frame
Trim editor - Ctrl+Z undoes, Ctrl+Y redoes and Ctrl+R restores the whole animation
Trim editor - cutting the middle of a walk keeps the character moving forward instead of jumping back
# API
RetargetRequest.KeptFrames - trims a conversion from code

## 2026-10-07
### Fixed
- Pinkies on characters made with "Create Citizen animation model" now curl with the ring finger in fists and grips, as on the stock Citizen, instead of sticking out backwards.

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
