#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HumanoidRetargeter.EditorTools.Embedded.ZstdSharp.Unsafe;

namespace HumanoidRetargeter.EditorTools.Embedded.ZstdSharp;

    /// <summary>
    /// Safely wraps an unmanaged Zstd compression context.
    /// </summary>
    internal sealed unsafe class SafeDctxHandle : SafeZstdHandle
    {
        /// <inheritdoc/>
        private SafeDctxHandle()
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="SafeDctxHandle"/>.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ZstdException">Creation failed.</exception>
        public static SafeDctxHandle Create()
        {
            var safeHandle = new SafeDctxHandle();
            bool success = false;
            try
            {
                var dctx = Methods.ZSTD_createDCtx();
                if (dctx == null)
                    throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_GENERIC, "Failed to create dctx");
                safeHandle.SetHandle((IntPtr)dctx);
                success = true;
            }
            finally
            {
                if (!success)
                {
                    safeHandle.SetHandleAsInvalid();
                }
            }
            return safeHandle;
        }

        /// <summary>
        /// Acquires a reference to the safe handle.
        /// </summary>
        /// <returns>
        /// A <see cref="SafeHandleHolder{T}"/> instance that can be implicitly converted to a pointer
        /// to <see cref="ZSTD_DCtx_s"/>.
        /// </returns>
        public SafeHandleHolder<ZSTD_DCtx_s> Acquire() => new(this);

        protected override bool ReleaseHandle()
        {
            return Methods.ZSTD_freeDCtx((ZSTD_DCtx_s*)handle) == 0;
        }
    }
