using System.Text;
using System.Text.Json;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Extracts a .pak, organizing files into per-category folders (Textures, Models,
/// Animations, Audio, Materials, Shaders, Particles, Prefabs, Mixers, Xml, Other),
/// assigning a real extension from the content and a human-readable name recovered
/// from the game XMLs. Also writes:
///   - manifest.json (full metadata for reinsertion)
///   - indexA.bin / indexB.bin (preserved index regions)
///
/// Identification combines the content signature (magic bytes) with the GUID role
/// and name mined from the XMLs (see <see cref="XmlRoleScanner"/> / <see cref="NameResolver"/>).
/// </summary>
public sealed class PakExtractor
{
    public const string ManifestFileName = "manifest.json";
    public const string IndexAFileName = "indexA.bin";
    public const string IndexBFileName = "indexB.bin";
    public const string SecondarySuffix = "_2";

    /// <summary>Optional progress callback: (processed, total, message).</summary>
    public Action<int, int, string>? Progress { get; set; }

    /// <summary>
    /// Extracts the .pak, automatically creating a folder named after the file
    /// (without extension) next to the .pak itself. Returns that folder's path.
    /// </summary>
    public string Extract(string pakPath)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(pakPath)) ?? ".";
        string outputDirectory = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(pakPath));
        Extract(pakPath, outputDirectory);
        return outputDirectory;
    }

    public void Extract(string pakPath, string outputDirectory)
    {
        var pak = PakArchive.Load(pakPath);
        Directory.CreateDirectory(outputDirectory);

        File.WriteAllBytes(Path.Combine(outputDirectory, IndexAFileName), pak.IndexRegionA);
        File.WriteAllBytes(Path.Combine(outputDirectory, IndexBFileName), pak.IndexRegionB);

        using var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // === Pass 1: mine the XMLs for each GUID's role and real name ===
        var (roles, names) = ScanXmls(fs, pak);

        // === Pass 2: categorize, name, and write ===
        var manifest = new PakManifest
        {
            SourceFileName = Path.GetFileName(pakPath),
            Magic = pak.Header.Magic,
            Version = pak.Header.Version,
        };
        foreach (var f in pak.Folders)
            manifest.Folders.Add(new PakManifestFolder { Name = f.Name, IndexOffset = f.IndexOffset, IndexSize = f.IndexSize });

        // Tracks names already used per category folder, to avoid collisions.
        var usedNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        int total = pak.Files.Count;
        int done = 0;
        foreach (var entry in pak.Files)
        {
            byte[] data = ReadPrimary(fs, entry);
            string guidLower = entry.Name.ToLowerInvariant();
            roles.TryGetValue(guidLower, out string? role);
            names.TryGetValue(guidLower, out string? realName);

            AssetType type = FileTypeDetector.Detect(data, role);
            string categoryDir = Path.Combine(outputDirectory, type.Category);
            Directory.CreateDirectory(categoryDir);

            string baseName = UniqueBaseName(usedNames, type.Category, realName, entry.Name);
            string fileName = baseName + type.Extension;
            File.WriteAllBytes(Path.Combine(categoryDir, fileName), data);

            string storedPath = $"{type.Category}/{fileName}";
            string secondaryStored = string.Empty;
            if (entry.HasSecondary)
            {
                byte[] secondary = ReadAt(fs, entry.SecondaryOffset, (int)entry.SecondarySize);
                string secName = baseName + SecondarySuffix + type.Extension;
                File.WriteAllBytes(Path.Combine(categoryDir, secName), secondary);
                secondaryStored = $"{type.Category}/{secName}";
            }

            manifest.Files.Add(new PakManifestFile
            {
                Index = entry.Index,
                Name = entry.Name,
                TypeId = entry.TypeId,
                RealName = realName ?? string.Empty,
                Category = type.Category,
                Role = role ?? string.Empty,
                StoredPath = storedPath,
                SecondaryStoredPath = secondaryStored,
                Size = entry.Size,
                WasCompressed = entry.IsCompressed,
                HasSecondary = entry.HasSecondary,
                SecondarySize = entry.SecondarySize,
            });

            done++;
            Progress?.Invoke(done, total, $"[{done}/{total}] {type.Category}/{fileName}");
        }

        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions));
    }

    /// <summary>Pass 1: read every XML and collect GUID → role and GUID → real name.</summary>
    private (Dictionary<string, string> roles, Dictionary<string, string> names) ScanXmls(FileStream fs, PakArchive pak)
    {
        var roleScanner = new XmlRoleScanner(pak.Files.Select(f => f.Name));
        var nameResolver = new NameResolver(pak.Files.Select(f => f.Name));

        int total = pak.Files.Count;
        int done = 0;
        foreach (var entry in pak.Files)
        {
            byte[] data = ReadPrimary(fs, entry);
            if (XmlRoleScanner.LooksLikeXml(data))
            {
                string xml = Encoding.Latin1.GetString(data);
                roleScanner.Scan(xml);
                nameResolver.Scan(entry.Name.ToLowerInvariant(), xml);
            }

            done++;
            if (done % 200 == 0 || done == total)
                Progress?.Invoke(done, total, $"Analyzing XMLs... {done}/{total}");
        }
        return (roleScanner.Resolve(), nameResolver.Resolve());
    }

    /// <summary>Builds a sanitized, collision-free base name for a file inside its category folder.</summary>
    private static string UniqueBaseName(Dictionary<string, HashSet<string>> usedNames,
        string category, string? realName, string guid)
    {
        if (!usedNames.TryGetValue(category, out var used))
            usedNames[category] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string candidate = Sanitize(realName) is { Length: > 0 } clean ? clean : guid;

        if (!used.Add(candidate))
        {
            // Name taken: disambiguate with a short slice of the GUID, then the full GUID.
            string disambiguated = $"{candidate}_{guid[..Math.Min(8, guid.Length)]}";
            if (!used.Add(disambiguated))
            {
                used.Add(guid);
                disambiguated = guid;
            }
            candidate = disambiguated;
        }
        return candidate;
    }

    /// <summary>Removes characters that are invalid in file names and trims to a sane length.</summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.' or '(' or ')' ? c : '_');

        string result = sb.ToString().Trim().Trim('.', ' ');
        if (result.Length > 100) result = result[..100].Trim();
        return result;
    }

    private static byte[] ReadPrimary(FileStream fs, FileEntry entry)
    {
        byte[] raw = ReadAt(fs, entry.Offset, (int)entry.CompressedSize);
        return entry.IsCompressed
            ? Lz4BlockDecoder.Decompress(raw, (int)entry.Size)
            : raw;
    }

    private static byte[] ReadAt(FileStream fs, long offset, int length)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = fs.Read(buffer, read, length - read);
            if (n <= 0)
                throw new EndOfStreamException($"Unexpected end of file reading offset {offset} ({length} bytes).");
            read += n;
        }
        return buffer;
    }

    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };
}
