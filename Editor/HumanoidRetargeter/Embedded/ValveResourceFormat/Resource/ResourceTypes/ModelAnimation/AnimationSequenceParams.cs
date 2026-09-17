#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using HumanoidRetargeterVrf.Serialization.KeyValues;

namespace HumanoidRetargeterVrf.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Contains sequence parameters for animation transitions.
    /// </summary>
    public readonly struct AnimationSequenceParams
    {
        /// <summary>
        /// Gets or sets the fade-in time in seconds.
        /// </summary>
        public float FadeInTime { get; init; }

        /// <summary>
        /// Gets or sets the fade-out time in seconds.
        /// </summary>
        public float FadeOutTime { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationSequenceParams"/> struct.
        /// </summary>
        public AnimationSequenceParams(KVObject data)
        {
            FadeInTime = data.GetFloatProperty("m_flFadeInTime");
            FadeOutTime = data.GetFloatProperty("m_flFadeOutTime");
        }
    }
}
