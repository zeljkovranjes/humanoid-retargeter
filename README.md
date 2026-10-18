# Humanoid Retargeter

Humanoid Retargeter converts skeletal animations from common humanoid rigs (Mixamo, ActorCore, UE
Mannequin, BVH mocap, glTF/VRM and more) onto the s&box Human, the classic Citizen or any custom
humanoid model, entirely inside the editor. Open **View → Humanoid Retargeter**, add animation files,
check the detected bone mapping in the live preview, and convert them to compiled, animgraph-ready VMDL
sequences. It is pure C# with no Blender, Python or native DLLs. The optional deep-learning fallback
ships SAME weights licensed CC BY-NC 4.0 (non-commercial).

- Input: FBX (7.x), BVH, glTF, GLB and VRM, plus MMD VMD/VPD motions and supported ANM, AN5 and CBA files. Multi-animation FBX files unpack into individual clips; batches can mix sources.
- Built-in profiles for Mixamo, ActorCore, UE Mannequin, 3ds Max Biped, Rigify and other common rigs; unknown rigs are auto-mapped, with manual correction and saved presets.
- Targets: s&box Human (default), classic Citizen, or a custom VMDL, FBX, GLB, glTF, PMX or PMD model.
- Output: a new animation VMDL, or clips spliced into an existing VMDL (with backup and collision guards). The s&box IK helper bones are baked the way the stock Citizen clips drive them, so the default animgraph works.

## Requirements

None. It runs inside the s&box editor; no external programs.

## Install

Search for **Humanoid Retargeter** in the s&box library manager, or add
`notpointless.chomnr_humanoid_retargeter`. Keep only one copy of the library installed.

## Quick start

### In the editor

1. Open **View → Humanoid Retargeter**.
2. Click **Add Files…**, or right-click animation assets and choose **Retarget Animation**.
3. Choose an s&box character or add a custom humanoid model.
4. Review the detected bone mapping and correct uncertain assignments.
5. Preview the animation and adjust the retargeting options.
   To shorten a clip or cut a section out, click its **Trim** (scissors) button: select frames on the timeline, press Delete to cut them or K to keep only them, Ctrl+Z / Ctrl+Y to undo and redo, Ctrl+R to restore the whole clip, then **Apply trim**.
6. Choose an output folder and filename, or select an existing VMDL.
7. Click **Convert All** and use the compiled sequences in your model or animgraph.

For MMD motion on regular humanoids, add a VMD/VPD and choose s&box Human, Citizen or your custom target; no PMX/PMD is required. A built-in standard MMD source skeleton handles common body, finger and IK controls. Optionally use the motion row's skeleton button to select a PMX/PMD for exact proportions and custom bones. PMX/PMD custom characters need their texture folders intact and also export an `.mmdanim` resource beside the VMDL. Add **MMD Playback** to a GameObject and select that resource for SDEF/QDEF, UV/material morphs and baked soft-body playback. See [MMD support and limits](docs/MMD.md).

### In code

The retargeting core does no file IO: pass bytes in, get DMX text and a VMDL out. From editor code:

```csharp
using HumanoidRetargeter.Core;
using HumanoidRetargeter.EditorTools;

var target = EditorPipeline.LoadSboxDefaultTarget();
var result = Retargeter.Convert( new RetargetRequest
{
	SourceData = File.ReadAllBytes( path ),
	SourceFileName = Path.GetFileName( path ),
}, target );

foreach ( var clip in result.Clips )
	Log.Info( $"{clip.ClipName}: {( clip.Success ? clip.DmxFileName : clip.Error )}" );
```

## Options

| Option | Default | What it does |
|---|---|---|
| Target | s&box Human | Character the clips are retargeted onto: s&box Human, classic Citizen or a custom model. |
| Folder | `animations/retargeted` | Assets-relative folder for the DMX files and the new VMDL. |
| Model name | `retargeted_animations` | File name of the new animation VMDL (ignored when writing into an existing VMDL). |
| Root motion | Keep as authored | Keep, strip (in place) or extract root motion. |
| Looping | From source | Keep the source's looping or force it on or off. |
| Foot-plant cleanup | On | Locks planted feet to the ground so they don't slide. |
| Arm effector IK | On | Pulls wrists onto limb-length-normalized source hand positions. |
| Natural shoulders, neck, head, feet | On | Keeps the target body's own carriage and transfers only the motion. |
| Hip scale | auto | Pelvis translation scale (horizontal / vertical); auto uses the hip-height ratio. |
| Sample fps | 30 | Rate source clips are resampled to on import. |
| Footstep events | Off | Adds `AE_FOOTSTEP` events from detected foot plants. |
| Mirrored variants (_M) | Off | Also writes a left/right-mirrored twin of every clip. |
| Additive variants, frame | Off, 0 | Also writes a `<clip>_delta` additive sequence against the given frame. |
| Trim (per animation) | Whole clip | Frames kept from the solved clip (`RetargetRequest.KeptFrames` in code). Travel continues across cuts. |

## How it works

Each source file is imported into a skeleton plus clip, its rig is matched against the built-in
profiles (or auto-mapped from bone names and topology) to assign humanoid roles, and the clip is
solved onto the target rig role by role, with fingers, foot plants, root motion and IK helper bones
handled in separate passes. The result is written as DMX animation files and a VMDL that s&box
compiles. Rigs nothing else can map can use the experimental SAME deep-learning solver (no finger
transfer). All of this lives in `Code/HumanoidRetargeter/Core` and builds as plain .NET; the editor
window, preview and asset writing live in `Editor/`.

## Multiplayer

Standard output is ordinary compiled animation. MMD Playback works locally in single-player, on a host and on clients; it does not synchronize its playback clock. A game's networking code decides which animation and time each client displays.

## Limitations

- Custom models need a rig and skin weights. Automatic mapping is not perfect; check the preview before exporting.
- MMD vertex/group morphs and bone morphs are supported on compatible custom targets. Other facial formats are not transferred.
- Advanced MMD deformation uses the MMD Playback component; standard VMDL output and the retargeter's skeletal preview use linear skinning. Soft-body simulation supports cloth/rope links, pins, anchors and rigid collisions, with limits described in [MMD details](docs/MMD.md). Physics can differ from MMD.
- A trim is kept per animation for the session only. After changing Sample fps, open Trim again: frame numbers change with the rate. Cuts keep the travel continuous but not the facing direction.
- Use FBX 7.x, and keep external model textures and glTF buffers alongside their model.
- RenderWare ANM/AN5 animations need a companion DFF skeleton. CBA animations need a joint-table JSON; compressed full-body CBA tracks are not supported.
- The deep-learning fallback does not transfer fingers, and its weights are non-commercial (CC BY-NC 4.0).

## Development

```
sbox-check
dotnet test tests\HumanoidRetargeter.Tests
dotnet test tests\CitizenAnimationSetup
dotnet test tests\PosedLocomotion
dotnet test tests\SmartPort\Parser
```

`dev\HumanoidRetargeter.Core.csproj` builds Core as plain .NET; `dev\HumanoidRetargeter.Dev.csproj`
is the net8.0 build the tests (and other libraries' tests) reference. Some tests read local-only data
(`dev\corpus`, `dev\HumanoidRetargeter.Tests\fixtures`) and pass silently without it. Editor rigs are
in `dev\editor-rig` (`run_ui_smoke.ps1`, `EditorUiCompileCheck.csproj`); engine play-mode tests for
Smart Port are in `tests\SmartPort\Engine` (see its README).

## License

No license file yet; the package is free for non-commercial use. The optional SAME weights are
**CC BY-NC 4.0 (non-commercial)**, see the [attribution](Assets/data/humanoid_retargeter/dl/ATTRIBUTION.md).
Vendored editor code (ValveResourceFormat, Datamodel.NET, ZstdSharp, ValveKeyValue) keeps its MIT/BSD
licenses in `Editor/Embedded/`.
The managed Bullet port retains its zlib-style notice. Adapted SDEF and soft-body code retains its MIT notices; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
