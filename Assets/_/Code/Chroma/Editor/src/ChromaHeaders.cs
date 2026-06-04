using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
/// <summary>
/// Core Chroma hierarchy rendering system. Hooks into OnHierarchyGUI to draw colored banners,
/// separators, tree guide lines, auto-color rules, zebra striping, RGB mode, and child-count labels.
/// Banners are specified in GameObject names (e.g., "blue center bold=Title") or via ChromaBanner components.
/// Configuration: ChromaConfig asset. Optimizations: cached styles, parsed specs, gradient textures, repaint guard.
/// </summary>
[InitializeOnLoad]
public static class ChromaHeaders
{
    private struct HeaderInfo
    {
        public bool m_isHeader;
        public bool m_isSeparator;
        public string m_separatorCaption;
        public Color m_background;
        public bool m_noBackground;     // "nobg": text-only header, row masked with the theme color
        public Texture2D m_gradientTex; // non-null => draw gradient instead of solid background
        public Color m_textColor;
        public TextAnchor m_alignment;
        public FontStyle m_fontStyle;
        public int m_fontSize;
        public string m_title;
    }

    #region Public


    #endregion


    #region Unity API

    static ChromaHeaders()
    {
        // Prevent duplicate subscriptions on assembly reload
        EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;

        AssemblyReloadEvents.beforeAssemblyReload -= ClearHeaderCache;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearComponentCache;
        EditorApplication.hierarchyChanged -= ClearComponentCache;
#if UNITY_EDITOR
        ChromaBanner.Changed -= OnComponentChanged;
#endif

        AssemblyReloadEvents.beforeAssemblyReload += ClearHeaderCache;
        AssemblyReloadEvents.beforeAssemblyReload += ClearComponentCache;
        EditorApplication.hierarchyChanged += ClearComponentCache;
#if UNITY_EDITOR
        ChromaBanner.Changed += OnComponentChanged;
#endif
        EditorApplication.delayCall += () => EnsureRgbPump(Config);
    }

    /// <summary>Renders colored banners, separators, tree lines, and tints for a single hierarchy row.</summary>
    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint) return;

        // InstanceIDToObject is obsolete in Unity 6000.2+ (use EntityIdToObject), but it exists on
        // every Unity version while EntityIdToObject doesn't — keep it for portability, mute the warning.
#pragma warning disable 618
        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#pragma warning restore 618
        if (obj == null) return;

        ChromaConfig cfg = Config;
        EnsureStyles();

        // GameObject.name allocates a string per access; fetch it once and reuse.
        string goName = obj.name;

        // A ChromaBanner component takes precedence over name-based banners and keeps the name clean.
        // compInfo is pre-assigned so the short-circuited && (when headers are disabled) can't leave
        // it unassigned.
        HeaderInfo compInfo = default;
        bool hasComp = cfg.m_enableHeaders && TryGetComponentInfo(instanceID, obj, goName, out compInfo);

        HeaderInfo info = (!hasComp && (cfg.m_enableSeparators || cfg.m_enableHeaders))
            ? GetHeaderInfo(goName)
            : default;

        bool bookmarked = ChromaBookmarks.IsBookmarked(instanceID);

        // Lowest layer: everything else paints over it.
        if (cfg.m_zebra)
            DrawZebra(selectionRect, cfg);

        if (hasComp)
        {
            if (cfg.m_enableTreeLines)
                DrawTreeLines(obj, selectionRect, cfg);
            DrawHeader(compInfo, selectionRect);
        }
        else if (cfg.m_enableSeparators && info.m_isSeparator)
        {
            DrawSeparator(info, selectionRect, cfg);
        }
        else
        {
            if (cfg.m_enableTreeLines)
                DrawTreeLines(obj, selectionRect, cfg);

            if (cfg.m_enableHeaders && info.m_isHeader)
                DrawHeader(info, selectionRect);
            else if (cfg.m_rgbMode)
                DrawRgbTint(selectionRect, cfg);
            else
                DrawRowTint(obj, cfg, selectionRect, goName);
        }

        // Right-side indicators, drawn last so a banner / separator / tint never hides them.
        // Slots fill from the far right: bookmark star, then the missing-script warning to its left;
        // the child count is right-padded to clear whatever icons are present.
        int rightIcons = 0;
        if (bookmarked)
        {
            DrawBookmark(selectionRect);
            rightIcons++;
        }
        if (cfg.m_warnMissingScripts && HasMissingScripts(instanceID, obj))
        {
            DrawMissingWarning(selectionRect, rightIcons);
            rightIcons++;
        }
        if (cfg.m_showChildCount && !info.m_isSeparator)
            DrawChildCount(obj, selectionRect, 2f + rightIcons * 16f);
    }

    #endregion


    #region Main API

    // Called by the window after a config edit, and by ChromaConfig.OnValidate for
    /// <summary>Notify ChromaHeaders of config changes. Clears caches and repaints the Hierarchy and Project windows.</summary>
    public static void OnConfigChanged(ChromaConfig cfg)
    {
        _configCache = cfg;
        _presetCache = null;
        ClearHeaderCache();
        InvalidateAutoColorCache(cfg);
        EnsureRgbPump(cfg);
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Subscribe/unsubscribe the RGB pump based on whether RGB mode is enabled. Throttled to ~30fps.</summary>
    private static void EnsureRgbPump(ChromaConfig cfg)
    {
        bool want = cfg != null && (cfg.m_rgbMode || cfg.m_rgbFolders);
        if (want && !_rgbPumping)
        {
            EditorApplication.update += RgbPump;
            _rgbPumping = true;
        }
        else if (!want && _rgbPumping)
        {
            EditorApplication.update -= RgbPump;
            _rgbPumping = false;
        }
    }

    /// <summary>Timer callback: repaints Hierarchy/Project windows at ~30fps to animate rainbow colors.</summary>
    private static void RgbPump()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - _lastRgbRepaint < 0.033) return;
        _lastRgbRepaint = now;

        ChromaConfig cfg = Config;
        if (cfg == null || cfg.m_rgbMode) EditorApplication.RepaintHierarchyWindow();
        if (cfg != null && cfg.m_rgbFolders) EditorApplication.RepaintProjectWindow();
    }

    /// <summary>Draw animated rainbow tint for a single row in RGB mode.</summary>
    private static void DrawRgbTint(Rect rect, ChromaConfig cfg)
    {
        float speed = cfg.m_rgbSpeed;
        if (cfg.m_rgbTheme != RGBTheme.Classic) speed *= 0.7f; // Reduce speed by 30% for themed modes

        float hue = Mathf.Repeat(
            (float)(EditorApplication.timeSinceStartup * speed) + rect.y * cfg.m_rgbSpread, 1f);
        float saturation = cfg.m_rgbSaturation;
        float brightness = cfg.m_rgbValue;

        GetRgbThemeHSV(cfg.m_rgbTheme, ref hue, ref saturation, ref brightness);

        Color c = Color.HSVToRGB(hue, saturation, brightness);
        c.a = cfg.m_rgbAlpha;
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height), c);
    }

    /// <summary>Apply theme-specific hue, saturation, and brightness adjustments. Public for use by other modules.</summary>
    public static void ApplyRgbTheme(RGBTheme theme, ref float hue, ref float saturation, ref float brightness)
    {
        GetRgbThemeHSV(theme, ref hue, ref saturation, ref brightness);
    }

    /// <summary>Apply theme-specific hue, saturation, and brightness adjustments.</summary>
    private static void GetRgbThemeHSV(RGBTheme theme, ref float hue, ref float saturation, ref float brightness)
    {
        switch (theme)
        {
            case RGBTheme.Halloween:
                // ULTRA STRICT: Orange -> Violet -> Black only, no other colors
                float halloPhase = hue;
                if (halloPhase < 0.33f)
                {
                    // Orange zone: keep orange hue, smooth brightness
                    hue = 0.08f;
                    saturation = 0.9f;
                    brightness = Mathf.Lerp(0.5f, 1f, halloPhase / 0.33f);
                }
                else if (halloPhase < 0.66f)
                {
                    // Violet/Black zone: violet hue, fade to black
                    float t = (halloPhase - 0.33f) / 0.33f;
                    hue = 0.78f;
                    saturation = Mathf.Lerp(0.9f, 0.05f, t);
                    brightness = Mathf.Lerp(1f, 0.1f, t);
                }
                else
                {
                    // Black zone: nearly black, transition back to orange
                    float t = (halloPhase - 0.66f) / 0.33f;
                    hue = Mathf.Lerp(0.78f, 0.08f, t);
                    saturation = Mathf.Lerp(0.05f, 0.9f, t);
                    brightness = Mathf.Lerp(0.1f, 0.5f, t);
                }
                break;

            case RGBTheme.Christmas:
                // Cycle: Red (0) -> Green (0.33) -> Gold (0.14) -> Red
                if (hue < 0.33f)
                {
                    hue = Mathf.Lerp(0f, 0.33f, hue / 0.33f);
                }
                else if (hue < 0.66f)
                {
                    hue = Mathf.Lerp(0.33f, 0.14f, (hue - 0.33f) / 0.33f);
                }
                else
                {
                    hue = Mathf.Lerp(0.14f, 0f, (hue - 0.66f) / 0.33f);
                }
                saturation = 0.85f;
                brightness = 0.9f;
                break;

            case RGBTheme.Valentine:
                // ULTRA STRICT: Pure Red -> Pure Pink -> Pure Magenta (NO interpolation)
                float valPhase = hue;
                if (valPhase < 0.33f)
                {
                    // Deep Red zone (pure red hue only)
                    hue = 0f;
                    saturation = 1f;
                    brightness = Mathf.Lerp(0.7f, 1f, valPhase / 0.33f);
                }
                else if (valPhase < 0.66f)
                {
                    // Hot Pink zone (pure pink hue only)
                    hue = 0.92f;
                    saturation = 1f;
                    brightness = Mathf.Lerp(1f, 1f, (valPhase - 0.33f) / 0.33f);
                }
                else
                {
                    // Magenta zone (pure magenta hue only)
                    hue = 0.83f;
                    saturation = 1f;
                    brightness = Mathf.Lerp(1f, 0.7f, (valPhase - 0.66f) / 0.33f);
                }
                break;

            case RGBTheme.Classic:
            default:
                break;
        }
    }

    /// <summary>Clear cached layer and regex compilations in auto-color rules.</summary>
    private static void InvalidateAutoColorCache(ChromaConfig cfg)
    {
        if (cfg == null || cfg.m_autoColorRules == null) return;
        for (int i = 0; i < cfg.m_autoColorRules.Count; i++)
        {
            ChromaConfig.AutoColorRule r = cfg.m_autoColorRules[i];
            if (r == null) continue;
            r.m_cachedLayerFor = null;
            r.m_cachedRegexFor = null;
        }
    }

    /// <summary>Extract the first background color from a banner spec, resolving preset references. Used by the window to draw color preview swatches.</summary>
    internal static bool TryGetPreviewColor(string spec, out Color color)
    {
        return TryGetPreviewColorInternal(spec, 0, out color);
    }

    private static bool TryGetPreviewColorInternal(string spec, int depth, out Color color)
    {
        color = new Color(0.3f, 0.3f, 0.3f, 1f);
        if (depth > 5 || string.IsNullOrEmpty(spec)) return false;

        foreach (string raw in spec.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("text:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
                continue;

            string lower = raw.ToLowerInvariant();
            switch (lower)
            {
                case "left": case "l":
                case "center": case "centre": case "c":
                case "right": case "r":
                case "bold": case "b":
                case "italic": case "i":
                case "bolditalic": case "bi":
                case "normal": case "n":
                case "vertical": case "vert":
                case "nobg": case "none":
                    continue;
            }
            if (lower.Length > 1 && lower[0] == 's' && int.TryParse(lower.Substring(1), out _)) continue;

            int gt = raw.IndexOf('>');
            if (gt > 0 && gt < raw.Length - 1)
            {
                if (TryGetColor(raw.Substring(0, gt), out color)) return true;
                continue;
            }

            if (Presets.TryGetValue(lower, out string expansion)
                && TryGetPreviewColorInternal(expansion, depth + 1, out color))
                return true;

            if (TryGetColor(raw, out color)) return true;
        }

        return false;
    }

    // Decomposed banner, used by the window's inline editor. Presets are expanded, so editing a
    // preset-based banner "bakes" the resolved values into explicit tokens on write-back.
    internal struct EditableBanner
    {
        public bool m_valid;
        public string m_title;
        public Color m_colorA;
        public bool m_hasGradient;
        public Color m_colorB;
        public bool m_vertical;
        public int m_align;        // 0 center, 1 left, 2 right
        public int m_style;        // 0 bold, 1 normal, 2 italic, 3 bolditalic
        public int m_size;         // 0 = default
        public Color m_textColor;
        public string m_bgToken;   // raw background token (color or "a>b"), for verbatim preservation
        public string m_textToken; // raw "text:..." token, or null if none
    }

    internal static bool TryParseEditable(string name, out EditableBanner e)
    {
        e = new EditableBanner { m_align = 0, m_style = 0, m_size = 0, m_textColor = Color.white };
        if (string.IsNullOrEmpty(name)) return false;

        int eq = name.IndexOf('=');
        if (eq < 0) return false;

        string spec = name.Substring(0, eq).Trim();
        e.m_title = name.Substring(eq + 1).Trim();
        if (spec.Length == 0) return false;

        char[] sep = { ' ', ',' };
        List<string> tokens = new List<string>();
        foreach (string raw in spec.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Presets.TryGetValue(raw.ToLowerInvariant(), out string expansion))
                tokens.AddRange(expansion.Split(sep, StringSplitOptions.RemoveEmptyEntries));
            else
                tokens.Add(raw);
        }

        bool hasBackground = false;
        foreach (string token in tokens)
        {
            string lower = token.ToLowerInvariant();

            if (lower.StartsWith("text:") || lower.StartsWith("t:"))
            {
                string c = ExtractAfterSeparator(token, ':');
                if (!string.IsNullOrEmpty(c) && TryGetColor(c, out Color tc)) { e.m_textColor = tc; e.m_textToken = token; }
                continue;
            }

            switch (lower)
            {
                case "left": case "l": e.m_align = 1; continue;
                case "center": case "centre": case "c": e.m_align = 0; continue;
                case "right": case "r": e.m_align = 2; continue;
                case "bold": case "b": e.m_style = 0; continue;
                case "normal": case "n": e.m_style = 1; continue;
                case "italic": case "i": e.m_style = 2; continue;
                case "bolditalic": case "bi": e.m_style = 3; continue;
                case "vertical": case "vert": e.m_vertical = true; continue;
                case "nobg": case "none": e.m_bgToken = "nobg"; hasBackground = true; continue;
            }

            if (lower.Length > 1 && lower[0] == 's' && int.TryParse(lower.Substring(1), out int size))
            {
                e.m_size = size;
                continue;
            }

            int gt = token.IndexOf('>');
            if (gt > 0 && gt < token.Length - 1)
            {
                if (TryGetColor(token.Substring(0, gt), out Color ca) && TryGetColor(token.Substring(gt + 1), out Color cb))
                {
                    e.m_colorA = ca;
                    e.m_colorB = cb;
                    e.m_hasGradient = true;
                    e.m_bgToken = token;
                    hasBackground = true;
                    continue;
                }
                return false;
            }

            if (TryGetColor(token, out Color bg))
            {
                e.m_colorA = bg;
                e.m_bgToken = token;
                hasBackground = true;
                continue;
            }

            return false; // unknown token -> not an editable banner
        }

        e.m_valid = hasBackground;
        return e.m_valid;
    }

    // Used by ChromaBuildStripper to drop banner/separator specs from names in built scenes.
    // Returns true when `name` is a recognized Chroma header or separator, and writes the
    // cleaned title/caption into `cleaned`. The bypassing of GetHeaderInfo is intentional:
    // we don't want build-time names to pollute the editor's render cache.
    internal static bool TryStripName(string name, out string cleaned)
    {
        cleaned = name;
        if (string.IsNullOrEmpty(name)) return false;

        HeaderInfo info = ParseHeader(name);
        try
        {
            if (info.m_isSeparator)
            {
                cleaned = info.m_separatorCaption ?? "";
                return true;
            }
            if (info.m_isHeader)
            {
                cleaned = info.m_title ?? "";
                return true;
            }
            return false;
        }
        finally
        {
            // ParseHeader allocates a Texture2D for gradient banners; release immediately.
            if (info.m_gradientTex != null)
                UnityEngine.Object.DestroyImmediate(info.m_gradientTex);
        }
    }

    #endregion


    #region Tools and Utilities

    private static void ClearHeaderCache()
    {
        foreach (var kv in _headerCache)
            if (kv.Value.m_gradientTex != null)
                UnityEngine.Object.DestroyImmediate(kv.Value.m_gradientTex);
        _headerCache.Clear();
    }

    private static void OnComponentChanged()
    {
        ClearComponentCache();
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void ClearComponentCache()
    {
        foreach (var kv in _compCache)
            if (kv.Value.m_gradientTex != null)
                UnityEngine.Object.DestroyImmediate(kv.Value.m_gradientTex);
        _compCache.Clear();
        _missingScriptCache.Clear();
    }

    // Cached per-object lookup of the ChromaBanner component. The cache stores a sentinel
    // (m_isHeader == false) for objects without a banner, so GetComponent runs only on a miss.
    // Invalidated on hierarchyChanged and ChromaBanner.Changed (add / edit / remove).
    private static bool TryGetComponentInfo(int instanceID, GameObject obj, string objName, out HeaderInfo info)
    {
        if (_compCache.TryGetValue(instanceID, out info))
            return info.m_isHeader;

        ChromaBanner b = obj.GetComponent<ChromaBanner>();
        info = (b != null && b.enabled) ? BuildInfoFromComponent(b, objName) : default;
        _compCache[instanceID] = info;
        return info.m_isHeader;
    }

    private static HeaderInfo BuildInfoFromComponent(ChromaBanner b, string objName)
    {
        HeaderInfo info = new HeaderInfo
        {
            m_isHeader = true,
            m_background = b.m_color,
            m_noBackground = !b.m_background,
            m_gradientTex = null,
            m_textColor = b.m_textColor,
            m_alignment = AlignOf(b.m_align),
            m_fontStyle = StyleOf(b.m_fontStyle),
            m_fontSize = b.m_fontSize,
            m_title = string.IsNullOrEmpty(b.m_title) ? objName : b.m_title
        };
        if (b.m_background && b.m_gradient)
            info.m_gradientTex = BuildGradient(b.m_color, b.m_color2, b.m_vertical);
        return info;
    }

    private static TextAnchor AlignOf(ChromaAlign a)
    {
        switch (a)
        {
            case ChromaAlign.Left: return TextAnchor.MiddleLeft;
            case ChromaAlign.Right: return TextAnchor.MiddleRight;
            default: return TextAnchor.MiddleCenter;
        }
    }

    private static FontStyle StyleOf(ChromaFontStyle s)
    {
        switch (s)
        {
            case ChromaFontStyle.Normal: return FontStyle.Normal;
            case ChromaFontStyle.Italic: return FontStyle.Italic;
            case ChromaFontStyle.BoldItalic: return FontStyle.BoldAndItalic;
            default: return FontStyle.Bold;
        }
    }

    private static ChromaConfig Config
    {
        get
        {
            if (_configCache != null) return _configCache;

            string[] guids = AssetDatabase.FindAssets("t:ChromaConfig");
            if (guids.Length > 0)
                _configCache = AssetDatabase.LoadAssetAtPath<ChromaConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            if (_configCache == null)
            {
                // No asset yet: in-memory defaults (not saved to disk).
                _configCache = ScriptableObject.CreateInstance<ChromaConfig>();
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
        _countStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 9 };
        _countStyle.normal.textColor = new Color(1f, 1f, 1f, 0.4f); // set once, not per row
        _sepContent = new GUIContent();
        _starContent = EditorGUIUtility.IconContent("Favorite Icon");
        _warnContent = EditorGUIUtility.IconContent("console.warnicon.sml");
        if (_warnContent != null) _warnContent.tooltip = "Missing script(s) on this GameObject";
        // Approximate Hierarchy row background, used to mask the native name on text-only ("nobg") banners.
        _rowMaskColor = EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.784f, 0.784f, 0.784f);
        _stylesReady = true;
    }

    private static void DrawHeader(HeaderInfo info, Rect selectionRect)
    {
        Rect fullRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + RowExtra, selectionRect.height);

        if (info.m_gradientTex != null)
            GUI.DrawTexture(fullRect, info.m_gradientTex, ScaleMode.StretchToFill);
        else
            // Text-only banner: mask the row with the theme color so the raw name doesn't bleed through.
            EditorGUI.DrawRect(fullRect, info.m_noBackground ? _rowMaskColor : info.m_background);

        _headerStyle.normal.textColor = info.m_textColor;
        _headerStyle.alignment = info.m_alignment;
        _headerStyle.fontStyle = info.m_fontStyle;
        _headerStyle.fontSize = info.m_fontSize; // 0 = default size
        _headerStyle.font = ResolveBannerFont(Config); // null = editor default

        Rect labelRect = fullRect;
        if (info.m_alignment == TextAnchor.MiddleLeft) { labelRect.x += 4f; labelRect.width -= 4f; }
        else if (info.m_alignment == TextAnchor.MiddleRight) { labelRect.width -= 4f; }

        EditorGUI.LabelField(labelRect, info.m_title, _headerStyle);
    }

    /// <summary>
    /// Resolve the font to use for Chroma banner / separator text: an explicit Font asset takes
    /// priority, then a named system font (created on demand and cached), else null (editor default).
    /// </summary>
    internal static Font ResolveBannerFont(ChromaConfig cfg)
    {
        if (cfg == null) return null;
        if (cfg.m_bannerFont != null) return cfg.m_bannerFont;
        return ResolveOSFont(cfg.m_bannerFontName);
    }

    /// <summary>Get (or create + cache) a dynamic font for an installed system font name. Empty = null.</summary>
    private static Font ResolveOSFont(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // A cached dynamic font can be unloaded by the editor; recreate when the entry is gone/destroyed.
        if (_osFontCache.TryGetValue(name, out Font f) && f != null) return f;
        f = Font.CreateDynamicFontFromOSFont(name, 14);
        _osFontCache[name] = f;
        return f;
    }

    private static void DrawRowTint(GameObject obj, ChromaConfig cfg, Rect rect, string objName)
    {
        Color tint;
        bool hasTint = TryGetAutoColor(obj, cfg, objName, out tint)
                       || (cfg.m_enableChildInherit && TryGetInheritedTint(obj, cfg, out tint));

        if (hasTint)
        {
            Rect rowRect = new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height);
            EditorGUI.DrawRect(rowRect, tint);
        }
    }

    // First enabled rule whose Tag / Layer / name-prefix matches the object.
    private static bool TryGetAutoColor(GameObject obj, ChromaConfig cfg, string objName, out Color color)
    {
        color = default;
        var rules = cfg.m_autoColorRules;
        if (rules == null) return false;

        for (int i = 0; i < rules.Count; i++)
        {
            ChromaConfig.AutoColorRule r = rules[i];
            if (r == null || !r.m_enabled || string.IsNullOrEmpty(r.m_value)) continue;

            bool match = false;
            switch (r.m_match)
            {
                case AutoColorMatch.Tag:
                    match = obj.tag == r.m_value;
                    break;
                case AutoColorMatch.Layer:
                    // Resolve once per value change instead of per row.
                    if (r.m_cachedLayerFor != r.m_value)
                    {
                        r.m_cachedLayer = LayerMask.NameToLayer(r.m_value);
                        r.m_cachedLayerFor = r.m_value;
                    }
                    match = r.m_cachedLayer >= 0 && obj.layer == r.m_cachedLayer;
                    break;
                case AutoColorMatch.NamePrefix:
                    match = objName.StartsWith(r.m_value, StringComparison.Ordinal);
                    break;
                case AutoColorMatch.Regex:
                    match = MatchRegex(r, objName);
                    break;
            }
            if (match) { color = r.m_color; return true; }
        }
        return false;
    }

    // Compiles and caches the rule's regex once per pattern change. A pattern that fails to
    // compile is cached as a null regex (with the marker set) so we never retry — and never
    // match — until the user edits it.
    private static bool MatchRegex(ChromaConfig.AutoColorRule r, string name)
    {
        if (r.m_cachedRegexFor != r.m_value)
        {
            r.m_cachedRegexFor = r.m_value;
            try
            {
                r.m_cachedRegex = new System.Text.RegularExpressions.Regex(r.m_value, System.Text.RegularExpressions.RegexOptions.None, System.TimeSpan.FromMilliseconds(500));
            }
            catch (ArgumentException)
            {
                r.m_cachedRegex = null; // invalid pattern -> never matches
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                r.m_cachedRegex = null; // regex too complex -> never matches
            }
        }
        try
        {
            return r.m_cachedRegex != null && r.m_cachedRegex.IsMatch(name);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false; // matching timeout -> no match
        }
    }

    // Walks up to the nearest banner ancestor and returns its color at a reduced opacity
    // (constant for Flat, fading per depth for DepthFade).
    private static bool TryGetInheritedTint(GameObject obj, ChromaConfig cfg, out Color tint)
    {
        tint = default;
        Transform t = obj.transform.parent;
        int depth = 1;

        while (t != null)
        {
            // Consider a component banner first, then a name-based one. Skip "no background"
            // banners — there's no color to inherit.
            GameObject go = t.gameObject;
            string goName = go.name;
            HeaderInfo info = TryGetComponentInfo(go.GetInstanceID(), go, goName, out HeaderInfo ci)
                ? ci
                : GetHeaderInfo(goName);

            if (info.m_isHeader && !info.m_noBackground)
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
    private static void DrawTreeLines(GameObject obj, Rect rect, ChromaConfig cfg)
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

    // Faint alternating row background. Parity is derived from the row's Y so it stays cheap and
    // stateless; it flips when scrolling by an odd number of rows, which is the expected behavior.
    private static void DrawZebra(Rect rect, ChromaConfig cfg)
    {
        int row = Mathf.FloorToInt(rect.y / Mathf.Max(1f, rect.height));
        if ((row & 1) == 0) return;
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height), cfg.m_zebraColor);
    }

    // "(N)" child count, right-aligned. rightPad reserves space for any far-right icons (star, warning).
    private static void DrawChildCount(GameObject obj, Rect rect, float rightPad)
    {
        int n = obj.transform.childCount;
        if (n == 0) return;

        Rect countRect = new Rect(rect.x, rect.y, rect.width - rightPad, rect.height);
        GUI.Label(countRect, CountLabel(n), _countStyle);
    }

    // True if the GameObject has a missing (deleted) MonoBehaviour script. Cached per instanceID;
    // the cache is cleared on hierarchyChanged / assembly reload (see ClearComponentCache).
    private static bool HasMissingScripts(int instanceID, GameObject obj)
    {
        if (_missingScriptCache.TryGetValue(instanceID, out bool has)) return has;
        has = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(obj) > 0;
        _missingScriptCache[instanceID] = has;
        return has;
    }

    // Warning icon for missing scripts. slotFromRight = how many icons already occupy the far edge.
    private static void DrawMissingWarning(Rect rect, int slotFromRight)
    {
        float x = rect.xMax - 16f - slotFromRight * 16f;
        Rect r = new Rect(x, rect.y + (rect.height - 14f) * 0.5f, 14f, 14f);
        if (_warnContent != null && _warnContent.image != null)
            GUI.DrawTexture(r, _warnContent.image, ScaleMode.ScaleToFit);
        else
            EditorGUI.DrawRect(r, new Color(0.95f, 0.4f, 0.1f, 0.9f)); // fallback marker
    }

    // Cached "(N)" labels so the common counts don't reallocate a string every repaint.
    private static string CountLabel(int n)
    {
        if (n >= 0 && n < _countLabels.Length)
            return _countLabels[n] ?? (_countLabels[n] = "(" + n + ")");
        return "(" + n + ")"; // very large counts: rare, accept the alloc
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
    private static void DrawSeparator(HeaderInfo info, Rect rect, ChromaConfig cfg)
    {
        // Paint over the row to hide the native "---..." text.
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height), cfg.m_separatorFillColor);

        Color line = cfg.m_separatorColor;
        float midY = rect.y + rect.height * 0.5f;
        float left = rect.x;
        float right = rect.xMax + RowExtra;

        if (string.IsNullOrEmpty(info.m_separatorCaption))
        {
            DrawStyledLine(left, midY, right - left, line, cfg.m_separatorStyle);
            return;
        }

        _sepStyle.fontStyle = cfg.m_separatorBold
            ? (cfg.m_separatorItalic ? FontStyle.BoldAndItalic : FontStyle.Bold)
            : (cfg.m_separatorItalic ? FontStyle.Italic : FontStyle.Normal);
        _sepStyle.normal.textColor = line;
        _sepStyle.font = ResolveBannerFont(cfg); // null = editor default
        _sepContent.text = cfg.m_separatorUppercase ? info.m_separatorCaption.ToUpperInvariant() : info.m_separatorCaption;
        Vector2 size = _sepStyle.CalcSize(_sepContent);
        float center = (left + right) * 0.5f;
        float capStart = center - size.x * 0.5f;
        float capEnd = center + size.x * 0.5f;

        DrawStyledLine(left, midY, (capStart - 6f) - left, line, cfg.m_separatorStyle);
        DrawStyledLine(capEnd + 6f, midY, right - (capEnd + 6f), line, cfg.m_separatorStyle);

        Rect capRect = new Rect(capStart, rect.y, size.x, rect.height);
        GUI.Label(capRect, _sepContent, _sepStyle);
    }

    // Horizontal divider in one of four styles. `y` is the center line; Double straddles it.
    private static void DrawStyledLine(float x, float y, float width, Color c, SeparatorStyle style)
    {
        if (width <= 0f) return;

        switch (style)
        {
            case SeparatorStyle.Solid:
                FillRect(x, y, width, 1f, c);
                break;

            case SeparatorStyle.Double:
                FillRect(x, y - 1.5f, width, 1f, c);
                FillRect(x, y + 0.5f, width, 1f, c);
                break;

            case SeparatorStyle.Dashed:
                DrawDashes(x, y, width, c, dash: 6f, gap: 4f);
                break;

            case SeparatorStyle.Dotted:
                DrawDashes(x, y, width, c, dash: 2f, gap: 3f);
                break;
        }
    }

    private static void DrawDashes(float x, float y, float width, Color c, float dash, float gap)
    {
        float step = dash + gap;
        float end = x + width;
        for (float px = x; px < end; px += step)
            FillRect(px, y, Mathf.Min(dash, end - px), 1f, c);
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
        bool hasNoBg = false;
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
                string c = ExtractAfterSeparator(token, ':');
                if (!string.IsNullOrEmpty(c) && TryGetColor(c, out Color tc)) info.m_textColor = tc;
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
                case "nobg": case "none": hasNoBg = true; continue;
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
                return default; // malformed gradient -> not a banner
            }

            if (TryGetColor(token, out Color bg))
            {
                info.m_background = bg;
                hasBackground = true;
                continue;
            }

            return default; // unknown token -> not a banner
        }

        // A color makes it a banner; "nobg" makes it a text-only banner (row masked, no fill).
        info.m_isHeader = hasBackground || hasNoBg;
        info.m_noBackground = hasNoBg && !hasBackground;
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

    private static string ExtractAfterSeparator(string text, char separator)
    {
        int idx = text.IndexOf(separator);
        return idx >= 0 && idx < text.Length - 1 ? text.Substring(idx + 1) : "";
    }

    internal static bool TryGetColor(string value, out Color color)
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
        return ColorUtility.TryParseHtmlString(html, out color);
    }

    #endregion


    #region Private and Protected

    private const float RowExtra = 40f;

    private static readonly Dictionary<string, HeaderInfo> _headerCache = new Dictionary<string, HeaderInfo>();
    private static readonly Dictionary<int, HeaderInfo> _compCache = new Dictionary<int, HeaderInfo>();
    private static readonly Dictionary<int, bool> _missingScriptCache = new Dictionary<int, bool>();
    private static readonly Dictionary<string, Font> _osFontCache = new Dictionary<string, Font>();
    private static Dictionary<string, string> _presetCache;
    private static ChromaConfig _configCache;

    private static GUIStyle _headerStyle;
    private static GUIStyle _sepStyle;
    private static GUIStyle _countStyle;
    private static GUIContent _sepContent;
    private static GUIContent _starContent;
    private static GUIContent _warnContent;
    private static Color _rowMaskColor;
    private static bool _stylesReady;
    private static readonly string[] _countLabels = new string[64]; // cached "(N)" child-count labels

    private static bool _rgbPumping;
    private static double _lastRgbRepaint;

    #endregion
}
}
