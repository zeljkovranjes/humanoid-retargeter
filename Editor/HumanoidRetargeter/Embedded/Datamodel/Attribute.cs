#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;

using AttrKVP = System.Collections.Generic.KeyValuePair<string, object?>;

namespace HumanoidRetargeterDmx
{
    /// <summary>
    /// A name/value pair associated with an <see cref="Element"/>.
    /// </summary>
    class Attribute
    {
        /// <summary>
        /// Creates a new Attribute with a specified name and value.
        /// </summary>
        /// <param name="name">The name of the Attribute, which must be unique to its owner.</param>
        /// <param name="value">The value of the Attribute, which must be of a supported HumanoidRetargeterDmx type.</param>
        public Attribute(string name, AttributeList owner, object? value)
        {
            ArgumentNullException.ThrowIfNull(name);

            Name = name;
            _Owner = owner;
            Value = value;
        }

        /// <summary>
        /// Creates a new Attribute with deferred loading.
        /// </summary>
        /// <param name="name">The name of the Attribute, which must be unique to its owner.</param>
        /// <param name="owner">The AttributeList which owns this Attribute.</param>
        /// <param name="defer_offset">The location in the encoded DMX stream at which this Attribute's value can be found.</param>
        public Attribute(string name, AttributeList owner, long defer_offset)
            : this(name, owner, null)
        {
            ArgumentNullException.ThrowIfNull(owner);

            Offset = defer_offset;
        }

        #region Properties
        /// <summary>
        /// Gets the name of this Attribute.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the Type of this Attribute's Value.
        /// </summary>
        public Type ValueType { get; private set; } = typeof(Element);

        /// <summary>
        /// Gets or sets the OverrideType of this Attributes.
        /// </summary>
        public AttributeList.OverrideType? OverrideType
        {
            get
            {
                return _OverrideType;
            }
            set
            {
                switch (value)
                {
                    case null:
                        break;
                    case AttributeList.OverrideType.Angle:
                        if (ValueType != typeof(global::System.Numerics.Vector3))
                            throw new AttributeTypeException("OverrideType.Angle can only be applied to global::System.Numerics.Vector3 attributes");
                        break;
                    case AttributeList.OverrideType.Binary:
                        if (ValueType != typeof(byte[]))
                            throw new AttributeTypeException("OverrideType.Binary can only be applied to byte[] attributes");
                        break;
                    default:
                        throw new NotImplementedException();
                }
                _OverrideType = value;
            }
        }
        AttributeList.OverrideType? _OverrideType;

        /// <summary>
        /// Gets the <see cref="AttributeList"/> which this Attribute is a member of.
        /// </summary>
        public AttributeList? Owner
        {
            get { return _Owner; }
            internal set
            {
                if (_Owner == value) return;

                if (Deferred && _Owner != null) DeferredLoad();
                _Owner = value;
            }
        }
        AttributeList? _Owner;

        HumanoidRetargeterDmx? OwnerHumanoidRetargeterDmx { get { return Owner?.Owner; } }

        /// <summary>
        /// Gets whether the value of this Attribute has yet to be decoded.
        /// </summary>
        public bool Deferred { get { return Offset != 0; } }

        /// <summary>
        /// Loads the value of this Attribute from the encoded source HumanoidRetargeterDmx.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the Attribute has already been loaded.</exception>
        /// <exception cref="CodecException">Thrown when the deferred load fails.</exception>
        public void DeferredLoad()
        {
            if (Offset == 0) throw new InvalidOperationException("Attribute already loaded.");

            if (OwnerHumanoidRetargeterDmx == null || OwnerHumanoidRetargeterDmx.Codec == null)
                throw new CodecException("Trying to load a deferred Attribute, but could not find codec.");

            try
            {
                lock (OwnerHumanoidRetargeterDmx.Codec)
                {
                    _Value = OwnerHumanoidRetargeterDmx.Codec.DeferredDecodeAttribute(OwnerHumanoidRetargeterDmx, Offset);
                }
            }
            catch (Exception err)
            {
                throw new CodecException($"Deferred loading of attribute \"{Name}\" on element {((Element?)Owner)?.ID} using {OwnerHumanoidRetargeterDmx.Codec} codec threw an exception.", err);
            }
            Offset = 0;

            if (_Value is ElementArray elem_array)
                elem_array.Owner = Owner;
        }

        /// <summary>
        /// Gets or sets the value held by this Attribute.
        /// </summary>
        /// <exception cref="CodecException">Thrown when deferred value loading fails.</exception>
        /// <exception cref="DestubException">Thrown when Element destubbing fails.</exception>
        public object? Value
        {
            get
            {
                if (Offset > 0)
                    DeferredLoad();

                if (OwnerHumanoidRetargeterDmx != null)
                {
                    // expand stubs
                    if (_Value is Element elem && elem.Stub)
                    {
                        try { _Value = OwnerHumanoidRetargeterDmx.OnStubRequest(elem.ID) ?? _Value; }
                        catch (Exception err) { throw new DestubException(this, err); }
                    }
                }

                return _Value;
            }
            set
            {
                ValueType = value == null ? typeof(Element) : value.GetType();

                if (!HumanoidRetargeterDmx.IsHumanoidRetargeterDmxType(ValueType))
                    throw new AttributeTypeException(String.Format("{0} is not a valid HumanoidRetargeterDmx attribute type. (If this is an array, it must implement IList<T>).", ValueType.FullName));

                if (value is Element elem)
                {
                    if (elem.Owner == null)
                        elem.Owner = OwnerHumanoidRetargeterDmx;
                    else if (elem.Owner != OwnerHumanoidRetargeterDmx)
                        throw new ElementOwnershipException();
                }

                if (value is IEnumerable<Element> elem_enumerable)
                {
                    if (elem_enumerable is not ElementArray)
                        throw new InvalidOperationException("Element array objects must derive from HumanoidRetargeterDmx.ElementArray");

                    var elem_array = (ElementArray)value;
                    if (elem_array.Owner == null)
                        elem_array.Owner = Owner;
                    else if (elem_array.Owner != Owner)
                        throw new InvalidOperationException("ElementArray is already owned by a different HumanoidRetargeterDmx.");

                    foreach (var arr_elem in elem_array)
                    {
                        if (arr_elem == null) continue;
                        else if (arr_elem.Owner == null)
                            arr_elem.Owner = OwnerHumanoidRetargeterDmx;

                        // todo: remove ownership from values
                        // this is being printed on a debuggerdisplay output for some reason
                        // else if (arr_elem.Owner != OwnerHumanoidRetargeterDmx)
                        //    throw new ElementOwnershipException("One or more Elements in the assigned collection are from a different HumanoidRetargeterDmx. Use ImportElement() to copy them to this one before assigning.");
                    }
                }

                _Value = value;
                Offset = 0;
            }
        }
        object? _Value = null;

        /// <summary>
        /// Gets the Attribute's Value without attempting deferred loading or destubbing.
        /// </summary>
        public object? RawValue { get { return _Value; } }

        #endregion

        long Offset;

        public override string ToString()
        {
            var type = Value != null ? Value.GetType() : typeof(Element);
            var inner_type = HumanoidRetargeterDmx.GetArrayInnerType(type);
            return String.Format("{0} <{1}>", Name, inner_type != null ? inner_type.FullName + "[]" : type.FullName);
        }

        public AttrKVP ToKeyValuePair()
        {
            return new AttrKVP(Name, Value);
        }
    }
}
