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
