using System.Text.RegularExpressions;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Varre o conteúdo dos arquivos XML do jogo para descobrir o "papel" de cada
/// GUID. Em cada tag XML que contém um atributo <c>Name="..."</c> e um GUID de
/// 32 caracteres hexadecimais, registra um voto: GUID → Name. No fim, o papel de
/// cada arquivo é o Name mais votado.
///
/// Exemplos reais encontrados no data.pak:
///   &lt;Property Name="TextureGUID" ...&gt;39d8cd2d...&lt;/Property&gt;
///   &lt;Property Name="ModelGuid"   ...&gt;0bc50f97...&lt;/Property&gt;
/// </summary>
public sealed partial class XmlRoleScanner
{
    private readonly HashSet<string> _targets;
    private readonly Dictionary<string, Dictionary<string, int>> _votes = new();

    /// <param name="fileHashes">Conjunto de nomes de arquivo (em minúsculo) que nos interessa rotular.</param>
    public XmlRoleScanner(IEnumerable<string> fileHashes)
    {
        _targets = new HashSet<string>(fileHashes.Select(h => h.ToLowerInvariant()));
    }

    /// <summary>Indica se o conteúdo aparenta ser XML (começa com '&lt;' ou BOM UTF-8 + '&lt;').</summary>
    public static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            data = data[3..];
        return data.Length > 0 && data[0] == (byte)'<';
    }

    /// <summary>Processa um documento XML (texto), acumulando os votos GUID → Name.</summary>
    public void Scan(string xml)
    {
        foreach (Match tag in TagRegex().Matches(xml))
        {
            Match name = NameAttrRegex().Match(tag.Value);
            if (!name.Success) continue;
            string role = name.Groups[1].Value;

            foreach (Match guid in GuidRegex().Matches(tag.Value))
            {
                string uid = guid.Value.ToLowerInvariant();
                if (!_targets.Contains(uid)) continue;

                if (!_votes.TryGetValue(uid, out var counter))
                    _votes[uid] = counter = new Dictionary<string, int>();
                counter[role] = counter.GetValueOrDefault(role) + 1;
            }
        }
    }

    /// <summary>Resolve, para cada GUID, o papel (Name) mais votado.</summary>
    public Dictionary<string, string> Resolve()
    {
        var result = new Dictionary<string, string>(_votes.Count);
        foreach (var (uid, counter) in _votes)
        {
            string best = counter.OrderByDescending(kv => kv.Value).First().Key;
            result[uid] = best;
        }
        return result;
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex("Name=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex NameAttrRegex();

    [GeneratedRegex("[0-9a-fA-F]{32}", RegexOptions.Compiled)]
    private static partial Regex GuidRegex();
}
