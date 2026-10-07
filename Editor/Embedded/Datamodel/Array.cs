#nullable enable
#pragma warning disable CS3021 // Upstream CLS attributes; host assembly does not claim CLS compliance.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace HumanoidRetargeter.EditorTools.Embedded.Datamodel;

    [DebuggerTypeProxy(typeof(Array<>.DebugView))]
    [DebuggerDisplay("Count = {Inner.Count}")]
    public abstract class Array<T> : IList<T>, IList
    {
        internal class DebugView(Array<T> arr)
        {
            readonly Array<T> Arr = arr;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public T[] Items { get { return [.. Arr.Inner]; } }
        }

        protected List<T> Inner;

        public virtual AttributeList? Owner
        {
            get => _Owner;
            internal set
            {
                _Owner = value;
            }
        }
        AttributeList? _Owner;

        protected Datamodel? OwnerHumanoidRetargeterDmx => Owner?.Owner;

        internal Array()
        {
            Inner = [];
        }

        internal Array(IEnumerable<T> enumerable)
        {
            if (enumerable != null)
                Inner = [.. enumerable];
            else
                Inner = [];
        }

        internal Array(int capacity)
        {
            Inner = new List<T>(capacity);
        }

        public int IndexOf(T item) => Inner.IndexOf(item);

        public void Insert(int index, T item) => Insert_Internal(index, item);
        protected virtual void Insert_Internal(int index, T item) => Inner.Insert(index, item);

        public void AddRange(IEnumerable<T> items) => Inner.AddRange(items);

        public void RemoveAt(int index) => Inner.RemoveAt(index);

        public virtual T this[int index]
        {
            get => Inner[index];
            set => Inner[index] = value;
        }

        public void Add(T item) => Insert(Inner.Count, item);

        public void Clear() => Inner.Clear();

        public bool Contains(T item) => Inner.Contains(item);

        public void CopyTo(T[] array, int offset)
        {
            CopyTo_Internal(array, offset);
        }

        protected virtual void CopyTo_Internal(T[] array, int offset) => Inner.CopyTo(array, offset);

        public int Count => Inner.Count;

        bool ICollection<T>.IsReadOnly { get { return false; } }

        public bool IsFixedSize => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public bool IsSynchronized => throw new NotImplementedException();

        public object SyncRoot => throw new NotImplementedException();

        object? IList.this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool Remove(T item) => Inner.Remove(item);

        public IEnumerator<T> GetEnumerator() => Inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Inner.GetEnumerator();

        #region IList
        int IList.Add(object? value)
        {
            if (value is not null)
                Add((T)value);
            return Count;
        }

        bool IList.Contains(object? value)
        {
            if (value is null)
                return false;
            return Contains((T)value);
        }

        int IList.IndexOf(object? value)
        {
            if (value is null)
                throw new InvalidOperationException("Trying to get the index of a null object");

            return IndexOf((T)value);
        }

        void IList.Insert(int index, object? value)
        {
            if (value is null)
                throw new InvalidOperationException("Trying to insert a null object");

            Insert(index, (T)value);
        }

        bool IList.IsFixedSize { get { return false; } }
        bool IList.IsReadOnly { get { return false; } }

        void IList.Remove(object? value)
        {
            if (value is null)
                throw new InvalidOperationException("Trying to remove a null object");

            Remove((T)value);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            CopyTo((T[])array, index);
        }

        #endregion IList
    }
