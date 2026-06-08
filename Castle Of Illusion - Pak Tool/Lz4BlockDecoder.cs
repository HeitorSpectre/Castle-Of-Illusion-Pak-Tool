namespace CastleOfIllusion.PakTool;

/// <summary>
/// Decodificador do formato LZ4 "raw block" (o mesmo usado pelo QuickBMS via
/// <c>comtype lz4</c> / <c>LZ4_decompress_safe</c>). Implementação própria, sem
/// dependências externas, para manter a ferramenta autocontida.
/// </summary>
public static class Lz4BlockDecoder
{
    /// <summary>
    /// Descomprime um bloco LZ4 cru.
    /// </summary>
    /// <param name="src">Buffer comprimido.</param>
    /// <param name="expectedSize">Tamanho descomprimido esperado (campo SIZE da entrada).</param>
    public static byte[] Decompress(ReadOnlySpan<byte> src, int expectedSize)
    {
        var dst = new byte[expectedSize];
        int produced = Decompress(src, dst);
        if (produced != expectedSize)
            throw new InvalidDataException(
                $"LZ4: descomprimido {produced} bytes, esperado {expectedSize}.");
        return dst;
    }

    /// <summary>
    /// Descomprime para um buffer de destino já alocado. Retorna a quantidade de bytes escritos.
    /// </summary>
    public static int Decompress(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int sIdx = 0;
        int dIdx = 0;
        int sLen = src.Length;
        int dLen = dst.Length;

        while (sIdx < sLen)
        {
            int token = src[sIdx++];

            // --- comprimento dos literais ---
            int literalLength = token >> 4;
            if (literalLength == 15)
            {
                int b;
                do
                {
                    if (sIdx >= sLen)
                        throw new InvalidDataException("LZ4: fim inesperado ao ler comprimento de literais.");
                    b = src[sIdx++];
                    literalLength += b;
                } while (b == 255);
            }

            // --- cópia dos literais ---
            if (literalLength > 0)
            {
                if (sIdx + literalLength > sLen || dIdx + literalLength > dLen)
                    throw new InvalidDataException("LZ4: estouro ao copiar literais.");
                src.Slice(sIdx, literalLength).CopyTo(dst.Slice(dIdx, literalLength));
                sIdx += literalLength;
                dIdx += literalLength;
            }

            // Fim do bloco: o último token só possui literais (sem sequência de match).
            if (sIdx >= sLen)
                break;

            // --- offset do match (2 bytes, little-endian) ---
            if (sIdx + 2 > sLen)
                throw new InvalidDataException("LZ4: fim inesperado ao ler offset.");
            int matchOffset = src[sIdx] | (src[sIdx + 1] << 8);
            sIdx += 2;
            if (matchOffset == 0)
                throw new InvalidDataException("LZ4: offset de match igual a zero.");

            // --- comprimento do match ---
            int matchLength = token & 0x0F;
            if (matchLength == 15)
            {
                int b;
                do
                {
                    if (sIdx >= sLen)
                        throw new InvalidDataException("LZ4: fim inesperado ao ler comprimento de match.");
                    b = src[sIdx++];
                    matchLength += b;
                } while (b == 255);
            }
            matchLength += 4; // mínimo de match (minmatch)

            // --- cópia do match (pode ser sobreposta, byte a byte) ---
            int matchPos = dIdx - matchOffset;
            if (matchPos < 0)
                throw new InvalidDataException("LZ4: offset de match aponta antes do início.");
            if (dIdx + matchLength > dLen)
                throw new InvalidDataException("LZ4: estouro ao copiar match.");

            for (int i = 0; i < matchLength; i++)
                dst[dIdx + i] = dst[matchPos + i];
            dIdx += matchLength;
        }

        return dIdx;
    }
}
