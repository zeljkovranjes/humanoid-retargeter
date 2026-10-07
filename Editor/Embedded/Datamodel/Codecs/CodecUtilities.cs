#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.Codecs;

    /// <summary>
    /// Helper methods for <see cref="ICodec"/> implementers.
    /// </summary>
    public static class CodecUtilities
    {
        /// <summary>
        /// Standard DMX header with CLR-style variable tokens.
        /// </summary>
        public const string HeaderPattern = "<!-- dmx encoding {0} {1} format {2} {3} -->";
        /// <summary>
        /// Standard DMX header as a regular expression pattern.
        /// </summary>
        public const string HeaderPattern_Regex = "<!-- dmx encoding (\\S+) ([0-9]+) format (\\S+) ([0-9]+) -->";
        //public const string HeaderPattern_Proto2 = "<!-- DMXVersion binary_v{0} -->";

        /// <summary>
        /// Creates a <see cref="List&lt;T&gt;"/> for the given Type with the given starting size.
        /// </summary>
        public static System.Collections.IList MakeList(Type t, int count)
        {
            if (t == typeof(Element))
                return new ElementArray(count);
            if (t == typeof(int))
                return new IntArray(count);
            if (t == typeof(float))
                return new FloatArray(count);
            if (t == typeof(bool))
                return new BoolArray(count);
            if (t == typeof(string))
                return new StringArray(count);
            if (t == typeof(byte[]))
                return new BinaryArray(count);
            if (t == typeof(TimeSpan))
                return new TimeSpanArray(count);
            if (t == typeof(Color))
                return new ColorArray(count);
            if (t == typeof(global::System.Numerics.Vector2))
                return new Vector2Array(count);
            if (t == typeof(global::System.Numerics.Vector3))
                return new Vector3Array(count);
            if (t == typeof(global::System.Numerics.Vector4))
                return new Vector4Array(count);
            if (t == typeof(global::System.Numerics.Quaternion))
                return new QuaternionArray(count);
            if (t == typeof(global::System.Numerics.Matrix4x4))
                return new MatrixArray(count);
            if (t == typeof(byte))
                return new ByteArray(count);
            if (t == typeof(ulong))
                return new UInt64Array(count);

            throw new ArgumentException($"Unhandled or invalid type: {t}");
        }

        /// <summary>
        /// Creates a <see cref="List&lt;T&gt;"/> for the given Type, copying items the given IEnumerable
        /// </summary>
        public static System.Collections.IList MakeList(Type t, System.Collections.IEnumerable source)
        {
            if (t == typeof(Element))
                return new ElementArray(source.Cast<Element>());
            if (t == typeof(int))
                return new IntArray(source.Cast<int>());
            if (t == typeof(float))
                return new FloatArray(source.Cast<float>());
            if (t == typeof(bool))
                return new BoolArray(source.Cast<bool>());
            if (t == typeof(string))
                return new StringArray(source.Cast<string>());
            if (t == typeof(byte[]))
                return new BinaryArray(source.Cast<byte[]>());
            if (t == typeof(TimeSpan))
                return new TimeSpanArray(source.Cast<TimeSpan>());
            if (t == typeof(Color))
                return new ColorArray(source.Cast<Color>());
            if (t == typeof(global::System.Numerics.Vector2))
                return new Vector2Array(source.Cast<global::System.Numerics.Vector2>());
            if (t == typeof(global::System.Numerics.Vector3))
                return new Vector3Array(source.Cast<global::System.Numerics.Vector3>());
            if (t == typeof(global::System.Numerics.Vector4))
                return new Vector4Array(source.Cast<global::System.Numerics.Vector4>());
            if (t == typeof(global::System.Numerics.Quaternion))
                return new QuaternionArray(source.Cast<global::System.Numerics.Quaternion>());
            if (t == typeof(global::System.Numerics.Matrix4x4))
                return new MatrixArray(source.Cast<global::System.Numerics.Matrix4x4>());
            if (t == typeof(byte))
                return new ByteArray(source.Cast<byte>());
            if (t == typeof(ulong))
                return new UInt64Array(source.Cast<ulong>());

            throw new ArgumentException("Unrecognised Type.");
        }

        /// <summary>
        /// Creates a new attribute on an <see cref="Element"/>. This method is intended for <see cref="ICodec"/> implementers and should not be directly called from any other code.
        /// </summary>
        /// <param name="elem">The Element to add to.</param>
        /// <param name="key">The name of the attribute. Must be unique on the Element.</param>
        /// <param name="defer_offset">The location in the encoded DMX stream at which this Attribute's value can be found.</param>
        public static void AddDeferredAttribute(Element elem, string key, long offset)
        {
            if (offset <= 0) throw new ArgumentOutOfRangeException(nameof(offset), "Address must be greater than 0.");
            elem.Add(key, offset);
        }

        public static Dictionary<string, Type> GetReflectionTypes(ReflectionParams reflectionParams)
        {
            Dictionary<string, Type> types = [];

            if (reflectionParams.AttemptReflection)
            {
                foreach (var assembly in reflectionParams.AssembliesToSearch)
                {
                    foreach (var classType in assembly.DefinedTypes)
                    {
                        if (classType.IsSubclassOf(typeof(Element)))
                        {
                            types.TryAdd(classType.Name, classType);
                        }
                    }
                }

                foreach (var type in reflectionParams.AdditionalTypes)
                {
                    if (type.IsSubclassOf(typeof(Element)))
                    {
                        types.TryAdd(type.Name, type);
                    }
                }
            }

            return types;
        }

        public static bool TryConstructCustomElement(Dictionary<string, Type> types, Datamodel dataModel, string elem_class, string elem_name, Guid elem_id, out Element? elem)
        {
            var matchedType = types.TryGetValue(elem_class, out var classType);

            if (!matchedType || classType is null)
            {
                elem = null;
                return false;
            }

            Type derivedType = classType;

            ConstructorInfo? elementConstructor = typeof(Element).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(Datamodel), typeof(string), typeof(Guid), typeof(string)],
                null
            );

            var customClassInitializer = derivedType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [],
                null
            );

            if (elementConstructor == null)
            {
                throw new InvalidOperationException("Failed to get constructor while attemption reflection based deserialisation");
            }

            if (customClassInitializer == null)
            {
                throw new InvalidOperationException("Failed to get custom element constructor.");
            }

            object uninitializedObject = RuntimeHelpers.GetUninitializedObject(derivedType);

            // this will initialize values such as
            // public Datamodel.ElementArray Children { get; } = [];
            customClassInitializer.Invoke(uninitializedObject, []);

            elementConstructor.Invoke(uninitializedObject, [dataModel, elem_name, elem_id, elem_class]);

            elem = (Element?)uninitializedObject;
            return true;
        }
    }
