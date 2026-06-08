namespace CastleOfIllusion.PakTool;

/// <summary>
/// Identifies the type of each file extracted from the .pak. Because the names
/// are GUIDs (no original names are stored), identification uses two signals:
///
///   1. The content signature (magic bytes) — decides the real EXTENSION and,
///      for concrete formats (textures, audio, shaders, known XML), the CATEGORY.
///   2. The GUID "role" discovered in the game XMLs (e.g. ModelGuid, AnimGUID) —
///      used only to disambiguate binary blobs that share the same signature
///      (models vs animations) and to classify generic XML.
/// </summary>
public static class FileTypeDetector
{
    // Folder (category) names.
    public const string CatTextures  = "Textures";
    public const string CatMaterials = "Materials";
    public const string CatModels    = "Models";
    public const string CatAnims     = "Animations";
    public const string CatAudio     = "Audio";
    public const string CatShaders   = "Shaders";
    public const string CatParticles = "Particles";
    public const string CatPrefabs   = "Prefabs";
    public const string CatMixers    = "Mixers";
    public const string CatXml       = "Xml";
    public const string CatOther     = "Other";

    /// <summary>
    /// Determines category + extension from the already-decompressed content and
    /// the optional role discovered in the XMLs.
    /// </summary>
    public static AssetType Detect(ReadOnlySpan<byte> data, string? role)
    {
        // Concrete content types: the signature decides the category.
        if (StartsWith(data, "DDS ")) return new AssetType(CatTextures, ".dds");
        if (HasMagic(data, 0xB8, 0x45, 0xF2, 0x17)) return new AssetType(CatAudio, ".snd");
        if (HasMagic(data, 0xA3, 0xD7, 0x01, 0x41)) return new AssetType(CatShaders, ".shader");

        ReadOnlySpan<byte> xml = SkipBom(data);
        if (StartsWith(xml, "<Material")) return new AssetType(CatMaterials, ".xml");
        if (StartsWith(xml, "<Entity")) return new AssetType(CatPrefabs, ".xml");
        if (StartsWith(xml, "<mReverb")) return new AssetType(CatAudio, ".xml");
        if (StartsWith(xml, "<"))
        {
            // Generic XML: the role tells us what kind of resource it is.
            string cat = CategoryFromRole(role) ?? CatXml;
            return new AssetType(cat, ".xml");
        }

        // Binary blob families: first u16 == 3 or 4 = models and animations.
        if (data.Length >= 2 && (data[0] == 0x04 || data[0] == 0x03) && data[1] == 0x00)
        {
            string cat = CategoryFromRole(role) ?? CatModels;
            return new AssetType(cat, ExtensionForCategory(cat));
        }

        // Any other binary: fall back to the role (e.g. some meshes start with 0x00).
        string? roleCat = CategoryFromRole(role);
        return roleCat is null
            ? AssetType.Unknown
            : new AssetType(roleCat, ExtensionForCategory(roleCat));
    }

    /// <summary>Best-guess extension for a category whose content has no standard signature.</summary>
    private static string ExtensionForCategory(string category) => category switch
    {
        CatModels => ".model",
        CatAnims => ".anim",
        CatAudio => ".snd",
        CatShaders => ".shader",
        _ => ".bin",
    };

    /// <summary>Maps the role (Name= attribute next to the GUID in the XML) to a category.</summary>
    private static string? CategoryFromRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return null;
        string r = role.ToLowerInvariant();

        if (r.Contains("texture")) return CatTextures;
        if (r.Contains("material")) return CatMaterials;
        if (r.Contains("model")) return CatModels;
        if (r.Contains("anim")) return CatAnims;
        if (r.Contains("shader")) return CatShaders;
        if (r.Contains("particle")) return CatParticles;
        if (r.Contains("prefab") || r.Contains("nextlevel")) return CatPrefabs;
        if (r.Contains("mixer") || r.Contains("duck")) return CatMixers;
        if (r.Contains("reverb") || r.Contains("sound") || r.Contains("audio") ||
            r.Contains("speech") || r.Contains("voice")) return CatAudio;

        return null;
    }

    // ----------------------------------------------------------------- helpers

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> data)
        => data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF
            ? data[3..]
            : data;

    private static bool StartsWith(ReadOnlySpan<byte> data, string ascii)
    {
        if (data.Length < ascii.Length) return false;
        for (int i = 0; i < ascii.Length; i++)
            if (data[i] != (byte)ascii[i]) return false;
        return true;
    }

    private static bool HasMagic(ReadOnlySpan<byte> data, byte b0, byte b1, byte b2, byte b3)
        => data.Length >= 4 && data[0] == b0 && data[1] == b1 && data[2] == b2 && data[3] == b3;
}
