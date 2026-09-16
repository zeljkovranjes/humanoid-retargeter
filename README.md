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

For a custom model with a matching Citizen or Human Citizen armature, **Create Citizen animation model** includes all stock animations and sets up the matching animgraph, IK and helper constraints. No animation files are needed. The button stays disabled until the compiled skeleton passes compatibility checks. Choose an unused output name; this action never overwrites an existing VMDL. Human Citizen keeps `CopyPinky` enabled for the stock animations, so pinkies follow ring fingers, including in subsequently added clips.

Complete fitted Citizen armatures also enable the green button. Clicking it retargets the stock animation sources to the custom bind pose, keeping the fitted rig settings and original sequence processing. Converted sources are saved under `<model name>_citizen_sources` in your output folder; exact stock armatures still use the faster direct-copy path.

**Copy editable Citizen animgraph** saves the actual graph to `graphs/<model name>.vanmgrph` inside your output folder and connects it to the model. Turn it off to keep using the shipped graph.

The complete stock animation definitions are retained, including nested blends, additive subtraction, events, timing and prefab dependencies—not just clip names. The setup also carries attachments, IK, pose parameters, bone masks, helper constraints and animation game data. Your custom mesh, materials and collision setup are preserved.

Use a clip's **Replace stock…** button to replace a supported idle, directional walk/run or jump slot. This works with any supported animation source, not just Mixamo. Replacements always use a project-owned graph copy; shipped assets stay untouched. Existing graph edits and other slots are preserved, with `.bak` backups. Review the slot's warning: fast locomotion variants, additive idle layers and airborne/landing states are separate, and blend timing may need adjustment.

Locomotion directions are suggested from clip names or clear straight-line travel relative to the character's facing. Ambiguous and in-place clips without direction names need a manual choice. Footstep events detect settled contacts after foot lifts, including in-place motion. Additive variants let you choose a zero-based reference frame in the sampled output; choose a suitable neutral pose and check the result in your graph.

Custom models need a rig and skin weights. Automatic mapping is not perfect; check the
preview before exporting. Facial and morph animations are not transferred. Use FBX 7.x,
and keep external model textures and glTF buffers alongside their model.

RenderWare ANM/AN5 animations require a companion DFF skeleton. CBA animations require
a joint-table JSON; compressed full-body CBA tracks are not supported.

The experimental deep-learning fallback uses [SAME](https://github.com/sunny-Codes/SAME)
and does not transfer fingers. Its pretrained weights are **CC BY-NC 4.0 (non-commercial)**;
see [attribution](Assets/humanoid_retargeter/dl/ATTRIBUTION.md).

Package ident: `notpointless.chomnr_humanoid_retargeter`
