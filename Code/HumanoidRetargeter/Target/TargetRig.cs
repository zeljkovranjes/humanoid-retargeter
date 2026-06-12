using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Target;

/// <summary>
/// The s&amp;box humanoid target rig: skeleton plus per-bone <see cref="BoneClass"/> and
/// (for animated bones) canonical <see cref="BoneRole"/> annotations. Loaded from the
/// committed <c>Assets/humanoid_retargeter/target_rig_sbox.json</c> produced by
/// <see cref="TargetRigGenerator"/>. This type does no file IO — callers pass JSON text.
/// </summary>
public sealed class TargetRig
{
    private readonly BoneClass[] _classes;
    private readonly BoneRole?[] _roles;
    private readonly Dictionary<BoneRole, int> _boneByRole;

    private TargetRig(string name, SkeletonModel skeleton, BoneClass[] classes, BoneRole?[] roles,
        Dictionary<BoneRole, int> boneByRole)
    {
        Name = name;
        Skeleton = skeleton;
        _classes = classes;
        _roles = roles;
        _boneByRole = boneByRole;
    }

    /// <summary>Rig name (e.g. <c>sbox_human_male</c>).</summary>
    public string Name { get; }

    /// <summary>The target skeleton (rest pose in centimeters, parents before children).</summary>
    public SkeletonModel Skeleton { get; }

    /// <summary>The class of the bone at <paramref name="boneIndex"/>.</summary>
    public BoneClass ClassOf(int boneIndex) => _classes[boneIndex];

    /// <summary>The canonical role of the bone at <paramref name="boneIndex"/>, or null
    /// for role-less (non-animated) bones.</summary>
    public BoneRole? RoleOf(int boneIndex) => _roles[boneIndex];

    /// <summary>The bone index carrying <paramref name="role"/>, or null when the rig has no
    /// bone for it (e.g. <see cref="BoneRole.Spine3"/>, <see cref="BoneRole.ThumbMetaL"/>).</summary>
    public int? BoneForRole(BoneRole role) => _boneByRole.TryGetValue(role, out var index) ? index : null;

    /// <summary>Indices of all bones of the given class, in skeleton order.</summary>
    public IEnumerable<int> BonesOfClass(BoneClass boneClass)
    {
        for (var i = 0; i < _classes.Length; i++)
        {
            if (_classes[i] == boneClass)
                yield return i;
        }
    }

    /// <summary>
    /// Parses a target-rig definition produced by <see cref="TargetRigGenerator"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the JSON does not match the expected
    /// schema or violates invariants (e.g. a role on a non-animated bone, duplicate roles).</exception>
    public static TargetRig Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = root.GetProperty("name").GetString()
            ?? throw new ArgumentException("Target rig JSON has no 'name'.");

        var bonesJson = root.GetProperty("bones");
        var count = bonesJson.GetArrayLength();
        var definitions = new List<BoneDefinition>(count);
        var classByName = new Dictionary<string, BoneClass>(count, StringComparer.Ordinal);
        var roleByName = new Dictionary<string, BoneRole>(count, StringComparer.Ordinal);

        foreach (var bone in bonesJson.EnumerateArray())
        {
            var boneName = bone.GetProperty("name").GetString()
                ?? throw new ArgumentException("Target rig bone with null name.");
            var parentProp = bone.GetProperty("parent");
            var parent = parentProp.ValueKind == JsonValueKind.Null ? null : parentProp.GetString();

            definitions.Add(new BoneDefinition(boneName, parent, new XForm(
                ReadVector3(bone.GetProperty("local_pos")),
                MathQ.Normalize(ReadQuaternion(bone.GetProperty("local_rot_xyzw"))))));

            var boneClass = Enum.Parse<BoneClass>(bone.GetProperty("class").GetString()!);
            classByName[boneName] = boneClass;

            if (bone.TryGetProperty("role", out var roleProp))
            {
                if (boneClass != BoneClass.Animated)
                    throw new ArgumentException(
                        $"Bone '{boneName}' is {boneClass} but carries a role — only Animated bones may have roles.");
                roleByName[boneName] = Enum.Parse<BoneRole>(roleProp.GetString()!);
            }
        }

        var skeleton = SkeletonModel.Create(definitions);

        var classes = new BoneClass[skeleton.Count];
        var roles = new BoneRole?[skeleton.Count];
        var boneByRole = new Dictionary<BoneRole, int>();
        for (var i = 0; i < skeleton.Count; i++)
        {
            var boneName = skeleton[i].Name;
            classes[i] = classByName[boneName];
            if (roleByName.TryGetValue(boneName, out var role))
            {
                roles[i] = role;
                if (!boneByRole.TryAdd(role, i))
                    throw new ArgumentException($"Role {role} is assigned to more than one bone.");
            }
        }

        return new TargetRig(name, skeleton, classes, roles, boneByRole);
    }

    private static Vector3 ReadVector3(JsonElement e)
        => new((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());

    private static Quaternion ReadQuaternion(JsonElement e)
        => new((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble(), (float)e[3].GetDouble());
}
