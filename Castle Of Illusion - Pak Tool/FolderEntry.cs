namespace CastleOfIllusion.PakTool;

/// <summary>
/// Entrada da tabela de pastas (40 bytes): nome de 32 bytes + 2 inteiros.
/// Cada pasta aponta para uma fatia da região de índices (listas de u16 com
/// os índices dos arquivos que pertencem a ela). Para a extração esses dados
/// não são necessários; para a reinserção devem ser preservados sem alteração.
/// </summary>
public sealed class FolderEntry
{
    /// <summary>Tamanho fixo de cada entrada em bytes.</summary>
    public const int RecordSize = 40;

    /// <summary>Nome (hash em hexadecimal ascii), armazenado em campo de 32 bytes.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Offset (cumulativo) dentro da região de índices.</summary>
    public uint IndexOffset { get; set; }

    /// <summary>Tamanho em bytes da fatia de índices desta pasta.</summary>
    public uint IndexSize { get; set; }
}
