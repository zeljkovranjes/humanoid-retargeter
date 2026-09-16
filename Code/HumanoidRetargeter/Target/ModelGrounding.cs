#nullable enable annotations
using System;
using System.Linq;
using HumanoidRetargeter.Maths;
using NVector3 = System.Numerics.Vector3;

namespace HumanoidRetargeter.Target;

/// <summary>A final model-space translation, shared by the mesh, bind pose and every animation.</summary>
public static class ModelGrounding
{
    public const string ModifierName = "HumanoidRetargeter_Ground";

    public static string Apply(string vmdl, float minimumZ)
    {
        if (!float.IsFinite(minimumZ)) throw new ArgumentOutOfRangeException(nameof(minimumZ));
        // Do not move an already grounded model, or lower intentionally elevated geometry.
        if (minimumZ >= -0.05f) return vmdl;
        var doc = Kv3.Parse(vmdl);
        var children = (KvArray)((KvObject)((KvObject)doc.Root)["rootNode"])["children"];
        var list = children.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == "ModelModifierList");
        if (list is null)
        {
            list = new KvObject { ["_class"] = new KvString("ModelModifierList"), ["children"] = new KvArray() };
            children.Items.Add(list);
        }
        var modifiers = (KvArray)list["children"];
        var previous = modifiers.Items.OfType<KvObject>().SingleOrDefault(IsGrounding);
        var height = previous is null ? 0 : Height(previous);
        if (previous is not null) modifiers.Items.Remove(previous);
        var translation = new KvArray();
        translation.Items.Add(new KvDouble(0)); translation.Items.Add(new KvDouble(0));
        translation.Items.Add(new KvDouble(height - minimumZ));
        // After ScaleAndMirror: this offset is in final engine units, not source centimeters.
        modifiers.Items.Add(new KvObject { ["_class"] = new KvString("ModelModifier_Translate"),
            ["name"] = new KvString(ModifierName), ["translation"] = translation });
        return Kv3.Serialize(doc);
    }

    public static bool IsGrounding(KvObject node) => node.GetString("_class") == "ModelModifier_Translate"
        && node.GetString("name") == ModifierName;

    public static float Offset(string vmdl)
    {
        var children = (KvArray)((KvObject)((KvObject)Kv3.Parse(vmdl).Root)["rootNode"])["children"];
        var list = children.Items.OfType<KvObject>().SingleOrDefault(n => n.GetString("_class") == "ModelModifierList");
        var modifier = (list?.GetOrNull("children") as KvArray)?.Items.OfType<KvObject>().SingleOrDefault(IsGrounding);
        return modifier is null ? 0 : Height(modifier);
    }

    public static XForm SourceLocal(XForm compiled, bool isRoot, float offset)
        => isRoot ? new XForm(compiled.Pos - NVector3.UnitZ * offset, compiled.Rot) : compiled;

    private static float Height(KvObject node)
    {
        var vector = (KvArray)node["translation"];
        static double Number(KvValue v) => v is KvDouble d ? d.Value : ((KvLong)v).Value;
        if (vector.Items.Count != 3 || Number(vector.Items[0]) != 0 || Number(vector.Items[1]) != 0
            || !double.IsFinite(Number(vector.Items[2])))
            throw new InvalidOperationException("Invalid grounding translation.");
        var height = (float)Number(vector.Items[2]);
        if (!float.IsFinite(height)) throw new InvalidOperationException("Invalid grounding translation.");
        return height;
    }
}
