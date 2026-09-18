#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using HumanoidRetargeterVrf.Serialization.KeyValues;

namespace HumanoidRetargeter.Editor;

internal static class SmartPortIkTargets
{
    internal static IReadOnlyDictionary<string, string> Read(string compiledModel, string graph)
    {
        using var resource = new Resource();
        resource.Read(compiledModel);
        var model = (Model)resource.DataBlock!;
        var goals = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!model.KeyValues.ContainsKey("ikdata")) return goals;
        var bones = model.Skeleton.Bones.ToDictionary(b => b.Name, b => b.Name, StringComparer.OrdinalIgnoreCase);
        var ends = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string goal, string end)
        {
            if (!bones.TryGetValue(goal, out var g) || !bones.TryGetValue(end, out var e)) return;
            if (goals.TryGetValue(g, out var previous) && previous != e)
                throw new InvalidOperationException("Conflicting IK effectors for goal: " + g);
            goals[g] = e;
        }
        foreach (var chain in model.KeyValues.GetSubCollection("ikdata").GetArray("m_IKChains"))
        {
            var joints = chain.GetArray("m_Joints");
            if (joints.Length == 0) continue;
            var end = joints[^1].GetSubCollection("m_Bone").GetStringProperty("m_Name");
            ends[chain.GetStringProperty("m_Name")] = end;
            var target = chain.GetSubCollection("m_DefaultTargetSettings");
            if (target.GetStringProperty("m_TargetSource") == "Bone")
                Add(target.GetSubCollection("m_Bone").GetStringProperty("m_Name"), end);
        }
        Visit(Kv3.Parse(graph).Root);
        return goals;

        void Visit(KvValue value)
        {
            if (value is KvObject node)
            {
                if (node.GetString("m_targetType") == "IkTarget_Bone"
                    && node.GetString("m_endEffectorType") == "IkEndEffector_Bone"
                    && ends.TryGetValue(node.GetString("m_ikChainName") ?? "", out var end))
                    Add(node.GetString("m_targetBoneName") ?? "", end);
                foreach (var key in node.Keys) Visit(node[key]);
            }
            else if (value is KvArray array)
                foreach (var item in array.Items) Visit(item);
        }
    }
}
