#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.IO;
using System.Text;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Serialization.KeyValues;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Blocks.ResourceEditInfoStructs;

    /// <summary>
    /// Represents an additional related file dependency.
    /// </summary>
    public class AdditionalRelatedFile
    {
        /// <summary>
        /// Gets or sets the content-relative filename.
        /// </summary>
        public string ContentRelativeFilename { get; set; }

        /// <summary>
        /// Gets or sets the content search path.
        /// </summary>
        public string ContentSearchPath { get; set; }

        /// <summary>
        /// Gets or sets whether this is a game file.
        /// </summary>
        public bool IsGameFile { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdditionalRelatedFile"/> class from a binary reader.
        /// </summary>
        public AdditionalRelatedFile(BinaryReader reader)
        {
            ContentRelativeFilename = reader.ReadOffsetString(Encoding.UTF8);
            ContentSearchPath = reader.ReadOffsetString(Encoding.UTF8);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdditionalRelatedFile"/> class from a KV object.
        /// </summary>
        public AdditionalRelatedFile(KVObject data)
        {
            ContentRelativeFilename = data.GetProperty<string>("m_RelativeFilename");
            ContentSearchPath = data.GetProperty<string>("m_SearchPath");
            IsGameFile = data.GetProperty<bool>("m_bIsGameFile");
        }
    }
