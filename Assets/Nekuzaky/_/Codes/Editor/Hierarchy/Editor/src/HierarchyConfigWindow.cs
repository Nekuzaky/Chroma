using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomHierarchy.Editor
{
// Config panel for the custom Hierarchy. Open via Tools/Custom Hierarchy.
public class HierarchyConfigWindow : EditorWindow
{
    private enum ApplyMode { Preset, Custom }

    #region Publics


    #endregion


    #region Unity API

    private void OnEnable()
    {
        _config = HierarchyConfig.GetOrCreate();
        _so = new SerializedObject(_config);
        CustomHierarchyHeaders.OnConfigChanged(_config);
    }

    private void OnGUI()
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();

        DrawHeaderBar();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);

        // Selection section: local fields + object renaming, does not touch the asset.
        BeginSection("Apply to selection");
        DrawApplyBody();
        EndSection();

        // Asset editing: only these changes mark the config dirty.
        EditorGUI.BeginChangeCheck();

        BeginSection("Display");
        DrawToggles();
        EndSection();

        BeginSection("Banner presets");
        DrawPresets();
        EndSection();

        if (EditorGUI.EndChangeCheck())
        {
            _so.ApplyModifiedProperties();
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            CustomHierarchyHeaders.OnConfigChanged(_config);
        }

        DrawFooter();

        EditorGUILayout.EndScrollView();
    }

    #endregion


    #region Main API

    [MenuItem("Tools/Custom Hierarchy")]
    private static void Open()
    {
        var win = GetWindow<HierarchyConfigWindow>("Custom Hierarchy");
        win.minSize = new Vector2(380f, 460f);
    }

    private void ApplyToSelection()
    {
        string spec = BuildSpec();
        if (string.IsNullOrEmpty(spec)) return;

        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Custom Hierarchy: apply banner");
            string title = !string.IsNullOrEmpty(_applyTitle) ? _applyTitle : ExtractTitle(go.name);
            go.name = spec + "=" + title;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    private void RecolorSelection(string colorToken)
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Custom Hierarchy: recolor");
            go.name = ReplaceColor(go.name, colorToken);
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    #endregion


    #region Tools and Utilies

    private void EnsureStyles()
    {
        if (_stylesBuilt) return;
        _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        _titleStyle.normal.textColor = Color.white;
        _subTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        _subTitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        _stylesBuilt = true;
    }

    private void DrawHeaderBar()
    {
        Rect r = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.17f, 0.19f, 0.23f));
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0f, 0f, 0f, 0.4f));

        Rect titleRect = new Rect(r.x + 12f, r.y + 4f, r.width - 24f, 18f);
        GUI.Label(titleRect, "Custom Hierarchy", _titleStyle);
        Rect subRect = new Rect(r.x + 12f, r.y + 20f, r.width - 24f, 14f);
        GUI.Label(subRect, "Structure your hierarchy: banners, gradients, counters", _subTitleStyle);
    }

    private void BeginSection(string title)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(title, _sectionStyle);
        EditorGUILayout.Space(2);
    }

    private void EndSection()
    {
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(6);
    }

    private void DrawToggles()
    {
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableHeaders"), new GUIContent("Section banners"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableChildCount"), new GUIContent("Child count"));
    }

    private void DrawApplyBody()
    {
        int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
        EditorGUILayout.LabelField(count == 0 ? "No object selected" : count + " object(s) selected", EditorStyles.miniLabel);

        _applyMode = (ApplyMode)EditorGUILayout.EnumPopup("Mode", _applyMode);

        if (_applyMode == ApplyMode.Preset)
        {
            if (_config.m_presets.Count == 0)
            {
                EditorGUILayout.HelpBox("No preset defined.", MessageType.Info);
            }
            else
            {
                string[] keys = new string[_config.m_presets.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = _config.m_presets[i].m_key;
                _presetIndex = Mathf.Clamp(_presetIndex, 0, keys.Length - 1);
                _presetIndex = EditorGUILayout.Popup("Preset", _presetIndex, keys);
            }
        }
        else
        {
            _customColor = EditorGUILayout.ColorField("Background color", _customColor);
            _customGradient = EditorGUILayout.ToggleLeft("Gradient", _customGradient);
            if (_customGradient)
            {
                EditorGUI.indentLevel++;
                _customColor2 = EditorGUILayout.ColorField("Color 2", _customColor2);
                _customVertical = EditorGUILayout.ToggleLeft("Vertical", _customVertical);
                EditorGUI.indentLevel--;
            }
            _alignIndex = EditorGUILayout.Popup("Alignment", _alignIndex, AlignLabels);
            _styleIndex = EditorGUILayout.Popup("Style", _styleIndex, StyleLabels);
            _customSize = EditorGUILayout.IntField("Size (0 = default)", _customSize);
        }

        _applyTitle = EditorGUILayout.TextField(new GUIContent("Title", "Empty = keep each object's current title"), _applyTitle);

        EditorGUILayout.Space(2);
        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button("Apply banner (" + count + ")", GUILayout.Height(24)))
                ApplyToSelection();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Recolor (keeps title + options)", EditorStyles.miniLabel);
        DrawSwatchGrid(count);
    }

    private void DrawSwatchGrid(int selectionCount)
    {
        using (new EditorGUI.DisabledScope(selectionCount == 0))
        {
            const int perRow = 5;
            for (int i = 0; i < Swatches.Length; i++)
            {
                if (i % perRow == 0) EditorGUILayout.BeginHorizontal();

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = Swatches[i].m_color;
                if (GUILayout.Button(Swatches[i].m_name, GUILayout.Height(22)))
                    RecolorSelection(Swatches[i].m_name);
                GUI.backgroundColor = prev;

                if (i % perRow == perRow - 1 || i == Swatches.Length - 1) EditorGUILayout.EndHorizontal();
            }
        }
    }

    private void DrawPresets()
    {
        SerializedProperty presets = _so.FindProperty("m_presets");

        for (int i = 0; i < presets.arraySize; i++)
        {
            SerializedProperty el = presets.GetArrayElementAtIndex(i);
            SerializedProperty key = el.FindPropertyRelative("m_key");
            SerializedProperty spec = el.FindPropertyRelative("m_spec");

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
        if (GUILayout.Button("+ Add preset"))
        {
            int idx = presets.arraySize;
            presets.arraySize++;
            SerializedProperty el = presets.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("m_key").stringValue = "new";
            el.FindPropertyRelative("m_spec").stringValue = "gray left text:white";
        }
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset to defaults", GUILayout.Height(22)))
        {
            if (EditorUtility.DisplayDialog("Custom Hierarchy", "Reset the whole config?", "Yes", "Cancel"))
            {
                Undo.RecordObject(_config, "Reset Hierarchy Config");
                _config.ResetToDefaults();
                _config.m_version++;
                EditorUtility.SetDirty(_config);
                _so.Update();
                CustomHierarchyHeaders.OnConfigChanged(_config);
            }
        }
        if (GUILayout.Button("Show asset", GUILayout.Height(22)))
        {
            EditorGUIUtility.PingObject(_config);
            Selection.activeObject = _config;
        }
        EditorGUILayout.EndHorizontal();
    }

    private string BuildSpec()
    {
        if (_applyMode == ApplyMode.Preset)
        {
            if (_config.m_presets.Count == 0) return null;
            _presetIndex = Mathf.Clamp(_presetIndex, 0, _config.m_presets.Count - 1);
            return _config.m_presets[_presetIndex].m_key;
        }

        string spec = "#" + ColorUtility.ToHtmlStringRGB(_customColor);
        if (_customGradient) spec += ">#" + ColorUtility.ToHtmlStringRGB(_customColor2);
        if (_alignIndex == 1) spec += " left";
        else if (_alignIndex == 2) spec += " right";
        if (_styleIndex == 1) spec += " normal";
        else if (_styleIndex == 2) spec += " italic";
        else if (_styleIndex == 3) spec += " bolditalic";
        if (_customSize > 0) spec += " s" + _customSize;
        if (_customGradient && _customVertical) spec += " vertical";
        return spec;
    }

    private static string ExtractTitle(string name)
    {
        int eq = name.IndexOf('=');
        return eq >= 0 ? name.Substring(eq + 1).Trim() : name;
    }

    // Replaces the background of an existing name with colorToken, keeping title + other options.
    private static string ReplaceColor(string name, string colorToken)
    {
        int eq = name.IndexOf('=');
        string title = eq >= 0 ? name.Substring(eq + 1).Trim() : name;
        string spec = eq >= 0 ? name.Substring(0, eq).Trim() : "";

        List<string> kept = new List<string>();
        foreach (string t in spec.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (!IsColorToken(t)) kept.Add(t);

        kept.Add(colorToken); // last => wins over any preset
        return string.Join(" ", kept) + "=" + title;
    }

    private static bool IsColorToken(string token)
    {
        if (token.StartsWith("#")) return true;       // includes "#a>#b" gradients
        if (token.IndexOf('>') > 0) return true;       // named-color gradient e.g. blue>orange
        return NamedColors.Contains(token.ToLowerInvariant());
    }

    #endregion


    #region Private and Protected

    private static readonly string[] AlignLabels = { "Center", "Left", "Right" };
    private static readonly string[] StyleLabels = { "Bold", "Normal", "Italic", "BoldItalic" };

    private static readonly (string m_name, Color m_color)[] Swatches =
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

    private static readonly HashSet<string> NamedColors = new HashSet<string>
    {
        "green", "red", "blue", "orange", "gray", "grey", "yellow",
        "mauve", "white", "black", "cyan", "purple", "pink"
    };

    private HierarchyConfig _config;
    private SerializedObject _so;
    private Vector2 _scroll;

    private ApplyMode _applyMode = ApplyMode.Custom;
    private int _presetIndex;
    private Color _customColor = new Color(0.15f, 0.45f, 0.90f);
    private bool _customGradient;
    private Color _customColor2 = new Color(0.48f, 0.18f, 0.91f);
    private bool _customVertical;
    private int _alignIndex;
    private int _styleIndex;
    private int _customSize;
    private string _applyTitle = "";

    private GUIStyle _titleStyle;
    private GUIStyle _subTitleStyle;
    private GUIStyle _sectionStyle;
    private bool _stylesBuilt;

    #endregion
}
}
