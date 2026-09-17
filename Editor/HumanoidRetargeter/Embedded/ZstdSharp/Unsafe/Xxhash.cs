#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using static HumanoidRetargeterZstd.UnsafeHelper;
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace HumanoidRetargeterZstd.Unsafe
{
    public static unsafe partial class Methods
    {

        /*!
         * @internal
         * @brief Modify this function to use a different routine than memcpy().
         */
private static void XXH_memcpy(void* dest, void* src, nuint size)
        {
            memcpy(dest, src, (uint)size);
        }
private static uint XXH_readLE32(void* ptr)
        {
            return BitConverter.IsLittleEndian ? MEM_read32(ptr) : BinaryPrimitives.ReverseEndianness(MEM_read32(ptr));
        }

        private static uint XXH_readLE32_align(void* ptr, XXH_alignment align)
        {
            if (align == XXH_alignment.XXH_unaligned)
            {
                return XXH_readLE32(ptr);
            }
            else
            {
                return BitConverter.IsLittleEndian ? *(uint*)ptr : BinaryPrimitives.ReverseEndianness(*(uint*)ptr);
            }
        }
private static ulong XXH_readLE64(void* ptr)
        {
            return BitConverter.IsLittleEndian ? MEM_read64(ptr) : BinaryPrimitives.ReverseEndianness(MEM_read64(ptr));
        }

        private static ulong XXH_readLE64_align(void* ptr, XXH_alignment align)
        {
            if (align == XXH_alignment.XXH_unaligned)
                return XXH_readLE64(ptr);
            else
                return BitConverter.IsLittleEndian ? *(ulong*)ptr : BinaryPrimitives.ReverseEndianness(*(ulong*)ptr);
        }

        /*! @copydoc XXH32_round */
private static ulong XXH64_round(ulong acc, ulong input)
        {
            acc += input * 0xC2B2AE3D27D4EB4FUL;
            acc = BitOperations.RotateLeft(acc, 31);
            acc *= 0x9E3779B185EBCA87UL;
            return acc;
        }
private static ulong XXH64_mergeRound(ulong acc, ulong val)
        {
            val = XXH64_round(0, val);
            acc ^= val;
            acc = acc * 0x9E3779B185EBCA87UL + 0x85EBCA77C2B2AE63UL;
            return acc;
        }

        /*! @copydoc XXH32_avalanche */
        private static ulong XXH64_avalanche(ulong hash)
        {
            hash ^= hash >> 33;
            hash *= 0xC2B2AE3D27D4EB4FUL;
            hash ^= hash >> 29;
            hash *= 0x165667B19E3779F9UL;
            hash ^= hash >> 32;
            return hash;
        }

        /*!
         * @internal
         * @brief Processes the last 0-31 bytes of @p ptr.
         *
         * There may be up to 31 bytes remaining to consume from the input.
         * This final stage will digest them to ensure that all input bytes are present
         * in the final mix.
         *
         * @param hash The hash to finalize.
         * @param ptr The pointer to the remaining input.
         * @param len The remaining length, modulo 32.
         * @param align Whether @p ptr is aligned.
         * @return The finalized hash
         * @see XXH32_finalize().
         */
        private static ulong XXH64_finalize(ulong hash, byte* ptr, nuint len, XXH_alignment align)
        {
            len &= 31;
            while (len >= 8)
            {
                ulong k1 = XXH64_round(0, XXH_readLE64_align(ptr, align));
                ptr += 8;
                hash ^= k1;
                hash = BitOperations.RotateLeft(hash, 27) * 0x9E3779B185EBCA87UL + 0x85EBCA77C2B2AE63UL;
                len -= 8;
            }

            if (len >= 4)
            {
                hash ^= XXH_readLE32_align(ptr, align) * 0x9E3779B185EBCA87UL;
                ptr += 4;
                hash = BitOperations.RotateLeft(hash, 23) * 0xC2B2AE3D27D4EB4FUL + 0x165667B19E3779F9UL;
                len -= 4;
            }

            while (len > 0)
            {
                hash ^= *ptr++ * 0x27D4EB2F165667C5UL;
                hash = BitOperations.RotateLeft(hash, 11) * 0x9E3779B185EBCA87UL;
                --len;
            }

            return XXH64_avalanche(hash);
        }

        /*!
         * @internal
         * @brief The implementation for @ref XXH64().
         *
         * @param input , len , seed Directly passed from @ref XXH64().
         * @param align Whether @p input is aligned.
         * @return The calculated hash.
         */
        private static ulong XXH64_endian_align(byte* input, nuint len, ulong seed, XXH_alignment align)
        {
            ulong h64;
            if (len >= 32)
            {
                byte* bEnd = input + len;
                byte* limit = bEnd - 31;
                ulong v1 = seed + 0x9E3779B185EBCA87UL + 0xC2B2AE3D27D4EB4FUL;
                ulong v2 = seed + 0xC2B2AE3D27D4EB4FUL;
                ulong v3 = seed + 0;
                ulong v4 = seed - 0x9E3779B185EBCA87UL;
                do
                {
                    v1 = XXH64_round(v1, XXH_readLE64_align(input, align));
                    input += 8;
                    v2 = XXH64_round(v2, XXH_readLE64_align(input, align));
                    input += 8;
                    v3 = XXH64_round(v3, XXH_readLE64_align(input, align));
                    input += 8;
                    v4 = XXH64_round(v4, XXH_readLE64_align(input, align));
                    input += 8;
                }
                while (input < limit);
                h64 = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
                h64 = XXH64_mergeRound(h64, v1);
                h64 = XXH64_mergeRound(h64, v2);
                h64 = XXH64_mergeRound(h64, v3);
                h64 = XXH64_mergeRound(h64, v4);
            }
            else
            {
                h64 = seed + 0x27D4EB2F165667C5UL;
            }

            h64 += len;
            return XXH64_finalize(h64, input, len, align);
        }

        /*! @ingroup XXH64_family */
        private static ulong ZSTD_XXH64(void* input, nuint len, ulong seed)
        {
            return XXH64_endian_align((byte*)input, len, seed, XXH_alignment.XXH_unaligned);
        }

        /*! @ingroup XXH64_family */
        private static XXH_errorcode ZSTD_XXH64_reset(XXH64_state_s* statePtr, ulong seed)
        {
            *statePtr = new XXH64_state_s();
            statePtr->v[0] = seed + 0x9E3779B185EBCA87UL + 0xC2B2AE3D27D4EB4FUL;
            statePtr->v[1] = seed + 0xC2B2AE3D27D4EB4FUL;
            statePtr->v[2] = seed + 0;
            statePtr->v[3] = seed - 0x9E3779B185EBCA87UL;
            return XXH_errorcode.XXH_OK;
        }

        /*! @ingroup XXH64_family */
        private static XXH_errorcode ZSTD_XXH64_update(XXH64_state_s* state, void* input, nuint len)
        {
            if (input == null)
            {
                return XXH_errorcode.XXH_OK;
            }

            {
                byte* p = (byte*)input;
                byte* bEnd = p + len;
                state->total_len += len;
                if (state->memsize + len < 32)
                {
                    XXH_memcpy((byte*)state->mem64 + state->memsize, input, len);
                    state->memsize += (uint)len;
                    return XXH_errorcode.XXH_OK;
                }

                if (state->memsize != 0)
                {
                    XXH_memcpy((byte*)state->mem64 + state->memsize, input, 32 - state->memsize);
                    state->v[0] = XXH64_round(state->v[0], XXH_readLE64(state->mem64 + 0));
                    state->v[1] = XXH64_round(state->v[1], XXH_readLE64(state->mem64 + 1));
                    state->v[2] = XXH64_round(state->v[2], XXH_readLE64(state->mem64 + 2));
                    state->v[3] = XXH64_round(state->v[3], XXH_readLE64(state->mem64 + 3));
                    p += 32 - state->memsize;
                    state->memsize = 0;
                }

                if (p + 32 <= bEnd)
                {
                    byte* limit = bEnd - 32;
                    do
                    {
                        state->v[0] = XXH64_round(state->v[0], XXH_readLE64(p));
                        p += 8;
                        state->v[1] = XXH64_round(state->v[1], XXH_readLE64(p));
                        p += 8;
                        state->v[2] = XXH64_round(state->v[2], XXH_readLE64(p));
                        p += 8;
                        state->v[3] = XXH64_round(state->v[3], XXH_readLE64(p));
                        p += 8;
                    }
                    while (p <= limit);
                }

                if (p < bEnd)
                {
                    XXH_memcpy(state->mem64, p, (nuint)(bEnd - p));
                    state->memsize = (uint)(bEnd - p);
                }
            }

            return XXH_errorcode.XXH_OK;
        }

        /*! @ingroup XXH64_family */
        private static ulong ZSTD_XXH64_digest(XXH64_state_s* state)
        {
            ulong h64;
            if (state->total_len >= 32)
            {
                h64 = BitOperations.RotateLeft(state->v[0], 1) + BitOperations.RotateLeft(state->v[1], 7) + BitOperations.RotateLeft(state->v[2], 12) + BitOperations.RotateLeft(state->v[3], 18);
                h64 = XXH64_mergeRound(h64, state->v[0]);
                h64 = XXH64_mergeRound(h64, state->v[1]);
                h64 = XXH64_mergeRound(h64, state->v[2]);
                h64 = XXH64_mergeRound(h64, state->v[3]);
            }
            else
            {
                h64 = state->v[2] + 0x27D4EB2F165667C5UL;
            }

            h64 += state->total_len;
            return XXH64_finalize(h64, (byte*)state->mem64, (nuint)state->total_len, XXH_alignment.XXH_aligned);
        }
    }
}