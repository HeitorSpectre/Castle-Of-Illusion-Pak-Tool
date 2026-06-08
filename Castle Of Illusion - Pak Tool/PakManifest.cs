namespace CastleOfIllusion.PakTool;

/// <summary>
/// Serialized metadata (manifest.json) produced during extraction and consumed
/// during reinsertion. Holds everything needed to rebuild the .pak byte for byte,
/// except the file data itself (which lives in the category folders).
/// </summary>
public sealed class PakManifest
{
    /// <summary>Schema version of the manifest itself (tool-internal).</summary>
    public int ManifestVersion { get; set; } = 1;

    /// <summary>Name of the original .pak file.</summary>
    public string SourceFileName { get; set; } = string.Empty;

    public uint Magic { get; set; }
    public uint Version { get; set; }

    /// <summary>Folder names, in original order (index offsets/sizes are recomputed on reinsertion).</summary>
    public List<PakManifestFolder> Folders { get; set; } = new();

    /// <summary>File entries, in original order.</summary>
    public List<PakManifestFile> Files { get; set; } = new();
}

public sealed class PakManifestFolder
{
    public string Name { get; set; } = string.Empty;
    public uint IndexOffset { get; set; }
    public uint IndexSize { get; set; }
}

public sealed class PakManifestFile
{
    public int Index { get; set; }

    /// <summary>The real (GUID) name of the file inside the container — used to rebuild the table.</summary>
    public string Name { get; set; } = string.Empty;

    public uint TypeId { get; set; }

    /// <summary>Human-readable name recovered from the XMLs. Empty if it could not be recovered.</summary>
    public string RealName { get; set; } = string.Empty;

    /// <summary>Category (folder) assigned during organization.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Role discovered in the XMLs (e.g. TextureGUID, ModelGuid). Empty if unknown.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Path of the extracted file, relative to the output folder (e.g. "Textures/abc.dds"). Used on reinsertion.</summary>
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>Path of the extracted secondary stream (relative), when present.</summary>
    public string SecondaryStoredPath { get; set; } = string.Empty;

    /// <summary>Uncompressed size of the primary data (reference; recomputed from the extracted file).</summary>
    public uint Size { get; set; }

    /// <summary>Whether the primary data was LZ4-compressed in the original .pak.</summary>
    public bool WasCompressed { get; set; }

    /// <summary>Whether the entry has a secondary stream.</summary>
    public bool HasSecondary { get; set; }

    /// <summary>Size of the secondary stream (stored uncompressed).</summary>
    public uint SecondarySize { get; set; }
}
