using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Mapping;

/// <summary>
/// Builds test skeletons from the committed ground-truth fixtures. Used instead of the FBX
/// importer (built in parallel as Task 2.2): mapping only needs names, hierarchy and rest
/// world positions, all of which the Blender-extracted JSONs carry exactly.
/// </summary>
internal static class MappingFixtures
{
    public static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>
    /// Mixamo skeleton (65 bones, <c>mixamorig1:*</c> names) from the Zombie Crawl animation
    /// ground truth. Built from <c>bones[].rest_local_pos/rest_local_rot_xyzw</c>; values are
    /// already source centimeters (hips rest world y ≈ 100), so no scaling is applied and the
    /// composed rest world positions match the stored <c>rest_world_pos</c>.
    /// </summary>
    public static SkeletonModel LoadZombieCrawl()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath("zombie_crawl.json")));
        var definitions = new List<BoneDefinition>();
        foreach (var bone in doc.RootElement.GetProperty("bones").EnumerateArray())
        {
            var name = bone.GetProperty("name").GetString()!;
            var parentProp = bone.GetProperty("parent");
            var parent = parentProp.ValueKind == JsonValueKind.Null ? null : parentProp.GetString();
            var pos = ReadVector3(bone.GetProperty("rest_local_pos"));
            var rotE = bone.GetProperty("rest_local_rot_xyzw");
            var rot = new Quaternion(
                (float)rotE[0].GetDouble(), (float)rotE[1].GetDouble(),
                (float)rotE[2].GetDouble(), (float)rotE[3].GetDouble());
            definitions.Add(new BoneDefinition(name, parent, new XForm(pos, MathQ.Normalize(rot))));
        }
        return SkeletonModel.Create(definitions);
    }

    private static Vector3 ReadVector3(JsonElement e)
        => new((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());

    /// <summary>ActorCore/CC skeleton (101 bones, <c>CC_Base_*</c> names) via RigJson.</summary>
    public static SkeletonModel LoadActorCore()
        => RigJson.Load(File.ReadAllText(FixturePath("rig_actorcore.json"))).Skeleton;

    /// <summary>
    /// Builds a skeleton from (name, parent, rest world position) triples using identity
    /// rest rotations, so rest world positions equal the given positions exactly.
    /// </summary>
    public static SkeletonModel FromWorldPositions(
        IReadOnlyList<(string Name, string? Parent, Vector3 World)> bones)
    {
        var worldByName = bones.ToDictionary(b => b.Name, b => b.World);
        var definitions = bones
            .Select(b => new BoneDefinition(
                b.Name, b.Parent,
                new XForm(
                    b.Parent is null ? b.World : b.World - worldByName[b.Parent],
                    Quaternion.Identity)))
            .ToList();
        return SkeletonModel.Create(definitions);
    }

    /// <summary>
    /// Rebuilds a skeleton with every bone renamed by <paramref name="rename"/> (index →
    /// new name), preserving hierarchy and rest locals — and therefore rest world positions
    /// and bone indices (construction order is unchanged).
    /// </summary>
    public static SkeletonModel RenameAll(SkeletonModel skeleton, Func<int, string> rename)
    {
        var definitions = new List<BoneDefinition>(skeleton.Count);
        for (var i = 0; i < skeleton.Count; i++)
        {
            var bone = skeleton[i];
            var parent = bone.ParentIndex < 0 ? null : rename(bone.ParentIndex);
            definitions.Add(new BoneDefinition(rename(i), parent, bone.RestLocal));
        }
        return SkeletonModel.Create(definitions);
    }

    /// <summary>
    /// Synthetic UE5 mannequin skeleton built from the real UE bone name list with a
    /// plausible hierarchy and Z-up rest world positions (left = +X), including twist
    /// bones that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildUeMannequin()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("root", null, new Vector3(0, 0, 0)),
            ("pelvis", "root", new Vector3(0, 0, 96)),
            ("spine_01", "pelvis", new Vector3(0, 0, 105)),
            ("spine_02", "spine_01", new Vector3(0, 1, 113)),
            ("spine_03", "spine_02", new Vector3(0, 2, 122)),
            ("spine_04", "spine_03", new Vector3(0, 2, 132)),
            ("spine_05", "spine_04", new Vector3(0, 1, 142)),
            ("neck_01", "spine_05", new Vector3(0, 0, 152)),
            ("neck_02", "neck_01", new Vector3(0, 0, 158)),
            ("head", "neck_02", new Vector3(0, 0, 164)),
        };

        foreach (var (s, x) in new[] { ("l", 1f), ("r", -1f) })
        {
            bones.Add(($"clavicle_{s}", "spine_05", new Vector3(4 * x, 1, 148)));
            bones.Add(($"upperarm_{s}", $"clavicle_{s}", new Vector3(17 * x, 2, 146)));
            bones.Add(($"upperarm_twist_01_{s}", $"upperarm_{s}", new Vector3(22 * x, 2, 140)));
            bones.Add(($"upperarm_twist_02_{s}", $"upperarm_{s}", new Vector3(28 * x, 2, 133)));
            bones.Add(($"lowerarm_{s}", $"upperarm_{s}", new Vector3(34 * x, 3, 126)));
            bones.Add(($"lowerarm_twist_01_{s}", $"lowerarm_{s}", new Vector3(40 * x, 3, 119)));
            bones.Add(($"lowerarm_twist_02_{s}", $"lowerarm_{s}", new Vector3(46 * x, 3, 113)));
            bones.Add(($"hand_{s}", $"lowerarm_{s}", new Vector3(52 * x, 4, 106)));

            var fingers = new (string Name, float Y)[]
            {
                ("thumb", 0f), ("index", 2f), ("middle", 3.2f), ("ring", 4.4f), ("pinky", 5.6f),
            };
            foreach (var (finger, y) in fingers)
            {
                var hasMeta = finger != "thumb";
                var baseParent = $"hand_{s}";
                if (hasMeta)
                {
                    bones.Add(($"{finger}_metacarpal_{s}", $"hand_{s}", new Vector3((54 + y) * x, 4 + y, 104)));
                    baseParent = $"{finger}_metacarpal_{s}";
                }
                bones.Add(($"{finger}_01_{s}", baseParent, new Vector3((57 + y) * x, 4 + y, 102)));
                bones.Add(($"{finger}_02_{s}", $"{finger}_01_{s}", new Vector3((60 + y) * x, 4 + y, 100)));
                bones.Add(($"{finger}_03_{s}", $"{finger}_02_{s}", new Vector3((62 + y) * x, 4 + y, 99)));
            }

            bones.Add(($"thigh_{s}", "pelvis", new Vector3(9 * x, 0, 92)));
            bones.Add(($"thigh_twist_01_{s}", $"thigh_{s}", new Vector3(9 * x, 0, 80)));
            bones.Add(($"thigh_twist_02_{s}", $"thigh_{s}", new Vector3(9 * x, 0, 68)));
            bones.Add(($"calf_{s}", $"thigh_{s}", new Vector3(9 * x, 1, 50)));
            bones.Add(($"calf_twist_01_{s}", $"calf_{s}", new Vector3(9 * x, 1, 38)));
            bones.Add(($"calf_twist_02_{s}", $"calf_{s}", new Vector3(9 * x, 1, 26)));
            bones.Add(($"foot_{s}", $"calf_{s}", new Vector3(9 * x, 2, 8)));
            bones.Add(($"ball_{s}", $"foot_{s}", new Vector3(9 * x, -10, 2)));
        }

        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic NVIDIA SOMA uniform-proportion skeleton (joint NAMES copied from the real
    /// dev/corpus/todo/Neutral_throw_ball_001__A057.bvh hierarchy; positions are plausible
    /// Y-up cm, left = +X): mixamo-identical upper body and fingers, generic
    /// Spine1/Spine2/Chest/Neck1 core, and legs Leg→Shin→Foot→ToeBase where "Leg" is the
    /// THIGH (mixamo's "Leg" is the calf).
    /// </summary>
    public static SkeletonModel BuildSomaBvh()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Root", null, new Vector3(0, 0, 0)),
            ("Hips", "Root", new Vector3(0, 98, 0)),
            ("Spine1", "Hips", new Vector3(0, 108, 0)),
            ("Spine2", "Spine1", new Vector3(0, 120, 0)),
            ("Chest", "Spine2", new Vector3(0, 132, 0)),
            ("Neck1", "Chest", new Vector3(0, 148, 0)),
            ("Neck2", "Neck1", new Vector3(0, 153, 0)),
            ("Head", "Neck2", new Vector3(0, 158, 0)),
            ("Jaw", "Head", new Vector3(0, 160, 4)),
            ("LeftEye", "Head", new Vector3(3, 165, 8)),
            ("RightEye", "Head", new Vector3(-3, 165, 8)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Shoulder", "Chest", new Vector3(5 * x, 146, 0)));
            bones.Add(($"{side}Arm", $"{side}Shoulder", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{side}ForeArm", $"{side}Arm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{side}Hand", $"{side}ForeArm", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                var parent = $"{side}Hand";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"{side}Hand{finger}{segment}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"{side}Leg", "Hips", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{side}Shin", $"{side}Leg", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{side}Foot", $"{side}Shin", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{side}ToeBase", $"{side}Foot", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic SMPL / SMPL-X skeleton (joint names per vchoutas/smplx joint_names.py and
    /// the Meshcapade SMPL FBX rigs). <paramref name="prefix"/> is the gendered FBX bone
    /// prefix ("m_avg_" / "f_avg_" / ""); <paramref name="abbreviated"/> selects "L_Hip"
    /// FBX-style names over "left_hip" model-joint names; <paramref name="withFingers"/>
    /// adds the SMPL-X left_index1..3-style hand joints.
    /// </summary>
    public static SkeletonModel BuildSmpl(string prefix, bool abbreviated, bool withFingers)
    {
        string C(string word) // center joint: "Pelvis" abbreviated-style, "pelvis" word-style
            => prefix + (abbreviated ? char.ToUpperInvariant(word[0]) + word[1..] : word);
        string S(string side, string joint) => abbreviated
            ? $"{prefix}{char.ToUpperInvariant(side[0])}_{char.ToUpperInvariant(joint[0])}{joint[1..]}"
            : $"{prefix}{side}_{joint}";

        var bones = new List<(string, string?, Vector3)>
        {
            (C("pelvis"), null, new Vector3(0, 95, 0)),
            (C("spine1"), C("pelvis"), new Vector3(0, 105, 0)),
            (C("spine2"), C("spine1"), new Vector3(0, 118, 0)),
            (C("spine3"), C("spine2"), new Vector3(0, 132, 0)),
            (C("neck"), C("spine3"), new Vector3(0, 148, 0)),
            (C("head"), C("neck"), new Vector3(0, 156, 0)),
        };
        foreach (var (side, x) in new[] { ("left", 1f), ("right", -1f) })
        {
            bones.Add((S(side, "collar"), C("spine3"), new Vector3(4 * x, 145, 0)));
            bones.Add((S(side, "shoulder"), S(side, "collar"), new Vector3(18 * x, 144, 0)));
            bones.Add((S(side, "elbow"), S(side, "shoulder"), new Vector3(44 * x, 144, 0)));
            bones.Add((S(side, "wrist"), S(side, "elbow"), new Vector3(66 * x, 144, 0)));
            bones.Add((S(side, "hand"), S(side, "wrist"), new Vector3(74 * x, 144, 0)));
            bones.Add((S(side, "hip"), C("pelvis"), new Vector3(9 * x, 92, 0)));
            bones.Add((S(side, "knee"), S(side, "hip"), new Vector3(9 * x, 50, 0)));
            bones.Add((S(side, "ankle"), S(side, "knee"), new Vector3(9 * x, 9, 0)));
            bones.Add((S(side, "foot"), S(side, "ankle"), new Vector3(9 * x, 2, 12)));

            if (!withFingers)
                continue;
            var fingerY = 0f;
            foreach (var finger in new[] { "thumb", "index", "middle", "ring", "pinky" })
            {
                var parent = S(side, "wrist");
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = S(side, $"{finger}{segment}");
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic 3ds Max Character Studio Biped skeleton (standard "Bip01 &lt;Part&gt;"
    /// names per the Autodesk Biped naming convention; positions are plausible Y-up cm,
    /// left = +X). Includes the COM root "Bip01", "Bip01 Footsteps", numbered finger
    /// chains (Finger0 = thumb, segments Finger01/Finger02) and toe segments
    /// Toe0/Toe01/Toe02 — only Toe0 may be mapped.
    /// </summary>
    public static SkeletonModel BuildBiped()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Bip01", null, new Vector3(0, 98, 0)),
            ("Bip01 Footsteps", "Bip01", new Vector3(0, 0, 0)),
            ("Bip01 Pelvis", "Bip01", new Vector3(0, 98, 0)),
            ("Bip01 Spine", "Bip01 Pelvis", new Vector3(0, 108, 0)),
            ("Bip01 Spine1", "Bip01 Spine", new Vector3(0, 118, 0)),
            ("Bip01 Spine2", "Bip01 Spine1", new Vector3(0, 128, 0)),
            ("Bip01 Spine3", "Bip01 Spine2", new Vector3(0, 138, 0)),
            ("Bip01 Neck", "Bip01 Spine3", new Vector3(0, 148, 0)),
            ("Bip01 Head", "Bip01 Neck", new Vector3(0, 156, 0)),
        };
        foreach (var (s, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"Bip01 {s} Clavicle", "Bip01 Neck", new Vector3(4 * x, 145, 0)));
            bones.Add(($"Bip01 {s} UpperArm", $"Bip01 {s} Clavicle", new Vector3(18 * x, 144, 0)));
            bones.Add(($"Bip01 {s} Forearm", $"Bip01 {s} UpperArm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"Bip01 {s} Hand", $"Bip01 {s} Forearm", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            for (var n = 0; n <= 4; n++)
            {
                bones.Add(($"Bip01 {s} Finger{n}", $"Bip01 {s} Hand", new Vector3(70 * x, 144 - fingerY, fingerY)));
                bones.Add(($"Bip01 {s} Finger{n}1", $"Bip01 {s} Finger{n}", new Vector3(72 * x, 144 - fingerY, fingerY)));
                bones.Add(($"Bip01 {s} Finger{n}2", $"Bip01 {s} Finger{n}1", new Vector3(74 * x, 144 - fingerY, fingerY)));
                fingerY += 1.5f;
            }
            bones.Add(($"Bip01 {s} Thigh", "Bip01 Pelvis", new Vector3(9 * x, 92, 0)));
            bones.Add(($"Bip01 {s} Calf", $"Bip01 {s} Thigh", new Vector3(9 * x, 50, 0)));
            bones.Add(($"Bip01 {s} Foot", $"Bip01 {s} Calf", new Vector3(9 * x, 9, 0)));
            bones.Add(($"Bip01 {s} Toe0", $"Bip01 {s} Foot", new Vector3(9 * x, 2, 12)));
            bones.Add(($"Bip01 {s} Toe01", $"Bip01 {s} Toe0", new Vector3(9 * x, 2, 16)));
            bones.Add(($"Bip01 {s} Toe02", $"Bip01 {s} Toe01", new Vector3(9 * x, 2, 19)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic DAZ/Poser classic skeleton (joint NAMES per the Poser/DAZ-Gen4 convention
    /// as in the real dev/corpus/unknown_rigs/makehuman_cmu_03_03_dazNames.bvh, extended
    /// with the DAZ Genesis abdomen2, toes and full 3-segment fingers): hip, abdomen[2],
    /// chest, neck, head, lCollar→lShldr→lForeArm→lHand, lThigh→lShin→lFoot→lToe, plus the
    /// l/rButtock thigh helpers and eyes that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildDazPoser()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("hip", null, new Vector3(0, 98, 0)),
            ("abdomen", "hip", new Vector3(0, 108, 0)),
            ("abdomen2", "abdomen", new Vector3(0, 118, 0)),
            ("chest", "abdomen2", new Vector3(0, 130, 0)),
            ("neck", "chest", new Vector3(0, 148, 0)),
            ("head", "neck", new Vector3(0, 156, 0)),
            ("leftEye", "head", new Vector3(3, 165, 8)),
            ("rightEye", "head", new Vector3(-3, 165, 8)),
        };
        foreach (var (p, x) in new[] { ("l", 1f), ("r", -1f) })
        {
            bones.Add(($"{p}Collar", "chest", new Vector3(5 * x, 146, 0)));
            bones.Add(($"{p}Shldr", $"{p}Collar", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{p}ForeArm", $"{p}Shldr", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{p}Hand", $"{p}ForeArm", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var finger in new[] { "Thumb", "Index", "Mid", "Ring", "Pinky" })
            {
                var parent = $"{p}Hand";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"{p}{finger}{segment}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"{p}Buttock", "hip", new Vector3(9 * x, 95, -4)));
            bones.Add(($"{p}Thigh", $"{p}Buttock", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{p}Shin", $"{p}Thigh", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{p}Foot", $"{p}Shin", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{p}Toe", $"{p}Foot", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic Blender Rigify human skeleton (bone NAMES per the metarig definition in
    /// rigify/metarigs/human.py): spine..spine.006 chain where "spine" is the pelvis,
    /// spine.004/005 the neck bones and spine.006 the head, plus pelvis.L/R, palm and
    /// heel.02 helpers that must never be mapped. <paramref name="defVariant"/> renames
    /// everything to the generated DEF- deform skeleton, including the segmented limb
    /// twins (DEF-upper_arm.L.001) that also must never be mapped.
    /// </summary>
    public static SkeletonModel BuildRigify(bool defVariant)
    {
        var d = defVariant ? "DEF-" : string.Empty;
        var bones = new List<(string, string?, Vector3)>
        {
            ($"{d}spine", null, new Vector3(0, 100, 0)),
            ($"{d}spine.001", $"{d}spine", new Vector3(0, 115, 0)),
            ($"{d}spine.002", $"{d}spine.001", new Vector3(0, 129, 0)),
            ($"{d}spine.003", $"{d}spine.002", new Vector3(0, 146, 0)),
            ($"{d}spine.004", $"{d}spine.003", new Vector3(0, 165, 0)),
            ($"{d}spine.005", $"{d}spine.004", new Vector3(0, 171, 0)),
            ($"{d}spine.006", $"{d}spine.005", new Vector3(0, 178, 0)),
        };
        if (!defVariant)
        {
            bones.Add(("pelvis.L", "spine", new Vector3(5, 105, -3)));
            bones.Add(("pelvis.R", "spine", new Vector3(-5, 105, -3)));
        }
        foreach (var (s, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"{d}shoulder.{s}", $"{d}spine.003", new Vector3(5 * x, 160, 0)));
            bones.Add(($"{d}upper_arm.{s}", $"{d}shoulder.{s}", new Vector3(19 * x, 158, 0)));
            bones.Add(($"{d}forearm.{s}", $"{d}upper_arm.{s}", new Vector3(45 * x, 158, 0)));
            bones.Add(($"{d}hand.{s}", $"{d}forearm.{s}", new Vector3(70 * x, 158, 0)));
            if (defVariant)
            {
                // Segmented deform twins of the limb bones (bendy-bone halves).
                bones.Add(($"DEF-upper_arm.{s}.001", $"DEF-upper_arm.{s}", new Vector3(32 * x, 158, 0)));
                bones.Add(($"DEF-forearm.{s}.001", $"DEF-forearm.{s}", new Vector3(58 * x, 158, 0)));
            }
            var fingerY = 0f;
            foreach (var finger in new[] { "thumb", "f_index", "f_middle", "f_ring", "f_pinky" })
            {
                if (!defVariant && finger != "thumb")
                {
                    var palmIndex = Array.IndexOf(new[] { "f_index", "f_middle", "f_ring", "f_pinky" }, finger) + 1;
                    bones.Add(($"palm.0{palmIndex}.{s}", $"hand.{s}", new Vector3(72 * x, 158 - fingerY, fingerY)));
                }
                var parent = $"{d}hand.{s}";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"{d}{finger}.0{segment}.{s}";
                    bones.Add((bone, parent, new Vector3((72 + 2 * segment) * x, 158 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"{d}thigh.{s}", $"{d}spine", new Vector3(9 * x, 95, 0)));
            bones.Add(($"{d}shin.{s}", $"{d}thigh.{s}", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{d}foot.{s}", $"{d}shin.{s}", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{d}toe.{s}", $"{d}foot.{s}", new Vector3(9 * x, 2, 12)));
            if (defVariant)
            {
                bones.Add(($"DEF-thigh.{s}.001", $"DEF-thigh.{s}", new Vector3(9 * x, 72, 0)));
                bones.Add(($"DEF-shin.{s}.001", $"DEF-shin.{s}", new Vector3(9 * x, 30, 0)));
            }
            else
            {
                bones.Add(($"heel.02.{s}", $"foot.{s}", new Vector3(9 * x, 1, -6)));
            }
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic VRoid/VRM skeleton (J_Bip_* bone NAMES per the VRoid Studio export
    /// convention / VRM humanoid spec, incl. "Little" = pinky), plus Root and secondary
    /// J_Sec/J_Adj bones that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildVrm()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Root", null, new Vector3(0, 0, 0)),
            ("J_Bip_C_Hips", "Root", new Vector3(0, 98, 0)),
            ("J_Bip_C_Spine", "J_Bip_C_Hips", new Vector3(0, 108, 0)),
            ("J_Bip_C_Chest", "J_Bip_C_Spine", new Vector3(0, 120, 0)),
            ("J_Bip_C_UpperChest", "J_Bip_C_Chest", new Vector3(0, 132, 0)),
            ("J_Bip_C_Neck", "J_Bip_C_UpperChest", new Vector3(0, 148, 0)),
            ("J_Bip_C_Head", "J_Bip_C_Neck", new Vector3(0, 156, 0)),
            ("J_Adj_L_FaceEye", "J_Bip_C_Head", new Vector3(3, 165, 8)),
            ("J_Adj_R_FaceEye", "J_Bip_C_Head", new Vector3(-3, 165, 8)),
            ("J_Sec_L_Bust1", "J_Bip_C_UpperChest", new Vector3(6, 135, 8)),
            ("J_Sec_R_Bust1", "J_Bip_C_UpperChest", new Vector3(-6, 135, 8)),
        };
        foreach (var (s, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"J_Bip_{s}_Shoulder", "J_Bip_C_UpperChest", new Vector3(5 * x, 146, 0)));
            bones.Add(($"J_Bip_{s}_UpperArm", $"J_Bip_{s}_Shoulder", new Vector3(18 * x, 144, 0)));
            bones.Add(($"J_Bip_{s}_LowerArm", $"J_Bip_{s}_UpperArm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"J_Bip_{s}_Hand", $"J_Bip_{s}_LowerArm", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                var parent = $"J_Bip_{s}_Hand";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"J_Bip_{s}_{finger}{segment}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"J_Bip_{s}_UpperLeg", "J_Bip_C_Hips", new Vector3(9 * x, 92, 0)));
            bones.Add(($"J_Bip_{s}_LowerLeg", $"J_Bip_{s}_UpperLeg", new Vector3(9 * x, 50, 0)));
            bones.Add(($"J_Bip_{s}_Foot", $"J_Bip_{s}_LowerLeg", new Vector3(9 * x, 9, 0)));
            bones.Add(($"J_Bip_{s}_ToeBase", $"J_Bip_{s}_Foot", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic Auto-Rig Pro export skeleton — bone NAMES copied 1:1 from the real
    /// dev/corpus/todo/Defenses.fbx hierarchy: root.x hips under a ground "root",
    /// spine_01..03.x, neck.x/head.x, arms shoulder→arm_stretch→forearm_stretch→hand,
    /// legs thigh_stretch→leg_stretch→foot→toes_01, c_-prefixed finger chains and the
    /// leftover mixamorig:*4 finger-tip markers that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildAutoRigPro()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("root", null, new Vector3(0, 0, 0)),
            ("root.x", "root", new Vector3(0, 98, 0)),
            ("spine_01.x", "root.x", new Vector3(0, 108, 0)),
            ("spine_02.x", "spine_01.x", new Vector3(0, 120, 0)),
            ("spine_03.x", "spine_02.x", new Vector3(0, 134, 0)),
            ("neck.x", "spine_03.x", new Vector3(0, 149, 0)),
            ("head.x", "neck.x", new Vector3(0, 158, 0)),
        };
        foreach (var (p, side, x) in new[] { ("l", "Left", 1f), ("r", "Right", -1f) })
        {
            bones.Add(($"shoulder.{p}", "spine_03.x", new Vector3(5 * x, 146, 0)));
            bones.Add(($"arm_stretch.{p}", $"shoulder.{p}", new Vector3(18 * x, 144, 0)));
            bones.Add(($"forearm_stretch.{p}", $"arm_stretch.{p}", new Vector3(44 * x, 144, 0)));
            bones.Add(($"hand.{p}", $"forearm_stretch.{p}", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var (finger, tip) in new[]
            {
                ("thumb", "Thumb"), ("index", "Index"), ("middle", "Middle"),
                ("ring", "Ring"), ("pinky", "Pinky"),
            })
            {
                var parent = $"hand.{p}";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"c_{finger}{segment}.{p}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                bones.Add(($"mixamorig:{side}Hand{tip}4", $"hand.{p}", new Vector3(76 * x, 144 - fingerY, fingerY)));
                fingerY += 1.5f;
            }
            bones.Add(($"thigh_stretch.{p}", "root.x", new Vector3(9 * x, 92, 0)));
            bones.Add(($"leg_stretch.{p}", $"thigh_stretch.{p}", new Vector3(9 * x, 50, 0)));
            bones.Add(($"foot.{p}", $"leg_stretch.{p}", new Vector3(9 * x, 9, 0)));
            bones.Add(($"toes_01.{p}", $"foot.{p}", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic Source engine ValveBiped skeleton (standard HL2/GMod humanoid bone names:
    /// "ValveBiped.Bip01_&lt;Part&gt;" with a spine chain that skips Spine3, Neck1/Head1,
    /// numbered finger chains Finger0..4 with phalanx-digit segments) plus the
    /// ValveBiped.forward / Anim_Attachment helpers that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildValveBiped()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("ValveBiped.Bip01_Pelvis", null, new Vector3(0, 98, 0)),
            ("ValveBiped.Bip01_Spine", "ValveBiped.Bip01_Pelvis", new Vector3(0, 106, 0)),
            ("ValveBiped.Bip01_Spine1", "ValveBiped.Bip01_Spine", new Vector3(0, 116, 0)),
            ("ValveBiped.Bip01_Spine2", "ValveBiped.Bip01_Spine1", new Vector3(0, 128, 0)),
            ("ValveBiped.Bip01_Spine4", "ValveBiped.Bip01_Spine2", new Vector3(0, 140, 0)),
            ("ValveBiped.Bip01_Neck1", "ValveBiped.Bip01_Spine4", new Vector3(0, 148, 0)),
            ("ValveBiped.Bip01_Head1", "ValveBiped.Bip01_Neck1", new Vector3(0, 156, 0)),
            ("ValveBiped.forward", "ValveBiped.Bip01_Pelvis", new Vector3(0, 98, 10)),
        };
        foreach (var (s, x) in new[] { ("L", 1f), ("R", -1f) })
        {
            bones.Add(($"ValveBiped.Bip01_{s}_Clavicle", "ValveBiped.Bip01_Spine4", new Vector3(4 * x, 145, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_UpperArm", $"ValveBiped.Bip01_{s}_Clavicle", new Vector3(18 * x, 144, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_Forearm", $"ValveBiped.Bip01_{s}_UpperArm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_Hand", $"ValveBiped.Bip01_{s}_Forearm", new Vector3(66 * x, 144, 0)));
            bones.Add(($"ValveBiped.Anim_Attachment_{(s == "L" ? "LH" : "RH")}",
                $"ValveBiped.Bip01_{s}_Hand", new Vector3(70 * x, 143, 2)));
            var fingerY = 0f;
            for (var n = 0; n <= 4; n++)
            {
                bones.Add(($"ValveBiped.Bip01_{s}_Finger{n}", $"ValveBiped.Bip01_{s}_Hand",
                    new Vector3(70 * x, 144 - fingerY, fingerY)));
                bones.Add(($"ValveBiped.Bip01_{s}_Finger{n}1", $"ValveBiped.Bip01_{s}_Finger{n}",
                    new Vector3(72 * x, 144 - fingerY, fingerY)));
                bones.Add(($"ValveBiped.Bip01_{s}_Finger{n}2", $"ValveBiped.Bip01_{s}_Finger{n}1",
                    new Vector3(74 * x, 144 - fingerY, fingerY)));
                fingerY += 1.5f;
            }
            bones.Add(($"ValveBiped.Bip01_{s}_Thigh", "ValveBiped.Bip01_Pelvis", new Vector3(9 * x, 92, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_Calf", $"ValveBiped.Bip01_{s}_Thigh", new Vector3(9 * x, 50, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_Foot", $"ValveBiped.Bip01_{s}_Calf", new Vector3(9 * x, 9, 0)));
            bones.Add(($"ValveBiped.Bip01_{s}_Toe0", $"ValveBiped.Bip01_{s}_Foot", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic Xsens MVN export skeleton (the 23-segment MVN body model as exported to
    /// FBX/BVH): Pelvis, vertebra spine L5→L3→T12→T8, Neck/Head, arms
    /// Shoulder→UpperArm→ForeArm→Hand, legs UpperLeg→LowerLeg→Foot→Toe. No fingers.
    /// </summary>
    public static SkeletonModel BuildXsensMvn()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Pelvis", null, new Vector3(0, 98, 0)),
            ("L5", "Pelvis", new Vector3(0, 106, 0)),
            ("L3", "L5", new Vector3(0, 116, 0)),
            ("T12", "L3", new Vector3(0, 126, 0)),
            ("T8", "T12", new Vector3(0, 138, 0)),
            ("Neck", "T8", new Vector3(0, 148, 0)),
            ("Head", "Neck", new Vector3(0, 156, 0)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Shoulder", "T8", new Vector3(4 * x, 145, 0)));
            bones.Add(($"{side}UpperArm", $"{side}Shoulder", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{side}ForeArm", $"{side}UpperArm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{side}Hand", $"{side}ForeArm", new Vector3(66 * x, 144, 0)));
            bones.Add(($"{side}UpperLeg", "Pelvis", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{side}LowerLeg", $"{side}UpperLeg", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{side}Foot", $"{side}LowerLeg", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{side}Toe", $"{side}Foot", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic Perception Neuron / Axis Neuron BVH skeleton: mixamo-like limb and finger
    /// names but a four-bone spine (Spine..Spine3), no toe joints, and per-finger
    /// "InHand" metacarpal helpers (RightInHandIndex) that must never be mapped.
    /// </summary>
    public static SkeletonModel BuildPerceptionNeuron()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Hips", null, new Vector3(0, 98, 0)),
            ("Spine", "Hips", new Vector3(0, 106, 0)),
            ("Spine1", "Spine", new Vector3(0, 116, 0)),
            ("Spine2", "Spine1", new Vector3(0, 126, 0)),
            ("Spine3", "Spine2", new Vector3(0, 138, 0)),
            ("Neck", "Spine3", new Vector3(0, 148, 0)),
            ("Head", "Neck", new Vector3(0, 156, 0)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Shoulder", "Spine3", new Vector3(4 * x, 145, 0)));
            bones.Add(($"{side}Arm", $"{side}Shoulder", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{side}ForeArm", $"{side}Arm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{side}Hand", $"{side}ForeArm", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                var parent = $"{side}Hand";
                if (finger != "Thumb")
                {
                    // Metacarpal helper between hand and phalanges (unmapped).
                    var inHand = $"{side}InHand{finger}";
                    bones.Add((inHand, parent, new Vector3(68 * x, 144 - fingerY, fingerY)));
                    parent = inHand;
                }
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"{side}Hand{finger}{segment}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"{side}UpLeg", "Hips", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{side}Leg", $"{side}UpLeg", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{side}Foot", $"{side}Leg", new Vector3(9 * x, 9, 0)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic DAZ Genesis 3/8 skeleton: hip root over the pelvis leg branch and the
    /// abdomenLower..chestUpper spine branch, neckLower/neckUpper, Bend+Twist limb pairs
    /// (only the Bend bones may be mapped), lMetatarsals between foot and toe, and the
    /// classic lThumb1..3-style fingers.
    /// </summary>
    public static SkeletonModel BuildDazGenesis()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("hip", null, new Vector3(0, 100, 0)),
            ("pelvis", "hip", new Vector3(0, 96, 0)),
            ("abdomenLower", "hip", new Vector3(0, 106, 0)),
            ("abdomenUpper", "abdomenLower", new Vector3(0, 116, 0)),
            ("chestLower", "abdomenUpper", new Vector3(0, 126, 0)),
            ("chestUpper", "chestLower", new Vector3(0, 138, 0)),
            ("neckLower", "chestUpper", new Vector3(0, 148, 0)),
            ("neckUpper", "neckLower", new Vector3(0, 152, 0)),
            ("head", "neckUpper", new Vector3(0, 156, 0)),
        };
        foreach (var (p, x) in new[] { ("l", 1f), ("r", -1f) })
        {
            bones.Add(($"{p}Collar", "chestUpper", new Vector3(5 * x, 146, 0)));
            bones.Add(($"{p}ShldrBend", $"{p}Collar", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{p}ShldrTwist", $"{p}ShldrBend", new Vector3(31 * x, 144, 0)));
            bones.Add(($"{p}ForearmBend", $"{p}ShldrTwist", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{p}ForearmTwist", $"{p}ForearmBend", new Vector3(55 * x, 144, 0)));
            bones.Add(($"{p}Hand", $"{p}ForearmTwist", new Vector3(66 * x, 144, 0)));
            var fingerY = 0f;
            foreach (var finger in new[] { "Thumb", "Index", "Mid", "Ring", "Pinky" })
            {
                var parent = $"{p}Hand";
                for (var segment = 1; segment <= 3; segment++)
                {
                    var bone = $"{p}{finger}{segment}";
                    bones.Add((bone, parent, new Vector3((68 + 2 * segment) * x, 144 - fingerY, fingerY)));
                    parent = bone;
                }
                fingerY += 1.5f;
            }
            bones.Add(($"{p}ThighBend", "pelvis", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{p}ThighTwist", $"{p}ThighBend", new Vector3(9 * x, 70, 0)));
            bones.Add(($"{p}Shin", $"{p}ThighTwist", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{p}Foot", $"{p}Shin", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{p}Metatarsals", $"{p}Foot", new Vector3(9 * x, 4, 6)));
            bones.Add(($"{p}Toe", $"{p}Metatarsals", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic classic-BVH skeleton (joint NAMES copied from the real
    /// dev/corpus/todo/Armchair1.bvh hierarchy — the MotionBuilder/Character-Studio BVH
    /// convention): Chest..Chest4 spine, arms Collar→Shoulder→Elbow→Wrist ("Shoulder" IS
    /// the upper arm) and legs Hip→Knee→Ankle→Toe (the sided "Hip" is the thigh).
    /// </summary>
    public static SkeletonModel BuildClassicBvh()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Hips", null, new Vector3(0, 98, 0)),
            ("Chest", "Hips", new Vector3(0, 108, 0)),
            ("Chest2", "Chest", new Vector3(0, 118, 0)),
            ("Chest3", "Chest2", new Vector3(0, 128, 0)),
            ("Chest4", "Chest3", new Vector3(0, 138, 0)),
            ("Neck", "Chest4", new Vector3(0, 148, 0)),
            ("Head", "Neck", new Vector3(0, 156, 0)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Collar", "Chest4", new Vector3(4 * x, 145, 0)));
            bones.Add(($"{side}Shoulder", $"{side}Collar", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{side}Elbow", $"{side}Shoulder", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{side}Wrist", $"{side}Elbow", new Vector3(66 * x, 144, 0)));
            bones.Add(($"{side}Hip", "Hips", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{side}Knee", $"{side}Hip", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{side}Ankle", $"{side}Knee", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{side}Toe", $"{side}Ankle", new Vector3(9 * x, 2, 12)));
        }
        return FromWorldPositions(bones);
    }

    /// <summary>
    /// Synthetic EA Fight Night boxer skeleton, modelled on the extracted
    /// <c>skeleton_boxer.json</c> (Fight Night Champion, 293 bones — reduced here to the
    /// mappable joints plus one of each DECOY family the profile must ignore):
    /// <c>Reference→AITrajectory→Hips</c> root chain, four-bone spine, two-bone neck, and
    /// boxing-glove hands where only the thumb (two segments) and a fused middle finger
    /// carrying its <c>InHand</c> metacarpal are real joints — index/ring/pinky exist only
    /// as <c>*Glove</c> mesh shells. Twist, Muscle/Jiggle, Offset and Helper bones stand in
    /// for the rig's simulation layer.
    /// </summary>
    public static SkeletonModel BuildFightNight()
    {
        var bones = new List<(string, string?, Vector3)>
        {
            ("Reference", null, new Vector3(0, 0, 0)),
            ("AITrajectory", "Reference", new Vector3(0, 0, 0)),
            ("Hips", "AITrajectory", new Vector3(0, 98, 0)),
            ("Spine", "Hips", new Vector3(0, 106, 0)),
            ("Spine1", "Spine", new Vector3(0, 114, 0)),
            ("Spine2", "Spine1", new Vector3(0, 124, 0)),
            ("Spine3", "Spine2", new Vector3(0, 136, 0)),
            ("Neck", "Spine3", new Vector3(0, 146, 0)),
            ("Neck1", "Neck", new Vector3(0, 150, 0)),
            ("Head", "Neck1", new Vector3(0, 156, 0)),
            ("Crotch", "Hips", new Vector3(0, 92, 4)),
            ("Spine2_Jiggle", "Spine2", new Vector3(0, 124, 3)),
            ("Gut_Center", "Spine", new Vector3(0, 108, 6)),
            ("StomachFront", "Spine", new Vector3(0, 110, 7)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Shoulder", "Spine3", new Vector3(4 * x, 145, 0)));
            bones.Add(($"{side}Arm", $"{side}Shoulder", new Vector3(18 * x, 144, 0)));
            bones.Add(($"{side}ForeArm", $"{side}Arm", new Vector3(44 * x, 144, 0)));
            bones.Add(($"{side}Hand", $"{side}ForeArm", new Vector3(66 * x, 144, 0)));

            // Simulation layer: twists, muscles and jiggle/offset helpers (never mapped).
            bones.Add(($"{side}ArmTwist", $"{side}Arm", new Vector3(26 * x, 144, 0)));
            bones.Add(($"{side}ForeArmTwist", $"{side}ForeArm", new Vector3(52 * x, 144, 0)));
            bones.Add(($"{side}GloveTwist", $"{side}ForeArm", new Vector3(60 * x, 144, 0)));
            bones.Add(($"Muscle_{side}_Bicep", $"{side}Arm", new Vector3(30 * x, 146, 2)));
            bones.Add(($"Offset_Muscle_{side}_Delt", $"{side}Arm", new Vector3(22 * x, 148, 0)));
            bones.Add(($"{side}_Elbow_Helper", $"{side}ForeArm", new Vector3(44 * x, 143, -1)));

            // Gloved hand: real joints are the two thumb segments and the fused middle
            // finger under its InHand metacarpal. The *Glove bones are mesh shells.
            bones.Add(($"{side}HandThumb1", $"{side}Hand", new Vector3(69 * x, 143, 2)));
            bones.Add(($"{side}HandThumb2", $"{side}HandThumb1", new Vector3(72 * x, 142, 3)));
            bones.Add(($"{side}HandThumbGlove", $"{side}HandThumb2", new Vector3(74 * x, 142, 3)));
            bones.Add(($"{side}InHandMiddle", $"{side}Hand", new Vector3(70 * x, 144, 0)));
            bones.Add(($"{side}HandMiddle1", $"{side}InHandMiddle", new Vector3(73 * x, 144, 0)));
            bones.Add(($"{side}HandMiddle2", $"{side}HandMiddle1", new Vector3(76 * x, 144, 0)));
            bones.Add(($"{side}HandIndexGlove", $"{side}InHandMiddle", new Vector3(73 * x, 145, 1)));
            bones.Add(($"{side}HandMiddleGlove", $"{side}InHandMiddle", new Vector3(73 * x, 145, 0)));
            bones.Add(($"{side}InHandRingGlove", $"{side}InHandMiddle", new Vector3(73 * x, 145, -1)));
            bones.Add(($"{side}HandPinkyGlove", $"{side}InHandMiddle", new Vector3(73 * x, 145, -2)));

            bones.Add(($"{side}UpLeg", "Hips", new Vector3(9 * x, 92, 0)));
            bones.Add(($"{side}Leg", $"{side}UpLeg", new Vector3(9 * x, 50, 0)));
            bones.Add(($"{side}Foot", $"{side}Leg", new Vector3(9 * x, 9, 0)));
            bones.Add(($"{side}ToeBase", $"{side}Foot", new Vector3(9 * x, 2, 12)));
            bones.Add(($"{side}FootEnd", $"{side}ToeBase", new Vector3(9 * x, 2, 18)));
            bones.Add(($"{side}UpLegTwist", $"{side}UpLeg", new Vector3(9 * x, 70, 0)));
            bones.Add(($"{side}LegTwist", $"{side}Leg", new Vector3(9 * x, 30, 0)));
            bones.Add(($"{side}_Knee_Helper", $"{side}UpLeg", new Vector3(9 * x, 51, 2)));
        }
        return FromWorldPositions(bones);
    }
}
