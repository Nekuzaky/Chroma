using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chroma.Editor
{
/// <summary>
/// Scene View overlay listing Chroma bookmarks as colored chips. Click a row to select + frame the
/// object. Hidden by default — enable it from the Scene View's overlays menu ("⋮" / Overlays).
/// Reuses the existing <see cref="ChromaBookmarks"/> data and <see cref="ChromaHeaders.TryGetRowColor"/>.
/// </summary>
[Overlay(typeof(SceneView), "chroma-bookmarks", "Chroma Bookmarks")]
public class ChromaBookmarksOverlay : Overlay
{
    private VisualElement _root;

    public override VisualElement CreatePanelContent()
    {
        _root = new VisualElement();
        _root.style.minWidth = 150;
        _root.style.paddingTop = 2;
        _root.style.paddingBottom = 2;
        Rebuild();
        return _root;
    }

    public override void OnCreated()
    {
        ChromaBookmarks.Changed += Rebuild;
        EditorApplication.hierarchyChanged += Rebuild;
    }

    public override void OnWillBeDestroyed()
    {
        ChromaBookmarks.Changed -= Rebuild;
        EditorApplication.hierarchyChanged -= Rebuild;
    }

    private void Rebuild()
    {
        if (_root == null) return;
        _root.Clear();

        System.Collections.Generic.IReadOnlyList<string> gids = ChromaBookmarks.Gids;
        if (gids.Count == 0)
        {
            var empty = new Label("No bookmarks");
            empty.style.fontSize = 11;
            empty.style.opacity = 0.6f;
            empty.style.paddingLeft = 2;
            _root.Add(empty);
            return;
        }

        for (int i = 0; i < gids.Count; i++)
            _root.Add(MakeRow(ChromaBookmarks.ResolveGid(gids[i])));
    }

    private static VisualElement MakeRow(GameObject go)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 1;

        var chip = new VisualElement();
        chip.style.width = 10;
        chip.style.height = 10;
        chip.style.flexShrink = 0;
        chip.style.marginRight = 6;
        chip.style.borderTopLeftRadius = 2;
        chip.style.borderTopRightRadius = 2;
        chip.style.borderBottomLeftRadius = 2;
        chip.style.borderBottomRightRadius = 2;
        Color c = (go != null && ChromaHeaders.TryGetRowColor(go, out Color rc)) ? rc : new Color(0.5f, 0.5f, 0.5f);
        c.a = 1f;
        chip.style.backgroundColor = c;
        row.Add(chip);

        string label = go != null ? CleanName(go.name) : "(not in scene)";
        var btn = new Button(() => { if (go != null) ChromaBookmarks.Jump(go); }) { text = label };
        btn.style.flexGrow = 1;
        btn.style.marginLeft = 0;
        btn.style.marginRight = 0;
        btn.style.marginTop = 0;
        btn.style.marginBottom = 0;
        btn.style.unityTextAlign = TextAnchor.MiddleLeft;
        btn.SetEnabled(go != null);
        row.Add(btn);

        return row;
    }

    private static string CleanName(string name)
    {
        int eq = name.IndexOf('=');
        return eq > 0 ? name.Substring(eq + 1).Trim() : name;
    }
}
}
