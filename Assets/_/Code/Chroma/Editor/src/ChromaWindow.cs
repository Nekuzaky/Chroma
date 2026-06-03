using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace Chroma.Editor
{
/// <summary>
/// Chroma editor control panel. Two tabs: Selection (banner editor for the selected object)
/// and Settings (global Chroma configuration, rules, presets, RGB mode, themes).
/// Open via Tools > Chroma or right-click GameObject > Chroma > Open Window.
/// </summary>
public class ChromaWindow : EditorWindow
{
    private enum Tab { Selection, Settings }
    private enum OutputMode { Name, Component }
    private enum SelSource { None, NameBanner, Component }

    #region Unity API

    private void OnEnable()
    {
        _config = ChromaConfig.GetOrCreate();
        _so = new SerializedObject(_config);
        ChromaHeaders.OnConfigChanged(_config);
        wantsMouseMove = true; // live hover highlight on section headers
        _tab = (Tab)EditorPrefs.GetInt("Chroma.Tab", 0);
    }

    private void OnSelectionChange()
    {
        // The selection panel re-evaluates itself in the draw loop (SyncSelection); just repaint.
        Repaint();
    }

    private void OnGUI()
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();

        DrawHeaderBar();
        DrawTabBar();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);

        if (_tab == Tab.Selection) DrawSelectionTab();
        else DrawSettingsTab();

        EditorGUILayout.EndScrollView();
    }

    #endregion


    #region Main API

    /// <summary>Opens the Chroma editor window. Accessible from Tools > Chroma menu.</summary>
    [MenuItem("Tools/Chroma")]
    private static void Open()
    {
        var win = GetWindow<ChromaWindow>("Chroma");
        win.minSize = new Vector2(360f, 480f);
    }

    #endregion


    #region Chrome (header, tabs, sections)

    /// <summary>Build cached GUIStyles and color constants on first layout pass.</summary>
    private void EnsureStyles()
    {
        if (_stylesBuilt) return;
        bool pro = EditorGUIUtility.isProSkin;

        _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        _titleStyle.normal.textColor = Color.white;
        _titleStyle.padding = new RectOffset(4, 4, 4, 4);

        _subTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 10 };
        _subTitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

        _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        _sectionStyle.normal.textColor = pro ? new Color(0.90f, 0.92f, 0.95f) : new Color(0.10f, 0.12f, 0.18f);
        _sectionStyle.padding = new RectOffset(6, 6, 4, 4);

        _cardBody = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 2, 2) };
        _bookmarkRowStyle = new GUIStyle(EditorStyles.label);
        _previewStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        _accent = new Color(0.27f, 0.52f, 1f);
        _accentDim = new Color(0.45f, 0.30f, 0.85f);
        _headerBarColor = new Color(0.14f, 0.16f, 0.20f);
        _sectionHeaderBg = pro ? new Color(0.22f, 0.25f, 0.29f) : new Color(0.76f, 0.78f, 0.84f);
        _sectionHeaderHover = pro ? new Color(0.28f, 0.31f, 0.36f) : new Color(0.82f, 0.84f, 0.90f);
        _previewMaskColor = pro ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.784f, 0.784f, 0.784f);

        _stylesBuilt = true;
    }

    /// <summary>Draw the top banner with Chroma title, description, and search field.</summary>
    private void DrawHeaderBar()
    {
        Rect r = GUILayoutUtility.GetRect(0f, 70f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, _headerBarColor);

        EditorGUI.DrawRect(new Rect(r.x + 14f, r.y + 10f, 12f, 12f), _accent);
        EditorGUI.DrawRect(new Rect(r.x + 18f, r.y + 14f, 12f, 12f), _accentDim);

        GUI.Label(new Rect(r.x + 40f, r.y + 8f, r.width - 50f, 20f), "Chroma", _titleStyle);
        GUI.Label(new Rect(r.x + 40f, r.y + 24f, r.width - 50f, 14f), "Editor hierarchy & folder colors", _subTitleStyle);

        Rect searchRect = new Rect(r.x + 14f, r.y + 42f, r.width - 28f, 18f);
        _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);

        float half = r.width * 0.5f;
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 3f, half, 3f), _accent);
        EditorGUI.DrawRect(new Rect(r.x + half, r.yMax - 3f, r.width - half, 3f), _accentDim);
    }

    /// <summary>Draw the Selection / Settings tab buttons.</summary>
    private void DrawTabBar()
    {
        int t = GUILayout.Toolbar((int)_tab, _tabLabels, GUILayout.Height(24));
        if (t != (int)_tab)
        {
            _tab = (Tab)t;
            EditorPrefs.SetInt("Chroma.Tab", t);
            GUI.FocusControl(null);
        }
    }

    /// <summary>Get or create a cached AnimBool for animating section foldouts.</summary>
    private AnimBool GetAnim(string key, bool initial)
    {
        if (_anims.TryGetValue(key, out AnimBool anim)) return anim;
        anim = new AnimBool(initial) { speed = 3.5f };
        anim.valueChanged.AddListener(Repaint);
        _anims[key] = anim;
        return anim;
    }

    /// <summary>Begin a foldable section with animated collapse/expand. Persists open state in EditorPrefs.</summary>
    private bool BeginSection(string title, string key)
    {
        EditorGUILayout.Space(6);
        string prefKey = "Chroma.Fold." + key;
        bool open = EditorPrefs.GetBool(prefKey, true);

        Rect rect = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, hover ? _sectionHeaderHover : _sectionHeaderBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), open ? _accent : _accentDim);

        string triangle = open ? "▼" : "▶";
        GUI.Label(new Rect(rect.x + 14f, rect.y + 1f, 20f, rect.height), triangle, _sectionStyle);
        GUI.Label(new Rect(rect.x + 34f, rect.y, rect.width - 40f, rect.height), title, _sectionStyle);

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

        _sectionShown = EditorGUILayout.BeginFadeGroup(anim.faded);
        if (_sectionShown) EditorGUILayout.BeginVertical(_cardBody);
        return _sectionShown;
    }

    /// <summary>End a foldable section (closes the animated fade group and container).</summary>
    private void EndSection()
    {
        if (_sectionShown) EditorGUILayout.EndVertical();
        EditorGUILayout.EndFadeGroup();
        EditorGUILayout.Space(2);
    }

    #endregion


    #region Selection tab

    /// <summary>Draw the Selection tab: banner editor for the selected GameObject(s).</summary>
    private void DrawSelectionTab()
    {
        SyncSelection();
        int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;

        EditorGUILayout.BeginVertical(_cardBody);
        EditorGUILayout.LabelField("Edit colors & styles for selected object(s)", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);

        // Context line with icon.
        string status;
        if (count == 0) status = "⚪ Nothing selected";
        else if (count > 1) status = "📦 " + count + " objects selected — new banner";
        else if (_selSource == SelSource.Component) status = "🔧 Editing " + _selObject.name + " (component)";
        else if (_selSource == SelSource.NameBanner) status = "✏️ Editing " + _selObject.name + " (name)";
        else status = "➕ New banner for " + _selObject.name;
        EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        DrawPreview();
        EditorGUILayout.Space(6);

        _dTitle = EditorGUILayout.TextField(new GUIContent("Title", "Empty = keep each object's name"), _dTitle);

        _dBackground = EditorGUILayout.Toggle("Background", _dBackground);
        using (new EditorGUI.DisabledScope(!_dBackground))
        {
            EditorGUI.indentLevel++;
            _dColor = EditorGUILayout.ColorField("Color", _dColor);
            _dGradient = EditorGUILayout.Toggle("Gradient", _dGradient);
            using (new EditorGUI.DisabledScope(!_dGradient))
            {
                EditorGUI.indentLevel++;
                _dColor2 = EditorGUILayout.ColorField("Color 2", _dColor2);
                _dVertical = EditorGUILayout.Toggle("Vertical", _dVertical);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
        if (!_dBackground)
            EditorGUILayout.LabelField("   Text-only label (no colored block).", EditorStyles.miniLabel);

        _dTextColor = EditorGUILayout.ColorField("Text color", _dTextColor);
        _dAlign = EditorGUILayout.Popup("Alignment", _dAlign, AlignLabels);
        _dStyle = EditorGUILayout.Popup("Style", _dStyle, StyleLabels);
        _dSize = EditorGUILayout.IntField("Size (0 = default)", _dSize);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Store as", GUILayout.Width(EditorGUIUtility.labelWidth));
        _outputMode = (OutputMode)GUILayout.Toolbar((int)_outputMode, _outputLabels);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(
            _outputMode == OutputMode.Component
                ? "   Adds a component — the object keeps its name."
                : "   Encodes the style in the object's name.",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(4);
        bool nameEditing = count == 1 && _selSource == SelSource.NameBanner;
        using (new EditorGUI.DisabledScope(count == 0))
        {
            Color prev = GUI.backgroundColor;
            if (nameEditing)
            {
                // Editing an existing banner: renaming is the common case, so title-only is the
                // primary action (keeps colors byte-for-byte). Full rewrite is the secondary one.
                if (count > 0) GUI.backgroundColor = _accent;
                if (GUILayout.Button("Apply title only (" + count + ")", GUILayout.Height(28)))
                    ApplyTitleOnly();
                GUI.backgroundColor = prev;
                if (GUILayout.Button("Apply changes  (colors too)", GUILayout.Height(20)))
                    ApplyDraft();
            }
            else
            {
                bool editing = count == 1 && _selSource != SelSource.None;
                if (count > 0) GUI.backgroundColor = _accent;
                string label = (editing ? "Apply changes" : "Apply banner") + " (" + count + ")";
                if (GUILayout.Button(label, GUILayout.Height(28)))
                    ApplyDraft();
                GUI.backgroundColor = prev;
            }
        }

        EditorGUILayout.EndVertical();

        if (BeginSection("Quick recolor", "recolor")) DrawRecolor(count);
        EndSection();

        if (_config.m_presets.Count > 0)
        {
            if (BeginSection("Quick preset", "quickpreset")) DrawQuickPreset(count);
            EndSection();
        }

        if (BeginSection("Bookmarks", "bookmarks")) DrawBookmarks();
        EndSection();
    }

    /// <summary>Draw a live preview of the banner with the current draft settings.</summary>
    private void DrawPreview()
    {
        EditorGUILayout.Space(2);
        Rect r = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));

        // Rounded corners effect with border
        EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.1f)); // slight border

        if (!_dBackground)
            EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), _previewMaskColor);
        else if (_dGradient)
            DrawGradientRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), _dColor, _dColor2, _dVertical);
        else
            EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), _dColor);

        _previewStyle.alignment = AlignAnchor(_dAlign);
        _previewStyle.fontStyle = FontStyleOf(_dStyle);
        _previewStyle.fontSize = _dSize > 0 ? Mathf.Min(_dSize, 18) : 12;
        _previewStyle.normal.textColor = _dTextColor;

        string txt = !string.IsNullOrEmpty(_dTitle) ? _dTitle
            : (_selObject != null ? _selObject.name : "Preview");
        Rect tr = new Rect(r.x + 8f, r.y + 2, r.width - 16f, r.height - 4);
        GUI.Label(tr, txt, _previewStyle);
        EditorGUILayout.Space(2);
    }

    /// <summary>Draw quick color swatch buttons to recolor the selected object(s).</summary>
    private void DrawRecolor(int count)
    {
        EditorGUILayout.LabelField("Recolors the selection (name banner or component).", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(count == 0))
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

        EditorGUILayout.BeginHorizontal();
        _freeRecolorColor = EditorGUILayout.ColorField(GUIContent.none, _freeRecolorColor, false, false, false, GUILayout.Width(50));
        using (new EditorGUI.DisabledScope(count == 0))
            if (GUILayout.Button("Recolor with custom", GUILayout.Height(20)))
                RecolorSelection("#" + ColorUtility.ToHtmlStringRGB(_freeRecolorColor));
        EditorGUILayout.EndHorizontal();
    }

    #endregion


    #region Selection logic

    /// <summary>Detect selection changes and refresh the draft editor when a new object is selected.</summary>
    private void SyncSelection()
    {
        GameObject current = (Selection.gameObjects != null && Selection.gameObjects.Length == 1)
            ? Selection.gameObjects[0]
            : null;
        string currentName = current != null ? current.name : null;
        if (current != _selEvaluatedFor || currentName != _selEvaluatedName)
        {
            _selEvaluatedFor = current;
            _selEvaluatedName = currentName;
            RefreshDraft();
        }
    }

    /// <summary>Pre-fill the draft editor from an existing banner on the selected object (component or name-based).</summary>
    private void RefreshDraft()
    {
        _selSource = SelSource.None;
        _selObject = null;
        _selOriginalSpec = "";

        GameObject[] sel = Selection.gameObjects;
        if (sel == null || sel.Length != 1 || sel[0] == null) return;
        GameObject go = sel[0];
        _selObject = go;

        ChromaBanner comp = go.GetComponent<ChromaBanner>();
        if (comp != null)
        {
            _selSource = SelSource.Component;
            _outputMode = OutputMode.Component;
            _dTitle = comp.m_title;
            _dBackground = comp.m_background;
            _dColor = comp.m_color;
            _dGradient = comp.m_gradient;
            _dColor2 = comp.m_color2;
            _dVertical = comp.m_vertical;
            _dTextColor = comp.m_textColor;
            _dAlign = (int)comp.m_align;
            _dStyle = (int)comp.m_fontStyle;
            _dSize = comp.m_fontSize;
            return;
        }

        if (ChromaHeaders.TryParseEditable(go.name, out ChromaHeaders.EditableBanner e) && e.m_valid)
        {
            _selSource = SelSource.NameBanner;
            _outputMode = OutputMode.Name;
            _dTitle = e.m_title;
            _dBackground = e.m_bgToken != "nobg";
            _dColor = e.m_colorA;
            _dGradient = e.m_hasGradient;
            _dColor2 = e.m_colorB;
            _dVertical = e.m_vertical;
            _dTextColor = e.m_textColor;
            _dAlign = e.m_align;
            _dStyle = e.m_style;
            _dSize = e.m_size;

            int eq = go.name.IndexOf('=');
            _selOriginalSpec = eq >= 0 ? go.name.Substring(0, eq).Trim() : "";
        }
    }

    /// <summary>Apply the draft editor settings to all selected GameObjects (as component or name).</summary>
    private void ApplyDraft()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        foreach (GameObject go in sel)
        {
            if (go == null) continue;
            if (_outputMode == OutputMode.Component) WriteComponent(go);
            else WriteName(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Write the draft settings to a ChromaBanner component on a GameObject.</summary>
    private void WriteComponent(GameObject go)
    {
        ChromaBanner b = go.GetComponent<ChromaBanner>();
        if (b == null) b = Undo.AddComponent<ChromaBanner>(go);
        else Undo.RecordObject(b, "Chroma: set banner");

        b.m_background = _dBackground;
        b.m_color = _dColor;
        b.m_gradient = _dBackground && _dGradient;
        b.m_color2 = _dColor2;
        b.m_vertical = _dVertical;
        b.m_textColor = _dTextColor;
        b.m_align = (ChromaAlign)_dAlign;
        b.m_fontStyle = (ChromaFontStyle)_dStyle;
        b.m_fontSize = _dSize;
        b.m_title = _dTitle; // empty = use the GameObject's name
        EditorUtility.SetDirty(b);
    }

    /// <summary>Write the draft settings to a GameObject's name (as a banner spec).</summary>
    private void WriteName(GameObject go)
    {
        Undo.RecordObject(go, "Chroma: apply banner");
        string title = !string.IsNullOrEmpty(_dTitle) ? _dTitle : ExtractTitle(go.name);
        go.name = BuildDraftSpec() + "=" + title;
        EditorUtility.SetDirty(go);
    }

    /// <summary>Update only the title part of an existing name banner, preserving the color spec.</summary>
    private void ApplyTitleOnly()
    {
        if (_selObject == null) return;
        Undo.RecordObject(_selObject, "Chroma: edit title");
        _selObject.name = _selOriginalSpec + "=" + _dTitle;
        EditorUtility.SetDirty(_selObject);
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Encode the current draft settings into a banner spec string (colors, alignment, style, etc.).</summary>
    private string BuildDraftSpec()
    {
        string spec;
        if (!_dBackground)
        {
            spec = "nobg";
        }
        else
        {
            spec = "#" + ColorUtility.ToHtmlStringRGB(_dColor);
            if (_dGradient) spec += ">#" + ColorUtility.ToHtmlStringRGB(_dColor2);
        }
        if (_dAlign == 1) spec += " left";
        else if (_dAlign == 2) spec += " right";
        if (_dStyle == 1) spec += " normal";
        else if (_dStyle == 2) spec += " italic";
        else if (_dStyle == 3) spec += " bolditalic";
        if (_dSize > 0) spec += " s" + _dSize;
        if (_dBackground && _dGradient && _dVertical) spec += " vertical";
        if (_dTextColor != Color.white) spec += " text:#" + ColorUtility.ToHtmlStringRGB(_dTextColor);
        return spec;
    }

    /// <summary>Apply a preset key to all selected GameObjects (as name banners).</summary>
    private void ApplyPresetToSelection(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            Undo.RecordObject(go, "Chroma: apply preset");
            string title = !string.IsNullOrEmpty(_dTitle) ? _dTitle : ExtractTitle(go.name);
            go.name = key + "=" + title;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Change the banner color of the selected object(s) without affecting other settings.</summary>
    private void RecolorSelection(string colorToken)
    {
        ChromaHeaders.TryGetColor(colorToken, out Color col);
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;
            ChromaBanner b = go.GetComponent<ChromaBanner>();
            if (b != null)
            {
                Undo.RecordObject(b, "Chroma: recolor");
                b.m_background = true;
                b.m_gradient = false;
                b.m_color = col;
                EditorUtility.SetDirty(b);
            }
            else
            {
                Undo.RecordObject(go, "Chroma: recolor");
                go.name = ReplaceColor(go.name, colorToken);
                EditorUtility.SetDirty(go);
            }
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Draw the bookmarks section: add, list, reorder, and jump to bookmarked objects.</summary>
    private void DrawBookmarks()
    {
        int sel = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
        using (new EditorGUI.DisabledScope(sel == 0))
            if (GUILayout.Button("Bookmark selection (" + sel + ")"))
                foreach (GameObject go in Selection.gameObjects)
                    ChromaBookmarks.Add(go);

        IReadOnlyList<string> gids = ChromaBookmarks.Gids;
        if (gids.Count == 0)
        {
            EditorGUILayout.LabelField("No bookmarks", EditorStyles.miniLabel);
            return;
        }

        bool hasSearch = !string.IsNullOrWhiteSpace(_search);
        string removeGid = null;
        int moveFrom = -1, moveTo = -1;
        GameObject jumpTarget = null;
        int shown = 0;

        for (int i = 0; i < gids.Count; i++)
        {
            string gid = gids[i];
            GameObject go = ChromaBookmarks.ResolveGid(gid);
            string label = go != null ? go.name : "(not in open scene)";

            if (hasSearch && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            shown++;

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(hasSearch || i == 0))
                if (GUILayout.Button("▲", GUILayout.Width(22), GUILayout.Height(18))) { moveFrom = i; moveTo = i - 1; }
            using (new EditorGUI.DisabledScope(hasSearch || i == gids.Count - 1))
                if (GUILayout.Button("▼", GUILayout.Width(22), GUILayout.Height(18))) { moveFrom = i; moveTo = i + 1; }

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
                if (GUILayout.Button("Go", GUILayout.Width(34))) jumpTarget = go;
            if (GUILayout.Button("X", GUILayout.Width(22))) removeGid = gid;

            EditorGUILayout.EndHorizontal();
        }

        if (hasSearch && shown == 0)
            EditorGUILayout.LabelField("No bookmark matches '" + _search + "'", EditorStyles.miniLabel);

        if (jumpTarget != null) ChromaBookmarks.Jump(jumpTarget);
        else if (moveFrom >= 0 && moveTo >= 0) ChromaBookmarks.Reorder(moveFrom, moveTo);
        else if (removeGid != null) ChromaBookmarks.Remove(removeGid);
    }

    #endregion


    #region Settings tab

    /// <summary>Draw the Settings tab: global Chroma configuration (display toggles, rules, RGB mode, presets).</summary>
    private void DrawSettingsTab()
    {
        // Themes and Folder colors: managed separately (own undo/version bumps)
        if (BeginSection("Themes", "themes")) DrawThemes();
        EndSection();

        if (BeginSection("Folder colors", "folders")) DrawFolderColors();
        EndSection();

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

        if (BeginSection("RGB mode", "rgb")) DrawRgbSection();
        EndSection();

        if (BeginSection("Build", "build")) DrawBuildSection();
        EndSection();

        if (BeginSection("Banner presets", "presets")) DrawPresets();
        EndSection();

        if (EditorGUI.EndChangeCheck())
        {
            _so.ApplyModifiedProperties();
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            ChromaHeaders.OnConfigChanged(_config);
        }

        DrawFooter();
    }

    /// <summary>Draw the Display section: banner enable, child count, zebra striping toggles.</summary>
    private void DrawToggles()
    {
        EditorGUILayout.LabelField("Show/hide various visual elements", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableHeaders"),
            new GUIContent("Section banners", "Show colored banners when names contain #color codes"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_showChildCount"),
            new GUIContent("Child count (N)", "Display number of children next to each object"));

        SerializedProperty zebra = _so.FindProperty("m_zebra");
        EditorGUILayout.PropertyField(zebra, new GUIContent("Zebra striping", "Alternate row backgrounds"));
        using (new EditorGUI.DisabledScope(!zebra.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_zebraColor"), new GUIContent("Stripe color"));
    }

    /// <summary>Draw the Tree Lines section: enable/disable guide lines and customize their color.</summary>
    private void DrawTreeLinesSection()
    {
        EditorGUILayout.LabelField("File explorer style connector lines in the indent gutter", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        SerializedProperty enabled = _so.FindProperty("m_enableTreeLines");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Tree guide lines", "File explorer style connector lines"));
        using (new EditorGUI.DisabledScope(!enabled.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_treeLineColor"), new GUIContent("Line color"));
    }

    /// <summary>Draw the Separators section: configure visual style and text styling for separator rows (---/___).</summary>
    private void DrawSeparatorsSection()
    {
        EditorGUILayout.LabelField("Visual separator rows (name objects '---' or '___')", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
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

    /// <summary>Draw the Child Inheritance section: control whether children inherit parent banner colors and fade settings.</summary>
    private void DrawInherit()
    {
        EditorGUILayout.LabelField("Children tint from parent banners (flat or depth-fading)", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        SerializedProperty enabled = _so.FindProperty("m_enableChildInherit");
        EditorGUILayout.PropertyField(enabled, new GUIContent("Inherit parent color", "Children inherit color from parent banners"));

        if (enabled.boolValue)
        {
            EditorGUI.indentLevel++;
            SerializedProperty mode = _so.FindProperty("m_childInheritMode");
            EditorGUILayout.PropertyField(mode, new GUIContent("Mode", "Flat: constant opacity, DepthFade: fades with depth"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_childInheritOpacity"), new GUIContent("Opacity", "Base opacity for inherited colors"));
            if (mode.enumValueIndex == (int)ChildInheritMode.DepthFade)
                EditorGUILayout.PropertyField(_so.FindProperty("m_childInheritFalloff"), new GUIContent("Depth falloff", "How quickly opacity fades per level"));
            EditorGUI.indentLevel--;
        }
    }

    /// <summary>Draw the Build section: toggle stripping of Chroma specs from names in built scenes.</summary>
    private void DrawBuildSection()
    {
        EditorGUILayout.LabelField("Clean up GameObject names before building", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.PropertyField(_so.FindProperty("m_stripNamesInBuild"),
            new GUIContent("Strip names in build",
                "When ON, GameObject names with Chroma specs ('#xxx center bold=Title') are reduced to just 'Title' in built scenes. Scene .unity assets on disk are not modified."));
        EditorGUILayout.LabelField("ChromaBanner components are always removed from builds.", EditorStyles.miniLabel);
    }

    /// <summary>Draw the Auto-Color Rules section: manage rules to tint rows by Tag, Layer, name, or regex.</summary>
    private void DrawAutoColorRules()
    {
        EditorGUILayout.LabelField("Auto-tint rows by Tag, Layer, name prefix, or regex pattern", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
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

    /// <summary>Draw the RGB Mode section: animate rows through rainbow colors (the signature Chroma feature).</summary>
    private void DrawRgbSection()
    {
        EditorGUILayout.LabelField("Animate rows through rainbow colors (~30fps). The original Chroma signature!", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.PropertyField(_so.FindProperty("m_rgbMode"),
            new GUIContent("Rainbow mode", "Animate every row through rainbow colors ~30fps"));
        using (new EditorGUI.DisabledScope(!_so.FindProperty("m_rgbMode").boolValue))
        {
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSpeed"), new GUIContent("Speed", "Animation speed"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSpread"), new GUIContent("Hue spread", "Color variance per row"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbSaturation"), new GUIContent("Saturation"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbValue"), new GUIContent("Brightness"));
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbAlpha"), new GUIContent("Opacity"));
        }
        EditorGUILayout.PropertyField(_so.FindProperty("m_rgbFolders"),
            new GUIContent("Rainbow folders", "Also animate Project window folder icons"));
        EditorGUILayout.LabelField("The original Chroma feature!", EditorStyles.miniLabel);
    }

    /// <summary>Draw the Folder Colors section: manage colors for folders in the Project window.</summary>
    private void DrawFolderColors()
    {
        EditorGUILayout.LabelField("Color folders in the Project window", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        bool en = EditorGUILayout.Toggle("Enable folder colors", _config.m_enableFolderColors);
        if (en != _config.m_enableFolderColors)
        {
            Undo.RecordObject(_config, "Chroma: toggle folder colors");
            _config.m_enableFolderColors = en;
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            ChromaFolders.Invalidate();
            EditorApplication.RepaintProjectWindow();
        }

        List<string> folders = SelectedFolderGuids();
        EditorGUILayout.BeginHorizontal();
        _folderPickColor = EditorGUILayout.ColorField(GUIContent.none, _folderPickColor, false, false, false, GUILayout.Width(50));
        using (new EditorGUI.DisabledScope(folders.Count == 0))
            if (GUILayout.Button("Color selected folder(s) (" + folders.Count + ")"))
                foreach (string guid in folders)
                    ChromaFolders.SetColor(guid, _folderPickColor);
        EditorGUILayout.EndHorizontal();

        var list = _config.m_folderColors;
        if (list == null || list.Count == 0)
        {
            EditorGUILayout.LabelField("No colored folders", EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ChromaConfig.FolderColor f = list[i];
            if (f == null) continue;
            string path = AssetDatabase.GUIDToAssetPath(f.m_guid);
            string label = string.IsNullOrEmpty(path) ? "(missing folder)" : Path.GetFileName(path);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label);
            Color edited = EditorGUILayout.ColorField(GUIContent.none, f.m_color, false, false, false, GUILayout.Width(50));
            if (edited != f.m_color)
                ChromaFolders.SetColor(f.m_guid, edited);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                ChromaFolders.SetColor(f.m_guid, null);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>Get the GUIDs of selected folders in the Project window.</summary>
    private List<string> SelectedFolderGuids()
    {
        var guids = new List<string>();
        UnityEngine.Object[] objs = Selection.objects;
        if (objs == null) return guids;
        foreach (UnityEngine.Object o in objs)
        {
            string path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                guids.Add(AssetDatabase.AssetPathToGUID(path));
        }
        return guids;
    }

    /// <summary>Draw the Themes section: buttons to apply predefined color schemes (Minimal, Vibrant, Soft, High-Contrast).</summary>
    private void DrawThemes()
    {
        EditorGUILayout.LabelField("One-click color schemes for tree lines, separators, and presets", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Minimal", GUILayout.Height(26))) ApplyTheme("minimal");
        if (GUILayout.Button("Vibrant", GUILayout.Height(26))) ApplyTheme("vibrant");
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Soft", GUILayout.Height(26))) ApplyTheme("soft");
        if (GUILayout.Button("High-Contrast", GUILayout.Height(26))) ApplyTheme("contrast");
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Apply a preset theme: update tree line color, separator style, and presets.</summary>
    private void ApplyTheme(string theme)
    {
        Undo.RecordObject(_config, "Chroma: apply theme");

        switch (theme)
        {
            case "minimal":
                _config.m_treeLineColor = new Color(1f, 1f, 1f, 0.10f);
                _config.m_separatorColor = new Color(0.55f, 0.55f, 0.55f, 1f);
                _config.m_separatorFillColor = new Color(0.20f, 0.20f, 0.20f, 1f);
                _config.m_separatorStyle = SeparatorStyle.Solid;
                _config.m_presets = ThemePresets("#3a3f44", "#4a4f55", "#2c2f33", "#33363a", "#3a3f44>#2c2f33");
                break;
            case "vibrant":
                _config.m_treeLineColor = new Color(0.40f, 0.70f, 1f, 0.25f);
                _config.m_separatorColor = new Color(0.90f, 0.90f, 0.95f, 1f);
                _config.m_separatorFillColor = new Color(0.16f, 0.17f, 0.22f, 1f);
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#1f6feb", "#e0457b", "#1ca672", "#f0883e", "#1f6feb>#7b2ff7");
                break;
            case "soft":
                _config.m_treeLineColor = new Color(1f, 1f, 1f, 0.12f);
                _config.m_separatorColor = new Color(0.75f, 0.72f, 0.80f, 1f);
                _config.m_separatorFillColor = new Color(0.24f, 0.23f, 0.27f, 1f);
                _config.m_separatorStyle = SeparatorStyle.Dotted;
                _config.m_presets = ThemePresets("#6ea8fe", "#f4a6c0", "#8fd3b6", "#f6c08a", "#6ea8fe>#b9a3f0");
                break;
            case "contrast":
                _config.m_treeLineColor = new Color(1f, 1f, 1f, 0.35f);
                _config.m_separatorColor = Color.white;
                _config.m_separatorFillColor = Color.black;
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#0a84ff", "#ff375f", "#30d158", "#ff9f0a", "#0a84ff>#bf5af2");
                break;
        }

        _config.m_version++;
        EditorUtility.SetDirty(_config);
        ChromaHeaders.OnConfigChanged(_config);
        EditorApplication.RepaintHierarchyWindow();
        Repaint();
    }

    /// <summary>Build a list of theme presets from color specs.</summary>
    private static List<ChromaConfig.Preset> ThemePresets(string h1, string h2, string h3, string cat, string grad)
    {
        return new List<ChromaConfig.Preset>
        {
            new ChromaConfig.Preset { m_key = "h1",   m_spec = h1 + " center bold s12 text:white" },
            new ChromaConfig.Preset { m_key = "h2",   m_spec = h2 + " left bold text:white" },
            new ChromaConfig.Preset { m_key = "h3",   m_spec = h3 + " left italic text:white" },
            new ChromaConfig.Preset { m_key = "cat",  m_spec = cat + " left bold text:white" },
            new ChromaConfig.Preset { m_key = "grad", m_spec = grad + " center bold text:white" },
        };
    }

    /// <summary>Draw the Quick Preset section: dropdown to select and apply a preset to selected objects.</summary>
    private void DrawQuickPreset(int count)
    {
        EditorGUILayout.LabelField("Apply a preset style to the selection", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        string[] keys = new string[_config.m_presets.Count];
        for (int i = 0; i < keys.Length; i++) keys[i] = _config.m_presets[i].m_key;
        _presetIndex = Mathf.Clamp(_presetIndex, 0, keys.Length - 1);

        EditorGUILayout.BeginHorizontal();
        _presetIndex = EditorGUILayout.Popup(_presetIndex, keys);
        using (new EditorGUI.DisabledScope(count == 0))
            if (GUILayout.Button("Apply preset", GUILayout.Width(96)))
                ApplyPresetToSelection(keys[_presetIndex]);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Writes a short '" + keys[_presetIndex] + "=Title' to the name.", EditorStyles.miniLabel);
    }

    /// <summary>Draw the Banner Presets section: manage preset keys and specs with live color swatches.</summary>
    private void DrawPresets()
    {
        EditorGUILayout.LabelField("Create custom presets for quick banner styling", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
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
            if (ChromaHeaders.TryGetPreviewColor(spec.stringValue, out Color preview))
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

    /// <summary>Draw the footer: Reset to Defaults, Show Asset, and Import/Export buttons.</summary>
    private void DrawFooter()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset to defaults", GUILayout.Height(22)))
        {
            if (EditorUtility.DisplayDialog("Chroma", "Reset the whole config?", "Yes", "Cancel"))
            {
                Undo.RecordObject(_config, "Reset Chroma Config");
                _config.ResetToDefaults();
                _config.m_version++;
                EditorUtility.SetDirty(_config);
                _so.Update();
                ChromaHeaders.OnConfigChanged(_config);
            }
        }
        if (GUILayout.Button("Show asset", GUILayout.Height(22)))
        {
            EditorGUIUtility.PingObject(_config);
            Selection.activeObject = _config;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export config...", GUILayout.Height(22))) ExportConfig();
        if (GUILayout.Button("Import config...", GUILayout.Height(22))) ImportConfig();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Export the current config to a JSON file.</summary>
    private void ExportConfig()
    {
        string path = EditorUtility.SaveFilePanel("Export Chroma config", "", "chroma-config.json", "json");
        if (string.IsNullOrEmpty(path)) return;
        try { File.WriteAllText(path, JsonUtility.ToJson(_config, true)); }
        catch (Exception ex) { EditorUtility.DisplayDialog("Chroma", "Export failed:\n" + ex.Message, "OK"); }
    }

    /// <summary>Import a previously exported config JSON file.</summary>
    private void ImportConfig()
    {
        string path = EditorUtility.OpenFilePanel("Import Chroma config", "", "json");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            string json = File.ReadAllText(path);
            Undo.RecordObject(_config, "Import Chroma config");
            JsonUtility.FromJsonOverwrite(json, _config);
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            _so.Update();
            ChromaHeaders.OnConfigChanged(_config);
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("Chroma", "Import failed:\n" + ex.Message, "OK"); }
    }

    #endregion


    #region Tools and Utilities

    /// <summary>Draw a horizontal or vertical gradient rectangle between two colors.</summary>
    private static void DrawGradientRect(Rect r, Color a, Color b, bool vertical)
    {
        const int n = 24;
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            Color c = Color.Lerp(a, b, t);
            Rect seg = vertical
                ? new Rect(r.x, r.y + r.height * (i / (float)n), r.width, r.height / n + 1f)
                : new Rect(r.x + r.width * (i / (float)n), r.y, r.width / n + 1f, r.height);
            EditorGUI.DrawRect(seg, c);
        }
    }

    /// <summary>Convert alignment index (0=Center, 1=Left, 2=Right) to TextAnchor.</summary>
    private static TextAnchor AlignAnchor(int i)
    {
        if (i == 1) return TextAnchor.MiddleLeft;
        if (i == 2) return TextAnchor.MiddleRight;
        return TextAnchor.MiddleCenter;
    }

    /// <summary>Convert style index (0=Bold, 1=Normal, 2=Italic, 3=BoldItalic) to FontStyle.</summary>
    private static FontStyle FontStyleOf(int i)
    {
        if (i == 1) return FontStyle.Normal;
        if (i == 2) return FontStyle.Italic;
        if (i == 3) return FontStyle.BoldAndItalic;
        return FontStyle.Bold;
    }

    /// <summary>Extract the title part from a banner spec (text after '=').</summary>
    private static string ExtractTitle(string name)
    {
        int eq = name.IndexOf('=');
        return eq >= 0 ? name.Substring(eq + 1).Trim() : name;
    }

    /// <summary>Replace the color token in a banner spec while preserving alignment, style, size, and title.</summary>
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

    /// <summary>Check if a token is a color: hex (#color), gradient (color>color), or named color.</summary>
    private static bool IsColorToken(string token)
    {
        if (token.StartsWith("#")) return true;
        if (token.IndexOf('>') > 0) return true;
        return NamedColors.Contains(token.ToLowerInvariant());
    }

    #endregion


    #region Private and Protected

    private static readonly string[] _tabLabels = { "Selection", "Settings" };
    private static readonly string[] _outputLabels = { "Name", "Component" };
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

    private ChromaConfig _config;
    private SerializedObject _so;
    private Vector2 _scroll;
    private Tab _tab;

    // Unified draft used by the Selection tab (pre-filled when editing an existing banner).
    private OutputMode _outputMode = OutputMode.Name;
    private string _dTitle = "";
    private bool _dBackground = true;
    private Color _dColor = new Color(0.15f, 0.45f, 0.90f);
    private bool _dGradient;
    private Color _dColor2 = new Color(0.48f, 0.18f, 0.91f);
    private bool _dVertical;
    private Color _dTextColor = Color.white;
    private int _dAlign;
    private int _dStyle;
    private int _dSize;

    private SelSource _selSource;
    private GameObject _selObject;
    private string _selOriginalSpec = "";
    private GameObject _selEvaluatedFor;
    private string _selEvaluatedName;

    private Color _freeRecolorColor = new Color(0.30f, 0.60f, 0.90f);
    private Color _folderPickColor = new Color(0.30f, 0.55f, 1f);
    private string _search = "";

    private readonly Dictionary<string, AnimBool> _anims = new Dictionary<string, AnimBool>();
    private int _presetIndex;

    private GUIStyle _titleStyle;
    private GUIStyle _subTitleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _cardBody;
    private GUIStyle _bookmarkRowStyle;
    private GUIStyle _previewStyle;
    private Color _accent;
    private Color _accentDim;
    private Color _headerBarColor;
    private Color _sectionHeaderBg;
    private Color _sectionHeaderHover;
    private Color _previewMaskColor;
    private bool _sectionShown;
    private bool _stylesBuilt;

    #endregion
}
}
