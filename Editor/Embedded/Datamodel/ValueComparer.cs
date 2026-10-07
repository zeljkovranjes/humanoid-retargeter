#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.ComponentModel;
using System.Numerics;

using AttrKVP = System.Collections.Generic.KeyValuePair<string, object?>;
using System.Reflection;
using System.IO;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    /// <summary>
    /// Compares two Attribute values, using <see cref="Element.IDComparer"/> for AttributeList comparisons.
    /// </summary>
    public class ValueComparer : IEqualityComparer
    {
        /// <summary>
        /// Gets a default Attribute value equality comparer.
        /// </summary>
        public static ValueComparer Default
        {
            get
            {
                _Default ??= new ValueComparer();
                return _Default;
            }
        }
        static ValueComparer? _Default;

        public new bool Equals(object? x, object? y)
        {
            if(x is null || y is null)
            {
                return false;
            }

            var type_x = x.GetType();
            var type_y = y.GetType();

            if (type_x == null && type_y == null)
                return true;

            if (type_x != type_y)
                return false;

            var inner = Datamodel.GetArrayInnerType(type_x);
            if (inner != null)
            {
                var array_left = (IList)x;
                var array_right = (IList)y;

                if (array_left.Count != array_right.Count) return false;

                return !Enumerable.Range(0, array_left.Count).Any(i => !Equals(array_left[i], array_right[i]));
            }
            else if (type_x == typeof(Element))
                return Element.IDComparer.Default.Equals((Element)x, (Element)y);
            else
                return EqualityComparer<object>.Default.Equals(x, y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is Element elem)
                return elem.ID.GetHashCode();

            var inner = Datamodel.GetArrayInnerType(obj.GetType());
            if (inner != null)
            {
                int hash = 0;
                foreach (var item in (IList)obj)
                    hash ^= item.GetHashCode();
                return hash;
            }

            return obj.GetHashCode();
        }
    }
