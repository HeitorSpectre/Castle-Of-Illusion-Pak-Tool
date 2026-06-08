namespace CastleOfIllusion.PakTool;

/// <summary>
/// LZ4 "raw block" compressor (the counterpart of <see cref="Lz4BlockDecoder"/>).
/// Self-contained implementation of the standard LZ4 fast algorithm — no external
/// dependencies. The output is a valid LZ4 block: any conforming decoder (this
/// tool's and the game's) reproduces the original bytes exactly.
///
/// Note: LZ4 allows many valid encodings for the same data, so this encoder's
/// output is not expected to be byte-identical to whatever encoder the game used;
/// it is, however, fully lossless (decompress(compress(x)) == x).
/// </summary>
public static class Lz4BlockEncoder
{
    private const int MinMatch = 4;
    private const int LastLiterals = 5;
    private const int MfLimit = 12;
    private const int HashLog = 16;
    private const int HashTableSize = 1 << HashLog;

    /// <summary>Maximum size the compressed output can take for a given input size.</summary>
    public static int MaxCompressedSize(int inputSize) => inputSize + inputSize / 255 + 16;

    /// <summary>Compresses <paramref name="src"/> and returns a right-sized buffer.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> src)
    {
        byte[] dst = new byte[MaxCompressedSize(src.Length)];
        int n = Compress(src, dst);
        return dst[..n];
    }

    /// <summary>Compresses into a caller-provided buffer; returns the number of bytes written.</summary>
    public static int Compress(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int srcLen = src.Length;
        int op = 0;
        int anchor = 0;

        // Too small to hold a match: everything becomes a single literal run.
        if (srcLen < MfLimit + 1)
            return EmitLastLiterals(src, dst, op, anchor, srcLen);

        int[] table = new int[HashTableSize]; // stores position+1 (0 = empty)

        int matchLimit = srcLen - LastLiterals;
        int mfLimit = srcLen - MfLimit;

        int ip = 1;
        int token;

        while (true)
        {
            // --- find the next match ---
            int match;
            int forwardIp = ip;
            int searchMatchNb = 1 << 6;
            int step = 1;
            do
            {
                ip = forwardIp;
                forwardIp += step;
                step = searchMatchNb++ >> 6;
                if (forwardIp > mfLimit) return EmitLastLiterals(src, dst, op, anchor, srcLen);

                uint seq = Read32(src, ip);
                int h = Hash(seq);
                match = table[h] - 1;
                table[h] = ip + 1;
            }
            while (match < 0 || (ip - match) > 65535 || Read32(src, match) != Read32(src, ip));

            // --- extend the match backwards over the pending literals ---
            while (ip > anchor && match > 0 && src[ip - 1] == src[match - 1]) { ip--; match--; }

            // --- emit literals ---
            int litLength = ip - anchor;
            token = op++;
            if (litLength >= 15)
            {
                dst[token] = 15 << 4;
                int l = litLength - 15;
                while (l >= 255) { dst[op++] = 255; l -= 255; }
                dst[op++] = (byte)l;
            }
            else dst[token] = (byte)(litLength << 4);
            for (int i = 0; i < litLength; i++) dst[op++] = src[anchor + i];

            // --- emit match(es) ---
            while (true)
            {
                int offset = ip - match;
                dst[op++] = (byte)offset;
                dst[op++] = (byte)(offset >> 8);

                ip += MinMatch;
                match += MinMatch;
                int matchStart = ip;
                while (ip < matchLimit && src[ip] == src[match]) { ip++; match++; }
                int matchLength = ip - matchStart;

                if (matchLength >= 15)
                {
                    dst[token] += 15;
                    int l = matchLength - 15;
                    while (l >= 255) { dst[op++] = 255; l -= 255; }
                    dst[op++] = (byte)l;
                }
                else dst[token] += (byte)matchLength;

                anchor = ip;
                if (ip > mfLimit) return EmitLastLiterals(src, dst, op, anchor, srcLen);

                // Index the position skipped over (helps find overlapping matches).
                table[Hash(Read32(src, ip - 2))] = (ip - 2) + 1;

                // Try to chain straight into another match with zero literals.
                uint seq = Read32(src, ip);
                int h = Hash(seq);
                match = table[h] - 1;
                table[h] = ip + 1;
                if (match >= 0 && (ip - match) <= 65535 && Read32(src, match) == seq)
                {
                    token = op++;
                    dst[token] = 0;
                    continue;
                }
                // Advance past this position before resuming the main search: its hash
                // slot was just set to ip+1 above, so re-probing it would match itself
                // (offset 0). Mirrors the reference LZ4's "forwardH = hash(++ip)".
                ip++;
                break;
            }
        }
    }

    private static int EmitLastLiterals(ReadOnlySpan<byte> src, Span<byte> dst, int op, int anchor, int srcLen)
    {
        int litLength = srcLen - anchor;
        int token = op++;
        if (litLength >= 15)
        {
            dst[token] = 15 << 4;
            int l = litLength - 15;
            while (l >= 255) { dst[op++] = 255; l -= 255; }
            dst[op++] = (byte)l;
        }
        else dst[token] = (byte)(litLength << 4);
        for (int i = 0; i < litLength; i++) dst[op++] = src[anchor + i];
        return op;
    }

    private static uint Read32(ReadOnlySpan<byte> s, int i)
        => (uint)(s[i] | (s[i + 1] << 8) | (s[i + 2] << 16) | (s[i + 3] << 24));

    private static int Hash(uint sequence)
        => (int)((sequence * 2654435761u) >> (32 - HashLog));
}
