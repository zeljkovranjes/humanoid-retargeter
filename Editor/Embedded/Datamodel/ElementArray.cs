#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    public class ElementArray : Array<Element>
    {
        public ElementArray() { }

        public ElementArray(IEnumerable<Element> enumerable)
            : base(enumerable)
        { }

        public ElementArray(int capacity)
            : base(capacity)
        { }

        /// <summary>
        /// Gets the values in the list without attempting destubbing.
        /// </summary>
        internal IEnumerable<Element> RawList { get { foreach (var elem in Inner) yield return elem; } }

        public override AttributeList? Owner
        {
            get => base.Owner;
            internal set
            {
                base.Owner = value;

                if (OwnerHumanoidRetargeterDmx != null)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        var elem = Inner[i];

                        if (elem == null) continue;
                        if (elem.Owner == null)
                        {
                            var importedElement = OwnerHumanoidRetargeterDmx.ImportElement(elem, Datamodel.ImportRecursionMode.Stubs, Datamodel.ImportOverwriteMode.Stubs);
                            
                            if(importedElement is not null)
                            {
                                Inner[i] = importedElement;
                            }
                        }
                        else if (elem.Owner != OwnerHumanoidRetargeterDmx)
                            throw new ElementOwnershipException();
                    }
                }
            }
        }

        protected override void Insert_Internal(int index, Element item)
        {
            if (item != null && OwnerHumanoidRetargeterDmx != null)
            {
                if (item.Owner == null)
                {
                    var importedElement = OwnerHumanoidRetargeterDmx.ImportElement(item, Datamodel.ImportRecursionMode.Recursive, Datamodel.ImportOverwriteMode.Stubs);
                
                    if(importedElement is not null)
                    {
                        item = importedElement;
                    }
                }
                else if (item.Owner != OwnerHumanoidRetargeterDmx)
                {
                    throw new ElementOwnershipException();
                }
            }

            base.Insert_Internal(index, item!);
        }

        public override Element this[int index]
        {
            get
            {
                var elem = Inner[index];
                if (elem != null && elem.Stub && elem.Owner != null)
                {
                    try
                    {
                        elem = Inner[index] = elem.Owner.OnStubRequest(elem.ID)!;
                    }
                    catch (Exception err)
                    {
                        throw new DestubException(this, index, err);
                    }
                }

                if (elem is null)
                {
                    throw new InvalidOperationException("Element at specified index is null");
                }

                return elem;
            }
            set => base[index] = value;
        }
    }
