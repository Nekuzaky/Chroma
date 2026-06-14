using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Chroma.Editor
{
// Context-menu entries (right-click in Hierarchy) and keyboard shortcuts.
// All entries operate on the current Selection; shortcuts are bindable in Edit > Shortcuts.
public static class ChromaMenu
{
    #region Private and Protected

    private static string _copiedSpec;

    #endregion


    #region Main API

    [MenuItem("GameObject/Chroma/Toggle Bookmark", true)]
    private static bool ValidateHasSelection() => Selection.activeGameObject != null;

    [MenuItem("GameObject/Chroma/Toggle Bookmark", false, 200)]
    private static void MenuToggleBookmark() => ToggleBookmarkOnSelection();

    [MenuItem("GameObject/Chroma/Strip Banner", true)]
    private static bool ValidateStrip() => Selection.activeGameObject != null;

    [MenuItem("GameObject/Chroma/Strip Banner", false, 201)]
    private static void MenuStripBanner() => StripBannerOnSelection();

    [MenuItem("GameObject/Chroma/Copy Banner Style", true)]
    private static bool ValidateCopyStyle()
        => Selection.activeGameObject != null && Selection.activeGameObject.name.IndexOf('=') >= 0;

    [MenuItem("GameObject/Chroma/Copy Banner Style", false, 210)]
    private static void MenuCopyStyle()
    {
        string name = Selection.activeGameObject.name;
        int eq = name.IndexOf('=');
        _copiedSpec = eq >= 0 ? name.Substring(0, eq).Trim() : null;
    }

    [MenuItem("GameObject/Chroma/Paste Banner Style", true)]
    private static bool ValidatePasteStyle()
        => !string.IsNullOrEmpty(_copiedSpec) && Selection.activeGameObject != null;

    [MenuItem("GameObject/Chroma/Paste Banner Style", false, 211)]
    private static void MenuPasteStyle()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null || string.IsNullOrEmpty(_copiedSpec)) return;
        for (int i = 0; i < sel.Length; i++)
        {
            GameObject go = sel[i];
            if (go == null) continue;
            int eq = go.name.IndexOf('=');
            string title = eq >= 0 ? go.name.Substring(eq + 1).Trim() : go.name;
            Undo.RecordObject(go, "Chroma: paste style");
            go.name = _copiedSpec + "=" + title;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    [MenuItem("GameObject/Chroma/Lint - Toggle Ignore", true)]
    private static bool ValidateLintIgnore() => Selection.activeGameObject != null;

    // Per-user opt-out: the linter skips ignored objects (rules stay shared in the config).
    [MenuItem("GameObject/Chroma/Lint - Toggle Ignore", false, 220)]
    private static void MenuLintIgnore()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
            ChromaLinter.ToggleIgnore(sel[i]);
    }

    [MenuItem("GameObject/Chroma/Set Scene Icon (banner color)", true)]
    private static bool ValidateSetIcon() => Selection.activeGameObject != null;

    // Persists Unity's nearest built-in colored label icon on the object (Scene + Hierarchy gizmo).
    // NOTE: this WRITES to the object (m_Icon) — it shows up in version control. Explicit/manual only.
    [MenuItem("GameObject/Chroma/Set Scene Icon (banner color)", false, 221)]
    private static void MenuSetIcon()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        int set = 0;
        for (int i = 0; i < sel.Length; i++)
        {
            GameObject go = sel[i];
            if (go == null || !ChromaHeaders.TryGetRowColor(go, out Color c)) continue;
            Texture2D icon = LabelIcon(NearestLabelIndex(c));
            if (icon == null) continue;
            EditorGUIUtility.SetIconForObject(go, icon);
            EditorUtility.SetDirty(go);
            set++;
        }
        if (set > 0) EditorApplication.RepaintHierarchyWindow();
    }

    [MenuItem("GameObject/Chroma/Clear Scene Icon", true)]
    private static bool ValidateClearIcon() => Selection.activeGameObject != null;

    [MenuItem("GameObject/Chroma/Clear Scene Icon", false, 222)]
    private static void MenuClearIcon()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
        {
            if (sel[i] == null) continue;
            EditorGUIUtility.SetIconForObject(sel[i], null);
            EditorUtility.SetDirty(sel[i]);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    [MenuItem("GameObject/Chroma/Open Window", false, 230)]
    private static void MenuOpenWindow() => OpenWindow();

    // --- Project window: folder colors ---

    [MenuItem("Assets/Chroma/Folder Color/Blue", true)]
    [MenuItem("Assets/Chroma/Folder Color/Green", true)]
    [MenuItem("Assets/Chroma/Folder Color/Red", true)]
    [MenuItem("Assets/Chroma/Folder Color/Orange", true)]
    [MenuItem("Assets/Chroma/Folder Color/Purple", true)]
    [MenuItem("Assets/Chroma/Folder Color/Clear", true)]
    private static bool ValidateFolderColor() => GetSelectedFolderGuids().Count > 0;

    [MenuItem("Assets/Chroma/Folder Color/Blue", false, 1000)]
    private static void FolderBlue() => SetFolderColor(new Color(0.30f, 0.55f, 1f));

    [MenuItem("Assets/Chroma/Folder Color/Green", false, 1001)]
    private static void FolderGreen() => SetFolderColor(new Color(0.35f, 0.80f, 0.40f));

    [MenuItem("Assets/Chroma/Folder Color/Red", false, 1002)]
    private static void FolderRed() => SetFolderColor(new Color(0.90f, 0.35f, 0.35f));

    [MenuItem("Assets/Chroma/Folder Color/Orange", false, 1003)]
    private static void FolderOrange() => SetFolderColor(new Color(0.95f, 0.60f, 0.25f));

    [MenuItem("Assets/Chroma/Folder Color/Purple", false, 1004)]
    private static void FolderPurple() => SetFolderColor(new Color(0.65f, 0.45f, 0.95f));

    [MenuItem("Assets/Chroma/Folder Color/Clear", false, 1015)]
    private static void FolderClear() => SetFolderColor(null);

    [Shortcut("Chroma/Toggle Bookmark on Selection", KeyCode.B, ShortcutModifiers.Action)]
    private static void ShortcutToggleBookmark() => ToggleBookmarkOnSelection();

    // Bindable but unassigned by default — the user picks a key in Edit > Shortcuts.
    [Shortcut("Chroma/Open Window")]
    private static void ShortcutOpenWindow() => OpenWindow();

    // Cycles the selection through lint violations. Unassigned by default.
    [Shortcut("Chroma/Next Lint Violation")]
    private static void ShortcutNextViolation() => ChromaLinter.JumpToNext();

    #endregion


    #region Tools and Utilities

    private static void ToggleBookmarkOnSelection()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
            ChromaBookmarks.Toggle(sel[i]);
    }

    private static void StripBannerOnSelection()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
        {
            GameObject go = sel[i];
            if (go == null) continue;
            if (!ChromaHeaders.TryStripName(go.name, out string cleaned)) continue;
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned == go.name) continue;
            Undo.RecordObject(go, "Chroma: strip banner");
            go.name = cleaned;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void OpenWindow() => EditorWindow.GetWindow<ChromaWindow>("Chroma");

    // Built-in colored label icon ("sv_label_0".."sv_label_7"), or null on older/newer editors.
    private static Texture2D LabelIcon(int index)
    {
        GUIContent c = EditorGUIUtility.IconContent("sv_label_" + Mathf.Clamp(index, 0, 7));
        return c != null ? c.image as Texture2D : null;
    }

    // Nearest of Unity's 8 built-in label colors to a color, matched by hue.
    private static int NearestLabelIndex(Color color)
    {
        Color.RGBToHSV(color, out float h, out float s, out _);
        if (s < 0.12f) return 0; // near-gray: default to the blue label
        float[] targets = { 0.60f, 0.50f, 0.33f, 0.16f, 0.08f, 0.00f, 0.88f, 0.76f };
        int best = 0;
        float bestDist = 2f;
        for (int i = 0; i < targets.Length; i++)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(h * 360f, targets[i] * 360f)) / 360f;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static System.Collections.Generic.List<string> GetSelectedFolderGuids()
    {
        var guids = new System.Collections.Generic.List<string>();
        foreach (Object o in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                guids.Add(AssetDatabase.AssetPathToGUID(path));
        }
        return guids;
    }

    private static void SetFolderColor(Color? color)
    {
        foreach (string guid in GetSelectedFolderGuids())
            ChromaFolders.SetColor(guid, color);
    }

    #endregion
}
}
