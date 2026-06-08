using System.Text;
using System.Text.Json;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Rebuilds (reinserts) a .pak from a folder previously produced by
/// <see cref="PakExtractor"/> (manifest.json + indexA/B.bin + the category folders).
///
/// The header, folder table and both index regions are written back verbatim, so
/// those sections are byte-identical to the original. File data is LZ4-compressed
/// (when the original entry was compressed) and the file table offsets/sizes are
/// recomputed. The secondary stream is always stored uncompressed, as in the original.
/// </summary>
public sealed class PakBuilder
{
    /// <summary>Optional progress callback: (processed, total, message).</summary>
    public Action<int, int, string>? Progress { get; set; }

    public void Build(string extractedDirectory, string outputPakPath)
    {
        string manifestPath = Path.Combine(extractedDirectory, PakExtractor.ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found in {extractedDirectory}.");

        var manifest = JsonSerializer.Deserialize<PakManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("Could not read manifest.json.");

        byte[] indexA = File.ReadAllBytes(Path.Combine(extractedDirectory, PakExtractor.IndexAFileName));
        byte[] indexB = File.ReadAllBytes(Path.Combine(extractedDirectory, PakExtractor.IndexBFileName));

        var files = manifest.Files.OrderBy(f => f.Index).ToList();

        using var fs = new FileStream(outputPakPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // --- header ---
        bw.Write(manifest.Magic);
        bw.Write(manifest.Version);
        bw.Write((uint)files.Count);
        bw.Write((uint)manifest.Folders.Count);

        // --- folder table (verbatim) ---
        foreach (var folder in manifest.Folders)
        {
            WriteFixedName(bw, folder.Name, 32);
            bw.Write(folder.IndexOffset);
            bw.Write(folder.IndexSize);
        }

        // --- index regions (verbatim) ---
        bw.Write(indexA);
        bw.Write(indexB);

        // --- reserve the file table; we fill it in after writing the data ---
        long fileTablePos = fs.Position;
        bw.Write(new byte[files.Count * FileEntry.RecordSize]);

        // --- data region ---
        var records = new FileTableRecord[files.Count];
        int total = files.Count;
        int done = 0;
        for (int i = 0; i < files.Count; i++)
        {
            PakManifestFile f = files[i];

            byte[] content = File.ReadAllBytes(Path.Combine(extractedDirectory, ToLocalPath(f.StoredPath)));
            (byte[] stored, uint zsize, uint size) = Encode(content, f.WasCompressed);

            uint offset = (uint)fs.Position;
            bw.Write(stored);

            uint size2 = 0, offset2 = 0;
            if (f.HasSecondary && !string.IsNullOrEmpty(f.SecondaryStoredPath))
            {
                byte[] secondary = File.ReadAllBytes(Path.Combine(extractedDirectory, ToLocalPath(f.SecondaryStoredPath)));
                offset2 = (uint)fs.Position;
                size2 = (uint)secondary.Length;
                bw.Write(secondary);
            }

            records[i] = new FileTableRecord(f.Name, zsize, size, offset, f.TypeId, size2, offset2);

            done++;
            if (done % 200 == 0 || done == total)
                Progress?.Invoke(done, total, $"Reinserting... {done}/{total}");
        }

        // --- back-fill the file table ---
        fs.Seek(fileTablePos, SeekOrigin.Begin);
        foreach (var r in records)
        {
            WriteFixedName(bw, r.Name, 32);
            bw.Write(r.CompressedSize);
            bw.Write(r.Size);
            bw.Write(r.Offset);
            bw.Write(r.TypeId);
            bw.Write(r.SecondarySize);
            bw.Write(r.SecondaryOffset);
        }
    }

    /// <summary>
    /// Compresses the content when the original entry was compressed (falling back to
    /// raw storage if compression does not actually shrink it). Returns the stored
    /// bytes plus the ZSIZE/SIZE to record in the file table.
    /// </summary>
    private static (byte[] stored, uint zsize, uint size) Encode(byte[] content, bool wasCompressed)
    {
        uint size = (uint)content.Length;
        if (!wasCompressed || content.Length == 0)
            return (content, size, size);

        byte[] compressed = Lz4BlockEncoder.Compress(content);
        return compressed.Length < content.Length
            ? (compressed, (uint)compressed.Length, size)
            : (content, size, size);
    }

    private static string ToLocalPath(string stored) => stored.Replace('/', Path.DirectorySeparatorChar);

    private static void WriteFixedName(BinaryWriter bw, string name, int length)
    {
        byte[] buffer = new byte[length];
        int n = Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, length), buffer, 0);
        bw.Write(buffer);
    }

    private readonly record struct FileTableRecord(
        string Name, uint CompressedSize, uint Size, uint Offset, uint TypeId, uint SecondarySize, uint SecondaryOffset);
}
