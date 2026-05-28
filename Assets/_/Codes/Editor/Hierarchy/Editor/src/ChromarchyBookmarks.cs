using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chromarchy.Editor
{
// Per-user, scene-aware bookmarks for the Hierarchy. Stored in EditorPrefs as GlobalObjectId
// strings (NOT in the shared config asset). A cached instanceID set gives O(1) per-row checks.
[InitializeOnLoad]
public static class ChromarchyBookmarks
{
    #region Publics

    public static IReadOnlyList<string> Gids => _gids;

    #endregion


    #region Unity API

    static ChromarchyBookmarks()
    {
        Load();
        EditorApplication.hierarchyChanged += RebuildIdCache;
    }

    #endregion


    #region Main API

    public static bool IsBookmarked(int instanceID) => _ids.Contains(instanceID);

    public static void Add(GameObject go)
    {
        if (go == null) return;
        string gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
        if (_gids.Contains(gid)) return;
        _gids.Add(gid);
        Save();
        RebuildIdCache();
        EditorApplication.RepaintHierarchyWindow();
    }

    public static void Remove(string gid)
    {
        if (_gids.Remove(gid))
        {
            Save();
            RebuildIdCache();
            EditorApplication.RepaintHierarchyWindow();
        }
    }

    public static GameObject ResolveGid(string gid)
    {
        if (GlobalObjectId.TryParse(gid, out GlobalObjectId id))
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
        return null;
    }

    public static void Jump(GameObject go)
    {
        if (go == null) return;
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }

    #endregion


    #region Tools and Utilies

    private static string Key => "Chromarchy.Bookmarks:" + Application.dataPath;

    private static void Load()
    {
        _gids.Clear();
        string raw = EditorPrefs.GetString(Key, "");
        if (!string.IsNullOrEmpty(raw))
            _gids.AddRange(raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        RebuildIdCache();
    }

    private static void Save()
    {
        EditorPrefs.SetString(Key, string.Join(";", _gids));
    }

    private static void RebuildIdCache()
    {
        _ids.Clear();
        foreach (string gid in _gids)
        {
            GameObject go = ResolveGid(gid);
            if (go != null) _ids.Add(go.GetInstanceID());
        }
    }

    #endregion


    #region Private and Protected

    private static readonly List<string> _gids = new List<string>();
    private static readonly HashSet<int> _ids = new HashSet<int>();

    #endregion
}
}
