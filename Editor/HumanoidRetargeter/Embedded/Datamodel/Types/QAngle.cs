#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Numerics;

namespace HumanoidRetargeterDmx;

public record struct QAngle(float Pitch, float Yaw, float Roll)
{
    public static implicit operator global::System.Numerics.Vector3(QAngle q) => new(q.Pitch, q.Yaw, q.Roll);
    public static implicit operator QAngle(global::System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
