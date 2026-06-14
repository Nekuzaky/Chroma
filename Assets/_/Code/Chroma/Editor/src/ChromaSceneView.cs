using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chroma.Editor
{
/// <summary>
/// Extends Chroma's color-coding into the Scene View: a floating colored name label and/or a
/// colored wireframe marker for every object that carries a Chroma banner (name spec or
/// <see cref="ChromaBanner"/> component). Editor-only, opt-in (off by default), and cheap: the
/// matched-object set (with its pre-measured label width) is cached and only rebuilt when the
/// hierarchy changes; the draw pass runs only on Repaint events, fetches each object's bounds once,
/// clips labels to the visible viewport, and is capped at <see cref="MaxObjects"/>.
/// </summary>
[InitializeOnLoad]
public static class ChromaSceneView
{
    private struct Entry
    {
        public GameObject m_go;
        public Color m_color;
        public string m_label;     // cleaned title, computed once at rebuild
        public float m_labelWidth; // measured once at rebuild (CalcSize is costly per frame)
    }

    #region Private and Protected

    // Cap the matched set so a scene full of banners can't make the Scene View crawl.
    private const int MaxObjects = 400;

    private static readonly List<Entry> _entries = new List<Entry>();
    private static bool _dirty = true;
    private static ChromaConfig _configCache;
    private static GUIStyle _labelStyle;
    private static readonly GUIContent _labelContent = new GUIContent();

    // Reusable per-repaint scratch for visible labels (sized to the entry count, never per-frame alloc).
    private static Vector2[] _lblPos = new Vector2[MaxObjects];
    private static int[] _lblIdx = new int[MaxObjects];

    #endregion


    #region Unity API

    static ChromaSceneView()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged -= MarkDirty;
        EditorApplication.hierarchyChanged += MarkDirty;
        ChromaBanner.Changed -= MarkDirty;
        ChromaBanner.Changed += MarkDirty;
    }

    /// <summary>Invalidate the cached matched-object set; it rebuilds on the next Scene View repaint.</summary>
    public static void MarkDirty() => _dirty = true;

    private static void OnSceneGUI(SceneView view)
    {
        ChromaConfig cfg = Config;
        if (cfg == null || (!cfg.m_sceneLabels && !cfg.m_sceneGizmos)) return;
        if (view == null || view.camera == null) return;

        if (_dirty) Rebuild();

        // duringSceneGui fires for every event (Layout/MouseMove/drag/…). The body below is pure
        // drawing/measuring with no layout controls or hit-testing, so restrict it to Repaint.
        if (Event.current.type != EventType.Repaint) return;

        bool gizmos = cfg.m_sceneGizmos;
        bool labels = cfg.m_sceneLabels;
        Camera cam = view.camera;
        int n = _entries.Count;
        int labelCount = 0;

        // Single pass: fetch each object's bounds ONCE, draw the world-space gizmo immediately,
        // and stash on-screen label positions for the GUI pass below.
        Color prevHandles = Handles.color;
        for (int i = 0; i < n; i++)
        {
            GameObject go = _entries[i].m_go;
            if (go == null) continue;

            bool hasBounds = TryGetBounds(go, out Bounds b);

            if (gizmos)
            {
                Handles.color = _entries[i].m_color;
                if (hasBounds) Handles.DrawWireCube(b.center, b.size);
                else Handles.DrawWireCube(go.transform.position, Vector3.one * 0.3f);
            }

            if (labels)
            {
                Vector3 world = hasBounds ? b.center + Vector3.up * b.extents.y : go.transform.position;
                Vector3 vp = cam.WorldToViewportPoint(world);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue; // off-screen
                _lblPos[labelCount] = HandleUtility.WorldToGUIPoint(world);
                _lblIdx[labelCount] = i;
                labelCount++;
            }
        }
        if (gizmos) Handles.color = prevHandles;

        if (labels && labelCount > 0)
        {
            EnsureStyle();
            Handles.BeginGUI();
            for (int j = 0; j < labelCount; j++)
            {
                int i = _lblIdx[j];
                Vector2 gui = _lblPos[j];
                float w = _entries[i].m_labelWidth + 10f;
                const float h = 17f;
                var r = new Rect(gui.x - w * 0.5f, gui.y - h - 6f, w, h);

                Color c = _entries[i].m_color;
                c.a = 0.92f;
                EditorGUI.DrawRect(r, c);
                _labelContent.text = _entries[i].m_label;
                GUI.Label(r, _labelContent, _labelStyle);
            }
            Handles.EndGUI();
        }
    }

    #endregion


    #region Tools and Utilities

    private static void Rebuild()
    {
        _entries.Clear();
        EnsureStyle(); // needed to measure label widths

        int count = SceneManager.sceneCount;
        for (int s = 0; s < count; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                CollectRecursive(roots[i].transform);
                if (_entries.Count >= MaxObjects) { Finish(); return; }
            }
        }
        Finish();

        void Finish()
        {
            _dirty = false;
            if (_lblPos.Length < _entries.Count)
            {
                _lblPos = new Vector2[_entries.Count];
                _lblIdx = new int[_entries.Count];
            }
        }
    }

    private static void CollectRecursive(Transform t)
    {
        GameObject go = t.gameObject;
        if (ChromaHeaders.TryGetRowColor(go, out Color color))
        {
            color.a = 1f;
            string label = LabelText(go.name);
            _labelContent.text = label;
            float width = _labelStyle.CalcSize(_labelContent).x;
            _entries.Add(new Entry { m_go = go, m_color = color, m_label = label, m_labelWidth = width });
            if (_entries.Count >= MaxObjects) return;
        }

        int n = t.childCount;
        for (int i = 0; i < n; i++)
        {
            CollectRecursive(t.GetChild(i));
            if (_entries.Count >= MaxObjects) return;
        }
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        if (go.TryGetComponent(out Renderer rend))
        {
            bounds = rend.bounds;
            return true;
        }
        bounds = default;
        return false;
    }

    // Show the clean title, not the raw "spec=Title" name.
    private static string LabelText(string name)
    {
        int eq = name.IndexOf('=');
        return eq > 0 ? name.Substring(eq + 1).Trim() : name;
    }

    private static void EnsureStyle()
    {
        if (_labelStyle != null) return;
        _labelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            clipping = TextClipping.Clip,
        };
        _labelStyle.normal.textColor = Color.white;
    }

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

    #endregion
}
}
