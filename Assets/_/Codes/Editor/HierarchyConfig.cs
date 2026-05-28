using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Config persistee (asset projet, partageable via git) pour CustomHierarchyHeaders.
// Edite via la fenetre Tools/Custom Hierarchy.
public class HierarchyConfig : ScriptableObject
{
    // Ordre fige : doit correspondre aux checks typed dans CustomHierarchyHeaders.TryGetTint.
    public static readonly string[] ComponentLabels =
    {
        "Camera", "Light", "AudioSource", "Canvas", "ParticleSystem", "Rigidbody", "Animator"
    };

    [Serializable]
    public class Preset
    {
        public string key;
        public string spec;
    }

    [Serializable]
    public class ComponentRule
    {
        public bool enabled = true;
        public Color tint = new Color(1f, 1f, 1f, 0.18f);
    }

    [Header("Affichage")]
    public bool enableHeaders = true;
    public bool enableRowTint = true;
    public bool enableChildCount = true;

    public List<Preset> presets = new List<Preset>();
    public ComponentRule[] componentRules;

    // Incremente a chaque modif via la fenetre.
    public int version;

    public void ResetToDefaults()
    {
        enableHeaders = true;
        enableRowTint = true;
        enableChildCount = true;

        presets = new List<Preset>
        {
            new Preset { key = "h1",  spec = "#1f6feb center bold s12 text:white" },
            new Preset { key = "h2",  spec = "gray left bold text:white" },
            new Preset { key = "h3",  spec = "#3a3f44 left italic text:white" },
            new Preset { key = "cat", spec = "#444 left bold text:white" },
        };

        componentRules = new[]
        {
            new ComponentRule { enabled = true, tint = new Color(0.10f, 0.70f, 0.75f, 0.18f) }, // Camera
            new ComponentRule { enabled = true, tint = new Color(0.80f, 0.78f, 0.25f, 0.18f) }, // Light
            new ComponentRule { enabled = true, tint = new Color(0.10f, 0.65f, 0.10f, 0.18f) }, // AudioSource
            new ComponentRule { enabled = true, tint = new Color(0.15f, 0.45f, 0.90f, 0.18f) }, // Canvas
            new ComponentRule { enabled = true, tint = new Color(0.55f, 0.20f, 0.75f, 0.18f) }, // ParticleSystem
            new ComponentRule { enabled = true, tint = new Color(0.90f, 0.50f, 0.05f, 0.18f) }, // Rigidbody
            new ComponentRule { enabled = true, tint = new Color(0.90f, 0.35f, 0.60f, 0.18f) }, // Animator
        };
    }

    void OnValidate()
    {
        // Garantit la longueur attendue par le drawer meme si l'asset est edite a la main.
        if (componentRules == null || componentRules.Length != ComponentLabels.Length)
        {
            var fixedRules = new ComponentRule[ComponentLabels.Length];
            for (int i = 0; i < fixedRules.Length; i++)
                fixedRules[i] = (componentRules != null && i < componentRules.Length && componentRules[i] != null)
                    ? componentRules[i]
                    : new ComponentRule();
            componentRules = fixedRules;
        }
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

        string dir = "Assets/_/Codes/Editor";
        if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets";

        AssetDatabase.CreateAsset(cfg, dir + "/HierarchyConfig.asset");
        AssetDatabase.SaveAssets();
        return cfg;
    }
}
