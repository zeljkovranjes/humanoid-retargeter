# Humanoid Retargeter

A humanoid animation retargeting tool for the [s&box](https://sbox.game) editor.

- FBX, BVH, glTF, GLB and VRM animation import, plus supported ANM, AN5 and CBA files.
- Built-in profiles for Mixamo, ActorCore, UE Mannequin and other common rigs.
- Automatic bone mapping with manual correction and saved presets.
- s&box Human, classic Citizen and custom VMDL, FBX, GLB or glTF targets.
- Body and finger retargeting with foot-plant correction and root-motion options.
- Live preview with playback controls and a source-skeleton overlay.
- Batch conversion to compiled VMDL animations, with optional mirrored clips and footstep events.

Add the library to your s&box project and open **View → Humanoid Retargeter**.
Keep only one copy of the library installed.

1. Click **Add Files…** or right-click animation assets and choose **Retarget Animation**.
2. Choose an s&box character or add a custom humanoid model.
3. Review the detected bone mapping and correct uncertain assignments.
4. Preview the animation and adjust the retargeting options.
5. Choose an output folder and filename, or select an existing VMDL.
6. Click **Convert All** and use the compiled sequences in your model or animgraph.

Custom models need a rig and skin weights. Automatic mapping is not perfect; check the
preview before exporting. Facial and morph animations are not transferred. Use FBX 7.x,
and keep external model textures and glTF buffers alongside their model.

RenderWare ANM/AN5 animations require a companion DFF skeleton. CBA animations require
a joint-table JSON; compressed full-body CBA tracks are not supported.

The experimental deep-learning fallback uses [SAME](https://github.com/sunny-Codes/SAME)
and does not transfer fingers. Its pretrained weights are **CC BY-NC 4.0 (non-commercial)**;
see [attribution](Assets/humanoid_retargeter/dl/ATTRIBUTION.md).

Package ident: `notpointless.chomnr_humanoid_retargeter`
