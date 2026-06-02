using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chromarchy.Editor
{
public enum ChildInheritMode { Flat, DepthFade }
public enum AutoColorMatch { Tag, Layer, NamePrefix, Regex }
public enum SeparatorStyle { Solid, Dashed, Dotted, Double }

// Persisted config (project asset, shareable via git) for ChromarchyHeaders.
// Edited through the Tools/Chromarchy window.
public class ChromarchyConfig : ScriptableObject
{
    [System.Serializable]
    public class Preset
    {
        public string m_key;
        public string m_spec;
    }

    [System.Serializable]
    public class AutoColorRule
    {
        public bool m_enabled = true;
        public AutoColorMatch m_match = AutoColorMatch.Tag;
        public string m_value = "";
        public Color m_color = new Color(0.20f, 0.50f, 0.90f, 0.18f);

        // Resolved layer index, cached to avoid LayerMask.NameToLayer per row.
        [System.NonSerialized] internal int m_cachedLayer;
        [System.NonSerialized] internal string m_cachedLayerFor;

        // Compiled regex, cached to avoid recompiling per row. m_cachedRegexFor tracks the
        // pattern it was built from; null regex with a non-null marker means "invalid pattern".
        [System.NonSerialized] internal System.Text.RegularExpressions.Regex m_cachedRegex;
        [System.NonSerialized] internal string m_cachedRegexFor;
    }

    #region Publics

    [Header("Display")]
    public bool m_enableHeaders = true;

    [Header("Tree lines")]
    public bool m_enableTreeLines = true;
    public Color m_treeLineColor = new Color(1f, 1f, 1f, 0.15f);

    [Header("Separators")]
    public bool m_enableSeparators = true;
    public Color m_separatorColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    public Color m_separatorFillColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    public SeparatorStyle m_separatorStyle = SeparatorStyle.Solid;
    public bool m_separatorBold = true;
    public bool m_separatorItalic = false;
    public bool m_separatorUppercase = false;

    [Header("Child inheritance")]
    public bool m_enableChildInherit = true;
    public ChildInheritMode m_childInheritMode = ChildInheritMode.Flat;
    [Range(0f, 1f)] public float m_childInheritOpacity = 0.15f;
    [Range(0f, 1f)] public float m_childInheritFalloff = 0.5f;

    [Header("Auto-color rules")]
    public List<AutoColorRule> m_autoColorRules = new List<AutoColorRule>();

    [Header("Build")]
    [Tooltip("Strip Chromarchy specs from GameObject names in built scenes ('#1f6feb center bold=Title' becomes 'Title'). Scene assets on disk are not modified.")]
    public bool m_stripNamesInBuild = true;

    [Header("RGB mode")]
    [Tooltip("Animate every non-banner row through a rainbow. Editor-only; repaints the Hierarchy ~30fps while enabled.")]
    public bool m_rgbMode = false;
    [Range(0.05f, 3f)] public float m_rgbSpeed = 0.5f;
    [Range(0f, 1f)] public float m_rgbSaturation = 0.55f;
    [Range(0f, 1f)] public float m_rgbValue = 0.9f;
    [Range(0.02f, 0.8f)] public float m_rgbAlpha = 0.30f;
    [Tooltip("Hue spread across rows. 0 = every row shares the same hue.")]
    [Range(0f, 0.02f)] public float m_rgbSpread = 0.004f;

    public List<Preset> m_presets = new List<Preset>();

    // Bumped on every edit from the window.
    public int m_version;

    #endregion


    #region Unity API

    private void OnValidate()
    {
        // Catches direct Inspector edits (the window already calls OnConfigChanged explicitly).
        ChromarchyHeaders.OnConfigChanged(this);
    }

    #endregion


    #region Main API

    public void ResetToDefaults()
    {
        m_enableHeaders = true;

        m_enableTreeLines = true;
        m_treeLineColor = new Color(1f, 1f, 1f, 0.15f);

        m_enableSeparators = true;
        m_separatorColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        m_separatorFillColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        m_separatorStyle = SeparatorStyle.Solid;
        m_separatorBold = true;
        m_separatorItalic = false;
        m_separatorUppercase = false;

        m_enableChildInherit = true;
        m_childInheritMode = ChildInheritMode.Flat;
        m_childInheritOpacity = 0.15f;
        m_childInheritFalloff = 0.5f;

        m_autoColorRules = new List<AutoColorRule>();

        m_stripNamesInBuild = true;

        m_rgbMode = false;
        m_rgbSpeed = 0.5f;
        m_rgbSaturation = 0.55f;
        m_rgbValue = 0.9f;
        m_rgbAlpha = 0.30f;
        m_rgbSpread = 0.004f;

        m_presets = new List<Preset>
        {
            new Preset { m_key = "h1",   m_spec = "#1f6feb center bold s12 text:white" },
            new Preset { m_key = "h2",   m_spec = "gray left bold text:white" },
            new Preset { m_key = "h3",   m_spec = "#3a3f44 left italic text:white" },
            new Preset { m_key = "cat",  m_spec = "#444 left bold text:white" },
            new Preset { m_key = "grad", m_spec = "#1f6feb>#7b2ff7 center bold text:white" },
        };
    }

    public static ChromarchyConfig GetOrCreate()
    {
        string[] guids = AssetDatabase.FindAssets("t:ChromarchyConfig");
        if (guids.Length > 0)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ChromarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (existing != null) return existing;
        }

        var cfg = CreateInstance<ChromarchyConfig>();
        cfg.ResetToDefaults();

        string dir = FindAssetFolder();
        AssetDatabase.CreateAsset(cfg, dir + "/ChromarchyConfig.asset");
        AssetDatabase.SaveAssets();
        return cfg;
    }

    // Place the asset next to the Chromarchy scripts if they live under Assets/; otherwise
    // fall back to Assets/Editor/Chromarchy/ (Packages/ is read-only and can't host assets).
    private static string FindAssetFolder()
    {
        string[] scriptGuids = AssetDatabase.FindAssets("ChromarchyHeaders t:Script");
        for (int i = 0; i < scriptGuids.Length; i++)
        {
            string p = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
            if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
            string dir = System.IO.Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(dir)) continue;
            return dir.Replace('\\', '/');
        }

        if (!AssetDatabase.IsValidFolder("Assets/Editor"))
            AssetDatabase.CreateFolder("Assets", "Editor");
        if (!AssetDatabase.IsValidFolder("Assets/Editor/Chromarchy"))
            AssetDatabase.CreateFolder("Assets/Editor", "Chromarchy");
        return "Assets/Editor/Chromarchy";
    }

    #endregion


    #region Tools and Utilies


    #endregion


    #region Private and Protected


    #endregion
}
}
