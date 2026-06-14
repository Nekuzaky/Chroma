using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chroma.Editor
{
/// <summary>
/// Convention-linter engine. Scans the loaded scenes (and the open prefab stage) against the
/// team's LintRules from ChromaConfig and keeps an instanceID -> violations map for O(1)
/// per-row lookups by the Hierarchy drawer. Scans are debounced (~0.35s after the last change)
/// and skipped entirely during play mode. Per-object opt-outs are per-user (EditorPrefs,
/// GlobalObjectId strings), like bookmarks — the rules themselves stay shared in the config.
/// </summary>
[InitializeOnLoad]
public static class ChromaLinter
{
    /// <summary>One rule violation on one GameObject.</summary>
    public struct Violation
    {
        public string m_ruleId;
        public string m_message;
        public LintSeverity m_severity;
    }

    #region Public

    /// <summary>Raised after every scan (results may be unchanged). Used by the window for live counts.</summary>
    public static event Action Changed;

    public static int ErrorCount { get; private set; }
    public static int WarningCount { get; private set; }
    public static int InfoCount { get; private set; }
    public static int Total => ErrorCount + WarningCount + InfoCount;

    /// <summary>True when the scan stopped early because too many objects were flagged.</summary>
    public static bool Truncated { get; private set; }

    /// <summary>All current violations, keyed by GameObject instanceID.</summary>
    public static IReadOnlyDictionary<int, List<Violation>> All => _violations;

    /// <summary>Flagged instanceIDs in scene order (stable across a scan). Used for jump-to-next.</summary>
    public static IReadOnlyList<int> Order => _order;

    #endregion


    #region Unity API

    static ChromaLinter()
    {
        EditorApplication.hierarchyChanged -= RequestRescan;
        EditorApplication.hierarchyChanged += RequestRescan;
        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        ObjectChangeEvents.changesPublished -= OnObjectChanges;
        ObjectChangeEvents.changesPublished += OnObjectChanges;

        // AssetDatabase isn't always ready inside [InitializeOnLoad] static ctors.
        EditorApplication.delayCall += () =>
        {
            LoadIgnores();
            RequestRescan();
        };
    }

    // Catches component add/remove and property edits that don't fire hierarchyChanged.
    private static void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        RequestRescan();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) RequestRescan();
    }

    /// <summary>Debounced scheduler: the actual scan runs ~0.35s after the last change.</summary>
    public static void RequestRescan()
    {
        _scanDue = EditorApplication.timeSinceStartup + 0.35;
        _scanQueued = true;
    }

    private static void Pump()
    {
        if (!_scanQueued) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return; // resume after play mode
        if (EditorApplication.timeSinceStartup < _scanDue) return;
        _scanQueued = false;
        ScanNow();
    }

    #endregion


    #region Main API

    /// <summary>O(1) lookup of the worst violation on a row. Used by the Hierarchy drawer.</summary>
    public static bool TryGetWorst(int instanceID, out Violation worst)
    {
        worst = default;
        if (!_violations.TryGetValue(instanceID, out List<Violation> list) || list.Count == 0)
            return false;

        worst = list[0];
        for (int i = 1; i < list.Count; i++)
            if (list[i].m_severity > worst.m_severity)
                worst = list[i];
        return true;
    }

    /// <summary>Called by ChromaHeaders.OnConfigChanged: clears per-rule caches and queues a rescan.</summary>
    public static void OnConfigChanged(ChromaConfig cfg)
    {
        _configCache = cfg;
        if (cfg != null && cfg.m_lintRules != null)
        {
            for (int i = 0; i < cfg.m_lintRules.Count; i++)
            {
                ChromaConfig.LintRule r = cfg.m_lintRules[i];
                if (r == null) continue;
                r.m_cachedScopeRegexFor = null;
                r.m_cachedAssertRegexFor = null;
                r.m_cachedLayerFor = null;
                r.m_cachedIntFor = null;
                r.m_cachedRuleId = null;
                r.m_cachedMessage = null;
            }
        }
        RequestRescan();
    }

    /// <summary>Run a full scan immediately (normally reached through the debounced RequestRescan).</summary>
    public static void ScanNow()
    {
        _violations.Clear();
        _order.Clear();
        ErrorCount = WarningCount = InfoCount = 0;
        Truncated = false;
        _jumpIndex = -1;

        ChromaConfig cfg = Config;
        bool active = cfg != null && cfg.m_enableLint
                      && cfg.m_lintRules != null && cfg.m_lintRules.Count > 0
                      && !EditorApplication.isPlayingOrWillChangePlaymode;

        if (active)
        {
            ResolveIgnoredIds();

            int sceneCount = SceneManager.sceneCount;
            for (int s = 0; s < sceneCount && !Truncated; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length && !Truncated; i++)
                    LintRecursive(roots[i].transform, 0, cfg);
            }

            // Prefab mode shows its own mini-hierarchy; lint it too.
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && !Truncated)
            {
                GameObject[] roots = stage.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length && !Truncated; i++)
                    LintRecursive(roots[i].transform, 0, cfg);
            }
        }

        Changed?.Invoke();
        EditorApplication.RepaintHierarchyWindow();
    }

    /// <summary>Cycle the selection through flagged objects (bind a key in Edit > Shortcuts).</summary>
    public static void JumpToNext()
    {
        if (_order.Count == 0) return;
        _jumpIndex = (_jumpIndex + 1) % _order.Count;

        // InstanceIDToObject is obsolete in Unity 6000.2+ but exists everywhere; see ChromaHeaders.
#pragma warning disable 618
        GameObject go = EditorUtility.InstanceIDToObject(_order[_jumpIndex]) as GameObject;
#pragma warning restore 618
        if (go == null) return;
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    #endregion


    #region Ignore list (per-user)

    /// <summary>True when the object is locally opted out of linting.</summary>
    public static bool IsIgnored(GameObject go)
    {
        if (go == null) return false;
        return _ignoreGids.Contains(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString());
    }

    /// <summary>Add/remove a local (per-user) lint opt-out for this object, then rescan.</summary>
    public static void ToggleIgnore(GameObject go)
    {
        if (go == null) return;
        string gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
        if (!_ignoreGids.Remove(gid)) _ignoreGids.Add(gid);
        SaveIgnores();
        RequestRescan();
    }

    /// <summary>Number of locally ignored objects (resolved or not).</summary>
    public static int IgnoreCount => _ignoreGids.Count;

    /// <summary>Drop all local lint opt-outs.</summary>
    public static void ClearIgnores()
    {
        _ignoreGids.Clear();
        SaveIgnores();
        RequestRescan();
    }

    // Keyed by the ChromaConfig asset's GUID, like ChromaBookmarks.
    private static string IgnoreKey
    {
        get
        {
            string[] guids = AssetDatabase.FindAssets("t:ChromaConfig");
            return "Chroma.LintIgnore:" + (guids.Length > 0 ? guids[0] : "default");
        }
    }

    private static void LoadIgnores()
    {
        _ignoreGids.Clear();
        string raw = EditorPrefs.GetString(IgnoreKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (string gid in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            _ignoreGids.Add(gid);
    }

    private static void SaveIgnores()
    {
        EditorPrefs.SetString(IgnoreKey, string.Join(";", _ignoreGids));
    }

    private static void ResolveIgnoredIds()
    {
        _ignoredIds.Clear();
        foreach (string gid in _ignoreGids)
        {
            if (!GlobalObjectId.TryParse(gid, out GlobalObjectId id)) continue;
            var go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            if (go != null) _ignoredIds.Add(go.GetInstanceID());
        }
    }

    #endregion


    #region Scan

    private static void LintRecursive(Transform t, int depth, ChromaConfig cfg)
    {
        GameObject go = t.gameObject;
        int id = go.GetInstanceID();

        if (!_ignoredIds.Contains(id))
        {
            _tmp.Clear();
            LintObject(go, depth, cfg, _tmp);
            if (_tmp.Count > 0)
            {
                _violations[id] = new List<Violation>(_tmp);
                _order.Add(id);
                for (int i = 0; i < _tmp.Count; i++)
                {
                    switch (_tmp[i].m_severity)
                    {
                        case LintSeverity.Error: ErrorCount++; break;
                        case LintSeverity.Warning: WarningCount++; break;
                        default: InfoCount++; break;
                    }
                }
                if (_order.Count >= MaxFlaggedObjects)
                {
                    Truncated = true;
                    return;
                }
            }
        }

        int n = t.childCount;
        for (int i = 0; i < n; i++)
        {
            LintRecursive(t.GetChild(i), depth + 1, cfg);
            if (Truncated) return;
        }
    }

    /// <summary>Run all enabled rules against one object. Internal for the EditMode tests.</summary>
    internal static void LintObject(GameObject go, int depth, ChromaConfig cfg, List<Violation> results)
    {
        List<ChromaConfig.LintRule> rules = cfg.m_lintRules;
        if (rules == null) return;
        string name = go.name;

        for (int i = 0; i < rules.Count; i++)
        {
            ChromaConfig.LintRule r = rules[i];
            if (r == null || !r.m_enabled) continue;
            if (!ScopeMatches(r, go, name)) continue;
            if (AssertHolds(r, go, name, depth)) continue;

            // Resolve display strings once per rule (m_assert.ToString() boxes the enum); reused
            // across every object this rule flags during the scan.
            if (r.m_cachedRuleId == null)
                r.m_cachedRuleId = string.IsNullOrEmpty(r.m_id) ? r.m_assert.ToString() : r.m_id;
            if (r.m_cachedMessage == null)
                r.m_cachedMessage = string.IsNullOrEmpty(r.m_message) ? DefaultMessage(r) : r.m_message;

            results.Add(new Violation
            {
                m_ruleId = r.m_cachedRuleId,
                m_message = r.m_cachedMessage,
                m_severity = r.m_severity
            });
        }
    }

    private static bool ScopeMatches(ChromaConfig.LintRule r, GameObject go, string name)
    {
        switch (r.m_scope)
        {
            case LintScope.All:
                return true;

            case LintScope.RootOnly:
                return go.transform.parent == null;

            case LintScope.Tag:
                // String compare (not CompareTag) so an unknown tag never throws.
                return !string.IsNullOrEmpty(r.m_scopeValue) && go.tag == r.m_scopeValue;

            case LintScope.Layer:
                if (string.IsNullOrEmpty(r.m_scopeValue)) return false;
                if (r.m_cachedLayerFor != r.m_scopeValue)
                {
                    r.m_cachedLayer = LayerMask.NameToLayer(r.m_scopeValue);
                    r.m_cachedLayerFor = r.m_scopeValue;
                }
                return r.m_cachedLayer >= 0 && go.layer == r.m_cachedLayer;

            case LintScope.NamePrefix:
                return !string.IsNullOrEmpty(r.m_scopeValue)
                       && name.StartsWith(r.m_scopeValue, StringComparison.Ordinal);

            case LintScope.Regex:
            {
                var rx = GetRegex(r.m_scopeValue, ref r.m_cachedScopeRegex, ref r.m_cachedScopeRegexFor);
                if (rx == null) return false;
                try { return rx.IsMatch(name); }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
            }
        }
        return false;
    }

    // True when the object satisfies the rule (i.e., no violation).
    private static bool AssertHolds(ChromaConfig.LintRule r, GameObject go, string name, int depth)
    {
        switch (r.m_assert)
        {
            case LintAssert.HasBanner:
            {
                ChromaBanner b = go.GetComponent<ChromaBanner>();
                if (b != null && b.enabled) return true;
                return ChromaHeaders.NameIsHeader(name);
            }

            case LintAssert.NameRegex:
            {
                var rx = GetRegex(r.m_assertValue, ref r.m_cachedAssertRegex, ref r.m_cachedAssertRegexFor);
                if (rx == null) return true; // invalid/empty pattern -> the rule never fires
                try { return rx.IsMatch(name); }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return true; }
            }

            case LintAssert.NoEmpty:
                return go.transform.childCount > 0 || go.GetComponents<Component>().Length > 1;

            case LintAssert.NoMissingScript:
                return GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0;

            case LintAssert.RequiredParent:
            {
                if (string.IsNullOrEmpty(r.m_assertValue)) return true;
                for (Transform p = go.transform.parent; p != null; p = p.parent)
                {
                    if (p.name == r.m_assertValue) return true;
                    if (p.GetComponent(r.m_assertValue) != null) return true; // e.g. "Canvas"
                }
                return false;
            }

            case LintAssert.MaxDepth:
                return depth <= GetInt(r, 8);

            case LintAssert.NoDefaultName:
                return !IsDefaultName(name);
        }
        return true;
    }

    #endregion


    #region Tools and Utilities

    private static string DefaultMessage(ChromaConfig.LintRule r)
    {
        switch (r.m_assert)
        {
            case LintAssert.HasBanner: return "Object should have a Chroma banner.";
            case LintAssert.NameRegex: return "Name does not match the required pattern.";
            case LintAssert.NoEmpty: return "Empty GameObject (no components, no children).";
            case LintAssert.NoMissingScript: return "Missing (deleted) script on this GameObject.";
            case LintAssert.RequiredParent: return "Expected ancestor is missing.";
            case LintAssert.MaxDepth: return "Object is nested too deeply.";
            case LintAssert.NoDefaultName: return "Default name — rename it.";
        }
        return "Convention violation.";
    }

    // Compile-once regex with the same ReDoS guards as the auto-color rules.
    private static System.Text.RegularExpressions.Regex GetRegex(
        string pattern,
        ref System.Text.RegularExpressions.Regex cache,
        ref string cacheFor)
    {
        if (cacheFor != pattern)
        {
            cacheFor = pattern;
            cache = null;
            if (!string.IsNullOrEmpty(pattern))
            {
                try
                {
                    cache = new System.Text.RegularExpressions.Regex(
                        pattern,
                        System.Text.RegularExpressions.RegexOptions.None,
                        TimeSpan.FromMilliseconds(500));
                }
                catch (ArgumentException) { cache = null; }
            }
        }
        return cache;
    }

    private static int GetInt(ChromaConfig.LintRule r, int fallback)
    {
        if (r.m_cachedIntFor != r.m_assertValue)
        {
            r.m_cachedIntFor = r.m_assertValue;
            r.m_cachedInt = int.TryParse(r.m_assertValue, out int v) ? v : fallback;
        }
        return r.m_cachedInt;
    }

    /// <summary>True for Unity's default creation names, including "Cube (3)"-style copies.</summary>
    internal static bool IsDefaultName(string name)
    {
        return _defaultNames.Contains(StripCopySuffix(name));
    }

    /// <summary>"Cube (3)" -> "Cube". Returns the input when there is no numeric copy suffix.</summary>
    internal static string StripCopySuffix(string name)
    {
        if (string.IsNullOrEmpty(name) || name[name.Length - 1] != ')') return name;
        int open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0) return name;
        int digits = 0;
        for (int i = open + 2; i < name.Length - 1; i++)
        {
            if (name[i] < '0' || name[i] > '9') return name;
            digits++;
        }
        return digits > 0 ? name.Substring(0, open) : name;
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


    #region Private and Protected

    // Hard cap: a scan stops once this many objects are flagged (keeps pathological scenes fast).
    private const int MaxFlaggedObjects = 500;

    private static readonly Dictionary<int, List<Violation>> _violations = new Dictionary<int, List<Violation>>();
    private static readonly List<int> _order = new List<int>();
    private static readonly List<Violation> _tmp = new List<Violation>(4);
    private static readonly HashSet<string> _ignoreGids = new HashSet<string>();
    private static readonly HashSet<int> _ignoredIds = new HashSet<int>();
    private static ChromaConfig _configCache;

    private static double _scanDue;
    private static bool _scanQueued;
    private static int _jumpIndex = -1;

    private static readonly HashSet<string> _defaultNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "GameObject", "New Game Object", "Cube", "Sphere", "Capsule",
        "Cylinder", "Plane", "Quad", "Particle System"
    };

    #endregion
}
}
