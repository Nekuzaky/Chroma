using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chromarchy.Editor
{
// Colors folders in the Project window by tinting their icon. Folder colors are stored in the
// shared ChromarchyConfig (keyed by folder GUID, so they survive moves and are git-shareable).
// The guid->color lookup is cached and rebuilt only when the config version changes.
[InitializeOnLoad]
public static class ChromarchyFolders
{
    #region Unity API

    static ChromarchyFolders()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
        EditorApplication.projectChanged += () => _dirty = true;
    }

    private static void OnProjectItemGUI(string guid, Rect rect)
    {
        if (Event.current.type != EventType.Repaint) return;

        ChromarchyConfig cfg = Config;
        if (cfg == null || !cfg.m_enableFolderColors) return;

        EnsureCache(cfg);
        if (!_colors.TryGetValue(guid, out Color col)) return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;

        Texture icon = AssetDatabase.GetCachedIcon(path);
        if (icon == null) return;

        bool gridView = rect.height > 20f;
        Rect iconRect;
        if (gridView)
        {
            float size = rect.width * 0.7f;
            iconRect = new Rect(rect.x + (rect.width - size) * 0.5f, rect.y + 2f, size, size);
        }
        else
        {
            iconRect = new Rect(rect.x, rect.y, rect.height, rect.height);
        }

        Color prev = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        GUI.color = prev;
    }

    #endregion


    #region Main API

    // null color clears the folder's entry. Bumps the config version so the cache rebuilds and
    // marks the asset dirty so the change persists.
    public static void SetColor(string guid, Color? color)
    {
        if (string.IsNullOrEmpty(guid)) return;
        ChromarchyConfig cfg = ChromarchyConfig.GetOrCreate();
        _configCache = cfg; // keep the render-path cache pointing at the live asset

        ChromarchyConfig.FolderColor entry = cfg.m_folderColors.Find(f => f != null && f.m_guid == guid);
        if (color.HasValue)
        {
            if (entry == null)
            {
                entry = new ChromarchyConfig.FolderColor { m_guid = guid };
                cfg.m_folderColors.Add(entry);
            }
            entry.m_color = color.Value;
        }
        else
        {
            cfg.m_folderColors.RemoveAll(f => f == null || f.m_guid == guid);
        }

        cfg.m_version++;
        EditorUtility.SetDirty(cfg);
        _dirty = true;
        EditorApplication.RepaintProjectWindow();
    }

    public static void Invalidate() => _dirty = true;

    #endregion


    #region Tools and Utilies

    private static ChromarchyConfig Config
    {
        get
        {
            if (_configCache != null) return _configCache;
            string[] guids = AssetDatabase.FindAssets("t:ChromarchyConfig");
            if (guids.Length > 0)
                _configCache = AssetDatabase.LoadAssetAtPath<ChromarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return _configCache;
        }
    }

    private static void EnsureCache(ChromarchyConfig cfg)
    {
        if (!_dirty && _cachedVersion == cfg.m_version) return;

        _colors.Clear();
        if (cfg.m_folderColors != null)
            foreach (ChromarchyConfig.FolderColor f in cfg.m_folderColors)
                if (f != null && !string.IsNullOrEmpty(f.m_guid))
                    _colors[f.m_guid] = f.m_color;

        _cachedVersion = cfg.m_version;
        _dirty = false;
    }

    #endregion


    #region Private and Protected

    private static ChromarchyConfig _configCache;
    private static readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();
    private static int _cachedVersion = -1;
    private static bool _dirty = true;

    #endregion
}
}
