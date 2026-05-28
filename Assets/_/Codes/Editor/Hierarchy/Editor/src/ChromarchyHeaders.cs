using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chromarchy.Editor
{
// Chromarchy: color-codes Unity's Hierarchy window. Configured via Tools/Chromarchy
// (ChromarchyConfig asset). Optimized rendering: cached styles, Repaint guard,
// cached parsing, cached gradient textures.
//
// BANNERS: rename a GameObject "<options>=<Title>". Options (space-separated, any order):
//   Background : name (green, red, blue, orange, gray/grey, yellow, mauve, white, black,
//                cyan, purple, pink) OR hex (#FF8800, #f80, #FF8800AA)
//   Gradient   : colorA>colorB  e.g. #1f6feb>#ff8800  or  blue>orange   (add "vertical" for top->bottom)
//   Align      : left | center | right    Style: bold | italic | bolditalic | normal
//   Size       : s<N>    Text: text:<color>    Preset: a key defined in the config (h1, h2, grad...)
// EXTRAS (toggled in the panel): tree guide lines, auto-color rules (Tag/Layer/name-prefix),
//   child-color inheritance, child count "(N)", and bookmark stars (ChromarchyBookmarks).
[InitializeOnLoad]
public static class ChromarchyHeaders
{
    private struct HeaderInfo
    {
        public bool m_isHeader;
        public bool m_isSeparator;
        public string m_separatorCaption;
        public Color m_background;
        public Texture2D m_gradientTex; // non-null => draw gradient instead of solid background
        public Color m_textColor;
        public TextAnchor m_alignment;
        public FontStyle m_fontStyle;
        public int m_fontSize;
        public string m_title;
    }

    #region Publics


    #endregion


    #region Unity API

    static ChromarchyHeaders()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        AssemblyReloadEvents.beforeAssemblyReload += ClearHeaderCache;
    }

    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint) return;

        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (obj == null) return;

        ChromarchyConfig cfg = Config;
        EnsureStyles();

        HeaderInfo info = (cfg.m_enableSeparators || cfg.m_enableHeaders)
            ? GetHeaderInfo(obj.name)
            : default;

        bool bookmarked = ChromarchyBookmarks.IsBookmarked(instanceID);

        if (cfg.m_enableSeparators && info.m_isSeparator)
        {
            DrawSeparator(info, selectionRect, cfg);
        }
        else
        {
            if (cfg.m_enableTreeLines)
                DrawTreeLines(obj, selectionRect, cfg);

            if (cfg.m_enableHeaders && info.m_isHeader)
                DrawHeader(info, selectionRect);
            else
                DrawRowTint(obj, cfg, selectionRect);
        }

        // Drawn last so a banner / separator / tint background never hides it.
        if (bookmarked)
            DrawBookmark(selectionRect);
    }

    #endregion


    #region Main API

    // Called by the window after a config edit.
    public static void OnConfigChanged(ChromarchyConfig cfg)
    {
        _configCache = cfg;
        _presetCache = null;
        ClearHeaderCache();
        EditorApplication.RepaintHierarchyWindow();
    }

    #endregion


    #region Tools and Utilies

    private static void ClearHeaderCache()
    {
        foreach (var kv in _headerCache)
            if (kv.Value.m_gradientTex != null)
                UnityEngine.Object.DestroyImmediate(kv.Value.m_gradientTex);
        _headerCache.Clear();
    }

    private static ChromarchyConfig Config
    {
        get
        {
            if (_configCache != null) return _configCache;

            string[] guids = AssetDatabase.FindAssets("t:ChromarchyConfig");
            if (guids.Length > 0)
                _configCache = AssetDatabase.LoadAssetAtPath<ChromarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            if (_configCache == null)
            {
                // No asset yet: in-memory defaults (not saved to disk).
                _configCache = ScriptableObject.CreateInstance<ChromarchyConfig>();
                _configCache.ResetToDefaults();
            }
            return _configCache;
        }
    }

    private static Dictionary<string, string> Presets
    {
        get
        {
            if (_presetCache != null) return _presetCache;
            _presetCache = new Dictionary<string, string>();
            foreach (var p in Config.m_presets)
                if (!string.IsNullOrEmpty(p.m_key))
                    _presetCache[p.m_key.ToLowerInvariant()] = p.m_spec ?? "";
            return _presetCache;
        }
    }

    private static void EnsureStyles()
    {
        if (_stylesReady) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel);
        _sepStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        _sepContent = new GUIContent();
        _starContent = EditorGUIUtility.IconContent("Favorite Icon");
        _stylesReady = true;
    }

    private static void DrawHeader(HeaderInfo info, Rect selectionRect)
    {
        Rect fullRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + RowExtra, selectionRect.height);

        if (info.m_gradientTex != null)
            GUI.DrawTexture(fullRect, info.m_gradientTex, ScaleMode.StretchToFill);
        else
            EditorGUI.DrawRect(fullRect, info.m_background);

        _headerStyle.normal.textColor = info.m_textColor;
        _headerStyle.alignment = info.m_alignment;
        _headerStyle.fontStyle = info.m_fontStyle;
        _headerStyle.fontSize = info.m_fontSize; // 0 = default size

        Rect labelRect = fullRect;
        if (info.m_alignment == TextAnchor.MiddleLeft) { labelRect.x += 4f; labelRect.width -= 4f; }
        else if (info.m_alignment == TextAnchor.MiddleRight) { labelRect.width -= 4f; }

        EditorGUI.LabelField(labelRect, info.m_title, _headerStyle);
    }

    private static void DrawRowTint(GameObject obj, ChromarchyConfig cfg, Rect rect)
    {
        Color tint;
        bool hasTint = TryGetAutoColor(obj, cfg, out tint)
                       || (cfg.m_enableChildInherit && TryGetInheritedTint(obj, cfg, out tint));

        if (hasTint)
        {
            Rect rowRect = new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height);
            EditorGUI.DrawRect(rowRect, tint);
        }
    }

    // First enabled rule whose Tag / Layer / name-prefix matches the object.
    private static bool TryGetAutoColor(GameObject obj, ChromarchyConfig cfg, out Color color)
    {
        color = default;
        var rules = cfg.m_autoColorRules;
        if (rules == null) return false;

        for (int i = 0; i < rules.Count; i++)
        {
            ChromarchyConfig.AutoColorRule r = rules[i];
            if (r == null || !r.m_enabled || string.IsNullOrEmpty(r.m_value)) continue;

            bool match = false;
            switch (r.m_match)
            {
                case AutoColorMatch.Tag:        match = obj.tag == r.m_value; break;
                case AutoColorMatch.Layer:      match = obj.layer == LayerMask.NameToLayer(r.m_value); break;
                case AutoColorMatch.NamePrefix: match = obj.name.StartsWith(r.m_value, StringComparison.Ordinal); break;
            }
            if (match) { color = r.m_color; return true; }
        }
        return false;
    }

    // Walks up to the nearest banner ancestor and returns its color at a reduced opacity
    // (constant for Flat, fading per depth for DepthFade).
    private static bool TryGetInheritedTint(GameObject obj, ChromarchyConfig cfg, out Color tint)
    {
        tint = default;
        Transform t = obj.transform.parent;
        int depth = 1;

        while (t != null)
        {
            HeaderInfo info = GetHeaderInfo(t.name);
            if (info.m_isHeader)
            {
                float opacity = cfg.m_childInheritMode == ChildInheritMode.Flat
                    ? cfg.m_childInheritOpacity
                    : cfg.m_childInheritOpacity * Mathf.Pow(1f - cfg.m_childInheritFalloff, depth - 1);

                tint = info.m_background;
                tint.a = Mathf.Clamp01(opacity);
                return tint.a > 0.001f;
            }
            t = t.parent;
            depth++;
        }
        return false;
    }

    // File-explorer style connector lines, drawn in the indent gutter (x < selectionRect.x).
    private static void DrawTreeLines(GameObject obj, Rect rect, ChromarchyConfig cfg)
    {
        Transform t = obj.transform;
        if (t.parent == null) return;

        Color col = cfg.m_treeLineColor;
        const float indent = 14f;
        float midY = rect.y + rect.height * 0.5f;

        // Connector column = one indent step left of the item icon.
        float cx = rect.x - indent + 7f;
        bool isLast = t.GetSiblingIndex() == t.parent.childCount - 1;

        FillRect(cx, rect.y, 1f, midY - rect.y, col);                 // top half down to the elbow
        if (!isLast) FillRect(cx, midY, 1f, rect.yMax - midY, col);   // continues to the next sibling
        FillRect(cx + 1f, midY, (rect.x - 2f) - cx, 1f, col);         // horizontal elbow to the icon

        // Continuation verticals for ancestors that still have siblings below them.
        Transform anc = t.parent;
        int k = 1;
        while (anc.parent != null)
        {
            if (anc.GetSiblingIndex() < anc.parent.childCount - 1)
                FillRect(cx - k * indent, rect.y, 1f, rect.height, col);
            anc = anc.parent;
            k++;
        }
    }

    private static void DrawBookmark(Rect rect)
    {
        Rect starRect = new Rect(rect.xMax - 16f, rect.y + (rect.height - 14f) * 0.5f, 14f, 14f);
        if (_starContent != null && _starContent.image != null)
            GUI.DrawTexture(starRect, _starContent.image, ScaleMode.ScaleToFit);
        else
            EditorGUI.DrawRect(starRect, new Color(0.95f, 0.78f, 0.20f, 0.9f)); // fallback marker
    }

    // Thin full-width divider (optional centered caption) that replaces the native "---..." label.
    private static void DrawSeparator(HeaderInfo info, Rect rect, ChromarchyConfig cfg)
    {
        // Paint over the row to hide the native "---..." text.
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height), cfg.m_separatorFillColor);

        Color line = cfg.m_separatorColor;
        float midY = rect.y + rect.height * 0.5f;
        float left = rect.x;
        float right = rect.xMax + RowExtra;

        if (string.IsNullOrEmpty(info.m_separatorCaption))
        {
            FillRect(left, midY, right - left, 1f, line);
            return;
        }

        _sepStyle.fontStyle = cfg.m_separatorBold
            ? (cfg.m_separatorItalic ? FontStyle.BoldAndItalic : FontStyle.Bold)
            : (cfg.m_separatorItalic ? FontStyle.Italic : FontStyle.Normal);
        _sepStyle.normal.textColor = line;
        _sepContent.text = cfg.m_separatorUppercase ? info.m_separatorCaption.ToUpperInvariant() : info.m_separatorCaption;
        Vector2 size = _sepStyle.CalcSize(_sepContent);
        float center = (left + right) * 0.5f;
        float capStart = center - size.x * 0.5f;
        float capEnd = center + size.x * 0.5f;

        FillRect(left, midY, (capStart - 6f) - left, 1f, line);
        FillRect(capEnd + 6f, midY, right - (capEnd + 6f), 1f, line);

        Rect capRect = new Rect(capStart, rect.y, size.x, rect.height);
        GUI.Label(capRect, _sepContent, _sepStyle);
    }

    private static void FillRect(float x, float y, float w, float h, Color c)
    {
        if (w <= 0f || h <= 0f) return;
        EditorGUI.DrawRect(new Rect(x, y, w, h), c);
    }

    private static HeaderInfo GetHeaderInfo(string name)
    {
        if (_headerCache.TryGetValue(name, out HeaderInfo cached)) return cached;
        if (_headerCache.Count > 1024) ClearHeaderCache();

        HeaderInfo info = ParseHeader(name);
        _headerCache[name] = info;
        return info;
    }

    private static HeaderInfo ParseHeader(string value)
    {
        HeaderInfo info = new HeaderInfo
        {
            m_isHeader = false,
            m_isSeparator = false,
            m_separatorCaption = "",
            m_background = Color.clear,
            m_gradientTex = null,
            m_textColor = Color.white,
            m_alignment = TextAnchor.MiddleCenter,
            m_fontStyle = FontStyle.Bold,
            m_fontSize = 0,
            m_title = ""
        };

        if (string.IsNullOrEmpty(value)) return info;

        string trimmed = value.Trim();
        if (trimmed.StartsWith("---", StringComparison.Ordinal) || trimmed.StartsWith("___", StringComparison.Ordinal))
        {
            info.m_isSeparator = true;
            info.m_separatorCaption = trimmed.Trim('-', '_', ' ');
            return info;
        }

        int eq = value.IndexOf('=');
        if (eq < 0) return info;

        string spec = value.Substring(0, eq).Trim();
        info.m_title = value.Substring(eq + 1).Trim();
        if (spec.Length == 0) return info;

        char[] separators = { ' ', ',' };
        bool hasBackground = false;
        bool hasGradient = false;
        Color colorB = Color.clear;
        bool vertical = false;

        List<string> tokens = new List<string>();
        foreach (string raw in spec.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Presets.TryGetValue(raw.ToLowerInvariant(), out string expansion))
                tokens.AddRange(expansion.Split(separators, StringSplitOptions.RemoveEmptyEntries));
            else
                tokens.Add(raw);
        }

        foreach (string token in tokens)
        {
            string lower = token.ToLowerInvariant();

            if (lower.StartsWith("text:") || lower.StartsWith("t:"))
            {
                string c = token.Substring(token.IndexOf(':') + 1);
                if (TryGetColor(c, out Color tc)) info.m_textColor = tc;
                continue;
            }

            switch (lower)
            {
                case "left": case "l": info.m_alignment = TextAnchor.MiddleLeft; continue;
                case "center": case "centre": case "c": info.m_alignment = TextAnchor.MiddleCenter; continue;
                case "right": case "r": info.m_alignment = TextAnchor.MiddleRight; continue;
                case "bold": case "b": info.m_fontStyle = FontStyle.Bold; continue;
                case "italic": case "i": info.m_fontStyle = FontStyle.Italic; continue;
                case "bolditalic": case "bi": info.m_fontStyle = FontStyle.BoldAndItalic; continue;
                case "normal": case "n": info.m_fontStyle = FontStyle.Normal; continue;
                case "vertical": case "vert": vertical = true; continue;
            }

            if (lower.Length > 1 && lower[0] == 's' && int.TryParse(lower.Substring(1), out int size))
            {
                info.m_fontSize = size;
                continue;
            }

            // Gradient token "A>B"
            int gt = token.IndexOf('>');
            if (gt > 0 && gt < token.Length - 1)
            {
                if (TryGetColor(token.Substring(0, gt), out Color ca) && TryGetColor(token.Substring(gt + 1), out Color cb))
                {
                    info.m_background = ca;
                    colorB = cb;
                    hasGradient = true;
                    hasBackground = true;
                    continue;
                }
                return info; // malformed gradient -> not a banner
            }

            if (TryGetColor(token, out Color bg))
            {
                info.m_background = bg;
                hasBackground = true;
                continue;
            }

            return info; // unknown token -> not a banner
        }

        info.m_isHeader = hasBackground;
        if (info.m_isHeader && hasGradient)
            info.m_gradientTex = BuildGradient(info.m_background, colorB, vertical);
        return info;
    }

    private static Texture2D BuildGradient(Color a, Color b, bool vertical)
    {
        const int n = 64;
        var tex = new Texture2D(vertical ? 1 : n, vertical ? n : 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            Color c = Color.Lerp(a, b, t);
            if (vertical) tex.SetPixel(0, n - 1 - i, c); // a at top, b at bottom
            else tex.SetPixel(i, 0, c);                  // a at left, b at right
        }

        tex.Apply();
        return tex;
    }

    private static bool TryGetColor(string value, out Color color)
    {
        color = Color.clear;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();

        switch (value.ToLowerInvariant())
        {
            case "green":  color = new Color(0.10f, 0.65f, 0.10f); return true;
            case "red":    color = new Color(0.75f, 0.10f, 0.10f); return true;
            case "blue":   color = new Color(0.15f, 0.45f, 0.90f); return true;
            case "orange": color = new Color(0.90f, 0.50f, 0.05f); return true;
            case "gray":
            case "grey":   color = new Color(0.45f, 0.45f, 0.45f); return true;
            case "yellow": color = new Color(0.80f, 0.78f, 0.25f); return true;
            case "mauve":  color = new Color(0.50f, 0.00f, 1.00f); return true;
            case "white":  color = Color.white; return true;
            case "black":  color = Color.black; return true;
            case "cyan":   color = new Color(0.10f, 0.70f, 0.75f); return true;
            case "purple": color = new Color(0.55f, 0.20f, 0.75f); return true;
            case "pink":   color = new Color(0.90f, 0.35f, 0.60f); return true;
        }

        string html = value[0] == '#' ? value : "#" + value;
        if (ColorUtility.TryParseHtmlString(html, out color)) return true;
        return ColorUtility.TryParseHtmlString(value, out color);
    }

    #endregion


    #region Private and Protected

    private const float RowExtra = 40f;

    private static readonly Dictionary<string, HeaderInfo> _headerCache = new Dictionary<string, HeaderInfo>();
    private static Dictionary<string, string> _presetCache;
    private static ChromarchyConfig _configCache;

    private static GUIStyle _headerStyle;
    private static GUIStyle _sepStyle;
    private static GUIContent _sepContent;
    private static GUIContent _starContent;
    private static bool _stylesReady;

    #endregion
}
}
