namespace CastleOfIllusion.PakTool;

/// <summary>
/// Result of identifying a file: the folder (category) it belongs to and the
/// suggested file extension.
/// </summary>
public sealed record AssetType(string Category, string Extension)
{
    /// <summary>Category used when the file could not be identified.</summary>
    public static readonly AssetType Unknown = new("Other", ".dat");
}
