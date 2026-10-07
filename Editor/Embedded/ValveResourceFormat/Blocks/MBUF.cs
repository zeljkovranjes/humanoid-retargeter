#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Blocks;

    /// <summary>
    /// "MBUF" block.
    /// </summary>
    public class MBUF : VBIB
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.MBUF;
    }
