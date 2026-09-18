#nullable enable
using System;
using System.Linq;
using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf.IO;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using NVector3 = System.Numerics.Vector3;

namespace HumanoidRetargeter.Editor;

/// <summary>Keeps fitted socket positions, but adopts the source graph's attachment axes.</summary>
internal static class SmartPortAttachments
{
    internal static string[] BoneNames(string compiledModel)
    {
        using var resource = new Resource();
        resource.Read(compiledModel);
        var model = (Model)resource.DataBlock!;
        _ = model.GetEmbeddedMeshes().ToArray(); // Embedded meshes can own attachment metadata.
        var bones = model.Skeleton.Bones.ToDictionary(b => b.Name, b => b.Name, StringComparer.OrdinalIgnoreCase);
        return model.Attachments.Values.SelectMany(a => a).Select(a => a.Name)
            .Where(bones.ContainsKey).Select(n => bones[n]).Distinct().ToArray();
    }

    internal static string Align(string targetText, string sourceText, SmartPortRig rig)
    {
        var target = Kv3.Parse(targetText);
        static KvObject[] Attachments(Kv3Document doc)
        {
            var root = (KvObject)((KvObject)doc.Root)["rootNode"];
            var list = ((KvArray)root["children"]).Items.OfType<KvObject>()
                .SingleOrDefault(n => n.GetString("_class") == "AttachmentList");
            return (list?.GetOrNull("children") as KvArray)?.Items.OfType<KvObject>().ToArray() ?? Array.Empty<KvObject>();
        }
        var source = Attachments(Kv3.Parse(sourceText)).ToDictionary(n => n.GetString("name")!, StringComparer.OrdinalIgnoreCase);
        var pose = rig.Transfer(rig.Source.Bones.Select(b => b.RestLocal).ToArray());
        var world = new XForm[rig.Target.Count];
        foreach (var bone in rig.Target.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        foreach (var attachment in Attachments(target))
        {
            if (!source.TryGetValue(attachment.GetString("name") ?? "", out var original)
                || attachment.GetOrNull("ignore_rotation") is KvBool { Value: true }
                || original.GetOrNull("ignore_rotation") is KvBool { Value: true }
                || attachment.GetOrNull("children") is KvArray { Items.Count: > 0 }
                || original.GetOrNull("children") is KvArray { Items.Count: > 0 }) continue;
            var sourceBone = rig.Source.Bones.FirstOrDefault(b => string.Equals(b.Name, original.GetString("parent_bone"), StringComparison.OrdinalIgnoreCase));
            if (sourceBone.Name is null || !rig.BoneNames.TryGetValue(sourceBone.Name, out var mapped)
                || !string.Equals(mapped, attachment.GetString("parent_bone"), StringComparison.OrdinalIgnoreCase)) continue;
            var index = rig.Target.IndexOf(mapped);
            var angles = original.GetOrNull("relative_angles") as KvArray;
            if (angles is null || angles.Items.Count != 3) continue;
            static float Number(KvValue value) => value is KvDouble d ? (float)d.Value : value is KvLong l ? l.Value : 0;
            var radians = angles.Items.Select(Number).Select(v => v * MathF.PI / 180).ToArray();
            var rotation = Quaternion.CreateFromAxisAngle(NVector3.UnitZ, radians[1])
                * Quaternion.CreateFromAxisAngle(NVector3.UnitY, radians[0]) * Quaternion.CreateFromAxisAngle(NVector3.UnitX, radians[2]);
            rotation = Quaternion.Normalize(Quaternion.Conjugate(world[index].Rot) * rig.Source.RestWorld[sourceBone.Index].Rot * rotation);
            var corrected = ModelExtract.ToEulerAngles(rotation);
            var output = new KvArray();
            foreach (var value in new[] { corrected.X, corrected.Y, corrected.Z }) output.Items.Add(new KvDouble(value));
            attachment["relative_angles"] = output;
        }
        return Kv3.Serialize(target);
    }
}
