using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chromarchy.Editor
{
public enum ChildInheritMode { Flat, DepthFade }
public enum AutoColorMatch { Tag, Layer, NamePrefix }

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

    public List<Preset> m_presets = new List<Preset>();

    // Bumped on every edit from the window.
    public int m_version;

    #endregion


    #region Unity API


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
        m_separatorBold = true;
        m_separatorItalic = false;
        m_separatorUppercase = false;

        m_enableChildInherit = true;
        m_childInheritMode = ChildInheritMode.Flat;
        m_childInheritOpacity = 0.15f;
        m_childInheritFalloff = 0.5f;

        m_autoColorRules = new List<AutoColorRule>();

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

        string dir = "Assets/_/Codes/Editor/Hierarchy/Editor/src";
        if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets";

        AssetDatabase.CreateAsset(cfg, dir + "/ChromarchyConfig.asset");
        AssetDatabase.SaveAssets();
        return cfg;
    }

    #endregion


    #region Tools and Utilies


    #endregion


    #region Private and Protected


    #endregion
}
}
