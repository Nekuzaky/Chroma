using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomHierarchy.Editor
{
// Customizes Unity's Hierarchy window. Configured via Tools/Custom Hierarchy
// (HierarchyConfig asset). Optimized rendering: cached styles, Repaint guard,
// cached parsing, cached gradient textures.
//
// BANNERS: rename a GameObject "<options>=<Title>". Options (space-separated, any order):
//   Background : name (green, red, blue, orange, gray/grey, yellow, mauve, white, black,
//                cyan, purple, pink) OR hex (#FF8800, #f80, #FF8800AA)
//   Gradient   : colorA>colorB  e.g. #1f6feb>#ff8800  or  blue>orange   (add "vertical" for top->bottom)
//   Align      : left | center | right    Style: bold | italic | bolditalic | normal
//   Size       : s<N>    Text: text:<color>    Preset: a key defined in the config (h1, h2, grad...)
// ROW DECORATIONS (normal objects): child count "(N)" on the right. Toggle from the panel.
[InitializeOnLoad]
public static class CustomHierarchyHeaders
{
    private struct HeaderInfo
    {
        public bool m_isHeader;
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

    static CustomHierarchyHeaders()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        AssemblyReloadEvents.beforeAssemblyReload += ClearHeaderCache;
    }

    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint) return;

        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (obj == null) return;

        HierarchyConfig cfg = Config;
        EnsureStyles();

        if (cfg.m_enableHeaders)
        {
            HeaderInfo info = GetHeaderInfo(obj.name);
            if (info.m_isHeader)
            {
                DrawHeader(info, selectionRect);
                return;
            }
        }

        DrawRowDecorations(obj, cfg, selectionRect);
    }

    #endregion


    #region Main API

    // Called by the window after a config edit.
    public static void OnConfigChanged(HierarchyConfig cfg)
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

    private static HierarchyConfig Config
    {
        get
        {
            if (_configCache != null) return _configCache;

            string[] guids = AssetDatabase.FindAssets("t:HierarchyConfig");
            if (guids.Length > 0)
                _configCache = AssetDatabase.LoadAssetAtPath<HierarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            if (_configCache == null)
            {
                // No asset yet: in-memory defaults (not saved to disk).
                _configCache = ScriptableObject.CreateInstance<HierarchyConfig>();
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
        _countStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 9 };
        _countStyle.normal.textColor = CountColor;
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

    private static void DrawRowDecorations(GameObject obj, HierarchyConfig cfg, Rect rect)
    {
        if (!cfg.m_enableChildCount) return;

        int childCount = obj.transform.childCount;
        if (childCount > 0)
        {
            Rect countRect = new Rect(rect.xMax - CountSlot - 2f, rect.y, CountSlot, rect.height);
            EditorGUI.LabelField(countRect, "(" + childCount + ")", _countStyle);
        }
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
            m_background = Color.clear,
            m_gradientTex = null,
            m_textColor = Color.white,
            m_alignment = TextAnchor.MiddleCenter,
            m_fontStyle = FontStyle.Bold,
            m_fontSize = 0,
            m_title = ""
        };

        if (string.IsNullOrEmpty(value)) return info;

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
    private const float CountSlot = 28f;
    private static readonly Color CountColor = new Color(1f, 1f, 1f, 0.35f);

    private static readonly Dictionary<string, HeaderInfo> _headerCache = new Dictionary<string, HeaderInfo>();
    private static Dictionary<string, string> _presetCache;
    private static HierarchyConfig _configCache;

    private static GUIStyle _headerStyle;
    private static GUIStyle _countStyle;
    private static bool _stylesReady;

    #endregion
}
}
