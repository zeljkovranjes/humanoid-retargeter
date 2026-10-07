#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp;

    /// <summary>
    /// Provides the base class for HumanoidRetargeter.EditorTools.Embedded.ZstdSharp <see cref="SafeHandle"/> implementations.
    /// </summary>
    /// <remarks>
    /// Even though HumanoidRetargeter.EditorTools.Embedded.ZstdSharp is a managed library, its internals are using unmanaged
    /// memory and we are using safe handles in the library's high-level API to ensure
    /// proper disposal of unmanaged resources and increase safety.
    /// </remarks>
    /// <seealso cref="SafeCctxHandle"/>
    /// <seealso cref="SafeDctxHandle"/>
    internal abstract unsafe class SafeZstdHandle : SafeHandle
    {
        /// <summary>
        /// Parameterless constructor is hidden. Use the static <c>Create</c> factory
        /// method to create a new safe handle instance.
        /// </summary>
        protected SafeZstdHandle() : base(IntPtr.Zero, true)
        {
        }

        public sealed override bool IsInvalid => handle == IntPtr.Zero;
    }
