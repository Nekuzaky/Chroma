using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomHierarchy.Editor
{
// Persisted config (project asset, shareable via git) for CustomHierarchyHeaders.
// Edited through the Tools/Custom Hierarchy window.
public class HierarchyConfig : ScriptableObject
{
    [System.Serializable]
    public class Preset
    {
        public string m_key;
        public string m_spec;
    }

    #region Publics

    [Header("Display")]
    public bool m_enableHeaders = true;
    public bool m_enableChildCount = true;

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
        m_enableChildCount = true;

        m_presets = new List<Preset>
        {
            new Preset { m_key = "h1",   m_spec = "#1f6feb center bold s12 text:white" },
            new Preset { m_key = "h2",   m_spec = "gray left bold text:white" },
            new Preset { m_key = "h3",   m_spec = "#3a3f44 left italic text:white" },
            new Preset { m_key = "cat",  m_spec = "#444 left bold text:white" },
            new Preset { m_key = "grad", m_spec = "#1f6feb>#7b2ff7 center bold text:white" },
        };
    }

    public static HierarchyConfig GetOrCreate()
    {
        string[] guids = AssetDatabase.FindAssets("t:HierarchyConfig");
        if (guids.Length > 0)
        {
            var existing = AssetDatabase.LoadAssetAtPath<HierarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (existing != null) return existing;
        }

        var cfg = CreateInstance<HierarchyConfig>();
        cfg.ResetToDefaults();

        string dir = "Assets/_/Codes/Editor/Hierarchy/Runtime/src";
        if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets";

        AssetDatabase.CreateAsset(cfg, dir + "/HierarchyConfig.asset");
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
