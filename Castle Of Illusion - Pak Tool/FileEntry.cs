namespace CastleOfIllusion.PakTool;

/// <summary>
/// Entrada da tabela de arquivos (56 bytes): nome de 32 bytes + 6 inteiros.
/// Ordem dos campos idêntica ao script .bms:
/// ZSIZE, SIZE, OFFSET, DUMMY, SIZE2, OFFSET2.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Tamanho fixo de cada entrada em bytes.</summary>
    public const int RecordSize = 56;

    /// <summary>Índice da entrada na tabela (0-based). Não está no arquivo, é atribuído na leitura.</summary>
    public int Index { get; set; }

    /// <summary>Nome (hash md5-like em hexadecimal). Os nomes originais não são armazenados.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tamanho comprimido (em disco). Se == <see cref="Size"/>, o arquivo está armazenado sem compressão.</summary>
    public uint CompressedSize { get; set; }

    /// <summary>Tamanho descomprimido (real) do arquivo.</summary>
    public uint Size { get; set; }

    /// <summary>Offset absoluto do dado, a partir do início do .pak.</summary>
    public uint Offset { get; set; }

    /// <summary>Campo de tipo/categoria (valores observados de 1 a 16). Preservado sem alteração.</summary>
    public uint TypeId { get; set; }

    /// <summary>Tamanho descomprimido do fluxo secundário (geralmente 0).</summary>
    public uint SecondarySize { get; set; }

    /// <summary>Offset absoluto do fluxo secundário (0 = não possui).</summary>
    public uint SecondaryOffset { get; set; }

    /// <summary>Indica se o dado principal está comprimido com LZ4 (block).</summary>
    public bool IsCompressed => CompressedSize != Size;

    /// <summary>Indica se a entrada possui um segundo fluxo de dados.</summary>
    public bool HasSecondary => SecondaryOffset != 0;
}
