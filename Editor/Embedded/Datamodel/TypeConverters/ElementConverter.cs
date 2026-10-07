#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel.TypeConverters;

        public class ElementConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            {
                if (sourceType == typeof(string) || sourceType == typeof(Guid)) return true;
                return base.CanConvertFrom(context, sourceType);
            }

            public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
            {
                Guid guid_value;

                if (value is string str_value)
                    guid_value = Guid.Parse(str_value);
                else if (value is Guid guid)
                    guid_value = guid;
                else
                    return base.ConvertFrom(context, culture, value);

                var result = new Element();

                var ir = (ISupportInitialize)result;
                ir.BeginInit();
                result.Stub = true;
                result.ID = guid_value;
                ir.EndInit();

                return result;
            }

            public override bool IsValid(ITypeDescriptorContext? context, object? value)
            {
                if (value is null)
                    return false;
                if (value is Guid)
                    return true;
                if (value is string str_value && Guid.TryParse(str_value, out _))
                    return true;
                return false;
            }

            public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
            {
                if (destinationType == typeof(Guid) || destinationType == typeof(string))
                    return true;
                return base.CanConvertTo(context, destinationType);
            }

            public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
            {
                if (value is null)
                    return null;
                var element = (Element)value;
                if (destinationType == typeof(Guid))
                    return element.ID;
                if (destinationType == typeof(string))
                    return element.ID.ToString();

                return base.ConvertTo(context, culture, value, destinationType);
            }
        }
