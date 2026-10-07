#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp;

    /// <summary>
    /// Provides a convenient interface to safely acquire pointers of a specific type
    /// from a <see cref="SafeHandle"/>, by utilizing <see langword="using"/> blocks.
    /// </summary>
    /// <typeparam name="T">The type of pointers to return.</typeparam>
    /// <remarks>
    /// Safe handle holders can be <see cref="Dispose"/>d to decrement the safe handle's
    /// reference count, and can be implicitly converted to pointers to <see cref="T"/>.
    /// </remarks>
    internal unsafe ref struct SafeHandleHolder<T> where T : unmanaged
    {
        private readonly SafeHandle _handle;

        private bool _refAdded;

        public SafeHandleHolder(SafeHandle safeHandle)
        {
            _handle = safeHandle;
            _refAdded = false;
            safeHandle.DangerousAddRef(ref _refAdded);
        }

        public static implicit operator T*(SafeHandleHolder<T> holder) =>
            (T*)holder._handle.DangerousGetHandle();

        public void Dispose()
        {
            if (_refAdded)
            {
                _handle.DangerousRelease();
                _refAdded = false;
            }
        }
    }
