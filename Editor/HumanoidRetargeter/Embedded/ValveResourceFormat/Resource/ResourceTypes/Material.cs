#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterVrf.Utils;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HumanoidRetargeterVrf.Serialization.KeyValues;


#nullable disable

namespace HumanoidRetargeterVrf.ResourceTypes
{
    /// <summary>
    /// Represents a material resource containing shader parameters and texture references.
    /// </summary>
    public class Material : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets or sets the material name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the shader name used by this material.
        /// </summary>
        public string ShaderName { get; set; } = string.Empty;

        /// <summary>
        /// Gets the integer shader parameters.
        /// </summary>
        public Dictionary<string, long> IntParams { get; } = [];

        /// <summary>
        /// Gets the floating-point shader parameters.
        /// </summary>
        public Dictionary<string, float> FloatParams { get; } = [];

        /// <summary>
        /// Gets the vector shader parameters.
        /// </summary>
        public Dictionary<string, global::System.Numerics.Vector4> VectorParams { get; } = [];

        /// <summary>
        /// Gets the texture shader parameters.
        /// </summary>
        public Dictionary<string, string> TextureParams { get; } = [];

        /// <summary>
        /// Gets the integer material attributes.
        /// </summary>
        public Dictionary<string, long> IntAttributes { get; } = [];

        /// <summary>
        /// Gets the floating-point material attributes.
        /// </summary>
        public Dictionary<string, float> FloatAttributes { get; } = [];

        /// <summary>
        /// Gets the vector material attributes.
        /// </summary>
        public Dictionary<string, global::System.Numerics.Vector4> VectorAttributes { get; } = [];

        /// <summary>
        /// Gets the string material attributes.
        /// </summary>
        public Dictionary<string, string> StringAttributes { get; } = [];

        /// <summary>
        /// Gets the evaluated dynamic expressions for dynamic scalar and texture parameters.
        /// </summary>
        public Dictionary<string, string> DynamicExpressions { get; } = [];

        private VsInputSignature? inputSignature;

        /// <summary>
        /// Gets the vertex shader input signature defining vertex attributes.
        /// </summary>
        public VsInputSignature InputSignature
        {
            get
            {
                if (!inputSignature.HasValue)
                {
                    var inputSignatureObject = GetInputSignatureObject();
                    inputSignature = inputSignatureObject != null ? new(inputSignatureObject) : VsInputSignature.Empty;
                }

                return inputSignature.Value;
            }
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader) => base.Read(reader);
        public Dictionary<string, byte> GetShaderArguments()
        {
            var arguments = new Dictionary<string, byte>();

            foreach (var (name, value) in IntParams)
            {
                if (name.StartsWith("F_", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.Add(name, (byte)value);
                }
            }

            return arguments;
        }

        private KVObject GetInputSignatureObject()
        {
            if (Resource is null)
            {
                return null;
            }

            if (Resource.ContainsBlockType(BlockType.INSG))
            {
                return ((BinaryKV3)Resource.GetBlockByType(BlockType.INSG)).Data;
            }

            // Material might not have REDI, or it might have RED2 without INSG
            if (Resource.EditInfo != null && Resource.EditInfo.Type != BlockType.REDI)
            {
                return null;
            }

            if (Resource.EditInfo?.SearchableUserData.FirstOrDefault(x => x.Key == "VSInputSignature").Value is not string inputSignatureString)
            {
                return null;
            }

            if (!inputSignatureString.StartsWith("<!-- kv3", StringComparison.InvariantCulture))
            {
                return null;
            }

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(inputSignatureString));

            return KeyValues3.ParseKVFile(ms).Root;
        }

        /// <summary>
        /// Represents the vertex shader input signature containing vertex attribute elements.
        /// </summary>
        public readonly struct VsInputSignature
        {
            /// <summary>
            /// An empty input signature with no elements.
            /// </summary>
            public static readonly VsInputSignature Empty = new();

            /// <summary>
            /// Gets the array of input signature elements.
            /// </summary>
            public InputSignatureElement[] Elements { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="VsInputSignature"/> struct with no elements.
            /// </summary>
            public VsInputSignature()
            {
                Elements = [];
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="VsInputSignature"/> struct from data.
            /// </summary>
            /// <param name="data">The key-value data containing element definitions.</param>
            public VsInputSignature(KVObject data)
            {
                Elements = [.. data.GetArray("m_elems").Select(x => new InputSignatureElement(x))];
            }
        }

        /// <summary>
        /// Represents a single element in the vertex shader input signature.
        /// </summary>
        [DebuggerDisplay("{Name,nq} ({Semantic,nq})")]
        public readonly struct InputSignatureElement
        {
            /// <summary>
            /// Gets the element name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the semantic name.
            /// </summary>
            public string Semantic { get; }

            /// <summary>
            /// Gets the Direct3D semantic name.
            /// </summary>
            public string D3DSemanticName { get; }

            /// <summary>
            /// Gets the Direct3D semantic index.
            /// </summary>
            public int D3DSemanticIndex { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="InputSignatureElement"/> struct from data.
            /// </summary>
            /// <param name="data">The key-value data containing element definition.</param>
            public InputSignatureElement(KVObject data)
            {
                Name = data.GetProperty<string>("m_pName");
                Semantic = data.GetProperty<string>("m_pSemantic");
                D3DSemanticName = data.GetProperty<string>("m_pD3DSemanticName");
                D3DSemanticIndex = (int)data.GetIntegerProperty("m_nD3DSemanticIndex");
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="InputSignatureElement"/> struct with specified values.
            /// </summary>
            /// <param name="name">The element name.</param>
            /// <param name="semantic">The semantic name.</param>
            /// <param name="d3dSemanticName">The Direct3D semantic name.</param>
            /// <param name="d3dSemanticIndex">The Direct3D semantic index.</param>
            public InputSignatureElement(string name, string semantic, string d3dSemanticName, int d3dSemanticIndex)
            {
                Name = name;
                Semantic = semantic;
                D3DSemanticName = d3dSemanticName;
                D3DSemanticIndex = d3dSemanticIndex;
            }
        }

        /// <summary>
        /// Finds an input signature element by Direct3D semantic name and index.
        /// </summary>
        /// <param name="insg">The input signature to search.</param>
        /// <param name="d3dName">The Direct3D semantic name.</param>
        /// <param name="d3dIndex">The Direct3D semantic index.</param>
        /// <returns>The matching element, or default if not found.</returns>
        public static InputSignatureElement FindD3DInputSignatureElement(VsInputSignature insg, string d3dName, int d3dIndex)
        {
            foreach (var element in insg.Elements)
            {
                if (element.D3DSemanticName == d3dName && element.D3DSemanticIndex == d3dIndex)
                {
                    return element;
                }
            }

            return default;
        }
    }
}
