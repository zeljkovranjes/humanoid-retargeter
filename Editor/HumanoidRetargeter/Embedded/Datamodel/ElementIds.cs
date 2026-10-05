#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HumanoidRetargeterDmx
{
    /// <summary>
    /// Where new <see cref="Element"/> ids come from: random GUIDs, as Datamodel.NET always did, unless a
    /// caller opens a <see cref="Deterministic"/> scope. Inside one, ids are derived from a seed and a
    /// running counter, so writing the same content in the same order gives byte-identical DMX files
    /// (an addition for Smart Port's headless use; random ids remain the default).
    /// </summary>
    public static class ElementIds
    {
        sealed class Sequence
        {
            public Sequence(string seed) => Seed = seed;
            public readonly string Seed;
            public long Count;
        }

        static readonly AsyncLocal<Sequence?> current = new();

        /// <summary>True inside a <see cref="Deterministic"/> scope: writers leave out what varies between builds (generator versions).</summary>
        public static bool IsDeterministic => current.Value is not null;

        /// <summary>A new element id: random, or the next id of the innermost deterministic scope.</summary>
        public static Guid Next()
        {
            var sequence = current.Value;
            if (sequence is null) return Guid.NewGuid();
            var n = Interlocked.Increment(ref sequence.Count);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sequence.Seed + ":" + n));
            return new Guid(hash.AsSpan(0, 16));
        }

        /// <summary>Ids created on this async flow until the returned scope is disposed are derived from <paramref name="seed"/>.</summary>
        public static IDisposable Deterministic(string seed)
        {
            var previous = current.Value;
            current.Value = new Sequence(seed ?? "");
            return new Scope(previous);
        }

        sealed class Scope : IDisposable
        {
            readonly Sequence? previous;
            bool disposed;

            public Scope(Sequence? previous) => this.previous = previous;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                current.Value = previous;
            }
        }
    }
}
