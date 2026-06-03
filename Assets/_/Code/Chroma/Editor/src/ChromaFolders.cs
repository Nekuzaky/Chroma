using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
// Colors folders in the Project window by tinting their icon. Folder colors are stored in the
// shared ChromaConfig (keyed by folder GUID, so they survive moves and are git-shareable).
// The guid->color lookup is cached and rebuilt only when the config version changes.
[InitializeOnLoad]
public static class ChromaFolders
{
    #region Private and Protected

    private static ChromaConfig _configCache;
    private static readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();
    private static readonly Dictionary<string, Texture> _folderIcon = new Dictionary<string, Texture>();
    private static int _cachedVersion = -1;
    private static bool _dirty = true;

    #endregion


    #region Unity API

    static ChromaFolders()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
        EditorApplication.projectChanged += () => { _dirty = true; _folderIcon.Clear(); };
    }

    private static void OnProjectItemGUI(string guid, Rect rect)
    {
        if (Event.current.type != EventType.Repaint) return;

        ChromaConfig cfg = Config;
        if (cfg == null) return;

        bool rgb = cfg.m_rgbFolders;
        if (!rgb && !cfg.m_enableFolderColors) return;

        Color col;
        if (rgb)
        {
            // Rainbow mode: every folder cycles through the hue wheel (overrides assigned colors).
            float hue = Mathf.Repeat(
                (float)(EditorApplication.timeSinceStartup * cfg.m_rgbSpeed) + rect.y * cfg.m_rgbSpread, 1f);
            col = Color.HSVToRGB(hue, cfg.m_rgbSaturation, cfg.m_rgbValue);
        }
        else
        {
            EnsureCache(cfg);
            if (!_colors.TryGetValue(guid, out col)) return;
        }

        Texture icon = FolderIcon(guid);
        if (icon == null) return; // not a folder

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
        ChromaConfig cfg = ChromaConfig.GetOrCreate();
        _configCache = cfg; // keep the render-path cache pointing at the live asset

        ChromaConfig.FolderColor entry = cfg.m_folderColors.Find(f => f != null && f.m_guid == guid);
        if (color.HasValue)
        {
            if (entry == null)
            {
                entry = new ChromaConfig.FolderColor { m_guid = guid };
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


    #region Tools and Utilities

    private static ChromaConfig Config
    {
        get
        {
            if (_configCache != null) return _configCache;
            string[] guids = AssetDatabase.FindAssets("t:ChromaConfig");
            if (guids.Length > 0)
                _configCache = AssetDatabase.LoadAssetAtPath<ChromaConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return _configCache;
        }
    }

    // Cached per guid: the folder icon, or null for non-folders. Avoids GUIDToAssetPath +
    // IsValidFolder + GetCachedIcon on every Project-window repaint (matters a lot at RGB 30fps).
    // Cleared on projectChanged.
    private static Texture FolderIcon(string guid)
    {
        if (_folderIcon.TryGetValue(guid, out Texture icon)) return icon;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        icon = (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            ? AssetDatabase.GetCachedIcon(path)
            : null;
        _folderIcon[guid] = icon;
        return icon;
    }

    private static void EnsureCache(ChromaConfig cfg)
    {
        if (!_dirty && _cachedVersion == cfg.m_version) return;

        _colors.Clear();
        if (cfg.m_folderColors != null)
            foreach (ChromaConfig.FolderColor f in cfg.m_folderColors)
                if (f != null && !string.IsNullOrEmpty(f.m_guid))
                    _colors[f.m_guid] = f.m_color;

        _cachedVersion = cfg.m_version;
        _dirty = false;
    }

    #endregion
}
}
