using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Panel d'edition de la config de la Hierarchy. Ouvre via Tools/Custom Hierarchy.
public class HierarchyConfigWindow : EditorWindow
{
    static readonly string[] AlignLabels = { "Center", "Left", "Right" };
    static readonly string[] StyleLabels = { "Bold", "Normal", "Italic", "BoldItalic" };

    static readonly (string name, Color color)[] Swatches =
    {
        ("green",  new Color(0.10f, 0.65f, 0.10f)),
        ("red",    new Color(0.75f, 0.10f, 0.10f)),
        ("blue",   new Color(0.15f, 0.45f, 0.90f)),
        ("orange", new Color(0.90f, 0.50f, 0.05f)),
        ("yellow", new Color(0.80f, 0.78f, 0.25f)),
        ("mauve",  new Color(0.50f, 0.00f, 1.00f)),
        ("purple", new Color(0.55f, 0.20f, 0.75f)),
        ("pink",   new Color(0.90f, 0.35f, 0.60f)),
        ("cyan",   new Color(0.10f, 0.70f, 0.75f)),
        ("gray",   new Color(0.45f, 0.45f, 0.45f)),
    };

    static readonly HashSet<string> NamedColors = new HashSet<string>
    {
        "green", "red", "blue", "orange", "gray", "grey", "yellow",
        "mauve", "white", "black", "cyan", "purple", "pink"
    };

    HierarchyConfig config;
    SerializedObject so;
    Vector2 scroll;

    // Section "appliquer a la selection".
    enum ApplyMode { Preset, Custom }
    ApplyMode applyMode = ApplyMode.Custom;
    int presetIndex;
    Color customColor = new Color(0.15f, 0.45f, 0.90f);
    int alignIndex;
    int styleIndex;
    int customSize;
    string applyTitle = "";

    // Styles caches du panel.
    GUIStyle titleStyle;
    GUIStyle subTitleStyle;
    GUIStyle sectionStyle;
    bool stylesBuilt;

    [MenuItem("Tools/Custom Hierarchy")]
    static void Open()
    {
        var win = GetWindow<HierarchyConfigWindow>("Custom Hierarchy");
        win.minSize = new Vector2(380f, 500f);
    }

    void OnEnable()
    {
        config = HierarchyConfig.GetOrCreate();
        so = new SerializedObject(config);
        CustomHierarchyHeaders.OnConfigChanged(config);
    }

    void EnsureStyles()
    {
        if (stylesBuilt) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        titleStyle.normal.textColor = Color.white;
        subTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        subTitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        stylesBuilt = true;
    }

    void OnGUI()
    {
        if (config == null) OnEnable();
        EnsureStyles();
        so.Update();

        DrawHeaderBar();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.Space(6);

        // Section selection : champs locaux + renommage d'objets, ne touche pas l'asset.
        BeginSection("Appliquer a la selection");
        DrawApplyBody();
        EndSection();

        // Edition de l'asset : seules ces modifs marquent la config dirty.
        EditorGUI.BeginChangeCheck();

        BeginSection("Affichage");
        DrawToggles();
        EndSection();

        BeginSection("Presets de bandeaux");
        DrawPresets();
        EndSection();

        BeginSection("Regles par composant");
        DrawComponentRules();
        EndSection();

        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            config.version++;
            EditorUtility.SetDirty(config);
            CustomHierarchyHeaders.OnConfigChanged(config);
        }

        DrawFooter();

        EditorGUILayout.EndScrollView();
    }

    void DrawHeaderBar()
    {
        Rect r = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.17f, 0.19f, 0.23f));
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0f, 0f, 0f, 0.4f));

        Rect titleRect = new Rect(r.x + 12f, r.y + 4f, r.width - 24f, 18f);
        GUI.Label(titleRect, "Custom Hierarchy", titleStyle);
        Rect subRect = new Rect(r.x + 12f, r.y + 20f, r.width - 24f, 14f);
        GUI.Label(subRect, "Structure ta hierarchy : bandeaux, teintes, compteurs", subTitleStyle);
    }

    void BeginSection(string title)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(title, sectionStyle);
        EditorGUILayout.Space(2);
    }

    void EndSection()
    {
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(6);
    }

    void DrawToggles()
    {
        EditorGUILayout.PropertyField(so.FindProperty("enableHeaders"), new GUIContent("Bandeaux de section"));
        EditorGUILayout.PropertyField(so.FindProperty("enableRowTint"), new GUIContent("Teinte de ligne (composant)"));
        EditorGUILayout.PropertyField(so.FindProperty("enableChildCount"), new GUIContent("Compteur d'enfants"));
    }

    void DrawApplyBody()
    {
        int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
        EditorGUILayout.LabelField(count == 0 ? "Aucun objet selectionne" : count + " objet(s) selectionne(s)", EditorStyles.miniLabel);

        applyMode = (ApplyMode)EditorGUILayout.EnumPopup("Mode", applyMode);

        if (applyMode == ApplyMode.Preset)
        {
            if (config.presets.Count == 0)
            {
                EditorGUILayout.HelpBox("Aucun preset defini.", MessageType.Info);
            }
            else
            {
                string[] keys = new string[config.presets.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = config.presets[i].key;
                presetIndex = Mathf.Clamp(presetIndex, 0, keys.Length - 1);
                presetIndex = EditorGUILayout.Popup("Preset", presetIndex, keys);
            }
        }
        else
        {
            customColor = EditorGUILayout.ColorField("Couleur de fond", customColor);
            alignIndex = EditorGUILayout.Popup("Alignement", alignIndex, AlignLabels);
            styleIndex = EditorGUILayout.Popup("Style", styleIndex, StyleLabels);
            customSize = EditorGUILayout.IntField("Taille (0 = defaut)", customSize);
        }

        applyTitle = EditorGUILayout.TextField(new GUIContent("Titre", "Vide = garde le titre actuel de chaque objet"), applyTitle);

        EditorGUILayout.Space(2);
        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button("Appliquer le bandeau (" + count + ")", GUILayout.Height(24)))
                ApplyToSelection();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Recolorer (garde titre + options)", EditorStyles.miniLabel);
        DrawSwatchGrid(count);
    }

    void DrawSwatchGrid(int selectionCount)
    {
        using (new EditorGUI.DisabledScope(selectionCount == 0))
        {
            const int perRow = 5;
            for (int i = 0; i < Swatches.Length; i++)
            {
                if (i % perRow == 0) EditorGUILayout.BeginHorizontal();

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = Swatches[i].color;
                if (GUILayout.Button(Swatches[i].name, GUILayout.Height(22)))
                    RecolorSelection(Swatches[i].name);
                GUI.backgroundColor = prev;

                if (i % perRow == perRow - 1 || i == Swatches.Length - 1) EditorGUILayout.EndHorizontal();
            }
        }
    }

    void DrawPresets()
    {
        SerializedProperty presets = so.FindProperty("presets");

        for (int i = 0; i < presets.arraySize; i++)
        {
            SerializedProperty el = presets.GetArrayElementAtIndex(i);
            SerializedProperty key = el.FindPropertyRelative("key");
            SerializedProperty spec = el.FindPropertyRelative("spec");

            EditorGUILayout.BeginHorizontal();
            key.stringValue = EditorGUILayout.TextField(key.stringValue, GUILayout.Width(70));
            spec.stringValue = EditorGUILayout.TextField(spec.stringValue);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                presets.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("+ Ajouter un preset"))
        {
            int idx = presets.arraySize;
            presets.arraySize++;
            SerializedProperty el = presets.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("key").stringValue = "new";
            el.FindPropertyRelative("spec").stringValue = "gray left text:white";
        }
    }

    void DrawComponentRules()
    {
        EditorGUILayout.LabelField("Coche + couleur de teinte (alpha = intensite)", EditorStyles.miniLabel);

        SerializedProperty rules = so.FindProperty("componentRules");
        int n = Mathf.Min(rules.arraySize, HierarchyConfig.ComponentLabels.Length);

        for (int i = 0; i < n; i++)
        {
            SerializedProperty el = rules.GetArrayElementAtIndex(i);
            SerializedProperty enabled = el.FindPropertyRelative("enabled");
            SerializedProperty tint = el.FindPropertyRelative("tint");

            EditorGUILayout.BeginHorizontal();
            enabled.boolValue = EditorGUILayout.ToggleLeft(HierarchyConfig.ComponentLabels[i], enabled.boolValue, GUILayout.Width(150));
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                tint.colorValue = EditorGUILayout.ColorField(GUIContent.none, tint.colorValue, false, true, false);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reinitialiser par defaut", GUILayout.Height(22)))
        {
            if (EditorUtility.DisplayDialog("Custom Hierarchy", "Reinitialiser toute la config ?", "Oui", "Annuler"))
            {
                Undo.RecordObject(config, "Reset Hierarchy Config");
                config.ResetToDefaults();
                config.version++;
                EditorUtility.SetDirty(config);
                so.Update();
                CustomHierarchyHeaders.OnConfigChanged(config);
            }
        }
        if (GUILayout.Button("Voir l'asset", GUILayout.Height(22)))
        {
            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }
        EditorGUILayout.EndHorizontal();
    }

    void ApplyToSelection()
    {
        string spec = BuildSpec();
        if (string.IsNullOrEmpty(spec)) return;

        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Custom Hierarchy: appliquer bandeau");
            string title = !string.IsNullOrEmpty(applyTitle) ? applyTitle : ExtractTitle(go.name);
            go.name = spec + "=" + title;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    void RecolorSelection(string colorToken)
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Custom Hierarchy: recolorer");
            go.name = ReplaceColor(go.name, colorToken);
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    string BuildSpec()
    {
        if (applyMode == ApplyMode.Preset)
        {
            if (config.presets.Count == 0) return null;
            presetIndex = Mathf.Clamp(presetIndex, 0, config.presets.Count - 1);
            return config.presets[presetIndex].key;
        }

        string spec = "#" + ColorUtility.ToHtmlStringRGB(customColor);
        if (alignIndex == 1) spec += " left";
        else if (alignIndex == 2) spec += " right";
        if (styleIndex == 1) spec += " normal";
        else if (styleIndex == 2) spec += " italic";
        else if (styleIndex == 3) spec += " bolditalic";
        if (customSize > 0) spec += " s" + customSize;
        return spec;
    }

    static string ExtractTitle(string name)
    {
        int eq = name.IndexOf('=');
        return eq >= 0 ? name.Substring(eq + 1).Trim() : name;
    }

    // Remplace le fond d'un nom existant par colorToken, en gardant titre + autres options.
    static string ReplaceColor(string name, string colorToken)
    {
        int eq = name.IndexOf('=');
        string title = eq >= 0 ? name.Substring(eq + 1).Trim() : name;
        string spec = eq >= 0 ? name.Substring(0, eq).Trim() : "";

        List<string> kept = new List<string>();
        foreach (string t in spec.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (!IsColorToken(t)) kept.Add(t);

        kept.Add(colorToken); // en dernier => l'emporte sur un eventuel preset
        return string.Join(" ", kept) + "=" + title;
    }

    static bool IsColorToken(string token)
    {
        if (token.StartsWith("#")) return true;
        return NamedColors.Contains(token.ToLowerInvariant());
    }
}
