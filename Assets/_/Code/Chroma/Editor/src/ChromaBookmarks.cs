using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
// Per-user, scene-aware bookmarks for the Hierarchy. Stored in EditorPrefs as GlobalObjectId
// strings (NOT in the shared config asset). A cached instanceID set gives O(1) per-row checks.
[InitializeOnLoad]
public static class ChromaBookmarks
{
    #region Public

    public static IReadOnlyList<string> Gids => _gids;

    /// <summary>Raised when the bookmark list changes (add / remove / reorder / load). Used by the Scene View overlay.</summary>
    public static event Action Changed;

    #endregion


    #region Private and Protected

    private static readonly List<string> _gids = new List<string>();
    private static readonly HashSet<int> _ids = new HashSet<int>();

    #endregion


    #region Unity API

    static ChromaBookmarks()
    {
        EditorApplication.hierarchyChanged += RebuildIdCache;
        // Defer the load: AssetDatabase isn't always ready inside [InitializeOnLoad] static ctors,
        // and the bookmark key depends on the ChromaConfig asset's GUID.
        EditorApplication.delayCall += Load;
    }

    #endregion


    #region Main API

    public static bool IsBookmarked(int instanceID) => _ids.Contains(instanceID);

    public static bool IsBookmarked(GameObject go)
    {
        if (go == null) return false;
        return _gids.Contains(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString());
    }

    public static void Add(GameObject go)
    {
        if (go == null) return;
        // Ensures the config asset exists so the bookmark key (based on its GUID) is stable
        // before the first write.
        ChromaConfig.GetOrCreate();
        string gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
        if (_gids.Contains(gid)) return;
        _gids.Add(gid);
        Save();
        RebuildIdCache();
        EditorApplication.RepaintHierarchyWindow();
    }

    public static void Toggle(GameObject go)
    {
        if (go == null) return;
        ChromaConfig.GetOrCreate();
        string gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
        bool removed = _gids.Remove(gid);
        if (!removed) _gids.Add(gid);
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

    public static void Reorder(int from, int to)
    {
        if (from < 0 || from >= _gids.Count) return;
        if (to < 0) to = 0;
        if (to >= _gids.Count) to = _gids.Count - 1;
        if (from == to) return;
        string g = _gids[from];
        _gids.RemoveAt(from);
        _gids.Insert(to, g);
        Save();
        EditorApplication.RepaintHierarchyWindow();
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


    #region Tools and Utilities

    // Keyed by the ChromaConfig asset's GUID (stable across project moves / renames).
    // Falls back to "default" when the asset doesn't exist yet; Add() materializes the asset
    // before the first write, so we never persist under "default" in practice.
    private static string Key
    {
        get
        {
            string[] guids = AssetDatabase.FindAssets("t:ChromaConfig");
            if (guids.Length > 0) return "Chroma.Bookmarks:" + guids[0];
            return "Chroma.Bookmarks:default";
        }
    }

    private static string LegacyKey => "Chroma.Bookmarks:" + Application.dataPath;

    private static void Load()
    {
        _gids.Clear();
        string key = Key;
        string raw = EditorPrefs.GetString(key, "");

        // One-shot migration from the old dataPath-based key.
        if (string.IsNullOrEmpty(raw))
        {
            string legacyRaw = EditorPrefs.GetString(LegacyKey, "");
            if (!string.IsNullOrEmpty(legacyRaw) && key != LegacyKey)
            {
                raw = legacyRaw;
                EditorPrefs.SetString(key, raw);
                EditorPrefs.DeleteKey(LegacyKey);
            }
        }

        if (!string.IsNullOrEmpty(raw))
            _gids.AddRange(raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        RebuildIdCache();
        Changed?.Invoke();
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void Save()
    {
        EditorPrefs.SetString(Key, string.Join(";", _gids));
        Changed?.Invoke();
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
}
}
