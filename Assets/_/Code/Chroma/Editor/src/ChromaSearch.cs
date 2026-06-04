using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chroma.Editor
{
/// <summary>Search and filter GameObjects by name, color, tag, and layer.</summary>
public static class ChromaSearch
{
    public enum FilterType { None, Name, Color, Tag, Layer }

    public class SearchFilter
    {
        public FilterType type;
        public string query;
        public Color? colorFilter;
        public int tagHash;
        public int layerMask;
    }

    /// <summary>Search all GameObjects in the scene with multiple filters.</summary>
    public static List<GameObject> Search(SearchFilter filter)
    {
        var results = new List<GameObject>();
        var config = ChromaConfig.GetOrCreate();
        if (config == null) return results;

        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (GameObject go in allObjects)
        {
            if (MatchesFilter(go, filter, config))
                results.Add(go);
        }

        return results;
    }

    /// <summary>Check if a GameObject matches the search filter.</summary>
    private static bool MatchesFilter(GameObject go, SearchFilter filter, ChromaConfig config)
    {
        if (filter == null || filter.type == FilterType.None)
            return true;

        switch (filter.type)
        {
            case FilterType.Name:
                return go.name.ToLowerInvariant().Contains(filter.query.ToLowerInvariant());

            case FilterType.Tag:
                return go.CompareTag(filter.query);

            case FilterType.Layer:
                return (filter.layerMask & (1 << go.layer)) != 0;

            case FilterType.Color:
                return IsObjectColor(go, filter.colorFilter, config);

            default:
                return true;
        }
    }

    /// <summary>Check if a GameObject has a specific color (from banner or auto-color rules).</summary>
    private static bool IsObjectColor(GameObject go, Color? targetColor, ChromaConfig config)
    {
        if (!targetColor.HasValue) return false;

        // Check ChromaBanner component color
        var banner = go.GetComponent<ChromaBanner>();
        if (banner != null)
        {
            Color bannerColor = banner.m_color;
            if (ColorsMatch(bannerColor, targetColor.Value))
                return true;
        }

        // Check if it matches auto-color rules
        foreach (var rule in config.m_autoColorRules)
        {
            if (rule == null || !rule.m_enabled) continue;

            bool matches = false;
            switch (rule.m_match)
            {
                case AutoColorMatch.Tag:
                    matches = go.CompareTag(rule.m_value);
                    break;
                case AutoColorMatch.Layer:
                    matches = LayerMask.NameToLayer(rule.m_value) == go.layer;
                    break;
                case AutoColorMatch.NamePrefix:
                    matches = go.name.StartsWith(rule.m_value);
                    break;
                case AutoColorMatch.Regex:
                    // Would need regex check here, skipping for simplicity
                    break;
            }

            if (matches && ColorsMatch(rule.m_color, targetColor.Value))
                return true;
        }

        return false;
    }

    /// <summary>Check if two colors are approximately equal (within tolerance).</summary>
    private static bool ColorsMatch(Color a, Color b, float tolerance = 0.1f)
    {
        return Vector4.Distance(a, b) < tolerance;
    }

    /// <summary>Parse a search query string like "red", "name:Enemy", "tag:Player", "layer:UI".</summary>
    public static SearchFilter ParseQuery(string query)
    {
        var filter = new SearchFilter { type = FilterType.None };
        if (string.IsNullOrEmpty(query)) return filter;

        query = query.Trim();

        // Color search: "red", "blue", "#ff0000", etc.
        if (ColorUtility.TryParseHtmlString("#" + query, out Color col) ||
            ChromaHeaders.TryGetColor(query, out col))
        {
            filter.type = FilterType.Color;
            filter.colorFilter = col;
            return filter;
        }

        // Prefixed searches: "name:X", "tag:X", "layer:X"
        if (query.Contains(":"))
        {
            string[] parts = query.Split(':');
            if (parts.Length == 2)
            {
                string prefix = parts[0].ToLowerInvariant().Trim();
                string value = parts[1].Trim();

                switch (prefix)
                {
                    case "name":
                        filter.type = FilterType.Name;
                        filter.query = value;
                        return filter;
                    case "tag":
                        filter.type = FilterType.Tag;
                        filter.query = value;
                        return filter;
                    case "layer":
                        filter.type = FilterType.Layer;
                        filter.layerMask = 1 << LayerMask.NameToLayer(value);
                        return filter;
                }
            }
        }

        // Default: search by name
        filter.type = FilterType.Name;
        filter.query = query;
        return filter;
    }
}
}
