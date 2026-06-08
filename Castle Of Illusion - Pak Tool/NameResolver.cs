using System.Text.RegularExpressions;

namespace CastleOfIllusion.PakTool;

/// <summary>
/// Recovers human-readable names for the GUID-named files by mining the game XMLs.
///
/// The original file names are NOT stored in the .pak (the names are GUIDs), but
/// the XMLs carry enough information to reconstruct meaningful names through three
/// complementary channels:
///
///   1. Self name — an XML resource names itself, e.g.
///        &lt;Material&gt;&lt;Name&gt;Mickey Font Material&lt;/Name&gt; ...
///        &lt;Entity Name="Dragon_Boss_Test" ...&gt;
///
///   2. Direct reference — a named owner points at an asset by GUID, e.g. a Material
///      named "cstl_bricks_01" references its texture, or an Entity named
///      "Enemy Toy Plane" references its model/animation. Each referenced GUID is
///      attributed to the nearest NON-generic &lt;Entity&gt; ancestor.
///
///   3. Transitive propagation — once an owner has a name (even one obtained by the
///      channels above), that name flows to the still-nameless assets it references.
///      A prefab "fx_pickup_stopwatch" thus names the particles, meshes and textures
///      it contains. Iterated to a fixed point.
///
/// Generic owner names (Root, ParticleSystem, numbers...) are ignored, so they
/// don't drown the meaningful ones.
/// </summary>
public sealed partial class NameResolver
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "root", "particlesystem", "particlesystemcomponent", "transformcomponent",
        "meshcomponent", "mesh component", "untitled", "entity", "node", "new entity",
        "particle", "scene", "",
    };

    private const int MaxTransitiveIterations = 6;

    private readonly HashSet<string> _files;
    private readonly Dictionary<string, string> _self = new();
    private readonly Dictionary<string, Dictionary<string, int>> _votes = new();
    private readonly Dictionary<string, HashSet<string>> _refs = new();

    public NameResolver(IEnumerable<string> fileHashes)
    {
        _files = new HashSet<string>(fileHashes.Select(h => h.ToLowerInvariant()));
    }

    /// <summary>Processes one XML document (its owner GUID + decoded text).</summary>
    public void Scan(string ownerGuidLower, string xml)
    {
        string body = xml.StartsWith('﻿') ? xml[1..] : xml;
        string head = body.Length > 600 ? body[..600] : body;

        // --- 1. self name ---
        Match selfMatch = NameChildRegex().Match(head);
        string? selfName = selfMatch.Success ? selfMatch.Groups[1].Value.Trim() : null;
        if (selfName is null)
        {
            Match ent = EntityRootRegex().Match(head);
            if (ent.Success) selfName = ent.Groups[1].Value.Trim();
        }
        if (selfName is not null && !IsGeneric(selfName))
            _self[ownerGuidLower] = selfName;

        // --- collect every referenced file GUID (used by direct + transitive channels) ---
        var owned = Referenced(ownerGuidLower);
        foreach (Match g in GuidRegex().Matches(body))
        {
            string guid = g.Value.ToLowerInvariant();
            if (guid != ownerGuidLower && _files.Contains(guid))
                owned.Add(guid);
        }

        // --- 2a. material -> referenced assets ---
        Match mat = MaterialNameRegex().Match(body.Length > 400 ? body[..400] : body);
        if (mat.Success)
        {
            string matName = mat.Groups[1].Value.Trim();
            if (!IsGeneric(matName))
                foreach (string g in owned)
                    Vote(g, matName);
        }

        // --- 2b. entity -> assets, attributed to the nearest non-generic Entity ancestor ---
        if (body.Contains("<Entity", StringComparison.Ordinal))
        {
            var stack = new List<string?>();
            foreach (Match tok in EntityOrGuidRegex().Matches(body))
            {
                string s = tok.Value;
                if (s.StartsWith("<Entity", StringComparison.Ordinal))
                {
                    if (!s.EndsWith("/>", StringComparison.Ordinal))
                    {
                        Match na = NameAttrRegex().Match(s);
                        stack.Add(na.Success ? na.Groups[1].Value.Trim() : null);
                    }
                }
                else if (s.StartsWith("</Entity", StringComparison.Ordinal))
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    string guid = s.ToLowerInvariant();
                    if (guid == ownerGuidLower || !_files.Contains(guid)) continue;
                    string? ancestor = NearestNonGeneric(stack);
                    if (ancestor is not null) Vote(guid, ancestor);
                }
            }
        }
    }

    /// <summary>Resolves the best name per GUID, then propagates names transitively to a fixed point.</summary>
    public Dictionary<string, string> Resolve()
    {
        var result = new Dictionary<string, string>(_self);
        ApplyVotes(result);

        for (int iteration = 0; iteration < MaxTransitiveIterations; iteration++)
        {
            int added = 0;
            foreach (var (owner, referenced) in _refs)
            {
                if (!result.TryGetValue(owner, out string? ownerName) || IsGeneric(ownerName))
                    continue;
                foreach (string guid in referenced)
                {
                    if (result.ContainsKey(guid)) continue;
                    Vote(guid, ownerName);
                    added++;
                }
            }
            if (added == 0) break;
            ApplyVotes(result);
        }
        return result;
    }

    private void ApplyVotes(Dictionary<string, string> result)
    {
        foreach (var (guid, counter) in _votes)
        {
            if (result.ContainsKey(guid)) continue;
            result[guid] = counter.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        }
    }

    private HashSet<string> Referenced(string owner)
    {
        if (!_refs.TryGetValue(owner, out var set))
            _refs[owner] = set = new HashSet<string>();
        return set;
    }

    private void Vote(string guid, string name)
    {
        if (!_votes.TryGetValue(guid, out var counter))
            _votes[guid] = counter = new Dictionary<string, int>();
        counter[name] = counter.GetValueOrDefault(name) + 1;
    }

    private static string? NearestNonGeneric(List<string?> stack)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
            if (stack[i] is { } n && !IsGeneric(n)) return n;
        return null;
    }

    private static bool IsGeneric(string name)
        => Generic.Contains(name) || name.All(char.IsDigit);

    [GeneratedRegex("<Name>([^<]+)</Name>", RegexOptions.Compiled)]
    private static partial Regex NameChildRegex();

    [GeneratedRegex("<Entity\\b[^>]*\\bName=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex EntityRootRegex();

    [GeneratedRegex("<Material>\\s*<Name>([^<]+)</Name>", RegexOptions.Compiled)]
    private static partial Regex MaterialNameRegex();

    [GeneratedRegex("Name=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex NameAttrRegex();

    [GeneratedRegex("[0-9a-fA-F]{32}", RegexOptions.Compiled)]
    private static partial Regex GuidRegex();

    [GeneratedRegex("<Entity\\b[^>]*?/?>|</Entity>|[0-9a-fA-F]{32}", RegexOptions.Compiled)]
    private static partial Regex EntityOrGuidRegex();
}
