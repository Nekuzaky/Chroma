using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chromarchy.Editor
{
// Strips Chromarchy banner specs from GameObject names in scenes during a player build.
// Modifies only the in-memory scene that Unity bakes into the player; the .unity asset on
// disk is left untouched. Gated by ChromarchyConfig.m_stripNamesInBuild (default true).
public class ChromarchyBuildStripper : IProcessSceneWithReport
{
    #region Publics

    public int callbackOrder => 0;

    #endregion


    #region Unity API

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // OnProcessScene fires on play mode entry too — `report` is null in that case.
        if (report == null) return;
        if (!ShouldStrip()) return;

        int count = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            count += StripRecursive(roots[i].transform);

        if (count > 0)
            Debug.Log($"Chromarchy: stripped {count} banner name(s) from '{scene.name}'.");
    }

    #endregion


    #region Tools and Utilies

    private static int StripRecursive(Transform t)
    {
        int count = 0;

        if (ChromarchyHeaders.TryStripName(t.name, out string cleaned)
            && !string.IsNullOrWhiteSpace(cleaned)
            && cleaned != t.name)
        {
            t.name = cleaned;
            count++;
        }

        int n = t.childCount;
        for (int i = 0; i < n; i++)
            count += StripRecursive(t.GetChild(i));

        return count;
    }

    private static bool ShouldStrip()
    {
        string[] guids = AssetDatabase.FindAssets("t:ChromarchyConfig");
        if (guids.Length == 0) return true; // no config asset => assume default behavior
        var cfg = AssetDatabase.LoadAssetAtPath<ChromarchyConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return cfg == null || cfg.m_stripNamesInBuild;
    }

    #endregion
}
}
