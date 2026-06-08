namespace CastleOfIllusion.PakTool;

/// <summary>
/// Cabeçalho do container (16 bytes, little-endian).
/// Equivale, no script .bms, à leitura de DUMMY / VER / FILES / FOLDERS.
/// </summary>
public sealed class PakHeader
{
    /// <summary>Assinatura do arquivo. Para o data.pak do Castle of Illusion = 0x852901FA.</summary>
    public const uint ExpectedMagic = 0x852901FA;

    public uint Magic { get; set; }

    /// <summary>Versão do formato. Observado = 1 (lido em little-endian, apesar de ser do Xbox 360).</summary>
    public uint Version { get; set; }

    /// <summary>Quantidade de arquivos contidos no container.</summary>
    public uint FileCount { get; set; }

    /// <summary>Quantidade de "pastas" (grupos de índices) descritas na tabela de pastas.</summary>
    public uint FolderCount { get; set; }

    public bool HasValidMagic => Magic == ExpectedMagic;
}
