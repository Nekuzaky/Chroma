using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CustomHierarchyHeaders
{
    static readonly Color CountColor = new Color(1f, 1f, 1f, 0.35f);
    const float RowExtra = 40f;
    const float CountSlot = 28f;

    static readonly Dictionary<string, HeaderInfo> headerCache = new Dictionary<string, HeaderInfo>();
    static Dictionary<string, string> presetCache;

    static HierarchyConfig configCache;

    static GUIStyle headerStyle;
    static GUIStyle countStyle;
    static bool stylesReady;

    static CustomHierarchyHeaders()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
    }

    struct HeaderInfo
    {
        public bool isHeader;
        public Color background;
        public Color textColor;
        public TextAnchor alignment;
        public FontStyle fontStyle;
        public int fontSize;
        public string title;
    }

    // Appelee par la fenetre apres une modif de config.
    public static void OnConfigChanged(HierarchyConfig cfg)
    {
        configCache = cfg;
        presetCache = null;
        headerCache.Clear();
        EditorApplication.RepaintHierarchyWindow();
    }

    static HierarchyConfig Config
    {
        get
        {
            if (configCache != null) return configCache;

            string[] guids = AssetDatabase.FindAssets("t:HierarchyConfig");
            if (guids.Length > 0)
                configCache = AssetDatabase.LoadAssetAtPath<HierarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            if (configCache == null)
            {
                // Aucun asset : valeurs par defaut en memoire (non sauvegardees).
                configCache = ScriptableObject.CreateInstance<HierarchyConfig>();
                configCache.ResetToDefaults();
            }
            return configCache;
        }
    }

    static Dictionary<string, string> Presets
    {
        get
        {
            if (presetCache != null) return presetCache;
            presetCache = new Dictionary<string, string>();
            foreach (var p in Config.presets)
                if (!string.IsNullOrEmpty(p.key))
                    presetCache[p.key.ToLowerInvariant()] = p.spec ?? "";
            return presetCache;
        }
    }

    static void EnsureStyles()
    {
        if (stylesReady) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel);
        countStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 9 };
        countStyle.normal.textColor = CountColor;
        stylesReady = true;
    }

    static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint) return;

        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (obj == null) return;

        HierarchyConfig cfg = Config;
        EnsureStyles();

        if (cfg.enableHeaders)
        {
            HeaderInfo info = GetHeaderInfo(obj.name);
            if (info.isHeader)
            {
                DrawHeader(info, selectionRect);
                return;
            }
        }

        DrawRowDecorations(obj, cfg, selectionRect);
    }

    static void DrawHeader(HeaderInfo info, Rect selectionRect)
    {
        Rect fullRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + RowExtra, selectionRect.height);
        EditorGUI.DrawRect(fullRect, info.background);

        headerStyle.normal.textColor = info.textColor;
        headerStyle.alignment = info.alignment;
        headerStyle.fontStyle = info.fontStyle;
        headerStyle.fontSize = info.fontSize; // 0 = taille par defaut

        Rect labelRect = fullRect;
        if (info.alignment == TextAnchor.MiddleLeft) { labelRect.x += 4f; labelRect.width -= 4f; }
        else if (info.alignment == TextAnchor.MiddleRight) { labelRect.width -= 4f; }

        EditorGUI.LabelField(labelRect, info.title, headerStyle);
    }

    static void DrawRowDecorations(GameObject obj, HierarchyConfig cfg, Rect rect)
    {
        bool wantTint = cfg.enableRowTint;
        bool wantCount = cfg.enableChildCount;
        if (!wantTint && !wantCount) return;

        if (wantTint && TryGetTint(obj, cfg, out Color tint))
        {
            Rect rowRect = new Rect(rect.x, rect.y, rect.width + RowExtra, rect.height);
            EditorGUI.DrawRect(rowRect, tint);
        }

        if (wantCount)
        {
            int childCount = obj.transform.childCount;
            if (childCount > 0)
            {
                Rect countRect = new Rect(rect.xMax - CountSlot - 2f, rect.y, CountSlot, rect.height);
                EditorGUI.LabelField(countRect, "(" + childCount + ")", countStyle);
            }
        }
    }

    // Un seul passage : premiere regle active (alpha > 0) dont le composant est present.
    static bool TryGetTint(GameObject obj, HierarchyConfig cfg, out Color tint)
    {
        tint = default;
        var rules = cfg.componentRules;
        if (rules == null || rules.Length < 7) return false;

        if (obj.TryGetComponent<Camera>(out _)          && Rule(rules[0], ref tint)) return true;
        if (obj.TryGetComponent<Light>(out _)           && Rule(rules[1], ref tint)) return true;
        if (obj.TryGetComponent<AudioSource>(out _)     && Rule(rules[2], ref tint)) return true;
        if (obj.TryGetComponent<Canvas>(out _)          && Rule(rules[3], ref tint)) return true;
        if (obj.TryGetComponent<ParticleSystem>(out _)  && Rule(rules[4], ref tint)) return true;
        if (obj.TryGetComponent<Rigidbody>(out _)       && Rule(rules[5], ref tint)) return true;
        if (obj.TryGetComponent<Animator>(out _)        && Rule(rules[6], ref tint)) return true;
        return false;
    }

    static bool Rule(HierarchyConfig.ComponentRule r, ref Color tint)
    {
        if (r == null || !r.enabled || r.tint.a <= 0f) return false;
        tint = r.tint;
        return true;
    }

    static HeaderInfo GetHeaderInfo(string name)
    {
        if (headerCache.TryGetValue(name, out HeaderInfo cached)) return cached;
        if (headerCache.Count > 1024) headerCache.Clear();

        HeaderInfo info = ParseHeader(name);
        headerCache[name] = info;
        return info;
    }

    static HeaderInfo ParseHeader(string value)
    {
        HeaderInfo info = new HeaderInfo
        {
            isHeader = false,
            background = Color.clear,
            textColor = Color.white,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 0,
            title = ""
        };

        if (string.IsNullOrEmpty(value)) return info;

        int eq = value.IndexOf('=');
        if (eq < 0) return info;

        string spec = value.Substring(0, eq).Trim();
        info.title = value.Substring(eq + 1).Trim();
        if (spec.Length == 0) return info;

        char[] separators = { ' ', ',' };
        bool hasBackground = false;

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
                if (TryGetColor(c, out Color tc)) info.textColor = tc;
                continue;
            }

            switch (lower)
            {
                case "left": case "l": info.alignment = TextAnchor.MiddleLeft; continue;
                case "center": case "centre": case "c": info.alignment = TextAnchor.MiddleCenter; continue;
                case "right": case "r": info.alignment = TextAnchor.MiddleRight; continue;
                case "bold": case "b": info.fontStyle = FontStyle.Bold; continue;
                case "italic": case "i": info.fontStyle = FontStyle.Italic; continue;
                case "bolditalic": case "bi": info.fontStyle = FontStyle.BoldAndItalic; continue;
                case "normal": case "n": info.fontStyle = FontStyle.Normal; continue;
            }

            if (lower.Length > 1 && lower[0] == 's' && int.TryParse(lower.Substring(1), out int size))
            {
                info.fontSize = size;
                continue;
            }

            if (TryGetColor(token, out Color bg))
            {
                info.background = bg;
                hasBackground = true;
                continue;
            }

            return info; // token inconnu -> pas un bandeau
        }

        info.isHeader = hasBackground;
        return info;
    }

    static bool TryGetColor(string value, out Color color)
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
}
