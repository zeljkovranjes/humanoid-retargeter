#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
namespace HumanoidRetargeterVrf.Blocks
{
    /// <summary>
    /// "MBUF" block.
    /// </summary>
    public class MBUF : VBIB
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.MBUF;
    }
}
