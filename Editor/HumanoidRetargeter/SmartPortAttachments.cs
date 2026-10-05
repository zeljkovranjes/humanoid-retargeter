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

    /// <summary>
    /// The attachments <see cref="Align"/> leaves as the source wrote them - those that ignore their bones' rotation
    /// and those blended from several bones - still have angles relative to the source's bone frames. An
    /// ignore_rotation attachment keeps its bones' bind orientation times its angles: the stock Citizen's
    /// forward_reference_modelspace (the frame its body aim turns the spine chain toward) then points elsewhere on a
    /// skeleton whose bones have other axes, and the aim bends the body over. This re-expresses the angles of the
    /// attachment and of each of its influences in the target's frames - against the target's bind pose when the
    /// rotation is ignored, against the transferred source rest pose (as <see cref="Align"/> does) otherwise - so
    /// every influence has the source's orientation. Positions are kept. Used by the headless Smart Port.
    /// </summary>
    internal static string AlignInfluencesAndFixedAxes(string targetText, string sourceText, SmartPortRig rig)
    {
        var target = Kv3.Parse(targetText);
        var source = AttachmentNodes(Kv3.Parse(sourceText)).ToDictionary(n => n.GetString("name")!, StringComparer.OrdinalIgnoreCase);
        var pose = rig.Transfer(rig.Source.Bones.Select(b => b.RestLocal).ToArray());
        var transferred = new XForm[rig.Target.Count];
        foreach (var bone in rig.Target.Bones)
            transferred[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(transferred[bone.ParentIndex], pose[bone.Index]);
        foreach (var attachment in AttachmentNodes(target))
        {
            if (!source.TryGetValue(attachment.GetString("name") ?? "", out var original)) continue;
            var ignore = attachment.GetOrNull("ignore_rotation") is KvBool { Value: true } || original.GetOrNull("ignore_rotation") is KvBool { Value: true };
            var influences = Influences(attachment);
            var originals = Influences(original);
            if (!ignore && influences.Length == 0 && originals.Length == 0) continue; // Align's
            if (influences.Length != originals.Length) continue;
            Rebase(attachment, original, ignore);
            for (var i = 0; i < influences.Length; i++) Rebase(influences[i], originals[i], ignore);
        }
        return Kv3.Serialize(target);

        void Rebase(KvObject node, KvObject original, bool ignore)
        {
            var sourceBone = rig.Source.Bones.FirstOrDefault(b => string.Equals(b.Name, original.GetString("parent_bone"), StringComparison.OrdinalIgnoreCase));
            if (sourceBone.Name is null || !rig.BoneNames.TryGetValue(sourceBone.Name, out var mapped)
                || !string.Equals(mapped, node.GetString("parent_bone"), StringComparison.OrdinalIgnoreCase)) return;
            var index = rig.Target.IndexOf(mapped);
            if (index < 0 || Angles(original) is not { } angles) return;
            var frame = ignore ? rig.Target.RestWorld[index].Rot : transferred[index].Rot;
            var rotation = Quaternion.Normalize(Quaternion.Conjugate(frame) * rig.Source.RestWorld[sourceBone.Index].Rot * angles);
            var corrected = ModelExtract.ToEulerAngles(rotation);
            var output = new KvArray();
            foreach (var value in new[] { corrected.X, corrected.Y, corrected.Z }) output.Items.Add(new KvDouble(value));
            node["relative_angles"] = output;
        }
    }

    static KvObject[] AttachmentNodes(Kv3Document doc)
    {
        var root = (KvObject)((KvObject)doc.Root)["rootNode"];
        var list = ((KvArray)root["children"]).Items.OfType<KvObject>()
            .SingleOrDefault(n => n.GetString("_class") == "AttachmentList");
        return (list?.GetOrNull("children") as KvArray)?.Items.OfType<KvObject>().ToArray() ?? Array.Empty<KvObject>();
    }

    static KvObject[] Influences(KvObject attachment) => (attachment.GetOrNull("children") as KvArray)?.Items.OfType<KvObject>()
        .Where(n => n.GetString("_class") == "AttachmentInfluence").ToArray() ?? Array.Empty<KvObject>();

    // relative_angles (pitch, yaw, roll in degrees) as the rotation Align reads them.
    static Quaternion? Angles(KvObject node)
    {
        if (node.GetOrNull("relative_angles") is not KvArray { Items.Count: 3 } angles) return null;
        static float Number(KvValue value) => value is KvDouble d ? (float)d.Value : value is KvLong l ? l.Value : 0;
        var radians = angles.Items.Select(Number).Select(v => v * MathF.PI / 180).ToArray();
        return Quaternion.CreateFromAxisAngle(NVector3.UnitZ, radians[1])
            * Quaternion.CreateFromAxisAngle(NVector3.UnitY, radians[0]) * Quaternion.CreateFromAxisAngle(NVector3.UnitX, radians[2]);
    }
}
