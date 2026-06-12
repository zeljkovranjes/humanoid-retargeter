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

    /// <summary>
    /// SMPL body model family (AMASS exports, Meshcapade FBX rigs). Joint names per the
    /// published model (vchoutas/smplx <c>joint_names.py</c>, Meshcapade wiki):
    /// <c>pelvis</c>, sided <c>hip→knee→ankle→foot</c> legs (the "hip" joint IS the thigh;
    /// "ankle" is the foot, "foot" is the toe region) and <c>collar→shoulder→elbow→wrist</c>
    /// arms ("shoulder" is the upper arm, "wrist" is the hand; the <c>hand</c> joint is a
    /// finger stub and stays unmapped). Both spellings occur in the wild: <c>left_hip</c>
    /// (model joints) and <c>L_Hip</c> with gendered FBX prefixes <c>m_avg_</c>/<c>f_avg_</c>
    /// (SMPL Unity/FBX rigs). No fingers — that is SMPL-X (<see cref="SmplX"/>), kept as a
    /// separate preset so a finger-less SMPL rig still reaches full optional coverage.
    /// </summary>
    public static Profile Smpl { get; } = BuildSmpl(withFingers: false);

    /// <summary>
    /// SMPL-X: the SMPL body joints (<see cref="Smpl"/>) plus articulated hands —
    /// <c>left_thumb1..3</c>/<c>left_index1..3</c>-style finger joints per
    /// vchoutas/smplx <c>joint_names.py</c> (jaw/eye joints carry no humanoid role).
    /// Evaluated before <see cref="Smpl"/> so it wins the tie on SMPL-X rigs (both score
    /// the body fully; only this one maps the fingers).
    /// </summary>
    public static Profile SmplX { get; } = BuildSmpl(withFingers: true);

    /// <summary>
    /// NVIDIA SOMA uniform-proportion skeleton (SOMA/SEED BVH exports, e.g.
    /// github.com/NVIDIA/soma-retargeter <c>assets/motions/bvh</c>). Mixamo-identical
    /// upper-body and finger names, but: spine is <c>Spine1→Spine2→Chest</c> (no plain
    /// "Spine"), neck is <c>Neck1→Neck2</c>, and the legs are <c>LeftLeg→LeftShin</c> —
    /// SOMA's <c>LeftLeg</c> is the THIGH (mixamo's is the calf), which is exactly why the
    /// mixamo preset must never claim these rigs.
    /// </summary>
    public static Profile SomaBvh { get; } = BuildSomaBvh();

    /// <summary>
    /// Classic BVH / Character-Studio-friendly naming (MotionBuilder "Export BVH to
    /// Character Studio" convention, ACCAD-style mocap BVHs): <c>Hips</c>,
    /// <c>Chest[2..4]</c> spine, arms <c>Collar→Shoulder→Elbow→Wrist</c> (the "Shoulder"
    /// is the upper arm) and legs <c>Hip→Knee→Ankle→Toe</c> (the sided "Hip" is the
    /// thigh). No fingers.
    /// </summary>
    public static Profile ClassicBvh { get; } = BuildClassicBvh();

    /// <summary>All built-in presets, in detection order (first wins score ties — see
    /// <see cref="SmplX"/> vs <see cref="Smpl"/>).</summary>
    public static IReadOnlyList<Profile> All { get; } =
        new[] { Mixamo, ActorCoreCc, UeMannequin, RokokoBvh, SmplX, Smpl, SomaBvh, ClassicBvh };

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

    // ---------------------------------------------------------------- smpl / smpl-x

    private static Profile BuildSmpl(bool withFingers)
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Pelvis" },
            [BoneRole.Spine0] = new[] { "Spine1" },
            [BoneRole.Spine1] = new[] { "Spine2" },
            [BoneRole.Spine2] = new[] { "Spine3" },
            [BoneRole.Neck] = new[] { "Neck" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var (roleSide, abbr, word) in new[] { ("L", "L", "left"), ("R", "R", "right") })
        {
            // Both documented spellings per role: abbreviated FBX-rig names ("L_Hip") and
            // spelled model joint names ("left_hip"). Comparison is separator-insensitive.
            aliases[Role("Clavicle", roleSide)] = new[] { $"{abbr}_Collar", $"{word}_collar" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{abbr}_Shoulder", $"{word}_shoulder" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{abbr}_Elbow", $"{word}_elbow" };
            aliases[Role("Hand", roleSide)] = new[] { $"{abbr}_Wrist", $"{word}_wrist" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{abbr}_Hip", $"{word}_hip" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{abbr}_Knee", $"{word}_knee" };
            aliases[Role("Foot", roleSide)] = new[] { $"{abbr}_Ankle", $"{word}_ankle" };
            aliases[Role("Toe", roleSide)] = new[] { $"{abbr}_Foot", $"{word}_foot" };

            if (!withFingers)
                continue;

            // SMPL-X finger joints (left_index1..3 etc., per vchoutas/smplx joint_names.py).
            foreach (var finger in new[] { "thumb", "index", "middle", "ring", "pinky" })
            {
                var name = char.ToUpperInvariant(finger[0]) + finger[1..];
                aliases[Role($"{name}Prox", roleSide)] = new[] { $"{word}_{finger}1" };
                aliases[Role($"{name}Mid", roleSide)] = new[] { $"{word}_{finger}2" };
                aliases[Role($"{name}Dist", roleSide)] = new[] { $"{word}_{finger}3" };
            }
        }
        // Gendered SMPL FBX rigs prefix every bone (m_avg_L_Hip, f_avg_Pelvis).
        return new Profile(withFingers ? "smpl_x" : "smpl", new[] { "^m_avg_", "^f_avg_" }, aliases);
    }

    // ---------------------------------------------------------------- nvidia soma bvh

    private static Profile BuildSomaBvh()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Hips" },
            [BoneRole.Spine0] = new[] { "Spine1" },
            [BoneRole.Spine1] = new[] { "Spine2" },
            [BoneRole.Spine2] = new[] { "Chest" },
            [BoneRole.Neck] = new[] { "Neck1" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var (roleSide, nameSide) in Sides())
        {
            aliases[Role("Clavicle", roleSide)] = new[] { $"{nameSide}Shoulder" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{nameSide}Arm" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{nameSide}ForeArm" };
            aliases[Role("Hand", roleSide)] = new[] { $"{nameSide}Hand" };
            // SOMA's "Leg" is the thigh, "Shin" the calf — the decisive difference from
            // mixamo, where "Leg" is the calf under "UpLeg".
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{nameSide}Leg" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{nameSide}Shin" };
            aliases[Role("Foot", roleSide)] = new[] { $"{nameSide}Foot" };
            aliases[Role("Toe", roleSide)] = new[] { $"{nameSide}ToeBase" };

            // Mixamo-style finger names; segment 4 ("LeftHandIndex4") and the *End markers
            // carry no role.
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
            {
                aliases[Role($"{finger}Prox", roleSide)] = new[] { $"{nameSide}Hand{finger}1" };
                aliases[Role($"{finger}Mid", roleSide)] = new[] { $"{nameSide}Hand{finger}2" };
                aliases[Role($"{finger}Dist", roleSide)] = new[] { $"{nameSide}Hand{finger}3" };
            }
        }
        return new Profile("soma_bvh", new string[0], aliases);
    }

    // ---------------------------------------------------------------- classic bvh

    private static Profile BuildClassicBvh()
    {
        var aliases = new Dictionary<BoneRole, string[]>
        {
            [BoneRole.Hips] = new[] { "Hips" },
            [BoneRole.Spine0] = new[] { "Chest" },
            [BoneRole.Spine1] = new[] { "Chest2" },
            [BoneRole.Spine2] = new[] { "Chest3" },
            [BoneRole.Spine3] = new[] { "Chest4" },
            [BoneRole.Neck] = new[] { "Neck" },
            [BoneRole.Head] = new[] { "Head" },
        };
        foreach (var (roleSide, nameSide) in Sides())
        {
            aliases[Role("Clavicle", roleSide)] = new[] { $"{nameSide}Collar" };
            aliases[Role("UpperArm", roleSide)] = new[] { $"{nameSide}Shoulder" };
            aliases[Role("LowerArm", roleSide)] = new[] { $"{nameSide}Elbow" };
            aliases[Role("Hand", roleSide)] = new[] { $"{nameSide}Wrist" };
            aliases[Role("UpperLeg", roleSide)] = new[] { $"{nameSide}Hip" };
            aliases[Role("LowerLeg", roleSide)] = new[] { $"{nameSide}Knee" };
            aliases[Role("Foot", roleSide)] = new[] { $"{nameSide}Ankle" };
            aliases[Role("Toe", roleSide)] = new[] { $"{nameSide}Toe" };
        }
        return new Profile("classic_bvh", new string[0], aliases);
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
