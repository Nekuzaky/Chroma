using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace Chromarchy.Editor
{
// Config panel for Chromarchy. Open via Tools/Chromarchy or GameObject/Chromarchy/Open Window.
public class ChromarchyWindow : EditorWindow
{
    private enum ApplyMode { Preset, Custom }

    #region Publics


    #endregion


    #region Unity API

    private void OnEnable()
    {
        _config = ChromarchyConfig.GetOrCreate();
        _so = new SerializedObject(_config);
        ChromarchyHeaders.OnConfigChanged(_config);
        wantsMouseMove = true; // enables live hover highlight on section headers
        RefreshEditState();
    }

    private void OnSelectionChange()
    {
        RefreshEditState();
        Repaint();
    }

    private void OnGUI()
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();

        DrawHeaderBar();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);

        // Local fields + object renaming / EditorPrefs: these do NOT touch the config asset.
        if (BeginSection("Apply to selection", "apply")) DrawApplyBody();
        EndSection();

        if (BeginSection("Bookmarks", "bookmarks")) DrawBookmarks();
        EndSection();

        // Asset editing: only these changes mark the config dirty.
        EditorGUI.BeginChangeCheck();

        if (BeginSection("Display", "display")) DrawToggles();
        EndSection();

        if (BeginSection("Tree lines", "treelines")) DrawTreeLinesSection();
        EndSection();

        if (BeginSection("Separators", "separators")) DrawSeparatorsSection();
        EndSection();

        if (BeginSection("Child inheritance", "inherit")) DrawInherit();
        EndSection();

        if (BeginSection("Auto-color rules", "autocolor")) DrawAutoColorRules();
        EndSection();

        if (BeginSection("Build", "build")) DrawBuildSection();
        EndSection();

        if (BeginSection("RGB mode", "rgb")) DrawRgbSection();
        EndSection();

        if (BeginSection("Banner presets", "presets")) DrawPresets();
        EndSection();

        if (EditorGUI.EndChangeCheck())
        {
            _so.ApplyModifiedProperties();
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            ChromarchyHeaders.OnConfigChanged(_config);
        }

        DrawFooter();

        EditorGUILayout.EndScrollView();
    }

    #endregion


    #region Main API

    [MenuItem("Tools/Chromarchy")]
    private static void Open()
    {
        var win = GetWindow<ChromarchyWindow>("Chromarchy");
        win.minSize = new Vector2(380f, 520f);
    }

    private void ApplyToSelection()
    {
        string spec = BuildSpec();
        if (string.IsNullOrEmpty(spec)) return;

        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Chromarchy: apply banner");
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
            Undo.RecordObject(go, "Chromarchy: recolor");
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
        bool pro = EditorGUIUtility.isProSkin;

        _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        _titleStyle.normal.textColor = Color.white;
        _subTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        _subTitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);

        _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        _sectionStyle.normal.textColor = pro ? new Color(0.85f, 0.87f, 0.92f) : new Color(0.15f, 0.16f, 0.20f);

        _cardBody = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 8) };
        _bookmarkRowStyle = new GUIStyle(EditorStyles.label);

        _accent = new Color(0.27f, 0.52f, 1f);
        _accentDim = new Color(0.45f, 0.30f, 0.85f);
        _headerBarColor = new Color(0.16f, 0.18f, 0.22f);
        _sectionHeaderBg = pro ? new Color(0.25f, 0.27f, 0.31f) : new Color(0.74f, 0.76f, 0.81f);
        _sectionHeaderHover = pro ? new Color(0.30f, 0.33f, 0.38f) : new Color(0.80f, 0.82f, 0.88f);

        _stylesBuilt = true;
    }

    private void DrawHeaderBar()
    {
        Rect r = GUILayoutUtility.GetRect(0f, 60f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, _headerBarColor);

        // Accent square "logo" + two-tone underline give the bar a designed feel.
        EditorGUI.DrawRect(new Rect(r.x + 12f, r.y + 9f, 14f, 14f), _accent);
        EditorGUI.DrawRect(new Rect(r.x + 16f, r.y + 13f, 14f, 14f), _accentDim);

        Rect titleRect = new Rect(r.x + 38f, r.y + 5f, r.width - 50f, 18f);
        GUI.Label(titleRect, "Chromarchy", _titleStyle);
        Rect subRect = new Rect(r.x + 38f, r.y + 21f, r.width - 50f, 14f);
        GUI.Label(subRect, "Color-code your hierarchy", _subTitleStyle);

        Rect searchRect = new Rect(r.x + 12f, r.y + 38f, r.width - 24f, 18f);
        _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);

        // Accent underline: blue fading into purple across the bar width.
        float half = r.width * 0.5f;
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, half, 2f), _accent);
        EditorGUI.DrawRect(new Rect(r.x + half, r.yMax - 2f, r.width - half, 2f), _accentDim);
    }

    private AnimBool GetAnim(string key, bool initial)
    {
        if (_anims.TryGetValue(key, out AnimBool anim)) return anim;
        anim = new AnimBool(initial);
        anim.speed = 3.5f; // snappy but visibly smooth
        anim.valueChanged.AddListener(Repaint);
        _anims[key] = anim;
        return anim;
    }

    // Foldable card section. Header strip is custom-drawn (accent bar + triangle + hover); the body
    // is a padded helpBox card that animates open/closed. Open state persisted via EditorPrefs.
    // Returns true when the body should be drawn (open or mid-animation).
    private bool BeginSection(string title, string key)
    {
        EditorGUILayout.Space(4);
        string prefKey = "Chromarchy.Fold." + key;
        bool open = EditorPrefs.GetBool(prefKey, true);

        Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, hover ? _sectionHeaderHover : _sectionHeaderBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), open ? _accent : _accentDim);
        GUI.Label(new Rect(rect.x + 12f, rect.y, rect.width - 14f, rect.height),
            (open ? "▾  " : "▸  ") + title, _sectionStyle);

        // Toggle on header click. We persist the new state and Repaint, but do NOT flip `open`
        // this pass: changing the layout-group count during a non-Layout event would desync
        // IMGUI. The new state takes effect on the next frame. GUI.changed is preserved so the
        // toggle never trips the surrounding config change-check.
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            bool prevChanged = GUI.changed;
            EditorPrefs.SetBool(prefKey, !open);
            GUI.changed = prevChanged;
            Event.current.Use();
            Repaint();
        }

        AnimBool anim = GetAnim(key, open);
        anim.target = open;

        // BeginFadeGroup must always be paired with EndFadeGroup; BeginVertical only when shown.
        _sectionShown = EditorGUILayout.BeginFadeGroup(anim.faded);
        if (_sectionShown) EditorGUILayout.BeginVertical(_cardBody);
        return _sectionShown;
    }

    private void EndSection()
    {
        if (_sectionShown) EditorGUILayout.EndVertical();
        EditorGUILayout.EndFadeGroup();
        EditorGUILayout.Space(2);
    }

    private void DrawToggles()
    {
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableHeaders"), new GUIContent("Section banners"));
    }

    private void DrawTreeLinesSection()
    {
        SerializedProperty enabled = _so.FindProperty("m_enableTreeLines");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Tree guide lines"));
        using (new EditorGUI.DisabledScope(!enabled.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_treeLineColor"), new GUIContent("Line color"));
    }

    private void DrawSeparatorsSection()
    {
        SerializedProperty enabled = _so.FindProperty("m_enableSeparators");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Separator rows"));
        using (new EditorGUI.DisabledScope(!enabled.boolValue))
        {
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorColor"), new GUIContent("Line color"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorFillColor"), new GUIContent("Background fill"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorStyle"), new GUIContent("Line style"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorBold"), new GUIContent("Caption bold"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorItalic"), new GUIContent("Caption italic"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_separatorUppercase"), new GUIContent("Uppercase caption"));
        }
        EditorGUILayout.LabelField("Name an object '---' / '___' (or '--- Label')", EditorStyles.miniLabel);
    }

    private void DrawInherit()
    {
        SerializedProperty enabled = _so.FindProperty("m_enableChildInherit");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Inherit parent color"));

        if (enabled.boolValue)
        {
            EditorGUI.indentLevel++;
            SerializedProperty mode = _so.FindProperty("m_childInheritMode");
            EditorGUILayout.PropertyField(mode, new GUIContent("Mode"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_childInheritOpacity"), new GUIContent("Opacity"));
            if (mode.enumValueIndex == (int)ChildInheritMode.DepthFade)
                EditorGUILayout.PropertyField(_so.FindProperty("m_childInheritFalloff"), new GUIContent("Depth falloff"));
            EditorGUI.indentLevel--;
        }
    }

    private void DrawBuildSection()
    {
        EditorGUILayout.PropertyField(_so.FindProperty("m_stripNamesInBuild"),
            new GUIContent("Strip names in build",
                "When ON, GameObject names with Chromarchy specs ('#xxx center bold=Title') are reduced to just 'Title' in built scenes. Scene .unity assets on disk are not modified."));
        EditorGUILayout.LabelField("Editor-only — scene assets are untouched.", EditorStyles.miniLabel);
    }

    private void DrawAutoColorRules()
    {
        EditorGUILayout.LabelField("Tint rows by Tag / Layer / name prefix / regex", EditorStyles.miniLabel);
        SerializedProperty rules = _so.FindProperty("m_autoColorRules");

        for (int i = 0; i < rules.arraySize; i++)
        {
            SerializedProperty el = rules.GetArrayElementAtIndex(i);
            SerializedProperty enabled = el.FindPropertyRelative("m_enabled");
            SerializedProperty match = el.FindPropertyRelative("m_match");
            SerializedProperty value = el.FindPropertyRelative("m_value");
            SerializedProperty color = el.FindPropertyRelative("m_color");

            EditorGUILayout.BeginHorizontal();
            enabled.boolValue = EditorGUILayout.Toggle(enabled.boolValue, GUILayout.Width(16));
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                EditorGUILayout.PropertyField(match, GUIContent.none, GUILayout.Width(86));
                value.stringValue = EditorGUILayout.TextField(value.stringValue);
                color.colorValue = EditorGUILayout.ColorField(GUIContent.none, color.colorValue, false, true, false, GUILayout.Width(50));
            }
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                rules.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("+ Add rule"))
        {
            int idx = rules.arraySize;
            rules.arraySize++;
            SerializedProperty el = rules.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("m_enabled").boolValue = true;
            el.FindPropertyRelative("m_match").enumValueIndex = (int)AutoColorMatch.Tag;
            el.FindPropertyRelative("m_value").stringValue = "";
            el.FindPropertyRelative("m_color").colorValue = new Color(0.20f, 0.50f, 0.90f, 0.18f);
        }
    }

    private void DrawApplyBody()
    {
        int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;

        DrawSelectedBannerEditor();

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
            Color prevBg = GUI.backgroundColor;
            if (count > 0) GUI.backgroundColor = _accent;
            if (GUILayout.Button("Apply banner (" + count + ")", GUILayout.Height(26)))
                ApplyToSelection();
            GUI.backgroundColor = prevBg;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Recolor (keeps title + options)", EditorStyles.miniLabel);
        DrawSwatchGrid(count);

        EditorGUILayout.BeginHorizontal();
        _freeRecolorColor = EditorGUILayout.ColorField(GUIContent.none, _freeRecolorColor, false, false, false, GUILayout.Width(50));
        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button("Recolor with custom", GUILayout.Height(20)))
                RecolorSelection("#" + ColorUtility.ToHtmlStringRGB(_freeRecolorColor));
        }
        EditorGUILayout.EndHorizontal();
    }

    // Reloads the inline-editor fields from the currently selected object (if it's a single banner).
    private void RefreshEditState()
    {
        _editValid = false;
        _editTarget = null;
        GameObject[] sel = Selection.gameObjects;
        if (sel == null || sel.Length != 1 || sel[0] == null) return;

        if (ChromarchyHeaders.TryParseEditable(sel[0].name, out ChromarchyHeaders.EditableBanner e) && e.m_valid)
        {
            _editValid = true;
            _editTarget = sel[0];
            _editTitle = e.m_title;
            _editColorA = e.m_colorA;
            _editGradient = e.m_hasGradient;
            _editColorB = e.m_colorB;
            _editVertical = e.m_vertical;
            _editAlign = e.m_align;
            _editStyle = e.m_style;
            _editSize = e.m_size;
            _editTextColor = e.m_textColor;
        }
    }

    private void DrawSelectedBannerEditor()
    {
        // Selection can change between events without OnSelectionChange firing for the same object
        // (e.g. the name was edited elsewhere); keep the target in sync defensively.
        if (_editTarget != null && (Selection.gameObjects == null || Selection.gameObjects.Length != 1 || Selection.gameObjects[0] != _editTarget))
            RefreshEditState();

        if (!_editValid || _editTarget == null) return;

        EditorGUILayout.LabelField("Edit selected banner", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(new GUIContent("Raw: " + _editTarget.name, _editTarget.name), EditorStyles.miniLabel);

        _editTitle = EditorGUILayout.TextField("Title", _editTitle);
        _editColorA = EditorGUILayout.ColorField(_editGradient ? "Color 1" : "Color", _editColorA);
        _editGradient = EditorGUILayout.ToggleLeft("Gradient", _editGradient);
        if (_editGradient)
        {
            EditorGUI.indentLevel++;
            _editColorB = EditorGUILayout.ColorField("Color 2", _editColorB);
            _editVertical = EditorGUILayout.ToggleLeft("Vertical", _editVertical);
            EditorGUI.indentLevel--;
        }
        _editTextColor = EditorGUILayout.ColorField("Text color", _editTextColor);
        _editAlign = EditorGUILayout.Popup("Alignment", _editAlign, AlignLabels);
        _editStyle = EditorGUILayout.Popup("Style", _editStyle, StyleLabels);
        _editSize = EditorGUILayout.IntField("Size (0 = default)", _editSize);

        EditorGUILayout.Space(2);
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = _accent;
        if (GUILayout.Button("Apply changes", GUILayout.Height(24)))
            ApplyEdit();
        GUI.backgroundColor = prevBg;

        Rect div = GUILayoutUtility.GetRect(0f, 8f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(div.x, div.y + 4f, div.width, 1f), new Color(1f, 1f, 1f, 0.08f));
        EditorGUILayout.LabelField("Or apply a new banner to the selection:", EditorStyles.miniLabel);
    }

    private void ApplyEdit()
    {
        if (_editTarget == null) return;
        Undo.RecordObject(_editTarget, "Chromarchy: edit banner");
        _editTarget.name = BuildEditSpec() + "=" + _editTitle;
        EditorUtility.SetDirty(_editTarget);
        EditorApplication.RepaintHierarchyWindow();
    }

    private string BuildEditSpec()
    {
        string spec = "#" + ColorUtility.ToHtmlStringRGB(_editColorA);
        if (_editGradient) spec += ">#" + ColorUtility.ToHtmlStringRGB(_editColorB);
        if (_editAlign == 1) spec += " left";
        else if (_editAlign == 2) spec += " right";
        if (_editStyle == 1) spec += " normal";
        else if (_editStyle == 2) spec += " italic";
        else if (_editStyle == 3) spec += " bolditalic";
        if (_editSize > 0) spec += " s" + _editSize;
        if (_editGradient && _editVertical) spec += " vertical";
        // Re-emit a non-white text color so editing it round-trips; white is the default, omit it.
        if (_editTextColor != Color.white) spec += " text:#" + ColorUtility.ToHtmlStringRGB(_editTextColor);
        return spec;
    }

    private void DrawRgbSection()
    {
        EditorGUILayout.PropertyField(_so.FindProperty("m_rgbMode"), new GUIContent("Rainbow mode"));
        using (new EditorGUI.DisabledScope(!_so.FindProperty("m_rgbMode").boolValue))
        {
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSpeed"), new GUIContent("Speed"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSpread"), new GUIContent("Hue spread / row"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSaturation"), new GUIContent("Saturation"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbValue"), new GUIContent("Brightness"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbAlpha"), new GUIContent("Opacity"));
        }
        EditorGUILayout.LabelField("Animated; tints non-banner rows. Editor-only.", EditorStyles.miniLabel);
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

    private void DrawBookmarks()
    {
        int sel = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
        using (new EditorGUI.DisabledScope(sel == 0))
            if (GUILayout.Button("Bookmark selection (" + sel + ")"))
                foreach (GameObject go in Selection.gameObjects)
                    ChromarchyBookmarks.Add(go);

        IReadOnlyList<string> gids = ChromarchyBookmarks.Gids;
        if (gids.Count == 0)
        {
            EditorGUILayout.LabelField("No bookmarks", EditorStyles.miniLabel);
            return;
        }

        bool hasSearch = !string.IsNullOrWhiteSpace(_search);
        // Mutate the bookmarks list after iteration to avoid corrupting GIDs while drawing.
        string removeGid = null;
        int moveFrom = -1, moveTo = -1;
        GameObject jumpTarget = null;
        int shown = 0;

        for (int i = 0; i < gids.Count; i++)
        {
            string gid = gids[i];
            GameObject go = ChromarchyBookmarks.ResolveGid(gid);
            string label = go != null ? go.name : "(not in open scene)";

            if (hasSearch && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            shown++;

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(hasSearch || i == 0))
                if (GUILayout.Button("▲", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    moveFrom = i; moveTo = i - 1;
                }
            using (new EditorGUI.DisabledScope(hasSearch || i == gids.Count - 1))
                if (GUILayout.Button("▼", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    moveFrom = i; moveTo = i + 1;
                }

            GUILayout.Label(label, _bookmarkRowStyle, GUILayout.ExpandWidth(true));
            Rect labelRect = GUILayoutUtility.GetLastRect();
            if (go != null
                && Event.current.type == EventType.MouseDown
                && Event.current.clickCount == 2
                && labelRect.Contains(Event.current.mousePosition))
            {
                jumpTarget = go;
                Event.current.Use();
            }

            using (new EditorGUI.DisabledScope(go == null))
                if (GUILayout.Button("Go", GUILayout.Width(34)))
                    jumpTarget = go;
            if (GUILayout.Button("X", GUILayout.Width(22)))
                removeGid = gid;

            EditorGUILayout.EndHorizontal();
        }

        if (hasSearch && shown == 0)
            EditorGUILayout.LabelField("No bookmark matches '" + _search + "'", EditorStyles.miniLabel);

        if (jumpTarget != null) ChromarchyBookmarks.Jump(jumpTarget);
        else if (moveFrom >= 0 && moveTo >= 0) ChromarchyBookmarks.Reorder(moveFrom, moveTo);
        else if (removeGid != null) ChromarchyBookmarks.Remove(removeGid);
    }

    private void DrawPresets()
    {
        SerializedProperty presets = _so.FindProperty("m_presets");
        bool hasSearch = !string.IsNullOrWhiteSpace(_search);
        int removeAt = -1;
        int shown = 0;

        for (int i = 0; i < presets.arraySize; i++)
        {
            SerializedProperty el = presets.GetArrayElementAtIndex(i);
            SerializedProperty key = el.FindPropertyRelative("m_key");
            SerializedProperty spec = el.FindPropertyRelative("m_spec");

            if (hasSearch
                && (key.stringValue ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0
                && (spec.stringValue ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            shown++;

            EditorGUILayout.BeginHorizontal();
            key.stringValue = EditorGUILayout.TextField(key.stringValue, GUILayout.Width(70));
            spec.stringValue = EditorGUILayout.TextField(spec.stringValue);

            Rect swatchRect = GUILayoutUtility.GetRect(22f, 18f, GUILayout.Width(22f));
            if (ChromarchyHeaders.TryGetPreviewColor(spec.stringValue, out Color preview))
            {
                EditorGUI.DrawRect(swatchRect, preview);
            }
            else
            {
                EditorGUI.DrawRect(swatchRect, new Color(0.2f, 0.2f, 0.2f));
                GUI.Label(swatchRect, "?", EditorStyles.centeredGreyMiniLabel);
            }

            if (GUILayout.Button("X", GUILayout.Width(22)))
                removeAt = i;
            EditorGUILayout.EndHorizontal();
        }

        if (removeAt >= 0)
            presets.DeleteArrayElementAtIndex(removeAt);

        if (hasSearch && shown == 0)
            EditorGUILayout.LabelField("No preset matches '" + _search + "'", EditorStyles.miniLabel);

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
            if (EditorUtility.DisplayDialog("Chromarchy", "Reset the whole config?", "Yes", "Cancel"))
            {
                Undo.RecordObject(_config, "Reset Chromarchy Config");
                _config.ResetToDefaults();
                _config.m_version++;
                EditorUtility.SetDirty(_config);
                _so.Update();
                ChromarchyHeaders.OnConfigChanged(_config);
            }
        }
        if (GUILayout.Button("Show asset", GUILayout.Height(22)))
        {
            EditorGUIUtility.PingObject(_config);
            Selection.activeObject = _config;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export config...", GUILayout.Height(22)))
            ExportConfig();
        if (GUILayout.Button("Import config...", GUILayout.Height(22)))
            ImportConfig();
        EditorGUILayout.EndHorizontal();
    }

    private void ExportConfig()
    {
        string path = EditorUtility.SaveFilePanel("Export Chromarchy config", "", "chromarchy-config.json", "json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(_config, true));
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Chromarchy", "Export failed:\n" + ex.Message, "OK");
        }
    }

    private void ImportConfig()
    {
        string path = EditorUtility.OpenFilePanel("Import Chromarchy config", "", "json");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            string json = File.ReadAllText(path);
            Undo.RecordObject(_config, "Import Chromarchy config");
            JsonUtility.FromJsonOverwrite(json, _config);
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            _so.Update();
            ChromarchyHeaders.OnConfigChanged(_config);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Chromarchy", "Import failed:\n" + ex.Message, "OK");
        }
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

    private ChromarchyConfig _config;
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

    private Color _freeRecolorColor = new Color(0.30f, 0.60f, 0.90f);
    private string _search = "";

    // Inline editor state for the single selected banner.
    private bool _editValid;
    private GameObject _editTarget;
    private string _editTitle = "";
    private Color _editColorA = new Color(0.15f, 0.45f, 0.90f);
    private bool _editGradient;
    private Color _editColorB = new Color(0.48f, 0.18f, 0.91f);
    private bool _editVertical;
    private int _editAlign;
    private int _editStyle;
    private int _editSize;
    private Color _editTextColor = Color.white;

    private readonly Dictionary<string, AnimBool> _anims = new Dictionary<string, AnimBool>();

    private GUIStyle _titleStyle;
    private GUIStyle _subTitleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _cardBody;
    private GUIStyle _bookmarkRowStyle;
    private Color _accent;
    private Color _accentDim;
    private Color _headerBarColor;
    private Color _sectionHeaderBg;
    private Color _sectionHeaderHover;
    private bool _sectionShown;
    private bool _stylesBuilt;

    #endregion
}
}
