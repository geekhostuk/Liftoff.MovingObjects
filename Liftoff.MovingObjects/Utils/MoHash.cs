namespace Liftoff.MovingObjects.Utils;

// A tiny deterministic hash, used to derive "random-looking" per-object values that are identical on
// every client without any networking.
//
// Why not UnityEngine.Random: it draws from (and advances) the game's global RNG stream, so using it
// would both give a different answer per client and perturb the game's own sequence as a side effect.
// Why not System.Random: its algorithm is explicitly implementation-defined — .NET makes no promise
// that the same seed yields the same sequence across runtimes or versions, so it can't be relied on
// to agree between two players' machines.
//
// These few lines are fully specified, so the same seed provably produces the same value everywhere,
// which is the entire point.
internal static class MoHash
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    // FNV-1a over the seed's bytes, then an xorshift finalizer to break up the poor avalanche FNV
    // shows on small, near-sequential inputs — instanceIDs are typically 1, 2, 3…, and without this
    // adjacent objects would get suspiciously similar offsets rather than a scattered spread.
    internal static uint Hash(int seed)
    {
        var value = (uint)seed;
        var hash = FnvOffsetBasis;

        for (var i = 0; i < 4; i++)
        {
            hash ^= (value >> (i * 8)) & 0xFF;
            hash *= FnvPrime;
        }

        hash ^= hash << 13;
        hash ^= hash >> 17;
        hash ^= hash << 5;
        return hash;
    }

    // Deterministic value in [0, 1). Divides by 2^32 so the result can never reach 1.0 exactly,
    // keeping callers' [0, max) ranges half-open as they'd expect from Random.Range.
    internal static float Unit(int seed)
    {
        return Hash(seed) / 4294967296f;
    }
}
