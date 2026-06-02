using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Chromarchy.Editor
{
// Context-menu entries (right-click in Hierarchy) and keyboard shortcuts.
// All entries operate on the current Selection; shortcuts are bindable in Edit > Shortcuts.
public static class ChromarchyMenu
{
    #region Main API

    [MenuItem("GameObject/Chromarchy/Toggle Bookmark", true)]
    private static bool ValidateHasSelection() => Selection.activeGameObject != null;

    [MenuItem("GameObject/Chromarchy/Toggle Bookmark", false, 200)]
    private static void MenuToggleBookmark()
    {
        ToggleBookmarkOnSelection();
    }

    [MenuItem("GameObject/Chromarchy/Strip Banner", true)]
    private static bool ValidateStrip() => Selection.activeGameObject != null;

    [MenuItem("GameObject/Chromarchy/Strip Banner", false, 201)]
    private static void MenuStripBanner()
    {
        StripBannerOnSelection();
    }

    [MenuItem("GameObject/Chromarchy/Open Window", false, 220)]
    private static void MenuOpenWindow()
    {
        OpenWindow();
    }

    [Shortcut("Chromarchy/Toggle Bookmark on Selection", KeyCode.B, ShortcutModifiers.Action)]
    private static void ShortcutToggleBookmark()
    {
        ToggleBookmarkOnSelection();
    }

    // Bindable but unassigned by default — the user picks a key in Edit > Shortcuts.
    [Shortcut("Chromarchy/Open Window")]
    private static void ShortcutOpenWindow()
    {
        OpenWindow();
    }

    #endregion


    #region Tools and Utilies

    private static void ToggleBookmarkOnSelection()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
            ChromarchyBookmarks.Toggle(sel[i]);
    }

    private static void StripBannerOnSelection()
    {
        GameObject[] sel = Selection.gameObjects;
        if (sel == null) return;
        for (int i = 0; i < sel.Length; i++)
        {
            GameObject go = sel[i];
            if (go == null) continue;
            if (!ChromarchyHeaders.TryStripName(go.name, out string cleaned)) continue;
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned == go.name) continue;
            Undo.RecordObject(go, "Chromarchy: strip banner");
            go.name = cleaned;
            EditorUtility.SetDirty(go);
        }
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void OpenWindow()
    {
        EditorWindow.GetWindow<ChromarchyWindow>("Chromarchy");
    }

    #endregion
}
}
