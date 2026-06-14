using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chroma.Editor
{
/// <summary>
/// Chroma editor control panel. Two tabs: Selection (banner editor for the selected object)
/// and Settings (global Chroma configuration, rules, presets, RGB mode, themes).
/// Open via Tools > Chroma or right-click GameObject > Chroma > Open Window.
/// </summary>
public class ChromaWindow : EditorWindow
{
    private enum Tab { Selection, Settings, Lint }
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

        ChromaLinter.Changed -= HandleLintChanged;
        ChromaLinter.Changed += HandleLintChanged;

        // Drive the animated chromatic header while the window is open.
        EditorApplication.update -= AnimateChrome;
        EditorApplication.update += AnimateChrome;
    }

    private void OnDisable()
    {
        ChromaLinter.Changed -= HandleLintChanged;
        EditorApplication.update -= AnimateChrome;
    }

    /// <summary>
    /// Animates the header chrome: the logo chip cycles through bright hues, the header background
    /// drifts through a subtle dark tint, and the rainbow divider scrolls — Chroma's RGB signature.
    /// Throttled to ~25fps and only runs while the window is open (unsubscribed in OnDisable).
    /// </summary>
    private void AnimateChrome()
    {
        double now = EditorApplication.timeSinceStartup;
        float baseHue = Mathf.Repeat((float)now * 0.05f, 1f);

        // Fast lane (~25fps): header chrome only — cheap, no IMGUI bodies re-run here.
        if (now - _lastAnim >= 0.04)
        {
            _lastAnim = now;
            if (_header != null) _header.style.backgroundColor = Color.HSVToRGB(baseHue, 0.20f, 0.14f);
            _rainbow?.MarkDirtyRepaint();
        }

        // Slow lane (~10fps): section-card accents (spread across the spectrum) + active tab tint.
        // Throttled because repainting a card can re-run its IMGUI body.
        if (now - _lastAccent >= 0.1)
        {
            _lastAccent = now;
            for (int i = 0; i < _accentCards.Count; i++)
            {
                if (_accentCards[i] == null) continue;
                float h = Mathf.Repeat(baseHue + i * 0.055f, 1f);
                _accentCards[i].style.borderLeftColor = Color.HSVToRGB(h, 0.7f, 0.92f);
            }

            if (_tabButtons != null)
                for (int i = 0; i < _tabButtons.Length; i++)
                {
                    if (_tabButtons[i] == null) continue;
                    if ((int)_tab == i) _tabButtons[i].style.backgroundColor = Color.HSVToRGB(baseHue, 0.5f, 0.55f);
                    else _tabButtons[i].style.backgroundColor = StyleKeyword.Null; // fall back to USS
                }
        }
    }

    /// <summary>Paint the full-width animated rainbow divider under the header.</summary>
    private void PaintRainbow()
    {
        if (_rainbow == null) return;
        Rect r = _rainbow.contentRect;
        if (r.width < 1f || r.height < 1f) return;

        float time = (float)EditorApplication.timeSinceStartup;
        int slices = Mathf.Max(16, Mathf.CeilToInt(r.width / 5f));
        float sw = r.width / slices;
        for (int i = 0; i < slices; i++)
        {
            float hue = Mathf.Repeat(time * 0.05f + i / (float)slices, 1f);
            EditorGUI.DrawRect(new Rect(i * sw, 0f, sw + 1f, r.height), Color.HSVToRGB(hue, 0.7f, 1f));
        }
    }

    /// <summary>Linter finished a scan: refresh the tab badge and the Lint tab bodies.</summary>
    private void HandleLintChanged()
    {
        UpdateLintTabLabel();
        RepaintSettings();
    }

    /// <summary>Show the violation count in the Lint tab label ("Lint (3)").</summary>
    private void UpdateLintTabLabel()
    {
        if (_tabButtons == null || _tabButtons.Length < 3 || _tabButtons[2] == null) return;
        int total = ChromaLinter.Total;
        _tabButtons[2].text = total > 0 ? "Lint (" + total + ")" : "Lint";
    }

    private void OnSelectionChange()
    {
        if (_tab == Tab.Selection && _uiTitle != null)
        {
            SyncSelection();
            RefreshSelectionUI();
        }
        _selExtras?.MarkDirtyRepaint();
        RepaintSettings(); // e.g. Folder colors' "Color selected folder(s)" depends on the Project selection
    }

    /// <summary>
    /// UI Toolkit entry point. Wraps the real build in a try/catch because UI Toolkit can swallow
    /// exceptions thrown during CreateGUI, leaving the window blank with nothing in the Console.
    /// </summary>
    private void CreateGUI()
    {
        try
        {
            BuildUI();
        }
        catch (Exception e)
        {
            Debug.LogError("[Chroma] Window build failed — please report this stack trace:\n" + e);
            BuildFallbackUI(e);
        }
    }

    /// <summary>Minimal UI shown when BuildUI throws, so the window still opens and shows the cause.</summary>
    private void BuildFallbackUI(Exception e)
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        var label = new Label("Chroma failed to build its UI:\n\n" + e.Message
            + "\n\nSee the Console for the full stack trace.");
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.paddingLeft = 12;
        label.style.paddingRight = 12;
        label.style.paddingTop = 12;
        root.Add(label);
    }

    /// <summary>
    /// Builds the modern UI Toolkit chrome (header, animated tab bar) and hosts the existing
    /// IMGUI panels inside an IMGUIContainer. UI Toolkit (USS) gives rounded corners, hover
    /// transitions and consistent theming that IMGUI can't.
    /// </summary>
    private void BuildUI()
    {
        if (_config == null) OnEnable();

        VisualElement root = rootVisualElement;
        root.Clear();

        StyleSheet uss = FindStyleSheet();
        if (uss != null) root.styleSheets.Add(uss);

        // --- Header (logo + title + search) ---
        var header = new VisualElement();
        header.AddToClassList("chroma-header");
        _header = header;
        var titleRow = new VisualElement();
        titleRow.AddToClassList("chroma-titlerow");
        var titles = new VisualElement();
        var title = new Label("Chroma");
        title.AddToClassList("chroma-title");
        var sub = new Label("Editor hierarchy & folder colors");
        sub.AddToClassList("chroma-sub");
        titles.Add(title);
        titles.Add(sub);
        titleRow.Add(titles);
        header.Add(titleRow);

        var search = new ToolbarSearchField();
        search.AddToClassList("chroma-search");
        search.value = _search ?? string.Empty;
        search.RegisterValueChangedCallback(e =>
        {
            _search = e.newValue;
            _selExtras?.MarkDirtyRepaint();
            RepaintSettings();
        });
        header.Add(search);
        root.Add(header);

        // Animated rainbow divider (full-bleed) — Chroma's signature, right under the header.
        _rainbow = new IMGUIContainer(PaintRainbow);
        _rainbow.AddToClassList("chroma-rainbow");
        _rainbow.pickingMode = PickingMode.Ignore;
        root.Add(_rainbow);

        // --- Tab bar ---
        var tabbar = new VisualElement();
        tabbar.AddToClassList("chroma-tabbar");
        _tabButtons = new[]
        {
            MakeTab("Selection", Tab.Selection),
            MakeTab("Settings", Tab.Settings),
            MakeTab("Lint", Tab.Lint)
        };
        tabbar.Add(_tabButtons[0]);
        tabbar.Add(_tabButtons[1]);
        tabbar.Add(_tabButtons[2]);
        root.Add(tabbar);
        RefreshTabClasses();

        // --- Content: Selection (native UIElements) + Settings / Lint (IMGUI), toggled by tab ---
        _settingsBodies = new List<IMGUIContainer>(); // shared by the Settings and Lint builders
        _accentCards.Clear();                         // rebuilt as foldouts are created

        _selectionRoot = BuildSelectionUI();
        _settingsRoot = BuildSettingsUI();
        _lintRoot = BuildLintUI();

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1f;
        scroll.Add(_selectionRoot);
        scroll.Add(_settingsRoot);
        scroll.Add(_lintRoot);
        root.Add(scroll);

        UpdateLintTabLabel();
        ShowTab(_tab);
    }

    /// <summary>Show one tab and hide the others; refresh the native Selection editor when shown.</summary>
    private void ShowTab(Tab tab)
    {
        _tab = tab;
        if (_selectionRoot != null)
            _selectionRoot.style.display = tab == Tab.Selection ? DisplayStyle.Flex : DisplayStyle.None;
        if (_settingsRoot != null)
            _settingsRoot.style.display = tab == Tab.Settings ? DisplayStyle.Flex : DisplayStyle.None;
        if (_lintRoot != null)
            _lintRoot.style.display = tab == Tab.Lint ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshTabClasses();

        if (tab == Tab.Selection)
        {
            SyncSelection();
            RefreshSelectionUI();
        }
        else
        {
            RepaintSettings();
        }
    }

    /// <summary>
    /// Build the Settings tab as native UI Toolkit <see cref="Foldout"/> sections, each hosting its
    /// existing IMGUI body. Config sections are wrapped in a change-check that applies + bumps the
    /// config version; Themes / Folder colors / Footer manage their own persistence.
    /// </summary>
    private VisualElement BuildSettingsUI()
    {
        var rootEl = new VisualElement();
        rootEl.AddToClassList("chroma-content");

        // Themes and Folder colors manage their own undo / version bumps (no shared change-check).
        rootEl.Add(MakeFoldout("Themes", "themes", () => DrawPlainSection(DrawThemes)));
        rootEl.Add(MakeFoldout("Folder colors", "folders", () => DrawPlainSection(DrawFolderColors)));

        rootEl.Add(MakeFoldout("Display", "display", () => DrawConfigSection(DrawToggles)));
        rootEl.Add(MakeFoldout("Scene View", "sceneview", () => DrawConfigSection(DrawSceneViewSection)));
        rootEl.Add(MakeFoldout("Font", "font", () => DrawConfigSection(DrawFontSection)));
        rootEl.Add(MakeFoldout("Tree lines", "treelines", () => DrawConfigSection(DrawTreeLinesSection)));
        rootEl.Add(MakeFoldout("Separators", "separators", () => DrawConfigSection(DrawSeparatorsSection)));
        rootEl.Add(MakeFoldout("Child inheritance", "inherit", () => DrawConfigSection(DrawInherit)));
        rootEl.Add(MakeFoldout("Auto-color rules", "autocolor", () => DrawConfigSection(DrawAutoColorRules)));
        rootEl.Add(MakeFoldout("RGB mode", "rgb", () => DrawConfigSection(DrawRgbSection)));
        rootEl.Add(MakeFoldout("Build", "build", () => DrawConfigSection(DrawBuildSection)));
        rootEl.Add(MakeFoldout("Banner presets", "presets", () => DrawConfigSection(DrawPresets)));

        var footer = new IMGUIContainer(() => DrawPlainSection(DrawFooter));
        footer.AddToClassList("chroma-fold-body");
        _settingsBodies.Add(footer);
        rootEl.Add(footer);

        return rootEl;
    }

    /// <summary>Create a native foldout (persisted open state) wrapping an IMGUI section body.</summary>
    private Foldout MakeFoldout(string title, string key, Action imguiBody)
    {
        var fold = new Foldout { text = title };
        fold.AddToClassList("chroma-fold");

        // Spectrum-distributed left accent stripe; AnimateChrome drifts it in sync with the header.
        int accentIndex = _accentCards.Count;
        fold.style.borderLeftWidth = 3f;
        fold.style.borderLeftColor = Color.HSVToRGB(Mathf.Repeat(accentIndex * 0.055f, 1f), 0.7f, 0.92f);
        _accentCards.Add(fold);

        string prefKey = "Chroma.Fold." + key;
        fold.value = EditorPrefs.GetBool(prefKey, true);
        fold.RegisterValueChangedCallback(e => EditorPrefs.SetBool(prefKey, e.newValue));

        var body = new IMGUIContainer(imguiBody);
        body.AddToClassList("chroma-fold-body");
        fold.Add(body);
        _settingsBodies.Add(body);
        return fold;
    }

    /// <summary>Run a config-editing IMGUI body inside a change-check that applies + bumps the version.</summary>
    private void DrawConfigSection(Action body)
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();

        EditorGUI.BeginChangeCheck();
        body();
        if (EditorGUI.EndChangeCheck())
        {
            _so.ApplyModifiedProperties();
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            ChromaHeaders.OnConfigChanged(_config);
            RepaintSettings(); // keep the other sections in sync (single-container behaviour)
        }
    }

    /// <summary>Run a self-managing IMGUI body (its own undo / persistence): just ensure styles + sync.</summary>
    private void DrawPlainSection(Action body)
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();
        EditorGUI.BeginChangeCheck();
        body();
        if (EditorGUI.EndChangeCheck()) RepaintSettings(); // a theme / folder change can touch other sections
    }

    /// <summary>Repaint all Settings section bodies (used on tab show and search changes).</summary>
    private void RepaintSettings()
    {
        if (_settingsBodies == null) return;
        foreach (IMGUIContainer body in _settingsBodies) body.MarkDirtyRepaint();
    }

    #endregion


    #region Main API

    /// <summary>Opens the Chroma editor window. Accessible from Tools > Chroma menu.</summary>
    [MenuItem("Tools/Chroma")]
    private static void Open()
    {
        try
        {
            var win = GetWindow<ChromaWindow>("Chroma");
            win.minSize = new Vector2(360f, 480f);
            win.Show();
            win.Focus();
        }
        catch (Exception e)
        {
            Debug.LogError("[Chroma] Failed to open the window:\n" + e);
        }
    }

    #endregion


    #region Chrome (header, tabs, sections)

    /// <summary>Build cached GUIStyles and color constants on first layout pass.</summary>
    private void EnsureStyles()
    {
        if (_stylesBuilt) return;
        bool pro = EditorGUIUtility.isProSkin;

        _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        _sectionStyle.normal.textColor = pro ? new Color(0.90f, 0.92f, 0.95f) : new Color(0.10f, 0.12f, 0.18f);
        _sectionStyle.padding = new RectOffset(6, 6, 4, 4);

        _cardBody = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 2, 2) };
        _bookmarkRowStyle = new GUIStyle(EditorStyles.label);
        _previewStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        _accent = new Color(0.27f, 0.52f, 1f);
        _accentDim = new Color(0.45f, 0.30f, 0.85f);
        _sectionHeaderBg = pro ? new Color(0.22f, 0.25f, 0.29f) : new Color(0.76f, 0.78f, 0.84f);
        _sectionHeaderHover = pro ? new Color(0.28f, 0.31f, 0.36f) : new Color(0.82f, 0.84f, 0.90f);
        _previewMaskColor = pro ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.784f, 0.784f, 0.784f);

        _stylesBuilt = true;
    }

    /// <summary>Build one segmented tab button wired to switch tabs and repaint the content.</summary>
    private Button MakeTab(string label, Tab tab)
    {
        var b = new Button(() =>
        {
            EditorPrefs.SetInt("Chroma.Tab", (int)tab);
            ShowTab(tab);
        })
        { text = label };
        b.AddToClassList("chroma-tab");
        return b;
    }

    /// <summary>Toggle the active styling on the tab buttons to match the current tab.</summary>
    private void RefreshTabClasses()
    {
        if (_tabButtons == null) return;
        _tabButtons[0].EnableInClassList("chroma-tab--active", _tab == Tab.Selection);
        _tabButtons[1].EnableInClassList("chroma-tab--active", _tab == Tab.Settings);
        if (_tabButtons.Length > 2)
            _tabButtons[2].EnableInClassList("chroma-tab--active", _tab == Tab.Lint);
    }

    /// <summary>Locate the window's USS stylesheet by name, wherever the Chroma folder lives.</summary>
    private static StyleSheet FindStyleSheet()
    {
        string[] guids = AssetDatabase.FindAssets("ChromaWindow t:StyleSheet");
        if (guids == null || guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
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

    /// <summary>Build the native (UI Toolkit) Selection editor: live preview, banner fields and apply actions.</summary>
    private VisualElement BuildSelectionUI()
    {
        var rootEl = new VisualElement();
        rootEl.AddToClassList("chroma-content");

        var card = new VisualElement();
        card.AddToClassList("chroma-card");

        var hint = new Label("Edit colors & styles for selected object(s)");
        hint.AddToClassList("chroma-hint");
        card.Add(hint);

        _uiStatus = new Label();
        _uiStatus.AddToClassList("chroma-hint");
        card.Add(_uiStatus);

        _uiPreview = new IMGUIContainer(() => { EnsureStyles(); DrawPreview(); });
        _uiPreview.AddToClassList("chroma-preview");
        card.Add(_uiPreview);

        _uiTitle = new TextField("Title") { tooltip = "Empty = keep each object's name" };
        _uiTitle.RegisterValueChangedCallback(e => { if (_refreshing) return; _dTitle = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiTitle);

        _uiBackground = new Toggle("Background");
        _uiBackground.RegisterValueChangedCallback(e => { if (_refreshing) return; _dBackground = e.newValue; UpdateEnabledStates(); _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiBackground);

        var bgGroup = new VisualElement();
        bgGroup.AddToClassList("chroma-indent");

        _uiColor = new ColorField("Color");
        _uiColor.RegisterValueChangedCallback(e => { if (_refreshing) return; _dColor = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        bgGroup.Add(_uiColor);

        _uiGradient = new Toggle("Gradient");
        _uiGradient.RegisterValueChangedCallback(e => { if (_refreshing) return; _dGradient = e.newValue; UpdateEnabledStates(); _uiPreview.MarkDirtyRepaint(); });
        bgGroup.Add(_uiGradient);

        var gradGroup = new VisualElement();
        gradGroup.AddToClassList("chroma-indent");

        _uiColor2 = new ColorField("Color 2");
        _uiColor2.RegisterValueChangedCallback(e => { if (_refreshing) return; _dColor2 = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        gradGroup.Add(_uiColor2);

        _uiVertical = new Toggle("Vertical");
        _uiVertical.RegisterValueChangedCallback(e => { if (_refreshing) return; _dVertical = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        gradGroup.Add(_uiVertical);

        bgGroup.Add(gradGroup);
        card.Add(bgGroup);

        _uiTextColor = new ColorField("Text color");
        _uiTextColor.RegisterValueChangedCallback(e => { if (_refreshing) return; _dTextColor = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiTextColor);

        _uiAlign = new DropdownField("Alignment", new List<string>(AlignLabels), 0);
        _uiAlign.RegisterValueChangedCallback(_ => { if (_refreshing) return; _dAlign = _uiAlign.index; _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiAlign);

        _uiStyle = new DropdownField("Style", new List<string>(StyleLabels), 0);
        _uiStyle.RegisterValueChangedCallback(_ => { if (_refreshing) return; _dStyle = _uiStyle.index; _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiStyle);

        _uiSize = new IntegerField("Size (0 = default)");
        _uiSize.RegisterValueChangedCallback(e => { if (_refreshing) return; _dSize = e.newValue; _uiPreview.MarkDirtyRepaint(); });
        card.Add(_uiSize);

        // "Store as" segmented control (Name / Component).
        var storeRow = new VisualElement();
        storeRow.AddToClassList("chroma-storerow");
        var storeLabel = new Label("Store as");
        storeLabel.AddToClassList("chroma-storelabel");
        _uiStoreName = new Button(() => { _outputMode = OutputMode.Name; RefreshStoreAs(); }) { text = "Name" };
        _uiStoreName.AddToClassList("chroma-seg");
        _uiStoreComponent = new Button(() => { _outputMode = OutputMode.Component; RefreshStoreAs(); }) { text = "Component" };
        _uiStoreComponent.AddToClassList("chroma-seg");
        storeRow.Add(storeLabel);
        storeRow.Add(_uiStoreName);
        storeRow.Add(_uiStoreComponent);
        card.Add(storeRow);

        _uiStoreHint = new Label();
        _uiStoreHint.AddToClassList("chroma-hint");
        card.Add(_uiStoreHint);

        _uiApplyPrimary = new Button(() =>
        {
            int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
            if (count == 0) return;
            if (IsNameEditing(count)) ApplyTitleOnly();
            else ApplyDraft();
            RefreshDraft();
            RefreshSelectionUI();
        })
        { text = "Apply banner" };
        _uiApplyPrimary.AddToClassList("chroma-apply");
        card.Add(_uiApplyPrimary);

        _uiApplySecondary = new Button(() =>
        {
            ApplyDraft();
            RefreshDraft();
            RefreshSelectionUI();
        })
        { text = "Apply changes (colors too)" };
        card.Add(_uiApplySecondary);

        rootEl.Add(card);

        // Auxiliary sections kept as IMGUI for now (recolor, presets, bookmarks).
        _selExtras = new IMGUIContainer(DrawSelectionExtrasIMGUI);
        rootEl.Add(_selExtras);

        return rootEl;
    }

    /// <summary>IMGUI body for the Selection tab extras: quick recolor, presets and bookmarks.</summary>
    private void DrawSelectionExtrasIMGUI()
    {
        if (_config == null) OnEnable();
        EnsureStyles();
        _so.Update();
        int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;

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

    /// <summary>Push the current draft + selection state into the native Selection controls.</summary>
    private void RefreshSelectionUI()
    {
        if (_uiTitle == null) return; // UI not built yet
        _refreshing = true;
        try
        {
            int count = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
            _uiStatus.text = SelectionStatus(count);

            _uiTitle.value = _dTitle ?? string.Empty;
            _uiBackground.value = _dBackground;
            _uiColor.value = _dColor;
            _uiGradient.value = _dGradient;
            _uiColor2.value = _dColor2;
            _uiVertical.value = _dVertical;
            _uiTextColor.value = _dTextColor;
            _uiAlign.index = Mathf.Clamp(_dAlign, 0, AlignLabels.Length - 1);
            _uiStyle.index = Mathf.Clamp(_dStyle, 0, StyleLabels.Length - 1);
            _uiSize.value = _dSize;

            RefreshStoreAs();
            UpdateEnabledStates();
            UpdateApplyButtons(count);
            _uiPreview.MarkDirtyRepaint();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Build the status line describing the current selection / edit mode.</summary>
    private string SelectionStatus(int count)
    {
        if (count == 0) return "⚪ Nothing selected";
        if (count > 1) return "📦 " + count + " objects selected — new banner";
        if (_selSource == SelSource.Component) return "🔧 Editing " + _selObject.name + " (component)";
        if (_selSource == SelSource.NameBanner) return "✏️ Editing " + _selObject.name + " (name)";
        return "➕ New banner for " + _selObject.name;
    }

    /// <summary>Enable/disable the color sub-fields based on the Background / Gradient toggles.</summary>
    private void UpdateEnabledStates()
    {
        _uiColor.SetEnabled(_dBackground);
        _uiGradient.SetEnabled(_dBackground);
        _uiColor2.SetEnabled(_dBackground && _dGradient);
        _uiVertical.SetEnabled(_dBackground && _dGradient);
    }

    /// <summary>Highlight the active "Store as" segment and update its hint text.</summary>
    private void RefreshStoreAs()
    {
        _uiStoreName.EnableInClassList("chroma-seg--active", _outputMode == OutputMode.Name);
        _uiStoreComponent.EnableInClassList("chroma-seg--active", _outputMode == OutputMode.Component);
        _uiStoreHint.text = _outputMode == OutputMode.Component
            ? "Adds a component — the object keeps its name."
            : "Encodes the style in the object's name.";
    }

    /// <summary>Update the apply buttons' labels, visibility and enabled state for the current selection.</summary>
    private void UpdateApplyButtons(int count)
    {
        _uiApplyPrimary.SetEnabled(count > 0);
        if (IsNameEditing(count))
        {
            _uiApplyPrimary.text = "Apply title only (" + count + ")";
            _uiApplySecondary.style.display = DisplayStyle.Flex;
        }
        else
        {
            bool editing = count == 1 && _selSource != SelSource.None;
            _uiApplyPrimary.text = (editing ? "Apply changes" : "Apply banner") + " (" + count + ")";
            _uiApplySecondary.style.display = DisplayStyle.None;
        }
    }

    /// <summary>True when editing a single object that already carries a name-encoded banner.</summary>
    private bool IsNameEditing(int count) => count == 1 && _selSource == SelSource.NameBanner;

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
        _previewStyle.font = ChromaHeaders.ResolveBannerFont(_config);

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

    /// <summary>Draw the Display section: banner enable, child count, zebra striping toggles.</summary>
    private void DrawToggles()
    {
        EditorGUILayout.LabelField("Show/hide various visual elements", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableHeaders"),
            new GUIContent("Section banners", "Show colored banners when names contain #color codes"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_showChildCount"),
            new GUIContent("Child count (N)", "Display number of children next to each object"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_warnMissingScripts"),
            new GUIContent("Missing-script warning", "Show a warning icon on rows whose GameObject has a missing (deleted) script"));

        EditorGUILayout.PropertyField(_so.FindProperty("m_showActiveToggle"),
            new GUIContent("Active toggle", "Always-visible activation checkbox at the right edge (click toggles SetActive, with Undo)"));
        SerializedProperty compIcons = _so.FindProperty("m_showComponentIcons");
        EditorGUILayout.PropertyField(compIcons,
            new GUIContent("Component icons", "Show the icons of each GameObject's components on the row"));
        using (new EditorGUI.DisabledScope(!compIcons.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_maxComponentIcons"),
                new GUIContent("Max icons", "Maximum number of component icons per row"));

        SerializedProperty zebra = _so.FindProperty("m_zebra");
        EditorGUILayout.PropertyField(zebra, new GUIContent("Zebra striping", "Alternate row backgrounds"));
        using (new EditorGUI.DisabledScope(!zebra.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_zebraColor"), new GUIContent("Stripe color"));

        SerializedProperty selAccent = _so.FindProperty("m_selectionAccent");
        EditorGUILayout.PropertyField(selAccent,
            new GUIContent("Selection accent", "Tint the selected row with the theme accent (stays visible on banner rows). Themes reseed the color"));
        using (new EditorGUI.DisabledScope(!selAccent.boolValue))
            EditorGUILayout.PropertyField(_so.FindProperty("m_selectionAccentColor"), new GUIContent("Accent color"));
    }

    /// <summary>Draw the Scene View section: floating colored labels and wireframe markers in the viewport.</summary>
    private void DrawSceneViewSection()
    {
        EditorGUILayout.LabelField("Extend banner colors into the Scene View (editor-only, opt-in).", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);
        EditorGUILayout.PropertyField(_so.FindProperty("m_sceneLabels"),
            new GUIContent("Floating labels", "Draw a colored name label above each banner-colored object in the Scene View"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_sceneGizmos"),
            new GUIContent("Wireframe markers", "Draw a colored wireframe box around each banner-colored object in the Scene View"));
        EditorGUILayout.LabelField("Only objects with a Chroma banner (name or component) are marked.", EditorStyles.miniLabel);
    }

    /// <summary>Draw the Font section: pick a Font asset or an installed system font for banner / separator text.</summary>
    private void DrawFontSection()
    {
        EditorGUILayout.LabelField("Font for Chroma banner & separator text.", EditorStyles.miniLabel);
        EditorGUILayout.Space(3);

        SerializedProperty fontProp = _so.FindProperty("m_bannerFont");
        SerializedProperty nameProp = _so.FindProperty("m_bannerFontName");

        EditorGUILayout.PropertyField(fontProp,
            new GUIContent("Custom font asset", "A Font asset (.ttf/.otf imported). Overrides the system font below when set."));

        // Quick category picks: select the first installed font of each kind (and clear the asset).
        EditorGUILayout.LabelField("Quick pick", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Sans", "Sans-serif (Segoe UI / Arial)"))) SetQuickFont(fontProp, nameProp, _sansFonts);
        if (GUILayout.Button(new GUIContent("Serif", "Serif (Georgia / Times New Roman)"))) SetQuickFont(fontProp, nameProp, _serifFonts);
        if (GUILayout.Button(new GUIContent("Mono", "Monospace (Consolas / Courier New)"))) SetQuickFont(fontProp, nameProp, _monoFonts);
        if (GUILayout.Button(new GUIContent("Comic", "Comic Sans MS"))) SetQuickFont(fontProp, nameProp, _comicFonts);
        if (GUILayout.Button(new GUIContent("Default", "Editor default font"))) { fontProp.objectReferenceValue = null; nameProp.stringValue = ""; }
        EditorGUILayout.EndHorizontal();

        bool hasAsset = fontProp.objectReferenceValue != null;
        using (new EditorGUI.DisabledScope(hasAsset))
        {
            EnsureOSFonts();
            int cur = Mathf.Max(0, System.Array.IndexOf(_osFontNames, nameProp.stringValue));
            int next = EditorGUILayout.Popup("System font", cur, _osFontDisplay);
            if (next != cur)
                nameProp.stringValue = next == 0 ? "" : _osFontNames[next];
        }

        EditorGUILayout.LabelField(
            hasAsset
                ? "   Using the custom font asset (system font ignored)."
                : "   '(default)' keeps the editor font.",
            EditorStyles.miniLabel);
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
            EditorGUILayout.PropertyField(_so.FindProperty("m_rgbTheme"), new GUIContent("Theme", "Rainbow theme: Classic, Halloween, Christmas, or Valentine"));
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

        using (new EditorGUI.IndentLevelScope())
        {
            bool inherit = EditorGUILayout.Toggle("Child folders inherit parent color", _config.m_folderColorInheritance);
            if (inherit != _config.m_folderColorInheritance)
            {
                Undo.RecordObject(_config, "Chroma: toggle folder color inheritance");
                _config.m_folderColorInheritance = inherit;
                _config.m_version++;
                EditorUtility.SetDirty(_config);
                ChromaFolders.Invalidate();
                EditorApplication.RepaintProjectWindow();
            }
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

        // Check project window selections (folders and assets)
        string[] selectedGuids = Selection.assetGUIDs;
        foreach (string guid in selectedGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                guids.Add(guid);
        }

        // Also check scene selections for compatibility
        UnityEngine.Object[] objs = Selection.objects;
        if (objs != null)
        {
            foreach (UnityEngine.Object o in objs)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (!guids.Contains(guid))
                        guids.Add(guid);
                }
            }
        }
        return guids;
    }

    /// <summary>Draw the Themes section: one clickable row per scheme, each with a live palette preview.</summary>
    private void DrawThemes()
    {
        EditorGUILayout.LabelField("One-click color schemes — the swatches preview each palette.", EditorStyles.miniLabel);
        EditorGUILayout.Space(4);

        DrawThemeRow("Minimal", "minimal", "Neutral dark gray, understated.");
        DrawThemeRow("Vibrant", "vibrant", "Punchy blue / pink / green / orange.");
        DrawThemeRow("Soft", "soft", "Light pastel, easy on the eyes.");
        DrawThemeRow("High-Contrast", "contrast", "Maximum legibility, bold colors.");
        DrawThemeRow("Okabe-Ito", "okabe", "Colorblind-safe (deuteranopia / protanopia) — Okabe & Ito.");
        DrawThemeRow("IBM", "ibm", "Colorblind-safe — IBM Design Library.");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("More palettes", EditorStyles.boldLabel);
        DrawThemeRow("Synthwave", "synthwave", "Sunset-grid 80s glow: hot magenta into deep violet night.");
        DrawThemeRow("Neon Noir", "neonnoir", "Rain-and-blood noir: crimson sign over deep midnight blue.");
        DrawThemeRow("Ocean", "ocean", "Sunlit teal shallows sinking into deep marine blue.");
        DrawThemeRow("Autumn", "autumn", "Rust, amber and faded olive — a crisp harvest morning.");
        DrawThemeRow("Ember", "ember", "Smoldering reds and molten orange, coals in the dark.");
        DrawThemeRow("Solar", "solar", "Warm-based teals and amber tuned from the Solarized canon.");
        DrawThemeRow("Teal Mono", "tealmono", "One teal hue from vivid lagoon to deep pine.");
        DrawThemeRow("Violet Mono", "violetmono", "A violet ladder from electric amethyst to dusk plum.");
        DrawThemeRow("Warm Gray", "warmgray", "A warm taupe grayscale with a quiet earthy undertone.");
        DrawThemeRow("Jewel Box", "jewel", "Sapphire, ruby, emerald and amethyst edged in gold.");

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Okabe-Ito & IBM are colorblind-safe (~8% of men are affected).", EditorStyles.miniLabel);
    }

    /// <summary>One theme row: a name button on the left, a live palette swatch strip on the right.</summary>
    private void DrawThemeRow(string label, string key, string tooltip)
    {
        EditorGUILayout.BeginHorizontal();

        bool apply = GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(24), GUILayout.Width(120));

        Color[] palette = ThemeSwatches(key);
        Rect strip = GUILayoutUtility.GetRect(10f, 24f, GUILayout.ExpandWidth(true));
        // Rounded backing so the swatch strip reads as a single pill.
        EditorGUI.DrawRect(new Rect(strip.x, strip.y + 3f, strip.width, strip.height - 6f), new Color(0f, 0f, 0f, 0.25f));
        const float pad = 2f;
        float w = (strip.width - pad * (palette.Length - 1)) / palette.Length;
        for (int i = 0; i < palette.Length; i++)
        {
            var sw = new Rect(strip.x + i * (w + pad), strip.y + 4f, w, strip.height - 8f);
            EditorGUI.DrawRect(sw, palette[i]);
        }

        EditorGUILayout.EndHorizontal();
        if (apply) ApplyTheme(key);
    }

    /// <summary>The representative swatch colors for a theme key (h1, h2, h3, category), for previewing.</summary>
    private static Color[] ThemeSwatches(string theme)
    {
        string[] hex;
        switch (theme)
        {
            case "minimal":    hex = new[] { "#3a3f44", "#4a4f55", "#2c2f33", "#33363a" }; break;
            case "vibrant":    hex = new[] { "#1f6feb", "#e0457b", "#1ca672", "#f0883e" }; break;
            case "soft":       hex = new[] { "#6ea8fe", "#f4a6c0", "#8fd3b6", "#f6c08a" }; break;
            case "contrast":   hex = new[] { "#0a84ff", "#ff375f", "#30d158", "#ff9f0a" }; break;
            case "okabe":      hex = new[] { "#0072B2", "#E69F00", "#009E73", "#56B4E9" }; break;
            case "ibm":        hex = new[] { "#648FFF", "#DC267F", "#785EF0", "#FE6100" }; break;
            case "synthwave":  hex = new[] { "#d6216b", "#7b2ff7", "#3a2b66", "#1b1233" }; break;
            case "neonnoir":   hex = new[] { "#b3194d", "#2563b8", "#241a30", "#100a16" }; break;
            case "ocean":      hex = new[] { "#0e7c8a", "#1565a8", "#2a4a6e", "#16303f" }; break;
            case "autumn":     hex = new[] { "#a84d1c", "#9c6b1a", "#6e6320", "#3a2a18" }; break;
            case "ember":      hex = new[] { "#cf2f2f", "#9e2418", "#6e1f12", "#3d140c" }; break;
            case "solar":      hex = new[] { "#1f7fc4", "#a85a1a", "#586e75", "#073642" }; break;
            case "tealmono":   hex = new[] { "#178079", "#1a7470", "#155553", "#143e3d" }; break;
            case "violetmono": hex = new[] { "#7a4fd0", "#634099", "#4a3270", "#352450" }; break;
            case "warmgray":   hex = new[] { "#6e6258", "#5a5048", "#453d37", "#332d29" }; break;
            case "jewel":      hex = new[] { "#1d4ed8", "#b91c4b", "#0f766e", "#6d28d9" }; break;
            default:           hex = new[] { "#888888" }; break;
        }
        var cols = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++) ChromaHeaders.TryGetColor(hex[i], out cols[i]);
        return cols;
    }

    /// <summary>Parse a hex color (e.g. "#1f6feb"); returns gray if it fails to parse.</summary>
    private static Color Hex(string hex)
    {
        return ChromaHeaders.TryGetColor(hex, out Color c) ? c : new Color(0.5f, 0.5f, 0.5f);
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
            case "okabe":
                // Okabe & Ito palette: distinguishable under deuteranopia / protanopia / tritanopia.
                _config.m_treeLineColor = new Color(1f, 1f, 1f, 0.18f);
                _config.m_separatorColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                _config.m_separatorFillColor = new Color(0.20f, 0.20f, 0.20f, 1f);
                _config.m_separatorStyle = SeparatorStyle.Solid;
                _config.m_presets = ThemePresets("#0072B2", "#E69F00", "#009E73", "#56B4E9", "#0072B2>#D55E00");
                break;
            case "ibm":
                // IBM Design Library colorblind-safe palette.
                _config.m_treeLineColor = new Color(1f, 1f, 1f, 0.18f);
                _config.m_separatorColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                _config.m_separatorFillColor = new Color(0.18f, 0.18f, 0.22f, 1f);
                _config.m_separatorStyle = SeparatorStyle.Solid;
                _config.m_presets = ThemePresets("#648FFF", "#DC267F", "#785EF0", "#FE6100", "#648FFF>#FFB000");
                break;

            // ---- Extended palettes (white-text-legible banner backgrounds) ----
            case "synthwave":
                _config.m_treeLineColor = new Color(1f, 0.27f, 0.6f, 0.22f);
                _config.m_separatorColor = Hex("#ff5fb0");
                _config.m_separatorFillColor = Hex("#1b1233");
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#d6216b", "#7b2ff7", "#3a2b66", "#1b1233", "#e02478>#5a1e96");
                break;
            case "neonnoir":
                _config.m_treeLineColor = new Color(0.85f, 0.18f, 0.42f, 0.16f);
                _config.m_separatorColor = Hex("#ff3d7f");
                _config.m_separatorFillColor = Hex("#100a16");
                _config.m_separatorStyle = SeparatorStyle.Solid;
                _config.m_presets = ThemePresets("#b3194d", "#2563b8", "#241a30", "#100a16", "#cc1259>#1c3a8f");
                break;
            case "ocean":
                _config.m_treeLineColor = new Color(0.32f, 0.58f, 0.64f, 0.24f);
                _config.m_separatorColor = Hex("#54c9d6");
                _config.m_separatorFillColor = Hex("#0e1f2a");
                _config.m_separatorStyle = SeparatorStyle.Dotted;
                _config.m_presets = ThemePresets("#0e7c8a", "#1565a8", "#2a4a6e", "#16303f", "#0e7c8a>#1d3a78");
                break;
            case "autumn":
                _config.m_treeLineColor = new Color(0.7f, 0.45f, 0.2f, 0.22f);
                _config.m_separatorColor = Hex("#e0913f");
                _config.m_separatorFillColor = Hex("#2a1d12");
                _config.m_separatorStyle = SeparatorStyle.Dashed;
                _config.m_presets = ThemePresets("#a84d1c", "#9c6b1a", "#6e6320", "#3a2a18", "#a84d1c>#5c5a1e");
                break;
            case "ember":
                _config.m_treeLineColor = new Color(0.85f, 0.35f, 0.2f, 0.22f);
                _config.m_separatorColor = Hex("#ff6a3c");
                _config.m_separatorFillColor = Hex("#2a0d08");
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#cf2f2f", "#9e2418", "#6e1f12", "#3d140c", "#d63e1c>#7a160d");
                break;
            case "solar":
                _config.m_treeLineColor = new Color(0.51f, 0.58f, 0.59f, 0.22f);
                _config.m_separatorColor = Hex("#2aa198");
                _config.m_separatorFillColor = Hex("#002b36");
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#1f7fc4", "#a85a1a", "#586e75", "#073642", "#1f7fc4>#1a5a78");
                break;
            case "tealmono":
                _config.m_treeLineColor = new Color(0.36f, 0.75f, 0.7f, 0.18f);
                _config.m_separatorColor = Hex("#74c9bf");
                _config.m_separatorFillColor = Hex("#102826");
                _config.m_separatorStyle = SeparatorStyle.Dashed;
                _config.m_presets = ThemePresets("#178079", "#1a7470", "#155553", "#143e3d", "#178079>#123f3e");
                break;
            case "violetmono":
                _config.m_treeLineColor = new Color(0.62f, 0.5f, 0.86f, 0.2f);
                _config.m_separatorColor = Hex("#b79ae8");
                _config.m_separatorFillColor = Hex("#211633");
                _config.m_separatorStyle = SeparatorStyle.Dotted;
                _config.m_presets = ThemePresets("#7a4fd0", "#634099", "#4a3270", "#352450", "#8a5be0>#352450");
                break;
            case "warmgray":
                _config.m_treeLineColor = new Color(0.78f, 0.72f, 0.64f, 0.16f);
                _config.m_separatorColor = Hex("#c2b6a8");
                _config.m_separatorFillColor = Hex("#241f1c");
                _config.m_separatorStyle = SeparatorStyle.Solid;
                _config.m_presets = ThemePresets("#6e6258", "#5a5048", "#453d37", "#332d29", "#776a5e>#332d29");
                break;
            case "jewel":
                _config.m_treeLineColor = new Color(0.55f, 0.4f, 0.9f, 0.22f);
                _config.m_separatorColor = Hex("#c4a747");
                _config.m_separatorFillColor = Hex("#1a1424");
                _config.m_separatorStyle = SeparatorStyle.Double;
                _config.m_presets = ThemePresets("#1d4ed8", "#b91c4b", "#0f766e", "#6d28d9", "#1d4ed8>#9d174d");
                break;
        }

        // Theme completeness: reseed palette-derived colors from the theme's primary (h1) color,
        // so zebra striping and the selection accent stay on-palette across all themes.
        if (_config.m_presets != null && _config.m_presets.Count > 0 && _config.m_presets[0] != null)
        {
            string h1Token = (_config.m_presets[0].m_spec ?? "").Split(' ')[0];
            if (ChromaHeaders.TryGetColor(h1Token, out Color accent))
            {
                _config.m_zebraColor = new Color(accent.r, accent.g, accent.b, 0.05f);
                _config.m_selectionAccentColor = new Color(accent.r, accent.g, accent.b, 0.18f);
            }
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

        // Security: Validate path is a safe location (Assets, Library, ProjectSettings, or temp)
        string fullPath = System.IO.Path.GetFullPath(path);
        string projectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        if (!fullPath.StartsWith(projectPath, System.StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Chroma", "Import rejected: File must be inside the project folder.", "OK");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Undo.RecordObject(_config, "Import Chroma config");
            JsonUtility.FromJsonOverwrite(json, _config);
            _config.ValidateAndClamp(); // Sanitize imported values to safe ranges
            _config.m_version++;
            EditorUtility.SetDirty(_config);
            _so.Update();
            ChromaHeaders.OnConfigChanged(_config);
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("Chroma", "Import failed:\n" + ex.Message, "OK"); }
    }

    #endregion


    #region Lint tab

    /// <summary>Build the Lint tab: violations list, team rules editor, and linter options.</summary>
    private VisualElement BuildLintUI()
    {
        var rootEl = new VisualElement();
        rootEl.AddToClassList("chroma-content");

        rootEl.Add(MakeFoldout("Violations", "lintviolations", () => DrawPlainSection(DrawLintViolations)));
        rootEl.Add(MakeFoldout("Rules", "lintrules", () => DrawConfigSection(DrawLintRules)));
        rootEl.Add(MakeFoldout("Options", "lintoptions", () => DrawConfigSection(DrawLintOptions)));

        return rootEl;
    }

    /// <summary>Draw the live violation list, grouped by rule, with jump / select / ignore actions.</summary>
    private void DrawLintViolations()
    {
        int errors = ChromaLinter.ErrorCount, warnings = ChromaLinter.WarningCount, infos = ChromaLinter.InfoCount;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            errors + " error(s)  ·  " + warnings + " warning(s)  ·  " + infos + " info", EditorStyles.boldLabel);
        if (GUILayout.Button("Rescan", GUILayout.Width(70)))
            ChromaLinter.ScanNow();
        EditorGUILayout.EndHorizontal();

        if (!_config.m_enableLint)
        {
            EditorGUILayout.HelpBox("The linter is disabled (see Options below).", MessageType.Info);
            return;
        }
        if (_config.m_lintRules == null || _config.m_lintRules.Count == 0)
        {
            EditorGUILayout.HelpBox("No rules yet. Add one below or load a ruleset.", MessageType.Info);
            return;
        }
        if (ChromaLinter.Truncated)
            EditorGUILayout.HelpBox("Scan stopped after 500 flagged objects — fix some and rescan.", MessageType.Warning);

        if (ChromaLinter.Total == 0)
        {
            EditorGUILayout.LabelField("No violations — the scene matches the team conventions. ✔", EditorStyles.miniLabel);
            return;
        }

        // Group instanceIDs by rule id (an object can appear under several rules).
        _lintGroups.Clear();
        foreach (var kv in ChromaLinter.All)
        {
            List<ChromaLinter.Violation> list = kv.Value;
            for (int i = 0; i < list.Count; i++)
            {
                if (!_lintGroups.TryGetValue(list[i].m_ruleId, out List<int> ids))
                {
                    ids = new List<int>();
                    _lintGroups[list[i].m_ruleId] = ids;
                }
                ids.Add(kv.Key);
            }
        }

        int shown = 0;
        bool capped = false;
        foreach (var group in _lintGroups)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(group.Key + "  (" + group.Value.Count + ")", EditorStyles.boldLabel);
            if (GUILayout.Button("Select all", GUILayout.Width(72)))
            {
                var gos = new List<UnityEngine.Object>(group.Value.Count);
                foreach (int id in group.Value)
                {
#pragma warning disable 618
                    var go = EditorUtility.InstanceIDToObject(id) as GameObject;
#pragma warning restore 618
                    if (go != null) gos.Add(go);
                }
                Selection.objects = gos.ToArray();
            }
            EditorGUILayout.EndHorizontal();

            foreach (int id in group.Value)
            {
                if (shown >= 100) { capped = true; break; }
#pragma warning disable 618
                GameObject go = EditorUtility.InstanceIDToObject(id) as GameObject;
#pragma warning restore 618
                if (go == null) continue;
                shown++;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(go.name, EditorStyles.label, GUILayout.ExpandWidth(true)))
                    ChromaBookmarks.Jump(go);
                if (GUILayout.Button("Ignore", GUILayout.Width(52)))
                    ChromaLinter.ToggleIgnore(go);
                EditorGUILayout.EndHorizontal();
            }
            if (capped) break;
        }

        if (capped)
            EditorGUILayout.LabelField("…list capped at 100 rows.", EditorStyles.miniLabel);

        if (ChromaLinter.IgnoreCount > 0)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ChromaLinter.IgnoreCount + " object(s) ignored locally (per-user).",
                EditorStyles.miniLabel);
            if (GUILayout.Button("Clear ignores", GUILayout.Width(96)))
                ChromaLinter.ClearIgnores();
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>Draw the team rule editor: one card per rule (scope, assertion, severity, message).</summary>
    private void DrawLintRules()
    {
        EditorGUILayout.LabelField("Rules live in the shared config — the whole team gets them via git.",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(3);

        SerializedProperty rules = _so.FindProperty("m_lintRules");

        for (int i = 0; i < rules.arraySize; i++)
        {
            SerializedProperty el = rules.GetArrayElementAtIndex(i);
            SerializedProperty enabled = el.FindPropertyRelative("m_enabled");
            SerializedProperty id = el.FindPropertyRelative("m_id");
            SerializedProperty severity = el.FindPropertyRelative("m_severity");
            SerializedProperty scope = el.FindPropertyRelative("m_scope");
            SerializedProperty scopeValue = el.FindPropertyRelative("m_scopeValue");
            SerializedProperty assert = el.FindPropertyRelative("m_assert");
            SerializedProperty assertValue = el.FindPropertyRelative("m_assertValue");
            SerializedProperty message = el.FindPropertyRelative("m_message");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            enabled.boolValue = EditorGUILayout.Toggle(enabled.boolValue, GUILayout.Width(16));
            id.stringValue = EditorGUILayout.TextField(id.stringValue);
            EditorGUILayout.PropertyField(severity, GUIContent.none, GUILayout.Width(70));
            bool remove = GUILayout.Button("X", GUILayout.Width(22));
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("On", GUILayout.Width(34));
                EditorGUILayout.PropertyField(scope, GUIContent.none, GUILayout.Width(90));
                bool scopeNeedsValue = scope.enumValueIndex != (int)LintScope.All
                                       && scope.enumValueIndex != (int)LintScope.RootOnly;
                using (new EditorGUI.DisabledScope(!scopeNeedsValue))
                    scopeValue.stringValue = EditorGUILayout.TextField(scopeValue.stringValue);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Check", GUILayout.Width(40));
                EditorGUILayout.PropertyField(assert, GUIContent.none, GUILayout.Width(120));
                bool assertNeedsValue = assert.enumValueIndex == (int)LintAssert.NameRegex
                                        || assert.enumValueIndex == (int)LintAssert.RequiredParent
                                        || assert.enumValueIndex == (int)LintAssert.MaxDepth;
                using (new EditorGUI.DisabledScope(!assertNeedsValue))
                    assertValue.stringValue = EditorGUILayout.TextField(assertValue.stringValue);
                EditorGUILayout.EndHorizontal();

                message.stringValue = EditorGUILayout.TextField(message.stringValue);
            }

            EditorGUILayout.EndVertical();

            if (remove)
            {
                rules.DeleteArrayElementAtIndex(i);
                break;
            }
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("+ Add rule"))
        {
            int idx = rules.arraySize;
            rules.arraySize++;
            SerializedProperty el = rules.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("m_enabled").boolValue = true;
            el.FindPropertyRelative("m_id").stringValue = "rule-" + (idx + 1);
            el.FindPropertyRelative("m_severity").enumValueIndex = (int)LintSeverity.Warning;
            el.FindPropertyRelative("m_scope").enumValueIndex = (int)LintScope.All;
            el.FindPropertyRelative("m_scopeValue").stringValue = "";
            el.FindPropertyRelative("m_assert").enumValueIndex = (int)LintAssert.NoDefaultName;
            el.FindPropertyRelative("m_assertValue").stringValue = "";
            el.FindPropertyRelative("m_message").stringValue = "";
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load starter ruleset")) LoadRuleset(ChromaConfig.StarterLintRules());
        if (GUILayout.Button("Load strict ruleset")) LoadRuleset(ChromaConfig.StrictLintRules());
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Loading a ruleset adds the rules your config is missing (matched by id).",
            EditorStyles.miniLabel);
    }

    /// <summary>Append the given rules to the config, skipping ids that already exist.</summary>
    private void LoadRuleset(List<ChromaConfig.LintRule> rules)
    {
        _so.ApplyModifiedProperties(); // flush pending field edits first

        Undo.RecordObject(_config, "Chroma: load lint ruleset");
        if (_config.m_lintRules == null) _config.m_lintRules = new List<ChromaConfig.LintRule>();

        foreach (ChromaConfig.LintRule rule in rules)
        {
            bool exists = false;
            foreach (ChromaConfig.LintRule existing in _config.m_lintRules)
            {
                if (existing != null && string.Equals(existing.m_id, rule.m_id, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists) _config.m_lintRules.Add(rule);
        }

        _config.m_version++;
        EditorUtility.SetDirty(_config);
        _so.Update();
        ChromaHeaders.OnConfigChanged(_config);
        RepaintSettings();
    }

    /// <summary>Draw the linter options: master toggle and Hierarchy icon visibility.</summary>
    private void DrawLintOptions()
    {
        EditorGUILayout.PropertyField(_so.FindProperty("m_enableLint"),
            new GUIContent("Enable linter", "Scan open scenes against the team's lint rules"));
        EditorGUILayout.PropertyField(_so.FindProperty("m_lintShowIcons"),
            new GUIContent("Icons in Hierarchy", "Show a severity icon (with tooltip) on offending rows"));
        EditorGUILayout.LabelField("Scanning pauses during play mode. 'Ignore' opt-outs are per-user.",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Tip: bind 'Chroma/Next Lint Violation' in Edit > Shortcuts.",
            EditorStyles.miniLabel);
    }

    #endregion


    #region Tools and Utilities

    /// <summary>Lazily build the installed-system-font lists (value + display), with "(default)" at index 0.</summary>
    private void EnsureOSFonts()
    {
        if (_osFontNames != null) return;
        string[] installed = Font.GetOSInstalledFontNames() ?? new string[0];
        System.Array.Sort(installed, System.StringComparer.OrdinalIgnoreCase);

        _osFontNames = new string[installed.Length + 1];
        _osFontDisplay = new string[installed.Length + 1];
        _osFontNames[0] = "";
        _osFontDisplay[0] = "(default)";
        for (int i = 0; i < installed.Length; i++)
        {
            _osFontNames[i + 1] = installed[i];
            _osFontDisplay[i + 1] = installed[i];
        }
    }

    /// <summary>Quick-pick a system font: clear any asset and select the first installed candidate.</summary>
    private void SetQuickFont(SerializedProperty fontProp, SerializedProperty nameProp, string[] candidates)
    {
        EnsureOSFonts();
        fontProp.objectReferenceValue = null; // system font only takes effect when no asset is set
        nameProp.stringValue = FirstInstalled(candidates);
    }

    /// <summary>Return the first candidate font that is actually installed, or "" (editor default).</summary>
    private string FirstInstalled(string[] candidates)
    {
        foreach (string c in candidates)
            if (System.Array.IndexOf(_osFontNames, c) > 0) return c; // > 0 skips the "" default slot
        return "";
    }

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

    private static readonly string[] AlignLabels = { "Center", "Left", "Right" };
    private static readonly string[] StyleLabels = { "Bold", "Normal", "Italic", "BoldItalic" };

    // Quick-pick font candidates (first installed one wins). Names match Font.GetOSInstalledFontNames.
    private static readonly string[] _sansFonts = { "Segoe UI", "Arial", "Helvetica", "Verdana", "Tahoma" };
    private static readonly string[] _serifFonts = { "Georgia", "Times New Roman", "Cambria", "Garamond" };
    private static readonly string[] _monoFonts = { "Consolas", "Cascadia Mono", "Courier New", "Lucida Console" };
    private static readonly string[] _comicFonts = { "Comic Sans MS", "Comic Sans" };

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
    private Tab _tab;

    // UI Toolkit chrome.
    private Button[] _tabButtons;
    private VisualElement _header;
    private IMGUIContainer _rainbow;
    private readonly List<VisualElement> _accentCards = new List<VisualElement>();
    private double _lastAnim;
    private double _lastAccent;
    private VisualElement _selectionRoot;
    private VisualElement _settingsRoot;
    private VisualElement _lintRoot;
    private List<IMGUIContainer> _settingsBodies;
    private IMGUIContainer _selExtras;
    private bool _refreshing;

    // Scratch map rebuilt on every Lint-tab repaint (ruleId -> flagged instanceIDs).
    private readonly Dictionary<string, List<int>> _lintGroups = new Dictionary<string, List<int>>();

    // Native Selection-tab controls.
    private Label _uiStatus;
    private Label _uiStoreHint;
    private IMGUIContainer _uiPreview;
    private TextField _uiTitle;
    private Toggle _uiBackground;
    private Toggle _uiGradient;
    private Toggle _uiVertical;
    private ColorField _uiColor;
    private ColorField _uiColor2;
    private ColorField _uiTextColor;
    private DropdownField _uiAlign;
    private DropdownField _uiStyle;
    private IntegerField _uiSize;
    private Button _uiStoreName;
    private Button _uiStoreComponent;
    private Button _uiApplyPrimary;
    private Button _uiApplySecondary;

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

    // Installed system fonts for the Font section (built lazily). [0] = "" / "(default)".
    private string[] _osFontNames;
    private string[] _osFontDisplay;

    private readonly Dictionary<string, AnimBool> _anims = new Dictionary<string, AnimBool>();
    private int _presetIndex;

    private GUIStyle _sectionStyle;
    private GUIStyle _cardBody;
    private GUIStyle _bookmarkRowStyle;
    private GUIStyle _previewStyle;
    private Color _accent;
    private Color _accentDim;
    private Color _sectionHeaderBg;
    private Color _sectionHeaderHover;
    private Color _previewMaskColor;
    private bool _sectionShown;
    private bool _stylesBuilt;

    #endregion
}
}
