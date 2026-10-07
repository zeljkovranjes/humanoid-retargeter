using HumanoidRetargeter.Core.Mapping;
using Xunit;

namespace HumanoidRetargeter.Tests.Mapping;

public class ProfileTests
{
    private static readonly BoneRole[] FingerProxMidDistBothSides = BuildFingerRoles();

    private static BoneRole[] BuildFingerRoles()
    {
        var fingers = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        var segments = new[] { "Prox", "Mid", "Dist" };
        var sides = new[] { "L", "R" };
        return (
            from finger in fingers
            from segment in segments
            from side in sides
            select Enum.Parse<BoneRole>(finger + segment + side)).ToArray();
    }

    // ---------------------------------------------------------------- detection: mixamo

    [Fact]
    public void Detect_ZombieCrawl_PicksMixamoWithHighConfidence()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("mixamo", profile.Name);
        Assert.Equal("mixamo", result.ProfileName);
        Assert.Equal(MappingSource.Preset, result.Source);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(result.RoleToBone.Count >= 20, $"Only {result.RoleToBone.Count} roles mapped");

        AssertRole(skeleton, result, BoneRole.Hips, "mixamorig1:Hips");
        AssertRole(skeleton, result, BoneRole.Spine0, "mixamorig1:Spine");
        AssertRole(skeleton, result, BoneRole.Spine1, "mixamorig1:Spine1");
        AssertRole(skeleton, result, BoneRole.Spine2, "mixamorig1:Spine2");
        AssertRole(skeleton, result, BoneRole.Neck, "mixamorig1:Neck");
        AssertRole(skeleton, result, BoneRole.Head, "mixamorig1:Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "mixamorig1:LeftShoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "mixamorig1:LeftArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "mixamorig1:LeftForeArm");
        AssertRole(skeleton, result, BoneRole.HandL, "mixamorig1:LeftHand");
        AssertRole(skeleton, result, BoneRole.IndexProxL, "mixamorig1:LeftHandIndex1");
        AssertRole(skeleton, result, BoneRole.MiddleDistR, "mixamorig1:RightHandMiddle3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "mixamorig1:LeftUpLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "mixamorig1:RightLeg");
        AssertRole(skeleton, result, BoneRole.FootL, "mixamorig1:LeftFoot");
        AssertRole(skeleton, result, BoneRole.ToeR, "mixamorig1:RightToeBase");

        // All 5 finger chains, both sides, all three phalanges.
        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");
    }

    // ---------------------------------------------------------------- detection: actorcore

    [Fact]
    public void Detect_ActorCore_PicksActorCorePreset()
    {
        var skeleton = MappingFixtures.LoadActorCore();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("actorcore_cc", profile.Name);
        Assert.Equal(MappingSource.Preset, result.Source);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(result.RoleToBone.Count >= 20, $"Only {result.RoleToBone.Count} roles mapped");

        // Empirical hips resolution: CC_Base_Hip is the parent of both CC_Base_Pelvis (leg
        // branch) and CC_Base_Waist (spine branch) — it is the LCA of legs+spine and the
        // translating root, so it carries the Hips role; CC_Base_Pelvis stays unmapped.
        AssertRole(skeleton, result, BoneRole.Hips, "CC_Base_Hip");
        AssertRole(skeleton, result, BoneRole.Spine0, "CC_Base_Waist");
        AssertRole(skeleton, result, BoneRole.Spine1, "CC_Base_Spine01");
        AssertRole(skeleton, result, BoneRole.Spine2, "CC_Base_Spine02");
        AssertRole(skeleton, result, BoneRole.Neck, "CC_Base_NeckTwist01");
        AssertRole(skeleton, result, BoneRole.Head, "CC_Base_Head");
        AssertRole(skeleton, result, BoneRole.ClavicleR, "CC_Base_R_Clavicle");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "CC_Base_L_Upperarm");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "CC_Base_R_Forearm");
        AssertRole(skeleton, result, BoneRole.HandL, "CC_Base_L_Hand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "CC_Base_L_Thumb1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "CC_Base_R_Mid2");
        AssertRole(skeleton, result, BoneRole.PinkyDistL, "CC_Base_L_Pinky3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "CC_Base_L_Thigh");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "CC_Base_R_Calf");
        AssertRole(skeleton, result, BoneRole.FootL, "CC_Base_L_Foot");
        AssertRole(skeleton, result, BoneRole.ToeL, "CC_Base_L_ToeBase");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // ShareBone helpers and limb twist bones must never be mapped. The single allowed
        // "Twist" name is CC_Base_NeckTwist01, which IS the rig's neck bone.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name.Contains("ShareBone"), $"{role} mapped to share bone {name}");
            if (name.Contains("Twist"))
                Assert.True(role == BoneRole.Neck && name == "CC_Base_NeckTwist01",
                    $"{role} mapped to twist bone {name}");
        }
    }

    // ---------------------------------------------------------------- detection: UE

    [Fact]
    public void Detect_SyntheticUeMannequin_PicksUePreset()
    {
        var skeleton = MappingFixtures.BuildUeMannequin();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("ue_mannequin", profile.Name);
        Assert.True(result.Confidence >= 0.8f, $"Confidence {result.Confidence} < 0.8");

        AssertRole(skeleton, result, BoneRole.Hips, "pelvis");
        AssertRole(skeleton, result, BoneRole.Spine0, "spine_01");
        AssertRole(skeleton, result, BoneRole.Spine4, "spine_05");
        AssertRole(skeleton, result, BoneRole.Neck, "neck_01");
        AssertRole(skeleton, result, BoneRole.Head, "head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "clavicle_l");
        AssertRole(skeleton, result, BoneRole.UpperArmR, "upperarm_r");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "lowerarm_l");
        AssertRole(skeleton, result, BoneRole.HandR, "hand_r");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "thumb_01_l");
        AssertRole(skeleton, result, BoneRole.IndexMetaL, "index_metacarpal_l");
        AssertRole(skeleton, result, BoneRole.RingDistR, "ring_03_r");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "thigh_l");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "calf_r");
        AssertRole(skeleton, result, BoneRole.FootL, "foot_l");
        AssertRole(skeleton, result, BoneRole.ToeL, "ball_l");

        // *_twist_* bones are never mapped.
        foreach (var (role, boneIndex) in result.RoleToBone)
            Assert.DoesNotContain("_twist_", skeleton[boneIndex].Name);
    }

    // ---------------------------------------------------------------- detection: rokoko/bvh

    [Fact]
    public void Detect_SyntheticRokokoBvhNames_PicksRokokoPreset()
    {
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("Hips", null, new(0, 98, 0)),
            ("Spine", "Hips", new(0, 108, 0)),
            ("Spine1", "Spine", new(0, 120, 0)),
            ("Spine2", "Spine1", new(0, 132, 0)),
            ("Spine3", "Spine2", new(0, 142, 0)),
            ("Neck", "Spine3", new(0, 150, 0)),
            ("Head", "Neck", new(0, 158, 0)),
        };
        foreach (var (side, x) in new[] { ("Left", 1f), ("Right", -1f) })
        {
            bones.Add(($"{side}Shoulder", "Spine3", new(5 * x, 146, 0)));
            bones.Add(($"{side}Arm", $"{side}Shoulder", new(18 * x, 144, 0)));
            bones.Add(($"{side}ForeArm", $"{side}Arm", new(44 * x, 144, 0)));
            bones.Add(($"{side}Hand", $"{side}ForeArm", new(66 * x, 144, 0)));
            bones.Add(($"{side}UpLeg", "Hips", new(9 * x, 92, 0)));
            bones.Add(($"{side}Leg", $"{side}UpLeg", new(9 * x, 50, 0)));
            bones.Add(($"{side}Foot", $"{side}Leg", new(9 * x, 9, 0)));
            bones.Add(($"{side}Toe", $"{side}Foot", new(9 * x, 2, 12)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("rokoko_bvh", profile.Name);
        AssertRole(skeleton, result, BoneRole.Hips, "Hips");
        AssertRole(skeleton, result, BoneRole.Spine0, "Spine");
        AssertRole(skeleton, result, BoneRole.Spine3, "Spine3");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "LeftArm");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "RightLeg");
        AssertRole(skeleton, result, BoneRole.ToeL, "LeftToe");
    }

    // ---------------------------------------------------------------- scoring honesty

    [Fact]
    public void Score_GenericCoreNamesOnly_NoPresetReachesThreshold()
    {
        // Only the generic center names (Hips/Spine1/Spine2/Neck/Head) match any preset;
        // every limb is named outside all alias families. Rich center matches alone must
        // never carry a preset over the detection threshold.
        var bones = new List<(string, string?, System.Numerics.Vector3)>
        {
            ("Hips", null, new(0, 98, 0)),
            ("Spine1", "Hips", new(0, 110, 0)),
            ("Spine2", "Spine1", new(0, 124, 0)),
            ("Neck", "Spine2", new(0, 148, 0)),
            ("Head", "Neck", new(0, 156, 0)),
        };
        foreach (var (side, x) in new[] { ("A", 1f), ("B", -1f) })
        {
            bones.Add(($"limb_{side}_1", "Spine2", new(18 * x, 144, 0)));
            bones.Add(($"limb_{side}_2", $"limb_{side}_1", new(44 * x, 144, 0)));
            bones.Add(($"limb_{side}_3", $"limb_{side}_2", new(66 * x, 144, 0)));
            bones.Add(($"limb_{side}_4", "Hips", new(9 * x, 92, 0)));
            bones.Add(($"limb_{side}_5", $"limb_{side}_4", new(9 * x, 50, 0)));
            bones.Add(($"limb_{side}_6", $"limb_{side}_5", new(9 * x, 9, 0)));
        }
        var skeleton = MappingFixtures.FromWorldPositions(bones);

        foreach (var profile in ProfileLibrary.All)
        {
            var score = ProfileDetector.Score(profile, skeleton);
            Assert.True(score < ProfileDetector.DetectionThreshold,
                $"{profile.Name} scored {score} on a rig with no matching limb names");
        }
    }

    // ---------------------------------------------------------------- detection: negative

    [Fact]
    public void Detect_UnknownNames_ReturnsNull()
    {
        var skeleton = MappingFixtures.RenameAll(MappingFixtures.LoadZombieCrawl(), i => $"bone_{i:000}");
        Assert.Null(ProfileDetector.Detect(skeleton));
    }

    [Fact]
    public void Score_MixamoProfileOnZombieCrawl_BeatsOtherPresets()
    {
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var mixamo = ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton);
        Assert.True(mixamo >= 0.9f);
        Assert.True(mixamo > ProfileDetector.Score(ProfileLibrary.ActorCoreCc, skeleton));
        Assert.True(mixamo > ProfileDetector.Score(ProfileLibrary.UeMannequin, skeleton));
        Assert.True(mixamo > ProfileDetector.Score(ProfileLibrary.RokokoBvh, skeleton));
    }

    // ---------------------------------------------------------------- json (de)serialization

    [Fact]
    public void Profile_JsonRoundTrips()
    {
        foreach (var profile in ProfileLibrary.All)
        {
            var json = profile.ToJson();
            var restored = Profile.FromJson(json);

            Assert.Equal(profile.Name, restored.Name);
            Assert.Equal(profile.NamespacePatterns, restored.NamespacePatterns);
            Assert.Equal(profile.Aliases.Count, restored.Aliases.Count);
            foreach (var (role, aliases) in profile.Aliases)
                Assert.Equal(aliases, restored.Aliases[role]);
        }
    }

    [Fact]
    public void Profile_UserPresetShape_V1RoundTrips()
    {
        // The exact document shape the editor's UserPresets.Save produces: name
        // "user_<sig8>", no namespace patterns, ONE alias per role - the literal source
        // bone name. This is the v1 user-preset contract; FromJson must keep reading it.
        var skeleton = MappingFixtures.LoadZombieCrawl();
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "mixamorig1:Hips" },
            [BoneRole.Spine0] = new[] { "mixamorig1:Spine" },
            [BoneRole.Head] = new[] { "mixamorig1:Head" },
            [BoneRole.HandL] = new[] { "mixamorig1:LeftHand" },
            [BoneRole.PinkyDistR] = new[] { "mixamorig1:RightHandPinky3" },
        };
        var profile = new Profile("user_a1b2c3d4", Array.Empty<string>(), aliases);

        var json = profile.ToJson();
        Assert.Contains("\"v\": 1", json);
        var restored = Profile.FromJson(json);

        Assert.Equal(profile.Name, restored.Name);
        Assert.Empty(restored.NamespacePatterns);
        Assert.Equal(aliases.Count, restored.Aliases.Count);
        foreach (var (role, names) in aliases)
            Assert.Equal(names, restored.Aliases[role]);

        // And the restored preset actually resolves on the rig it was saved from.
        var applied = ProfileDetector.Apply(restored, skeleton);
        foreach (var (role, names) in aliases)
            Assert.Equal(names[0], skeleton[applied.RoleToBone[role]].Name);
    }

    [Fact]
    public void PresetAssetsFolder_HasNoOrphanProfiles()
    {
        // Every committed (non-user) profile JSON must correspond to a shipped preset -
        // an orphan file would silently diverge from ProfileLibrary.
        var dir = FindRepoFile(Path.Combine("Assets", "data", "humanoid_retargeter", "profiles"));
        Assert.True(Directory.Exists(dir), $"profiles folder missing: {dir}");

        var known = ProfileLibrary.All.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var orphans = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !known.Contains(name!))
            .ToList();
        Assert.True(orphans.Count == 0,
            $"Orphan profile JSONs without a ProfileLibrary preset: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Profile_FromJson_RejectsUnknownSchemaVersion()
    {
        var json = """{"v": 99, "name": "x", "namespace_patterns": [], "aliases": {}}""";
        Assert.Throws<ArgumentException>(() => Profile.FromJson(json));
    }

    [Fact]
    public void Profile_FromJson_RejectsUnknownRole()
    {
        var json = """{"v": 1, "name": "x", "namespace_patterns": [], "aliases": {"NotARole": ["a"]}}""";
        Assert.Throws<ArgumentException>(() => Profile.FromJson(json));
    }

    // ---------------------------------------------------------------- preset asset regeneration

    [Fact]
    public void PresetJsons_MatchCommittedAssets()
    {
        foreach (var profile in ProfileLibrary.All)
        {
            var generated = profile.ToJson();
            var assetPath = FindRepoFile(Path.Combine(
                "Assets", "data", "humanoid_retargeter", "profiles", profile.Name + ".json"));

            if (!File.Exists(assetPath))
            {
                // Regenerate-and-diff pattern: first run writes the committed artifact.
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
                File.WriteAllText(assetPath, generated);
            }

            var committed = File.ReadAllText(assetPath);
            Assert.Equal(Normalize(committed), Normalize(generated));
        }
    }

    [Fact]
    public void PresetLibrary_HasExpectedProfiles()
    {
        Assert.Equal(
            new[]
            {
                "sbox", "mixamo", "actorcore_cc", "ue_mannequin", "xsens_mvn", "perception_neuron",
                "fight_night", "rokoko_bvh", "smpl_x", "smpl", "soma_bvh", "classic_bvh",
                "valvebiped", "biped", "daz_genesis", "daz_poser",
                "rigify", "vrm", "auto_rig_pro", "advanced_skeleton",
            },
            ProfileLibrary.All.Select(p => p.Name).ToArray());
    }

    // ---------------------------------------------------------------- detection: fight night

    /// <summary>
    /// The EA Fight Night boxer rig uses MotionBuilder/HumanIK joint names, so mixamo and
    /// perception_neuron both score respectably on it — the dedicated preset must win
    /// outright by covering the fourth spine bone, the toes AND declaring only the finger
    /// roles a gloved hand actually has.
    /// </summary>
    [Fact]
    public void Detect_SyntheticFightNight_PicksFightNightPreset()
    {
        var skeleton = MappingFixtures.BuildFightNight();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("fight_night", profile.Name);
        Assert.True(result.Confidence >= 0.99f, $"Confidence {result.Confidence} < 0.99");

        // It must beat the HumanIK-family presets that also match these names.
        var fightNight = ProfileDetector.Score(ProfileLibrary.FightNight, skeleton);
        Assert.True(fightNight > ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton),
            "fight_night must outscore mixamo on a boxer rig");
        Assert.True(fightNight > ProfileDetector.Score(ProfileLibrary.PerceptionNeuron, skeleton),
            "fight_night must outscore perception_neuron on a boxer rig");

        AssertRole(skeleton, result, BoneRole.Hips, "Hips");
        AssertRole(skeleton, result, BoneRole.Spine0, "Spine");
        AssertRole(skeleton, result, BoneRole.Spine3, "Spine3");
        Assert.False(result.RoleToBone.ContainsKey(BoneRole.Spine4), "Spine4 role double-mapped");
        // Neck→Neck1→Head: the second neck bone stays unmapped.
        AssertRole(skeleton, result, BoneRole.Neck, "Neck");
        AssertRole(skeleton, result, BoneRole.Head, "Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "LeftShoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmR, "RightArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "LeftForeArm");
        AssertRole(skeleton, result, BoneRole.HandL, "LeftHand");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "LeftUpLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "RightLeg");
        AssertRole(skeleton, result, BoneRole.FootL, "LeftFoot");
        AssertRole(skeleton, result, BoneRole.ToeR, "RightToeBase");

        // Gloved hand: two thumb segments, and the middle finger's InHand metacarpal maps
        // to the Meta role (NOT a phalanx — that is the SOMA finger bug).
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "LeftHandThumb1");
        AssertRole(skeleton, result, BoneRole.ThumbMidL, "LeftHandThumb2");
        AssertRole(skeleton, result, BoneRole.MiddleMetaL, "LeftInHandMiddle");
        AssertRole(skeleton, result, BoneRole.MiddleProxL, "LeftHandMiddle1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "RightHandMiddle2");
    }

    /// <summary>
    /// Every decoy family on the boxer rig stays unmapped: the glove mesh shells (which
    /// carry finger NAMES), the root/trajectory bones above the hips, the second neck bone
    /// and the twist/muscle/jiggle simulation layer.
    /// </summary>
    [Fact]
    public void FightNight_GloveShellsRootAndSimulationBones_AreNeverMapped()
    {
        var skeleton = MappingFixtures.BuildFightNight();

        var result = ProfileDetector.Apply(ProfileLibrary.FightNight, skeleton);

        var mapped = result.RoleToBone.Values.Select(i => skeleton[i].Name).ToHashSet(StringComparer.Ordinal);
        foreach (var decoy in new[]
        {
            "LeftHandThumbGlove", "LeftHandIndexGlove", "LeftHandMiddleGlove",
            "LeftInHandRingGlove", "LeftHandPinkyGlove", "RightHandIndexGlove",
            "Reference", "AITrajectory", "Neck1", "Crotch",
            "LeftArmTwist", "LeftForeArmTwist", "LeftGloveTwist", "LeftUpLegTwist",
            "Muscle_Left_Bicep", "Offset_Muscle_Left_Delt", "Left_Elbow_Helper",
            "Left_Knee_Helper", "LeftFootEnd", "Spine2_Jiggle", "Gut_Center", "StomachFront",
        })
        {
            Assert.True(skeleton.IndexOf(decoy) >= 0, $"fixture missing decoy bone {decoy}");
            Assert.False(mapped.Contains(decoy), $"{decoy} must never carry a role");
        }

        // No index/ring/pinky roles exist on a gloved hand.
        foreach (var role in new[]
        {
            BoneRole.IndexProxL, BoneRole.IndexProxR, BoneRole.RingProxL,
            BoneRole.PinkyProxL, BoneRole.ThumbDistL, BoneRole.MiddleDistL,
        })
        {
            Assert.False(result.RoleToBone.ContainsKey(role), $"{role} must not be mapped");
        }
    }

    /// <summary>The boxer preset must not claim the rigs whose naming it shares.</summary>
    [Fact]
    public void FightNight_DoesNotOutscoreNativePresetsOnTheirOwnRigs()
    {
        var mixamo = MappingFixtures.LoadZombieCrawl();
        Assert.True(
            ProfileDetector.Score(ProfileLibrary.Mixamo, mixamo)
            > ProfileDetector.Score(ProfileLibrary.FightNight, mixamo),
            "fight_night must not outscore mixamo on a real Mixamo rig");
        Assert.Equal("mixamo", ProfileDetector.Detect(mixamo)!.Value.Profile.Name);

        var neuron = MappingFixtures.BuildPerceptionNeuron();
        Assert.True(
            ProfileDetector.Score(ProfileLibrary.PerceptionNeuron, neuron)
            > ProfileDetector.Score(ProfileLibrary.FightNight, neuron),
            "fight_night must not outscore perception_neuron on a Neuron rig");
        Assert.Equal("perception_neuron", ProfileDetector.Detect(neuron)!.Value.Profile.Name);
    }

    // ---------------------------------------------------------------- detection: valvebiped

    [Fact]
    public void Detect_SyntheticValveBiped_PicksValveBipedPreset()
    {
        var skeleton = MappingFixtures.BuildValveBiped();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("valvebiped", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Biped, skeleton) < 0.8f,
            "the plain Biped preset must not claim a ValveBiped rig");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim a ValveBiped rig");

        AssertRole(skeleton, result, BoneRole.Hips, "ValveBiped.Bip01_Pelvis");
        AssertRole(skeleton, result, BoneRole.Spine0, "ValveBiped.Bip01_Spine");
        AssertRole(skeleton, result, BoneRole.Spine2, "ValveBiped.Bip01_Spine2");
        // The stock chain skips Spine3: Spine4 is the chest and fills the Spine3 role.
        AssertRole(skeleton, result, BoneRole.Spine3, "ValveBiped.Bip01_Spine4");
        Assert.False(result.RoleToBone.ContainsKey(BoneRole.Spine4), "Spine4 role double-mapped");
        AssertRole(skeleton, result, BoneRole.Neck, "ValveBiped.Bip01_Neck1");
        AssertRole(skeleton, result, BoneRole.Head, "ValveBiped.Bip01_Head1");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "ValveBiped.Bip01_L_Clavicle");
        AssertRole(skeleton, result, BoneRole.UpperArmR, "ValveBiped.Bip01_R_UpperArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "ValveBiped.Bip01_L_Forearm");
        AssertRole(skeleton, result, BoneRole.HandL, "ValveBiped.Bip01_L_Hand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "ValveBiped.Bip01_L_Finger0");
        AssertRole(skeleton, result, BoneRole.ThumbDistL, "ValveBiped.Bip01_L_Finger02");
        AssertRole(skeleton, result, BoneRole.IndexProxL, "ValveBiped.Bip01_L_Finger1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "ValveBiped.Bip01_R_Finger21");
        AssertRole(skeleton, result, BoneRole.PinkyDistR, "ValveBiped.Bip01_R_Finger42");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "ValveBiped.Bip01_L_Thigh");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "ValveBiped.Bip01_R_Calf");
        AssertRole(skeleton, result, BoneRole.FootL, "ValveBiped.Bip01_L_Foot");
        AssertRole(skeleton, result, BoneRole.ToeL, "ValveBiped.Bip01_L_Toe0");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // Attachment/helper bones carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name == "ValveBiped.forward" || name.Contains("Anim_Attachment"),
                $"{role} mapped to helper {name}");
        }
    }

    // ---------------------------------------------------------------- detection: xsens mvn

    [Fact]
    public void Detect_SyntheticXsensMvn_PicksXsensPreset()
    {
        var skeleton = MappingFixtures.BuildXsensMvn();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("xsens_mvn", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.RokokoBvh, skeleton) < 0.8f,
            "rokoko_bvh must not claim an MVN rig (no 'Hips' bone)");

        AssertRole(skeleton, result, BoneRole.Hips, "Pelvis");
        AssertRole(skeleton, result, BoneRole.Spine0, "L5");
        AssertRole(skeleton, result, BoneRole.Spine1, "L3");
        AssertRole(skeleton, result, BoneRole.Spine2, "T12");
        AssertRole(skeleton, result, BoneRole.Spine3, "T8");
        AssertRole(skeleton, result, BoneRole.Neck, "Neck");
        AssertRole(skeleton, result, BoneRole.Head, "Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "LeftShoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmR, "RightUpperArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "LeftForeArm");
        AssertRole(skeleton, result, BoneRole.HandR, "RightHand");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "LeftUpperLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "RightLowerLeg");
        AssertRole(skeleton, result, BoneRole.FootL, "LeftFoot");
        AssertRole(skeleton, result, BoneRole.ToeR, "RightToe");
    }

    // ---------------------------------------------------------------- detection: perception neuron

    [Fact]
    public void Detect_SyntheticPerceptionNeuron_PicksNeuronPreset()
    {
        var skeleton = MappingFixtures.BuildPerceptionNeuron();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("perception_neuron", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        // Mixamo matches most Neuron names but misses Spine3 and the (absent) toes; the
        // dedicated preset must outscore it.
        Assert.True(
            ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < result.Confidence,
            "perception_neuron must outscore mixamo on a Neuron rig");

        AssertRole(skeleton, result, BoneRole.Hips, "Hips");
        AssertRole(skeleton, result, BoneRole.Spine0, "Spine");
        AssertRole(skeleton, result, BoneRole.Spine3, "Spine3");
        AssertRole(skeleton, result, BoneRole.Neck, "Neck");
        AssertRole(skeleton, result, BoneRole.Head, "Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "LeftShoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "LeftArm");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "RightForeArm");
        AssertRole(skeleton, result, BoneRole.HandL, "LeftHand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "LeftHandThumb1");
        AssertRole(skeleton, result, BoneRole.IndexProxL, "LeftHandIndex1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "RightHandMiddle2");
        AssertRole(skeleton, result, BoneRole.PinkyDistR, "RightHandPinky3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "LeftUpLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "RightLeg");
        AssertRole(skeleton, result, BoneRole.FootL, "LeftFoot");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // The InHand metacarpal helpers must never be mapped (mapping them as phalanges
        // shifts every curl one joint outward — the SOMA finger bug class).
        foreach (var (role, boneIndex) in result.RoleToBone)
            Assert.DoesNotContain("InHand", skeleton[boneIndex].Name);
    }

    // ---------------------------------------------------------------- detection: daz genesis

    [Fact]
    public void Detect_SyntheticDazGenesis_PicksDazGenesisPreset()
    {
        var skeleton = MappingFixtures.BuildDazGenesis();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("daz_genesis", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.DazPoser, skeleton) < 0.8f,
            "daz_poser must not claim a Genesis 3/8 rig (lShldr vs lShldrBend)");

        // hip is the LCA of the pelvis leg branch and the abdomen spine branch — it is
        // the hips; pelvis stays unmapped (ActorCore Hip/Pelvis policy).
        AssertRole(skeleton, result, BoneRole.Hips, "hip");
        AssertRole(skeleton, result, BoneRole.Spine0, "abdomenLower");
        AssertRole(skeleton, result, BoneRole.Spine1, "abdomenUpper");
        AssertRole(skeleton, result, BoneRole.Spine2, "chestLower");
        AssertRole(skeleton, result, BoneRole.Spine3, "chestUpper");
        AssertRole(skeleton, result, BoneRole.Neck, "neckLower");
        AssertRole(skeleton, result, BoneRole.Head, "head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "lCollar");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "lShldrBend");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "rForearmBend");
        AssertRole(skeleton, result, BoneRole.HandL, "lHand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "lThumb1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "rMid2");
        AssertRole(skeleton, result, BoneRole.PinkyDistL, "lPinky3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "lThighBend");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "rShin");
        AssertRole(skeleton, result, BoneRole.FootL, "lFoot");
        AssertRole(skeleton, result, BoneRole.ToeL, "lToe");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // Twist roll helpers, the pelvis intermediate, neckUpper and the metatarsals
        // carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.DoesNotContain("Twist", name);
            Assert.False(name is "pelvis" or "neckUpper", $"{role} mapped to {name}");
            Assert.DoesNotContain("Metatarsals", name);
        }
    }

    // ---------------------------------------------------------------- detection: 3ds max biped

    [Fact]
    public void Detect_SyntheticBiped_PicksBipedPreset()
    {
        var skeleton = MappingFixtures.BuildBiped();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("biped", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim a Biped rig");

        AssertRole(skeleton, result, BoneRole.Hips, "Bip01 Pelvis");
        AssertRole(skeleton, result, BoneRole.Spine0, "Bip01 Spine");
        AssertRole(skeleton, result, BoneRole.Spine3, "Bip01 Spine3");
        AssertRole(skeleton, result, BoneRole.Neck, "Bip01 Neck");
        AssertRole(skeleton, result, BoneRole.Head, "Bip01 Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "Bip01 L Clavicle");
        AssertRole(skeleton, result, BoneRole.UpperArmR, "Bip01 R UpperArm");
        AssertRole(skeleton, result, BoneRole.LowerArmL, "Bip01 L Forearm");
        AssertRole(skeleton, result, BoneRole.HandL, "Bip01 L Hand");
        // Numbered fingers: Finger0 chain is the thumb, segment digits are phalanges.
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "Bip01 L Finger0");
        AssertRole(skeleton, result, BoneRole.ThumbDistL, "Bip01 L Finger02");
        AssertRole(skeleton, result, BoneRole.IndexProxL, "Bip01 L Finger1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "Bip01 R Finger21");
        AssertRole(skeleton, result, BoneRole.PinkyDistR, "Bip01 R Finger42");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "Bip01 L Thigh");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "Bip01 R Calf");
        AssertRole(skeleton, result, BoneRole.FootL, "Bip01 L Foot");
        AssertRole(skeleton, result, BoneRole.ToeL, "Bip01 L Toe0");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // The COM root, Footsteps and toe segments carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name is "Bip01" or "Bip01 Footsteps", $"{role} mapped to {name}");
            Assert.False(name.EndsWith("Toe01") || name.EndsWith("Toe02"), $"{role} mapped to toe segment {name}");
        }
    }

    [Fact]
    public void Detect_BipedUnderscoreNames_StillDetected()
    {
        // Some exporters mangle the Biped spaces to underscores (Bip001_L_Thigh); the
        // namespace pattern and separator-insensitive normalization must absorb both.
        var original = MappingFixtures.BuildBiped();
        var skeleton = MappingFixtures.RenameAll(
            original, i => original[i].Name.Replace("Bip01 ", "Bip001_").Replace(' ', '_'));

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        Assert.Equal("biped", detected.Value.Profile.Name);
        Assert.True(detected.Value.Result.Confidence >= 0.9f);
        AssertRole(skeleton, detected.Value.Result, BoneRole.UpperLegL, "Bip001_L_Thigh");
    }

    // ---------------------------------------------------------------- detection: daz / poser

    [Fact]
    public void Detect_SyntheticDazPoser_PicksDazPreset()
    {
        var skeleton = MappingFixtures.BuildDazPoser();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("daz_poser", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim a DAZ/Poser rig");

        AssertRole(skeleton, result, BoneRole.Hips, "hip");
        AssertRole(skeleton, result, BoneRole.Spine0, "abdomen");
        AssertRole(skeleton, result, BoneRole.Spine1, "abdomen2");
        AssertRole(skeleton, result, BoneRole.Spine2, "chest");
        AssertRole(skeleton, result, BoneRole.Neck, "neck");
        AssertRole(skeleton, result, BoneRole.Head, "head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "lCollar");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "lShldr");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "rForeArm");
        AssertRole(skeleton, result, BoneRole.HandL, "lHand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "lThumb1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "rMid2");
        AssertRole(skeleton, result, BoneRole.PinkyDistL, "lPinky3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "lThigh");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "rShin");
        AssertRole(skeleton, result, BoneRole.FootL, "lFoot");
        AssertRole(skeleton, result, BoneRole.ToeL, "lToe");

        // Buttock thigh-helpers and eyes carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.DoesNotContain("Buttock", name);
            Assert.DoesNotContain("Eye", name);
        }
    }

    // ---------------------------------------------------------------- detection: rigify

    [Theory]
    [InlineData(false)] // metarig names: spine, spine.001.., upper_arm.L
    [InlineData(true)]  // generated deform skeleton: DEF-spine, DEF-upper_arm.L (+ .001 twins)
    public void Detect_SyntheticRigify_PicksRigifyPreset(bool defVariant)
    {
        var skeleton = MappingFixtures.BuildRigify(defVariant);

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("rigify", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim a rigify rig");

        var d = defVariant ? "DEF-" : string.Empty;
        // rigify's "spine" IS the pelvis bone (rigify/metarigs/human.py); spine.004 is
        // the first neck bone, spine.006 the head. spine.005 stays unmapped.
        AssertRole(skeleton, result, BoneRole.Hips, $"{d}spine");
        AssertRole(skeleton, result, BoneRole.Spine0, $"{d}spine.001");
        AssertRole(skeleton, result, BoneRole.Spine2, $"{d}spine.003");
        AssertRole(skeleton, result, BoneRole.Neck, $"{d}spine.004");
        AssertRole(skeleton, result, BoneRole.Head, $"{d}spine.006");
        Assert.DoesNotContain(skeleton.Bones.First(b => b.Name == $"{d}spine.005").Index,
            result.RoleToBone.Values);
        AssertRole(skeleton, result, BoneRole.ClavicleL, $"{d}shoulder.L");
        AssertRole(skeleton, result, BoneRole.UpperArmL, $"{d}upper_arm.L");
        AssertRole(skeleton, result, BoneRole.LowerArmR, $"{d}forearm.R");
        AssertRole(skeleton, result, BoneRole.HandL, $"{d}hand.L");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, $"{d}thumb.01.L");
        AssertRole(skeleton, result, BoneRole.IndexProxL, $"{d}f_index.01.L");
        AssertRole(skeleton, result, BoneRole.RingDistR, $"{d}f_ring.03.R");
        AssertRole(skeleton, result, BoneRole.UpperLegL, $"{d}thigh.L");
        AssertRole(skeleton, result, BoneRole.LowerLegR, $"{d}shin.R");
        AssertRole(skeleton, result, BoneRole.FootL, $"{d}foot.L");
        AssertRole(skeleton, result, BoneRole.ToeL, $"{d}toe.L");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // pelvis.L/R, palm, heel helpers and segmented DEF twins carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name.StartsWith("pelvis.") || name.StartsWith("palm.") || name.StartsWith("heel."),
                $"{role} mapped to helper {name}");
            Assert.False(name.EndsWith(".001") && name.StartsWith("DEF-") && !name.Contains("spine"),
                $"{role} mapped to segmented deform twin {name}");
        }
    }

    // ---------------------------------------------------------------- detection: vrm / vroid

    [Fact]
    public void Detect_SyntheticVrm_PicksVrmPreset()
    {
        var skeleton = MappingFixtures.BuildVrm();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("vrm", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim a VRM rig");

        AssertRole(skeleton, result, BoneRole.Hips, "J_Bip_C_Hips");
        AssertRole(skeleton, result, BoneRole.Spine0, "J_Bip_C_Spine");
        AssertRole(skeleton, result, BoneRole.Spine1, "J_Bip_C_Chest");
        AssertRole(skeleton, result, BoneRole.Spine2, "J_Bip_C_UpperChest");
        AssertRole(skeleton, result, BoneRole.Neck, "J_Bip_C_Neck");
        AssertRole(skeleton, result, BoneRole.Head, "J_Bip_C_Head");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "J_Bip_L_Shoulder");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "J_Bip_L_UpperArm");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "J_Bip_R_LowerArm");
        AssertRole(skeleton, result, BoneRole.HandL, "J_Bip_L_Hand");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "J_Bip_L_Thumb1");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "J_Bip_R_Middle2");
        // VRM names the pinky "Little" (littleProximal.. humanoid bones).
        AssertRole(skeleton, result, BoneRole.PinkyDistL, "J_Bip_L_Little3");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "J_Bip_L_UpperLeg");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "J_Bip_R_LowerLeg");
        AssertRole(skeleton, result, BoneRole.FootL, "J_Bip_L_Foot");
        AssertRole(skeleton, result, BoneRole.ToeL, "J_Bip_L_ToeBase");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // Root and the secondary physics/adjust bones carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name == "Root" || name.StartsWith("J_Sec_") || name.StartsWith("J_Adj_"),
                $"{role} mapped to non-humanoid bone {name}");
        }
    }

    // ---------------------------------------------------------------- detection: auto-rig pro

    [Fact]
    public void Detect_SyntheticAutoRigPro_PicksAutoRigProPreset()
    {
        var skeleton = MappingFixtures.BuildAutoRigPro();

        var detected = ProfileDetector.Detect(skeleton);

        Assert.NotNull(detected);
        var (profile, result) = detected.Value;
        Assert.Equal("auto_rig_pro", profile.Name);
        Assert.True(result.Confidence >= 0.9f, $"Confidence {result.Confidence} < 0.9");
        Assert.True(ProfileDetector.Score(ProfileLibrary.Mixamo, skeleton) < 0.8f,
            "mixamo must not claim an Auto-Rig Pro rig");

        // root.x is the hips (the ground bone "root" carries no role).
        AssertRole(skeleton, result, BoneRole.Hips, "root.x");
        AssertRole(skeleton, result, BoneRole.Spine0, "spine_01.x");
        AssertRole(skeleton, result, BoneRole.Spine2, "spine_03.x");
        AssertRole(skeleton, result, BoneRole.Neck, "neck.x");
        AssertRole(skeleton, result, BoneRole.Head, "head.x");
        AssertRole(skeleton, result, BoneRole.ClavicleL, "shoulder.l");
        AssertRole(skeleton, result, BoneRole.UpperArmL, "arm_stretch.l");
        AssertRole(skeleton, result, BoneRole.LowerArmR, "forearm_stretch.r");
        AssertRole(skeleton, result, BoneRole.HandL, "hand.l");
        AssertRole(skeleton, result, BoneRole.ThumbProxL, "c_thumb1.l");
        AssertRole(skeleton, result, BoneRole.MiddleMidR, "c_middle2.r");
        AssertRole(skeleton, result, BoneRole.PinkyDistL, "c_pinky3.l");
        AssertRole(skeleton, result, BoneRole.UpperLegL, "thigh_stretch.l");
        AssertRole(skeleton, result, BoneRole.LowerLegR, "leg_stretch.r");
        AssertRole(skeleton, result, BoneRole.FootL, "foot.l");
        AssertRole(skeleton, result, BoneRole.ToeL, "toes_01.l");

        foreach (var role in FingerProxMidDistBothSides)
            Assert.True(result.RoleToBone.ContainsKey(role), $"Finger role {role} unmapped");

        // The ground bone and the leftover mixamorig finger-tip markers carry no role.
        foreach (var (role, boneIndex) in result.RoleToBone)
        {
            var name = skeleton[boneIndex].Name;
            Assert.False(name == "root" || name.StartsWith("mixamorig:"),
                $"{role} mapped to non-deform bone {name}");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertRole(
        HumanoidRetargeter.Core.Skeleton.Skeleton skeleton, MappingResult result, BoneRole role, string expectedBone)
    {
        Assert.True(result.RoleToBone.TryGetValue(role, out var index), $"Role {role} unmapped");
        Assert.Equal(expectedBone, skeleton[index].Name);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(dir.FullName, relativePath);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found above test directory.");
    }
}
