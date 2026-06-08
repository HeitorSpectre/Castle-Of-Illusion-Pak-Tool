using System.Text;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Representa a estrutura de um arquivo .pak do Castle of Illusion já interpretada.
/// Faz apenas o parsing do cabeçalho e das tabelas; os dados dos arquivos
/// continuam no .pak em disco e são lidos sob demanda pelo extrator.
///
/// Layout (little-endian):
///   [0x00] Header (16 bytes): MAGIC, VER, FILES, FOLDERS
///   [0x10] Tabela de pastas: FOLDERS * 40 bytes
///   [   ?] Região de índices A: S bytes
///   [   ?] Região de índices B: S bytes   (S = soma dos IndexSize das pastas)
///   [   ?] Tabela de arquivos: FILES * 56 bytes
///   [   ?] Região de dados (até o fim do arquivo)
/// </summary>
public sealed class PakArchive
{
    public PakHeader Header { get; private set; } = new();
    public List<FolderEntry> Folders { get; } = new();
    public List<FileEntry> Files { get; } = new();

    /// <summary>Primeira região de índices (preservada como bytes brutos para reinserção).</summary>
    public byte[] IndexRegionA { get; private set; } = Array.Empty<byte>();

    /// <summary>Segunda região de índices (preservada como bytes brutos para reinserção).</summary>
    public byte[] IndexRegionB { get; private set; } = Array.Empty<byte>();

    /// <summary>Offset absoluto onde começa a tabela de arquivos.</summary>
    public long FileTableOffset { get; private set; }

    /// <summary>Offset absoluto onde começa a região de dados (logo após a tabela de arquivos).</summary>
    public long DataOffset { get; private set; }

    /// <summary>Caminho do .pak de origem.</summary>
    public string SourcePath { get; private set; } = string.Empty;

    public static PakArchive Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);
        var pak = new PakArchive { SourcePath = path };
        pak.ReadFrom(br);
        return pak;
    }

    private void ReadFrom(BinaryReader br)
    {
        // --- Cabeçalho ---
        Header = new PakHeader
        {
            Magic = br.ReadUInt32(),
            Version = br.ReadUInt32(),
            FileCount = br.ReadUInt32(),
            FolderCount = br.ReadUInt32(),
        };

        if (!Header.HasValidMagic)
            throw new InvalidDataException(
                $"Assinatura inválida (0x{Header.Magic:X8}). Esperado 0x{PakHeader.ExpectedMagic:X8}. " +
                "O arquivo não parece ser um data.pak do Castle of Illusion.");

        // --- Tabela de pastas ---
        long indexRegionSize = 0;
        for (int i = 0; i < Header.FolderCount; i++)
        {
            var folder = new FolderEntry
            {
                Name = ReadFixedName(br, 32),
                IndexOffset = br.ReadUInt32(),
                IndexSize = br.ReadUInt32(),
            };
            Folders.Add(folder);
            indexRegionSize = folder.IndexOffset + folder.IndexSize; // cumulativo: o último fecha o total
        }

        // --- Duas regiões de índices, cada uma com 'indexRegionSize' bytes ---
        IndexRegionA = br.ReadBytes((int)indexRegionSize);
        IndexRegionB = br.ReadBytes((int)indexRegionSize);

        // --- Tabela de arquivos ---
        FileTableOffset = br.BaseStream.Position;
        for (int i = 0; i < Header.FileCount; i++)
        {
            var entry = new FileEntry
            {
                Index = i,
                Name = ReadFixedName(br, 32),
                CompressedSize = br.ReadUInt32(),
                Size = br.ReadUInt32(),
                Offset = br.ReadUInt32(),
                TypeId = br.ReadUInt32(),
                SecondarySize = br.ReadUInt32(),
                SecondaryOffset = br.ReadUInt32(),
            };
            Files.Add(entry);
        }

        DataOffset = br.BaseStream.Position;
    }

    private static string ReadFixedName(BinaryReader br, int length)
    {
        byte[] raw = br.ReadBytes(length);
        int end = Array.IndexOf(raw, (byte)0);
        if (end < 0) end = length;
        return Encoding.ASCII.GetString(raw, 0, end);
    }
}
