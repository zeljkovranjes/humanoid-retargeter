using System.Collections.Generic;

namespace HumanoidRetargeter.Mapping;

/// <summary>
/// Built-in preset profiles, embedded as C# data (the same data is written to
/// <c>Assets/humanoid_retargeter/profiles/*.json</c> by a regenerate-and-diff test so the
/// shipped JSON can never drift from the code).
/// </summary>
public static class ProfileLibrary
{
    /// <summary>Mixamo / Adobe rigs: <c>mixamorig[N]:</c> namespace, <c>LeftArm</c> /
    /// <c>LeftForeArm</c> / <c>LeftHandIndex1..3</c> style names.</summary>
    public static Profile Mixamo { get; } = BuildMixamo();

    /// <summary>
    /// Reallusion ActorCore / AccuRig / Character Creator rigs (<c>CC_Base_*</c>).
    /// Empirical notes from <c>research/rig_actorcore.json</c>:
    /// <list type="bullet">
    /// <item><c>CC_Base_Hip</c> is the parent of BOTH <c>CC_Base_Pelvis</c> (leg branch) and
    /// <c>CC_Base_Waist</c> (spine branch), i.e. the LCA of legs+spine and the true animated
    /// hips root → it carries <see cref="BoneRole.Hips"/>; <c>CC_Base_Pelvis</c> is a
    /// leg-branch intermediate and stays unmapped.</item>
    /// <item>The neck chain is <c>CC_Base_NeckTwist01 → CC_Base_NeckTwist02 → CC_Base_Head</c>;
    /// despite the name, <c>NeckTwist01</c> IS the neck bone (there is no plain
    /// <c>CC_Base_Neck</c>), so it is the <see cref="BoneRole.Neck"/> alias. NeckTwist02 is
    /// left unmapped. All other Twist/ShareBone helpers are excluded (no aliases).</item>
    /// <item><c>CC_Base_L_ToeBase</c> is the toe role; the co-located
    /// <c>CC_Base_L_ToeBaseShareBone</c> is a helper and must never be mapped.</item>
    /// </list>
    /// </summary>
    public static Profile ActorCoreCc { get; } = BuildActorCoreCc();

    /// <summary>Unreal Engine mannequin (UE4/UE5): <c>pelvis</c>, <c>spine_01..05</c>,
    /// <c>clavicle_l</c>, <c>thumb_01_l</c>, UE5 <c>*_metacarpal_*</c>; <c>*_twist_*</c>
    /// bones have no aliases and are never mapped.</summary>
    public static Profile UeMannequin { get; } = BuildUeMannequin();

    /// <summary>Rokoko / Xsens style BVH rigs: plain <c>Hips</c>/<c>Spine..Spine4</c>/<c>
    /// LeftArm|LeftUpperArm</c> name variants, usually no fingers.</summary>
    public static Profile RokokoBvh { get; } = BuildRokokoBvh();

    /// <summary>All built-in presets, in detection order.</summary>
    public static IReadOnlyList<Profile> All { get; } = new[] { Mixamo, ActorCoreCc, UeMannequin, RokokoBvh };

    // ---------------------------------------------------------------- mixamo

    private static Profile BuildMixamo()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Hips" },
            [BoneRole.Spine0] = new[] { "Spine" },
            [BoneRole.Spine1] = new[] { "Spine1" },
            [BoneRole.Spine2] = new[] { "Spine2" },
            [BoneRole.Neck] = new[] { "Neck" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var (roleSide, nameSide) in Sides())
        {
            aliases[Role("Clavicle", roleSide)] = new[] { $"{nameSide}Shoulder" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{nameSide}Arm" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{nameSide}ForeArm" };
            aliases[Role("Hand", roleSide)] = new[] { $"{nameSide}Hand" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{nameSide}UpLeg" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{nameSide}Leg" };
            aliases[Role("Foot", roleSide)] = new[] { $"{nameSide}Foot" };
            aliases[Role("Toe", roleSide)] = new[] { $"{nameSide}ToeBase" };

            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                aliases[Role($"{finger}Prox", roleSide)] = new[] { $"{nameSide}Hand{finger}1" };
                aliases[Role($"{finger}Mid", roleSide)] = new[] { $"{nameSide}Hand{finger}2" };
                aliases[Role($"{finger}Dist", roleSide)] = new[] { $"{nameSide}Hand{finger}3" };
            }
        }
        // Both ':' (FBX namespace) and '_' (namespace mangled by some exporters) forms occur
        // in the wild; some Mixamo downloads ship with no namespace at all, which still
        // matches because the aliases are the bare names.
        return new Profile("mixamo", new[] { "^mixamorig[0-9]*:", "^mixamorig[0-9]*_" }, aliases);
    }

    // ---------------------------------------------------------------- actorcore / cc

    private static Profile BuildActorCoreCc()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Hip" },
            [BoneRole.Spine0] = new[] { "Waist" },
            [BoneRole.Spine1] = new[] { "Spine01" },
            [BoneRole.Spine2] = new[] { "Spine02" },
            [BoneRole.Neck] = new[] { "NeckTwist01" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var roleSide in new[] { "L", "R" })
        {
            var nameSide = roleSide; // CC bones use the bare side letter: CC_Base_L_Thigh.
            aliases[Role("Clavicle", roleSide)] = new[] { $"{nameSide}_Clavicle" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{nameSide}_Upperarm" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{nameSide}_Forearm" };
            aliases[Role("Hand", roleSide)] = new[] { $"{nameSide}_Hand" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{nameSide}_Thigh" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{nameSide}_Calf" };
            aliases[Role("Foot", roleSide)] = new[] { $"{nameSide}_Foot" };
            aliases[Role("Toe", roleSide)] = new[] { $"{nameSide}_ToeBase" };

            foreach (var (role, cc) in new[]
            {
                ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Mid"), ("Ring", "Ring"), ("Pinky", "Pinky"),
            })
            {
                aliases[Role($"{role}Prox", roleSide)] = new[] { $"{nameSide}_{cc}1" };
                aliases[Role($"{role}Mid", roleSide)] = new[] { $"{nameSide}_{cc}2" };
                aliases[Role($"{role}Dist", roleSide)] = new[] { $"{nameSide}_{cc}3" };
            }
        }
        return new Profile("actorcore_cc", new[] { "^CC_Base_" }, aliases);
    }

    // ---------------------------------------------------------------- ue mannequin

    private static Profile BuildUeMannequin()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "pelvis" },
            [BoneRole.Spine0] = new[] { "spine_01" },
            [BoneRole.Spine1] = new[] { "spine_02" },
            [BoneRole.Spine2] = new[] { "spine_03" },
            [BoneRole.Spine3] = new[] { "spine_04" },
            [BoneRole.Spine4] = new[] { "spine_05" },
            [BoneRole.Neck] = new[] { "neck_01" },
            [BoneRole.Head] = new[] { "head" },
        };
        foreach (var (roleSide, s) in new[] { ("L", "l"), ("R", "r") })
        {
            aliases[Role("Clavicle", roleSide)] = new[] { $"clavicle_{s}" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"upperarm_{s}" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"lowerarm_{s}" };
            aliases[Role("Hand", roleSide)] = new[] { $"hand_{s}" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"thigh_{s}" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"calf_{s}" };
            aliases[Role("Foot", roleSide)] = new[] { $"foot_{s}" };
            aliases[Role("Toe", roleSide)] = new[] { $"ball_{s}" };

            foreach (var (role, ue) in new[]
            {
                ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Pinky", "pinky"),
            })
            {
                // UE5 mannequin adds metacarpals for the four fingers (not the thumb).
                if (role != "Thumb")
                    aliases[Role($"{role}Meta", roleSide)] = new[] { $"{ue}_metacarpal_{s}" };
                aliases[Role($"{role}Prox", roleSide)] = new[] { $"{ue}_01_{s}" };
                aliases[Role($"{role}Mid", roleSide)] = new[] { $"{ue}_02_{s}" };
                aliases[Role($"{role}Dist", roleSide)] = new[] { $"{ue}_03_{s}" };
            }
        }
        return new Profile("ue_mannequin", new string[0], aliases);
    }

    // ---------------------------------------------------------------- rokoko / xsens bvh

    private static Profile BuildRokokoBvh()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Hips" },
            // Spine naming varies (Spine, Spine1..Spine4); ordered alias preference plus the
            // used-bone exclusion in the detector shifts the chain up when "Spine" is absent.
            [BoneRole.Spine0] = new[] { "Spine", "Spine1" },
            [BoneRole.Spine1] = new[] { "Spine1", "Spine2" },
            [BoneRole.Spine2] = new[] { "Spine2", "Spine3" },
            [BoneRole.Spine3] = new[] { "Spine3", "Spine4" },
            [BoneRole.Spine4] = new[] { "Spine4" },
            [BoneRole.Neck] = new[] { "Neck", "Neck1" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var (roleSide, nameSide) in Sides())
        {
            aliases[Role("Clavicle", roleSide)] = new[] { $"{nameSide}Shoulder", $"{nameSide}Collar" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{nameSide}Arm", $"{nameSide}UpperArm" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{nameSide}ForeArm", $"{nameSide}LowerArm" };
            aliases[Role("Hand", roleSide)] = new[] { $"{nameSide}Hand" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{nameSide}UpLeg", $"{nameSide}Thigh", $"{nameSide}UpperLeg" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{nameSide}Leg", $"{nameSide}Shin", $"{nameSide}LowerLeg" };
            aliases[Role("Foot", roleSide)] = new[] { $"{nameSide}Foot" };
            aliases[Role("Toe", roleSide)] = new[] { $"{nameSide}Toe", $"{nameSide}ToeBase" };
        }
        return new Profile("rokoko_bvh", new string[0], aliases);
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<(string RoleSide, string NameSide)> Sides()
    {
        yield return ("L", "Left");
        yield return ("R", "Right");
    }

    private static BoneRole Role(string baseName, string side)
        => System.Enum.Parse<BoneRole>(baseName + side);
}
