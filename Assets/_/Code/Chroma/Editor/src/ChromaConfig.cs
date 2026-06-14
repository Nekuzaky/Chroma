using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
public enum ChildInheritMode { Flat, DepthFade }
public enum AutoColorMatch { Tag, Layer, NamePrefix, Regex }
public enum SeparatorStyle { Solid, Dashed, Dotted, Double }
public enum RGBTheme { Classic, Halloween, Christmas, Valentine }

/// <summary>Severity of a lint rule violation. Order matters: higher = worse.</summary>
public enum LintSeverity { Info, Warning, Error }
/// <summary>Which objects a lint rule applies to.</summary>
public enum LintScope { All, RootOnly, Tag, Layer, NamePrefix, Regex }
/// <summary>What must hold for objects in a lint rule's scope.</summary>
public enum LintAssert { HasBanner, NameRegex, NoEmpty, NoMissingScript, RequiredParent, MaxDepth, NoDefaultName }

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

    /// <summary>
    /// Convention-linter rule: a scope (which objects), an assertion (what must hold),
    /// a severity and a message. Violations are surfaced inline in the Hierarchy and in the
    /// window's Lint tab. Rules live in the shared config, so the whole team is aligned.
    /// </summary>
    [System.Serializable]
    public class LintRule
    {
        /// <summary>Enable/disable this rule without removing it.</summary>
        public bool m_enabled = true;
        /// <summary>Short identifier used to group violations (e.g., "no-default-name").</summary>
        public string m_id = "";
        /// <summary>Severity of a violation: Info, Warning, or Error.</summary>
        public LintSeverity m_severity = LintSeverity.Warning;
        /// <summary>Which objects the rule applies to.</summary>
        public LintScope m_scope = LintScope.All;
        /// <summary>Scope parameter (tag name, layer name, prefix, or regex). Unused for All/RootOnly.</summary>
        public string m_scopeValue = "";
        /// <summary>What must hold for objects in scope.</summary>
        public LintAssert m_assert = LintAssert.NoDefaultName;
        /// <summary>Assertion parameter (regex, parent name/component, or max depth number).</summary>
        public string m_assertValue = "";
        /// <summary>Human message shown in the tooltip and the Lint tab.</summary>
        public string m_message = "";

        // Per-rule caches (resolved layer index, compiled regexes, parsed int), mirroring
        // AutoColorRule. Cleared whenever the config changes.
        [System.NonSerialized] internal int m_cachedLayer;
        [System.NonSerialized] internal string m_cachedLayerFor;
        [System.NonSerialized] internal System.Text.RegularExpressions.Regex m_cachedScopeRegex;
        [System.NonSerialized] internal string m_cachedScopeRegexFor;
        [System.NonSerialized] internal System.Text.RegularExpressions.Regex m_cachedAssertRegex;
        [System.NonSerialized] internal string m_cachedAssertRegexFor;
        [System.NonSerialized] internal int m_cachedInt;
        [System.NonSerialized] internal string m_cachedIntFor;
    }

    #region Public

    [Header("Display")]
    [Tooltip("Show colored banners when GameObject names contain #color codes")]
    public bool m_enableHeaders = true;

    [Header("Banner font")]
    [Tooltip("Custom Font asset for banner & separator text. Overrides the system font below when set; leave empty to use a system font or the editor default")]
    public Font m_bannerFont;
    [Tooltip("Name of an installed system font for banner & separator text (pick it in Tools > Chroma > Settings > Font). Empty = editor default")]
    public string m_bannerFontName = "";

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
    [Tooltip("Show a warning icon on rows whose GameObject has a missing (deleted) script component")]
    public bool m_warnMissingScripts = true;

    [Header("Row widgets")]
    [Tooltip("Always-visible activation checkbox at the right edge of every row (click toggles SetActive, with Undo)")]
    public bool m_showActiveToggle = true;
    [Tooltip("Show the icons of each GameObject's components at the right edge of the row")]
    public bool m_showComponentIcons = true;
    [Range(1, 8)]
    [Tooltip("Maximum number of component icons shown per row")]
    public int m_maxComponentIcons = 4;

    [Header("Selection accent")]
    [Tooltip("Tint the selected hierarchy row with the theme accent (stays visible even on banner rows, which hide the native blue highlight)")]
    public bool m_selectionAccent = true;
    [Tooltip("Accent color (and alpha) washed over the selected row. Themes reseed this from their primary color")]
    public Color m_selectionAccentColor = new Color(0.27f, 0.52f, 1f, 0.18f);

    [Header("Scene View")]
    [Tooltip("Draw a floating colored name label above each banner-colored object in the Scene View")]
    public bool m_sceneLabels = false;
    [Tooltip("Draw a colored wireframe marker around each banner-colored object in the Scene View")]
    public bool m_sceneGizmos = false;

    [Header("Folder colors (Project window)")]
    [Tooltip("Enable color tinting for folders in the Project window")]
    public bool m_enableFolderColors = true;
    [Tooltip("List of folder GUIDs and their assigned colors")]
    public List<FolderColor> m_folderColors = new List<FolderColor>();
    [Tooltip("Child folders inherit color from parent folder")]
    public bool m_folderColorInheritance = true;

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

    [Header("Convention linter")]
    [Tooltip("Scan open scenes against the team's lint rules and flag violations")]
    public bool m_enableLint = true;
    [Tooltip("Show a severity icon (with tooltip) on rows that violate a lint rule")]
    public bool m_lintShowIcons = true;
    [Tooltip("Team lint rules: scope (which objects) + assertion (what must hold) + severity + message")]
    public List<LintRule> m_lintRules = new List<LintRule>();

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
    [Tooltip("RGB theme: Classic rainbow, Halloween, Christmas, or Valentine")]
    public RGBTheme m_rgbTheme = RGBTheme.Classic;

    [Tooltip("Quick color presets that can be applied as banner styles")]
    public List<Preset> m_presets = new List<Preset>();

    /// <summary>Internal version stamp; bumped on every config edit from the window to notify listeners.</summary>
    public int m_version;

    /// <summary>Schema version for data migrations. Independent from m_version (which is an edit counter).</summary>
    public int m_schemaVersion;

    #endregion


    #region Unity API

    private void OnValidate()
    {
        // Migrate old configs to new versions without data loss.
        MigrateIfNeeded();
        // Catches direct Inspector edits (the window already calls OnConfigChanged explicitly).
        ChromaHeaders.OnConfigChanged(this);
    }

    #endregion


    #region Migration

    /// <summary>
    /// Run migrations if this config is older than the current schema. Preserves all user settings.
    /// Uses m_schemaVersion (NOT m_version, which is bumped on every edit and therefore useless
    /// as a migration gate — that was a v0.2 design bug).
    /// </summary>
    private void MigrateIfNeeded()
    {
        const int CURRENT_SCHEMA = 4;
        if (m_schemaVersion >= CURRENT_SCHEMA) return;

        // Migration chain: each schema upgrades to the next.
        // v2 (RGB themes), v3 (lint rules + row widgets) and v4 (selection accent + Scene View)
        // only ADD fields with safe defaults, so deserialization handles them — nothing to rewrite.
        m_schemaVersion = CURRENT_SCHEMA;
    }

    // FUTURE MIGRATIONS:
    // if (m_schemaVersion < 4) MigrateV3ToV4();
    // ... and so on

    #endregion


    #region Main API

    /// <summary>Reset all settings to factory defaults and repopulate preset list.</summary>
    public void ResetToDefaults()
    {
        m_enableHeaders = true;
        m_bannerFont = null;
        m_bannerFontName = "";
        m_enableTreeLines = true;
        m_treeLineColor = new Color(1f, 1f, 1f, 0.15f);
        m_showChildCount = false;
        m_zebra = false;
        m_zebraColor = new Color(1f, 1f, 1f, 0.03f);
        m_warnMissingScripts = true;
        m_showActiveToggle = true;
        m_showComponentIcons = true;
        m_maxComponentIcons = 4;
        m_selectionAccent = true;
        m_selectionAccentColor = new Color(0.27f, 0.52f, 1f, 0.18f);
        m_sceneLabels = false;
        m_sceneGizmos = false;
        m_enableLint = true;
        m_lintShowIcons = true;
        m_lintRules = StarterLintRules();
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

    /// <summary>
    /// Gentle default ruleset: catches real mistakes (missing scripts, default names, empty
    /// objects) without imposing any team-specific structure.
    /// </summary>
    public static List<LintRule> StarterLintRules()
    {
        return new List<LintRule>
        {
            new LintRule { m_id = "no-missing-script", m_severity = LintSeverity.Error,
                m_assert = LintAssert.NoMissingScript,
                m_message = "Missing (deleted) script on this GameObject." },
            new LintRule { m_id = "no-default-name", m_severity = LintSeverity.Warning,
                m_assert = LintAssert.NoDefaultName,
                m_message = "Default name — rename it to something meaningful." },
            new LintRule { m_id = "no-empty-object", m_severity = LintSeverity.Info,
                m_assert = LintAssert.NoEmpty,
                m_message = "Empty GameObject (no components, no children)." },
        };
    }

    /// <summary>Stricter ruleset for teams: starter rules + scene-structure conventions.</summary>
    public static List<LintRule> StrictLintRules()
    {
        List<LintRule> rules = StarterLintRules();
        rules.Add(new LintRule { m_id = "root-needs-banner", m_severity = LintSeverity.Warning,
            m_scope = LintScope.RootOnly, m_assert = LintAssert.HasBanner,
            m_message = "Root objects should carry a Chroma banner so the scene reads at a glance." });
        rules.Add(new LintRule { m_id = "max-depth", m_severity = LintSeverity.Warning,
            m_assert = LintAssert.MaxDepth, m_assertValue = "8",
            m_message = "Nested deeper than 8 levels — consider flattening." });
        return rules;
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

    /// <summary>Find or create the asset folder for Chroma config. Prefers Assets/Chroma; falls back to the Chroma script directory.</summary>
    private static string FindAssetFolder()
    {
        // Always use Assets/Chroma as the default config location
        if (!AssetDatabase.IsValidFolder("Assets/Chroma"))
            AssetDatabase.CreateFolder("Assets", "Chroma");
        return "Assets/Chroma";
    }

    #endregion


    #region Tools and Utilities

    /// <summary>Validate and clamp all deserialized values to safe ranges. Call after FromJsonOverwrite.</summary>
    public void ValidateAndClamp()
    {
        const int maxStringLen = 1000;
        const int maxRegexLen = 100;
        const int maxRules = 256;

        // Clamp numeric ranges
        m_childInheritOpacity = Mathf.Clamp01(m_childInheritOpacity);
        m_childInheritFalloff = Mathf.Clamp01(m_childInheritFalloff);
        m_rgbSpeed = Mathf.Clamp(m_rgbSpeed, 0.05f, 3f);
        m_rgbSaturation = Mathf.Clamp01(m_rgbSaturation);
        m_rgbValue = Mathf.Clamp01(m_rgbValue);
        m_rgbAlpha = Mathf.Clamp(m_rgbAlpha, 0.02f, 0.8f);
        m_rgbSpread = Mathf.Clamp(m_rgbSpread, 0f, 0.02f);

        // Validate banner font name
        if (!string.IsNullOrEmpty(m_bannerFontName) && m_bannerFontName.Length > maxStringLen)
            m_bannerFontName = m_bannerFontName.Substring(0, maxStringLen);

        // Validate folder colors
        if (m_folderColors == null) m_folderColors = new List<FolderColor>();
        for (int i = m_folderColors.Count - 1; i >= 0; i--)
        {
            if (m_folderColors[i] == null || string.IsNullOrEmpty(m_folderColors[i].m_guid) || m_folderColors[i].m_guid.Length != 36)
                m_folderColors.RemoveAt(i);
        }

        // Validate and limit auto-color rules
        if (m_autoColorRules == null) m_autoColorRules = new List<AutoColorRule>();
        if (m_autoColorRules.Count > maxRules) m_autoColorRules.RemoveRange(maxRules, m_autoColorRules.Count - maxRules);

        for (int i = 0; i < m_autoColorRules.Count; i++)
        {
            var rule = m_autoColorRules[i];
            if (rule == null) continue;

            // Clamp regex pattern length to prevent ReDoS
            if (!string.IsNullOrEmpty(rule.m_value) && rule.m_value.Length > maxRegexLen)
                rule.m_value = rule.m_value.Substring(0, maxRegexLen);

            // Clear cached regex to force recompilation with new timeout
            rule.m_cachedRegex = null;
            rule.m_cachedRegexFor = null;
            rule.m_cachedLayer = 0;
            rule.m_cachedLayerFor = null;
        }

        // Clamp row-widget options
        m_maxComponentIcons = Mathf.Clamp(m_maxComponentIcons, 1, 8);

        // Validate and limit lint rules
        if (m_lintRules == null) m_lintRules = new List<LintRule>();
        if (m_lintRules.Count > maxRules) m_lintRules.RemoveRange(maxRules, m_lintRules.Count - maxRules);
        for (int i = 0; i < m_lintRules.Count; i++)
        {
            LintRule rule = m_lintRules[i];
            if (rule == null) continue;

            if (!string.IsNullOrEmpty(rule.m_id) && rule.m_id.Length > 100)
                rule.m_id = rule.m_id.Substring(0, 100);
            if (!string.IsNullOrEmpty(rule.m_scopeValue) && rule.m_scopeValue.Length > maxRegexLen)
                rule.m_scopeValue = rule.m_scopeValue.Substring(0, maxRegexLen);
            if (!string.IsNullOrEmpty(rule.m_assertValue) && rule.m_assertValue.Length > maxRegexLen)
                rule.m_assertValue = rule.m_assertValue.Substring(0, maxRegexLen);
            if (!string.IsNullOrEmpty(rule.m_message) && rule.m_message.Length > maxStringLen)
                rule.m_message = rule.m_message.Substring(0, maxStringLen);

            // Clear caches so regexes recompile with the timeout
            rule.m_cachedScopeRegex = null;
            rule.m_cachedScopeRegexFor = null;
            rule.m_cachedAssertRegex = null;
            rule.m_cachedAssertRegexFor = null;
            rule.m_cachedLayer = 0;
            rule.m_cachedLayerFor = null;
            rule.m_cachedIntFor = null;
        }

        // Validate presets
        if (m_presets == null) m_presets = new List<Preset>();
        for (int i = m_presets.Count - 1; i >= 0; i--)
        {
            var p = m_presets[i];
            if (p == null || string.IsNullOrEmpty(p.m_key))
            {
                m_presets.RemoveAt(i);
                continue;
            }
            if (p.m_key.Length > 100) p.m_key = p.m_key.Substring(0, 100);
            if (p.m_spec.Length > maxStringLen) p.m_spec = p.m_spec.Substring(0, maxStringLen);
        }
    }

    #endregion


    #region Private and Protected


    #endregion
}
}

