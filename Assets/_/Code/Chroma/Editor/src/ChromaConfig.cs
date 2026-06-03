using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
public enum ChildInheritMode { Flat, DepthFade }
public enum AutoColorMatch { Tag, Layer, NamePrefix, Regex }
public enum SeparatorStyle { Solid, Dashed, Dotted, Double }

/// <summary>
/// Persisted Chroma configuration asset. Controls hierarchy and folder colors, tree lines, separators,
/// auto-coloring rules, and visual options. One config per project; shared via git.
/// Edited through Tools > Chroma panel.
/// </summary>
public class ChromaConfig : ScriptableObject
{
    /// <summary>Quick color scheme preset: a name and color/gradient spec that can be applied to objects.</summary>
    [System.Serializable]
    public class Preset
    {
        /// <summary>Unique key used in banner specs (e.g., "h1" in "h1=Title").</summary>
        public string m_key;
        /// <summary>Color/gradient specification: hex colors, gradients (color>color), alignment, style. E.g., "#1f6feb center bold".</summary>
        public string m_spec;
    }

    /// <summary>Maps a Project folder GUID to a display color in the Project window.</summary>
    [System.Serializable]
    public class FolderColor
    {
        /// <summary>GUID of the folder asset.</summary>
        public string m_guid;
        /// <summary>Color to display for this folder in the Project window.</summary>
        public Color m_color = new Color(0.30f, 0.55f, 1f);
    }

    /// <summary>Rule to automatically tint rows by Tag, Layer, name prefix, or regex pattern.</summary>
    [System.Serializable]
    public class AutoColorRule
    {
        /// <summary>Enable/disable this rule without removing it.</summary>
        public bool m_enabled = true;
        /// <summary>Match type: Tag, Layer, NamePrefix, or Regex.</summary>
        public AutoColorMatch m_match = AutoColorMatch.Tag;
        /// <summary>Value to match (e.g., "Player" tag, "UI" layer, "Enemy_" prefix, or regex pattern).</summary>
        public string m_value = "";
        /// <summary>Color to tint matching rows with.</summary>
        public Color m_color = new Color(0.20f, 0.50f, 0.90f, 0.18f);

        // Resolved layer index, cached to avoid LayerMask.NameToLayer per row.
        [System.NonSerialized] internal int m_cachedLayer;
        [System.NonSerialized] internal string m_cachedLayerFor;

        // Compiled regex, cached to avoid recompiling per row. m_cachedRegexFor tracks the
        // pattern it was built from; null regex with a non-null marker means "invalid pattern".
        [System.NonSerialized] internal System.Text.RegularExpressions.Regex m_cachedRegex;
        [System.NonSerialized] internal string m_cachedRegexFor;
    }

    #region Public

    [Header("Display")]
    [Tooltip("Show colored banners when GameObject names contain #color codes")]
    public bool m_enableHeaders = true;

    [Header("Tree lines")]
    [Tooltip("File explorer style connector lines in the hierarchy indent gutter")]
    public bool m_enableTreeLines = true;
    [Tooltip("Color of tree guide lines")]
    public Color m_treeLineColor = new Color(1f, 1f, 1f, 0.15f);

    [Header("Row extras")]
    [Tooltip("Display number of children as (N) next to each object")]
    public bool m_showChildCount = false;
    [Tooltip("Alternate row background colors for visual separation")]
    public bool m_zebra = false;
    [Tooltip("Color for zebra striped rows")]
    public Color m_zebraColor = new Color(1f, 1f, 1f, 0.03f);

    [Header("Folder colors (Project window)")]
    [Tooltip("Enable color tinting for folders in the Project window")]
    public bool m_enableFolderColors = true;
    [Tooltip("List of folder GUIDs and their assigned colors")]
    public List<FolderColor> m_folderColors = new List<FolderColor>();

    [Header("Separators")]
    [Tooltip("Show separator rows (objects named '---' or '___')")]
    public bool m_enableSeparators = true;
    [Tooltip("Color of separator lines")]
    public Color m_separatorColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("Background fill color behind separator text")]
    public Color m_separatorFillColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    [Tooltip("Visual style: Solid, Dashed, Dotted, or Double")]
    public SeparatorStyle m_separatorStyle = SeparatorStyle.Solid;
    [Tooltip("Make separator text bold")]
    public bool m_separatorBold = true;
    [Tooltip("Make separator text italic")]
    public bool m_separatorItalic = false;
    [Tooltip("Capitalize separator text")]
    public bool m_separatorUppercase = false;

    [Header("Child inheritance")]
    [Tooltip("Children inherit color tint from parent banners")]
    public bool m_enableChildInherit = true;
    [Tooltip("Flat: constant opacity. DepthFade: opacity fades per nesting level")]
    public ChildInheritMode m_childInheritMode = ChildInheritMode.Flat;
    [Range(0f, 1f)]
    [Tooltip("Base opacity for inherited colors")]
    public float m_childInheritOpacity = 0.15f;
    [Range(0f, 1f)]
    [Tooltip("How quickly opacity fades per depth level (DepthFade mode)")]
    public float m_childInheritFalloff = 0.5f;

    [Header("Auto-color rules")]
    [Tooltip("Rules to automatically tint rows by Tag, Layer, name prefix, or regex match")]
    public List<AutoColorRule> m_autoColorRules = new List<AutoColorRule>();

    [Header("Build")]
    [Tooltip("Strip Chroma specs from GameObject names in built scenes (#1f6feb center bold=Title becomes Title). Scene assets are not modified")]
    public bool m_stripNamesInBuild = true;

    [Header("RGB mode")]
    [Tooltip("Animate every non-banner row through a rainbow. Editor-only; repaints the Hierarchy ~30fps while enabled")]
    public bool m_rgbMode = false;
    [Range(0.05f, 3f)]
    [Tooltip("Animation speed multiplier")]
    public float m_rgbSpeed = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("Color saturation (0 = grayscale, 1 = vivid)")]
    public float m_rgbSaturation = 0.55f;
    [Range(0f, 1f)]
    [Tooltip("Color brightness (0 = black, 1 = bright)")]
    public float m_rgbValue = 0.9f;
    [Range(0.02f, 0.8f)]
    [Tooltip("Alpha opacity of the rainbow tint")]
    public float m_rgbAlpha = 0.30f;
    [Tooltip("Hue spread across rows (0 = every row same hue, 0.02 = full spectrum)")]
    [Range(0f, 0.02f)]
    public float m_rgbSpread = 0.004f;
    [Tooltip("Also animate Project-window folder icons through the rainbow")]
    public bool m_rgbFolders = false;

    [Tooltip("Quick color presets that can be applied as banner styles")]
    public List<Preset> m_presets = new List<Preset>();

    /// <summary>Internal version stamp; bumped on every config edit from the window to notify listeners.</summary>
    public int m_version;

    #endregion


    #region Unity API

    private void OnValidate()
    {
        // Catches direct Inspector edits (the window already calls OnConfigChanged explicitly).
        ChromaHeaders.OnConfigChanged(this);
    }

    #endregion


    #region Main API

    /// <summary>Reset all settings to factory defaults and repopulate preset list.</summary>
    public void ResetToDefaults()
    {
        m_enableHeaders = true;
        m_enableTreeLines = true;
        m_treeLineColor = new Color(1f, 1f, 1f, 0.15f);
        m_showChildCount = false;
        m_zebra = false;
        m_zebraColor = new Color(1f, 1f, 1f, 0.03f);
        m_enableFolderColors = true;
        m_folderColors = new List<FolderColor>();
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
        m_rgbFolders = false;
        m_presets = new List<Preset>
        {
            new Preset { m_key = "h1",   m_spec = "#1f6feb center bold s12 text:white" },
            new Preset { m_key = "h2",   m_spec = "gray left bold text:white" },
            new Preset { m_key = "h3",   m_spec = "#3a3f44 left italic text:white" },
            new Preset { m_key = "cat",  m_spec = "#444 left bold text:white" },
            new Preset { m_key = "grad", m_spec = "#1f6feb>#7b2ff7 center bold text:white" },
        };
    }

    /// <summary>Load the project's ChromaConfig, or create one with default settings if none exists.</summary>
    public static ChromaConfig GetOrCreate()
    {
        string[] guids = AssetDatabase.FindAssets("t:ChromaConfig");
        if (guids.Length > 0)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ChromaConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (existing != null) return existing;
        }

        var cfg = CreateInstance<ChromaConfig>();
        cfg.ResetToDefaults();

        string dir = FindAssetFolder();
        AssetDatabase.CreateAsset(cfg, dir + "/ChromaConfig.asset");
        AssetDatabase.SaveAssets();
        return cfg;
    }

    /// <summary>Find or create the asset folder for Chroma config. Prefers the Chroma script directory; falls back to Assets/Editor/Chroma/.</summary>
    private static string FindAssetFolder()
    {
        string[] scriptGuids = AssetDatabase.FindAssets("ChromaHeaders t:Script");
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
        if (!AssetDatabase.IsValidFolder("Assets/Editor/Chroma"))
            AssetDatabase.CreateFolder("Assets/Editor", "Chroma");
        return "Assets/Editor/Chroma";
    }

    #endregion


    #region Tools and Utilities


    #endregion


    #region Private and Protected


    #endregion
}
}
