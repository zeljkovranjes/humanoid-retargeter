#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeterDmx.Codecs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security;
using System.Numerics;
using CodecRegistration = System.Tuple<string, int>;
using System.Reflection;

namespace HumanoidRetargeterDmx
{
    /// <summary>
    /// Represents a thread-safe tree of <see cref="Element"/>s.
    /// </summary>
    [DebuggerDisplay("Format = {Format,nq} {FormatVersion}, Encoding = {Encoding,nq} {EncodingVersion}")]
    [DebuggerTypeProxy(typeof(DebugView))]
    public partial class HumanoidRetargeterDmx : INotifyPropertyChanged, IDisposable, ISupportInitialize
    {
        internal class DebugView
        {
            public DebugView(HumanoidRetargeterDmx dm)
            {
                DM = dm;
            }

            readonly HumanoidRetargeterDmx DM;

            public Element? Root => DM.Root;
            public ElementList AllElements => DM.AllElements;
            public AttributeList PrefixAttributes => DM.PrefixAttributes;
        }

        #region Attribute types

        private static readonly Type[] attributeTypes = [
            typeof(Element),
            typeof(int),
            typeof(float),
            typeof(bool),
            typeof(string),
            typeof(byte[]),
            typeof(TimeSpan),
            typeof(Color),
            typeof(global::System.Numerics.Vector2),
            typeof(global::System.Numerics.Vector3),
            typeof(global::System.Numerics.Vector4),
            typeof(global::System.Numerics.Quaternion),
            typeof(global::System.Numerics.Matrix4x4),
            typeof(byte),
            typeof(ulong),
            typeof(QAngle),
        ];

        public static Type[] AttributeTypes => attributeTypes;

        /// <summary>
        /// Determines whether the given Type is valid as a HumanoidRetargeterDmx <see cref="Attribute"/>.
        /// </summary>
        /// <remarks><see cref="ICollection&lt;T&gt;"/> objects pass if their generic argument is valid.</remarks>
        /// <seealso cref="IsHumanoidRetargeterDmxArrayType"/>
        /// <param name="t">The Type to check.</param>
        public static bool IsHumanoidRetargeterDmxType(Type t)
        {
            return HumanoidRetargeterDmx.AttributeTypes.Contains(t) || IsHumanoidRetargeterDmxArrayType(t) || t.IsSubclassOf(typeof(Element));
        }

        /// <summary>
        /// Determines whether the given Type is valid as a HumanoidRetargeterDmx <see cref="Attribute"/> array.
        /// </summary>
        /// <seealso cref="IsHumanoidRetargeterDmxType"/>
        /// <seealso cref="GetArrayInnerType"/>
        /// <param name="t">The Type to check.</param>
        public static bool IsHumanoidRetargeterDmxArrayType(Type t)
        {
            var inner = GetArrayInnerType(t);
            return inner != null && HumanoidRetargeterDmx.AttributeTypes.Contains(inner);
        }

        /// <summary>
        /// Returns the inner Type of an object which implements IList&lt;T&gt;, or null if there is no inner Type.
        /// </summary>
        /// <param name="t">The Type to check.</param>
        public static Type? GetArrayInnerType(Type t)
        {
            if (t == typeof(Element))
            {
                return null;
            }

            var i_type = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>) ? t : t.GetInterface("IList`1");
            if (i_type == null)
            {
                return null;
            }

            var inner = i_type.GetGenericArguments()[0];
            return inner;
        }
        #endregion

        static HumanoidRetargeterDmx()
        {
            RegisterCodec(typeof(Binary));
            RegisterCodec(typeof(KeyValues2));
            TextEncoding = new System.Text.UTF8Encoding(false);
        }

        #region Codecs
        public static readonly Dictionary<CodecRegistration, Type> Codecs = [];

        public static IEnumerable<CodecRegistration> CodecsRegistered => Codecs.Keys.OrderBy(reg => string.Join(null, reg.Item1, reg.Item2)).ToArray();

        /// <summary>
        /// Registers a new <see cref="ICodec"/> with an encoding name and one or more encoding versions.
        /// </summary>
        /// <remarks>Existing codecs will be replaced.</remarks>
        /// <param name="type">The ICodec implementation being registered.</param>
        public static void RegisterCodec(Type type)
        {
            if (type.GetInterface(typeof(ICodec).FullName!) == null)
            {
                throw new CodecException($"{type.Name} does not implement HumanoidRetargeterDmx.Codecs.ICodec.");
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new CodecException($"{type.Name} does not have a default constructor.");
            }

            var format_attrs = (CodecFormatAttribute[])type.GetCustomAttributes(typeof(CodecFormatAttribute), true);
            if (format_attrs.Length == 0)
            {
                throw new CodecException($"{type.Name} does not provide HumanoidRetargeterDmx.Codecs.CodecFormatAttribute.");
            }

            foreach (var format_attr in format_attrs)
            {
                foreach (var version in format_attr.Versions)
                {
                    var reg = new CodecRegistration(format_attr.Name, version);
                    AddCodec(type, format_attr, reg);
                }
            }

            static void AddCodec(Type type, CodecFormatAttribute format_attr, CodecRegistration reg)
            {
                if (Codecs.ContainsKey(reg) && Codecs[reg] != type)
                {
                    Trace.TraceInformation("HumanoidRetargeterDmx.NET: Replacing existing codec for {0} {1} ({2}) with {3}", format_attr.Name, reg.Item2, Codecs[reg].Name, type.Name);
                }

                Codecs[reg] = type;
            }
        }

        private static ICodec GetCodec(string encoding, int encoding_version)
        {
            Type? codec_type;
            if (!Codecs.TryGetValue(new CodecRegistration(encoding, encoding_version), out codec_type))
            {
                throw new CodecException($"No codec found for {encoding} version {encoding_version}.");
            }

            var codecConstructor = codec_type.GetConstructor(Type.EmptyTypes);

            if(codecConstructor is null)
            {
                throw new InvalidOperationException("Failed to get codec constructor.");
            }

            return (ICodec)codecConstructor.Invoke(null);
        }

        /// <summary>
        /// Determines whether a codec has been registered for the given encoding name and version.
        /// </summary>
        /// <param name="encoding">The name of the encoding to check for.</param>
        /// <param name="encoding_version">The version of the encoding to check for.</param>
        public static bool HaveCodec(string encoding, int encoding_version)
        {
            return Codecs.ContainsKey(new CodecRegistration(encoding, encoding_version));
        }
        #endregion

        #region Save / Load

        /// <summary>
        /// Gets or sets the assumed encoding of text in DMX files. Defaults to UTF8.
        /// </summary>
        /// <remarks>Changing this value does not alter HumanoidRetargeterDmxs which are already in memory.</remarks>
        public static System.Text.Encoding TextEncoding
        {
            get; set;
        }

        private const string FormatBlankError = "Cannot save while HumanoidRetargeterDmx.Format is blank";

        /// <summary>
        /// Writes this HumanoidRetargeterDmx to a <see cref="Stream"/> with the given encoding and encoding version.
        /// </summary>
        /// <param name="stream">The output Stream.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value of <see cref="HumanoidRetargeterDmx.Format"/> is null or whitespace.</exception>
        public void Save(Stream stream, string encoding, int encoding_version)
        {
            if (string.IsNullOrWhiteSpace(Format))
            {
                throw new InvalidOperationException(FormatBlankError);
            }

            GetCodec(encoding, encoding_version).Encode(this, encoding, encoding_version, stream);
        }

        /// <summary>
        /// Writes this HumanoidRetargeterDmx to a file path with the given encoding and encoding version.
        /// </summary>
        /// <param name="path">The destination file path.</param>
        /// <param name="encoding">The desired encoding.</param>
        /// <param name="encoding_version">The desired encoding version.</param>
        /// <exception cref="InvalidOperationException">Thrown when the value of <see cref="HumanoidRetargeterDmx.Format"/> is null or whitespace.</exception>
        public void Save(string path, string encoding, int encoding_version)
        {
            if (string.IsNullOrWhiteSpace(Format))
            {
                throw new InvalidOperationException(FormatBlankError);
            }

            using var stream = System.IO.File.Create(path);
            Save(stream, encoding, encoding_version);
        }

        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The input Stream.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static HumanoidRetargeterDmx Load(Stream stream, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            return Load_Internal<Element>(stream, Assembly.GetCallingAssembly(), defer_mode, null);
        }
        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The input Stream.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        /// <typeparam  name="T">Type hint for what the Root of this datamodel should be when using reflection</param>
        public static HumanoidRetargeterDmx Load<T>(Stream stream, DeferredMode defer_mode = DeferredMode.Automatic, ReflectionParams? reflectionParams = null)
            where T : Element
        {
            return Load_Internal<T>(stream, Assembly.GetCallingAssembly(), defer_mode, reflectionParams);
        }

        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a byte array.
        /// </summary>
        /// <param name="data">The input byte array.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static HumanoidRetargeterDmx Load(byte[] data, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            return Load_Internal<Element>(new MemoryStream(data, true), Assembly.GetCallingAssembly(), defer_mode);
        }
        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a byte array.
        /// </summary>
        /// <param name="data">The input byte array.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        /// <typeparam  name="T">Type hint for what the Root of this datamodel should be when using reflection</param>
        public static HumanoidRetargeterDmx Load<T>(byte[] data, ReflectionParams? reflectionParams = null)
             where T : Element
        {
            return Load_Internal<T>(new MemoryStream(data, true), Assembly.GetCallingAssembly(), DeferredMode.Disabled, reflectionParams);
        }

        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a file path.
        /// </summary>
        /// <param name="path">The source file path.</param>
        /// <param name="defer_mode">How to handle deferred loading.</param>
        public static HumanoidRetargeterDmx Load(string path, DeferredMode defer_mode = DeferredMode.Automatic)
        {
            var stream = File.OpenRead(path);
            HumanoidRetargeterDmx? dm = null;
            try
            {
                dm = Load_Internal<Element>(stream, Assembly.GetCallingAssembly(), defer_mode);
                return dm;
            }
            finally
            {
                if (defer_mode == DeferredMode.Disabled || (dm != null && dm.Codec == null)) stream.Dispose();
            }
        }
        /// <summary>
        /// Loads a HumanoidRetargeterDmx from a file path, unserializing the Root as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="path">The source file path.</param>
        /// <typeparam  name="T">Type hint for what the Root of this datamodel should be when using reflection</param>
        public static HumanoidRetargeterDmx Load<T>(string path, ReflectionParams? reflectionParams = null)
            where T : Element
        {
            using var stream = File.OpenRead(path);
            return Load_Internal<T>(stream, Assembly.GetCallingAssembly(), DeferredMode.Disabled, reflectionParams);
        }

        private static HumanoidRetargeterDmx Load_Internal<T>(Stream stream, Assembly callingAssembly, DeferredMode defer_mode = DeferredMode.Automatic, ReflectionParams? reflectionParams = null)
            where T : Element
        {
            reflectionParams ??= new ();

            if(typeof(T) == typeof(Element))
            {
                reflectionParams.AttemptReflection = false;
            }

            reflectionParams.AssembliesToSearch.Add(callingAssembly);

            stream.Seek(0, SeekOrigin.Begin);
            var header = string.Empty;
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                header += (char)b;
                if (b == '>')
                    break;
                if (header.Length > 128) // probably not a DMX at this point
                    break;
            }

            var match = System.Text.RegularExpressions.Regex.Match(header, CodecUtilities.HeaderPattern_Regex);

            if (!match.Success || match.Groups.Count != 5)
                throw new InvalidOperationException("Could not read file header.");

            string encoding = match.Groups[1].Value;
            int encoding_version = int.Parse(match.Groups[2].Value);

            string format = match.Groups[3].Value;
            int format_version = int.Parse(match.Groups[4].Value);

            ICodec codec = GetCodec(encoding, encoding_version);

            var dm = codec.Decode(encoding, encoding_version, format, format_version, stream, defer_mode, reflectionParams);
            if (defer_mode == DeferredMode.Automatic && codec is IDeferredAttributeCodec deferredCodec)
            {
                dm.Stream = stream;
                dm.Codec = deferredCodec;
            }

            dm.Format = format;
            dm.FormatVersion = format_version;

            dm.Encoding = encoding;
            dm.EncodingVersion = encoding_version;

            dm.Root = (T?)dm.Root;

            return dm;
        }

        internal Element? OnStubRequest(Guid id)
        {
            Element? result = null;
            if (StubRequest != null)
            {
                result = StubRequest(id);

                if(result is null)
                {
                    throw new InvalidDataException("Stub request failed, result was null");
                }

                if (result.ID != id)
                    throw new InvalidOperationException("HumanoidRetargeterDmx.StubRequest returned an Element with a an ID different from the one requested.");
                if (result.Owner != this)
                    result = ImportElement(result, ImportRecursionMode.Stubs, ImportOverwriteMode.Stubs);
            }

            return result ?? AllElements[id];
        }

        /// <summary>
        /// Occurs when an attempt is made to access a stub elment.
        /// </summary>
        public event StubRequestHandler? StubRequest;

        #endregion

        /// <summary>
        /// Creates a new HumanoidRetargeterDmx with a specified Format and FormatVersion.
        /// </summary>
        /// <param name="format">The format of the HumanoidRetargeterDmx. This is not the same as the encoding used to save or load the HumanoidRetargeterDmx.</param>
        /// <param name="format_version">The version of the format in use.</param>
        public HumanoidRetargeterDmx(string format, int format_version)
            : this()
        {
            Format = format;
            FormatVersion = format_version;
        }

        /// <summary>
        /// Creates a new HumanoidRetargeterDmx.
        /// </summary>
        public HumanoidRetargeterDmx()
        {
            AllElements = new ElementList(this);
            PrefixAttributes = new AttributeList(this);
        }

        protected bool Initialising
        {
            get; private set;
        }
        void ISupportInitialize.BeginInit()
        {
            if (Initialising) throw new InvalidOperationException("HumanoidRetargeterDmx is already initializing.");
            Initialising = true;
        }

        void ISupportInitialize.EndInit()
        {
            if (!Initialising) throw new InvalidOperationException("HumanoidRetargeterDmx is not initializing.");

            if (Format == null)
                throw new InvalidOperationException("A Format name must be defined.");

            Initialising = false;
        }

        /// <summary>
        /// Releases any <see cref="Stream"/> being used to deferred load the HumanoidRetargeterDmx's <see cref="Attribute"/>s.
        /// </summary>
        public void Dispose()
        {
            Stream?.Dispose();
            AllElements.Dispose();
        }

        #region Properties

        /// <summary>
        /// Gets or sets whether new Elements with random IDs can be created by this HumanoidRetargeterDmx.
        /// </summary>
        public bool AllowRandomIDs
        {
            get => _AllowRandomIDs;
            set
            {
                _AllowRandomIDs = value; OnPropertyChanged();
            }
        }
        bool _AllowRandomIDs = true;

        /// <summary>
        /// Gets or sets the format of the HumanoidRetargeterDmx.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when an attempt is made to set a value containing the space character.</exception>
        public string Format
        {
            get => _Format;
            set
            {
                if(value is null)
                {
                    throw new InvalidDataException("Format can not be null");
                }

                if (value.Contains(' '))
                    throw new ArgumentException("Format name cannot contain spaces.");
                _Format = value;
                OnPropertyChanged();
            }
        }
        string _Format = "";

        /// <summary>
        /// Gets or sets the version of the <see cref="Format"/> in use.
        /// </summary>
        public int FormatVersion
        {
            get => _FormatVersion;
            set
            {
                _FormatVersion = value; OnPropertyChanged();
            }
        }
        int _FormatVersion;

        /// <summary>
        /// Gets or sets the encoding with which this HumanoidRetargeterDmx should be stored.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when an attempt is made to set a value containing the space character.</exception>
        public string Encoding
        {
            get => _Encoding;
            set
            {
                if(value is null)
                {
                    throw new InvalidDataException("Encoding can not be null");
                }

                if (value.Contains(' '))
                    throw new ArgumentException("Encoding name cannot contain spaces.");
                _Encoding = value;
                OnPropertyChanged();
            }
        }
        string _Encoding = "";

        /// <summary>
        /// Gets or sets the version of the <see cref="Encoding"/> in use.
        /// </summary>
        public int EncodingVersion
        {
            get => _EncodingVersion;
            set
            {
                _EncodingVersion = value; OnPropertyChanged();
            }
        }
        int _EncodingVersion;

        Stream? Stream;
        internal IDeferredAttributeCodec? Codec;

        /// <summary>
        /// Gets or sets the first Element of the HumanoidRetargeterDmx. Only Elements referenced by the Root element or one of its children are considered a part of the HumanoidRetargeterDmx.
        /// </summary>
        /// <exception cref="ElementOwnershipException">Thown when an attempt is made to assign an Element from another HumanoidRetargeterDmx to this property.</exception>
        public Element? Root
        {
            get => _Root;
            set
            {
                if (value != null)
                {
                    if (value.Owner == null)
                        value = ImportElement(value, ImportRecursionMode.Recursive, ImportOverwriteMode.All);
                    else if (value.Owner != this)
                        throw new ElementOwnershipException();
                }
                _Root = value;
                OnPropertyChanged();
            }
        }
        Element? _Root;

        public AttributeList PrefixAttributes
        {
            get; protected set;
        }

        /// <summary>
        /// Gets all Elements owned by this HumanoidRetargeterDmx. Only Elements which are referenced by the Root element or one of its children are actually considered part of the HumanoidRetargeterDmx.
        /// </summary>
        public ElementList AllElements
        {
            get; protected set;
        }
        #endregion

        #region Element handling
        /// <summary>
        /// Copies another HumanoidRetargeterDmx's <see cref="Element"/> into this one.
        /// </summary>
        /// <remarks>The return value will be owned by this HumanoidRetargeterDmx. It can be:
        /// 1. foreign_element. This is the case when foreign_element has no owner and no Element with the same ID exists in this HumanoidRetargeterDmx.
        /// 2. An existing Element owned by this HumanoidRetargeterDmx with the same ID as foreign_element.
        /// 3. A copy of foreign_element. This is the case when foreign_element already had an owner and a corresponding Element was not found in this HumanoidRetargeterDmx.</remarks>
        /// <param name="foreign_element">The Element to import. Must be owned by a different HumanoidRetargeterDmx.</param>
        /// <param name="import_mode">How to respond when foreign_element references other foreign Elements.</param>
        /// <param name="overwrite_mode">How to respond when the ID of a foreign Element is already in use in this HumanoidRetargeterDmx.</param>
        /// <returns>foreign_element, a local Element, or a new copy of foreign_element. See Remarks for more details.</returns>
        /// <exception cref="ArgumentNullException">Thrown if foreign_element is null.</exception>
        /// <exception cref="ElementOwnershipException">Thrown if foreign_element is already owned by this HumanoidRetargeterDmx.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the maximum number of Elements allowed in a HumanoidRetargeterDmx has been reached.</exception>
        /// <seealso cref="Element.Stub"/>
        /// <seealso cref="Element.ID"/>
        public Element? ImportElement(Element foreign_element, ImportRecursionMode import_mode, ImportOverwriteMode overwrite_mode)
        {
            ArgumentNullException.ThrowIfNull(foreign_element);

            if (foreign_element.Owner == this)
            {
                throw new ElementOwnershipException("Element is already a part of this HumanoidRetargeterDmx.");
            }

            return ImportElement_internal(foreign_element, new ImportJob(import_mode, overwrite_mode));
        }

        public enum ImportRecursionMode
        {
            /// <summary>
            /// Import the given Element only. Any Element reference will become null.
            /// </summary>
            Nulls,
            /// <summary>
            /// Import the given Element only. Any Element references will be represented with stubs.
            /// </summary>
            Stubs,
            /// <summary>
            /// Recursively import all referenced Elements.
            /// </summary>
            Recursive,
        }

        public enum ImportOverwriteMode
        {
            /// <summary>
            /// If a local Element has the same ID as a foreign Element, ignore the foreign Element.
            /// </summary>
            None,
            /// <summary>
            /// If a local stub Element has the same ID as a non-stub foreign Element, replace it.
            /// </summary>
            Stubs,
            /// <summary>
            /// If a local Element has the same ID as a non-stub foreign Element, replace it.
            /// </summary>
            All,
        }

        private struct ImportJob
        {
            public ImportRecursionMode ImportMode
            {
                get; private set;
            }
            public ImportOverwriteMode OverwriteMode
            {
                get; private set;
            }
            public Dictionary<Element, Element> ImportMap
            {
                get; private set;
            }
            public int Depth
            {
                get; set;
            }

            public ImportJob(ImportRecursionMode import_mode, ImportOverwriteMode overwrite_mode)
                : this()
            {
                ImportMode = import_mode;
                OverwriteMode = overwrite_mode;
                ImportMap = [];
            }
        }

        private object? CopyValue(object value, ImportJob job)
        {
            if (value == null) return null;
            var attr_type = value.GetType();

            // do nothing for a value type or string...
            if (attr_type.IsValueType || attr_type == typeof(string))
                return value;

            // ...copy a reference type
            else if (attr_type == typeof(Element))
            {
                var foreign_element = (Element)value;
                var local_element = AllElements[foreign_element.ID];
                Element? best_element;

                if (local_element != null && !local_element.Stub)
                    best_element = local_element;
                else if (!foreign_element.Stub && job.ImportMode == ImportRecursionMode.Recursive)
                {
                    job.Depth++;
                    best_element = ImportElement_internal(foreign_element, job);
                    job.Depth--;
                }
                else
                    best_element = local_element ?? (job.ImportMode == ImportRecursionMode.Stubs ? new Element(this, foreign_element.ID) : null);

                return best_element;
            }
            else if (attr_type == typeof(byte[]))
            {
                var inbytes = (byte[])value;
                var outbytes = new byte[inbytes.Length];
                inbytes.CopyTo(outbytes, 0);
                return outbytes;
            }

            else throw new ArgumentException("CopyValue: unhandled type.");
        }

        private Element? ImportElement_internal(Element? foreign_element, ImportJob job)
        {
            if (foreign_element == null) return null;
            if (foreign_element.Owner == this) return foreign_element;

            Element? local_element;

            // don't import the same Element twice
            if (job.ImportMap.TryGetValue(foreign_element, out local_element))
                return local_element;

            lock (foreign_element.SyncRoot)
            {
                // Claim an unowned Element
                if (foreign_element.Owner == null && AllElements[foreign_element.ID] == null)
                {
                    foreign_element.Owner = this;
                    foreach (var attr in foreign_element)
                    {
                        if (attr.Value is Element elem)
                            foreign_element[attr.Key] = ImportElement_internal(elem, job);

                        if (attr.Value is ElementArray elem_array)
                        {
                            elem_array.Owner = foreign_element;
                            for (int i = 0; i < elem_array.Count; i++)
                            {
                                var item = elem_array[i];
                                if (item != null && item.Owner != null)
                                {
                                    var importedElement = ImportElement_internal(item, job);
                                    if (importedElement is not null)
                                    {
                                        elem_array[i] = importedElement;
                                    }
                                }
                            }
                        }
                    }
                    return foreign_element;
                }

                // find a local element with the same ID and either return or stub it
                local_element = AllElements[foreign_element.ID];
                if (local_element != null)
                {
                    if (!foreign_element.Stub && (job.OverwriteMode == ImportOverwriteMode.All || (job.OverwriteMode == ImportOverwriteMode.Stubs && local_element.Stub)))
                    {
                        local_element.Name = foreign_element.Name;
                        local_element.ClassName = foreign_element.ClassName;
                        local_element.Stub = false;
                    }
                    else
                        return local_element;
                }
                else
                {
                    // Create a new local Element
                    if (foreign_element.Stub || (job.ImportMode == ImportRecursionMode.Stubs && job.Depth > 0))
                        local_element = new Element(this, foreign_element.ID);
                    else if (job.ImportMode == ImportRecursionMode.Recursive || job.Depth == 0)
                        local_element = new Element(this, foreign_element.Name, foreign_element.ID, foreign_element.ClassName);
                    else
                        local_element = null;
                }

                if(local_element is null)
                {
                    return null;
                }

                job.ImportMap.Add(foreign_element, local_element);

                // Copy attributes
                if (local_element != null && !local_element.Stub)
                {
                    local_element.Clear();
                    foreach (var attr in foreign_element)
                    {
                        if (attr.Value == null)
                            local_element[attr.Key] = null;
                        else if (IsHumanoidRetargeterDmxArrayType(attr.Value.GetType()))
                        {
                            var list = (System.Collections.ICollection)attr.Value;
                            var inner_type = GetArrayInnerType(list.GetType());

                            if(inner_type is null)
                            {
                                throw new InvalidOperationException("Failed to get inner_type while importing element");
                            }

                            var copied_array = CodecUtilities.MakeList(inner_type, list.Count);
                            foreach (var item in list)
                                copied_array.Add(CopyValue(item, job));

                            local_element[attr.Key] = copied_array;
                        }
                        else
                            local_element[attr.Key] = CopyValue(attr.Value, job);
                    }
                }
                return local_element;
            }
        }

        #endregion

        #region Events
        /// <summary>
        /// Raised when the HumanoidRetargeterDmx's <see cref="Format"/>, <see cref="FormatVersion"/>, or <see cref="Root"/> changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName()] string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
        #endregion
    }

    /// <summary>
    /// Represents the method that will handle the <see cref="HumanoidRetargeterDmx.StubRequest"/> event.
    /// </summary>
    /// <param name="id">The Element ID to search for.</param>
    /// <returns>An Element with the requested ID.</returns>
    public delegate Element StubRequestHandler(Guid id);

    #region Exceptions
    /// <summary>
    /// The exception that is thrown when the value of an <see cref="Attribute"/> is an unsupported <see cref="Type"/>.
    /// </summary>
    [Serializable]
    public class AttributeTypeException : ArgumentException
    {
        public AttributeTypeException(string message)
            : base(message)
        {
        }

        [SecuritySafeCritical]
        protected AttributeTypeException(SerializationInfo info, StreamingContext context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown when an <see cref="Element.ID"/> collision occurrs.
    /// </summary>
    [Serializable]
    public class ElementIdException : InvalidOperationException
    {
        internal ElementIdException(string message)
            : base(message)
        {
        }

        [SecuritySafeCritical]
        protected ElementIdException(SerializationInfo info, StreamingContext context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown when a HumanoidRetargeterDmx tries to manipulate an <see cref="Element"/> with an innapropriate owner.
    /// </summary>
    [Serializable]
    public class ElementOwnershipException : InvalidOperationException
    {
        internal ElementOwnershipException(string message)
            : base(message)
        {
        }

        internal ElementOwnershipException()
            : base("Cannot add an Element from a different HumanoidRetargeterDmx. Use ImportElement() first.")
        {
        }

        [SecuritySafeCritical]
        protected ElementOwnershipException(SerializationInfo info, StreamingContext context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown when an error occurs in an <see cref="ICodec"/>.
    /// </summary>
    [Serializable]
    public class CodecException : Exception
    {
        public CodecException(string message)
            : base(message)
        {
        }

        public CodecException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        [SecuritySafeCritical]
        protected CodecException(SerializationInfo info, StreamingContext context)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown when an error occurs while destubbing an attribute value.
    /// </summary>
    [Serializable]
    public class DestubException : Exception
    {
        internal DestubException(Attribute attr, Exception innerException)
            : base("An exception occured while destubbing the value of an attribute.", innerException)
        {
            Data.Add("Element", ((Element?)attr.Owner)?.ID);
            Data.Add("Attribute", attr.Name);
        }

        internal DestubException(ElementArray array, int index, Exception innerException)
            : base("An exception occured while destubbing an array item.", innerException)
        {
            var arrayOwner = array.Owner;
            if(arrayOwner is not null)
            {
                Data.Add("Element", ((Element)arrayOwner).ID);
            }
            else
            {
                Data.Add("Element", null);
            }
            Data.Add("Index", index);
        }

        [SecuritySafeCritical]
        protected DestubException(SerializationInfo info, StreamingContext context)
        {
        }
    }

    #endregion

    static class Extensions
    {
        public static Type MakeListType(this Type t)
        {
            return typeof(List<>).MakeGenericType(t);
        }
    }
}
