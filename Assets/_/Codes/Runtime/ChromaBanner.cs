using System;
using UnityEngine;

namespace Chromarchy
{
public enum ChromaAlign { Center, Left, Right }
public enum ChromaFontStyle { Bold, Normal, Italic, BoldItalic }

// Editor-only decoration: colors a GameObject's Hierarchy row without touching its name.
// The Hierarchy drawer (ChromaHeaders, Editor-only) reads these fields. Toggle the component's
// own "enabled" checkbox to turn the banner off. Removed from built scenes by ChromaBuildStripper,
// so it has zero runtime footprint.
[DisallowMultipleComponent]
[AddComponentMenu("Chroma/Chroma Banner")]
public class ChromaBanner : MonoBehaviour
{
    [Header("Background")]
    public bool m_background = true;
    public Color m_color = new Color(0.15f, 0.45f, 0.90f);
    public bool m_gradient = false;
    public Color m_color2 = new Color(0.48f, 0.18f, 0.91f);
    public bool m_vertical = false;

    [Header("Font")]
    public Color m_textColor = Color.white;
    public ChromaAlign m_align = ChromaAlign.Center;
    public ChromaFontStyle m_fontStyle = ChromaFontStyle.Bold;
    public int m_fontSize = 0;

    [Header("Title")]
    [Tooltip("Empty = use the GameObject's name.")]
    public string m_title = "";

#if UNITY_EDITOR
    // Lets the Hierarchy drawer refresh its cache when this component is added, edited, or removed.
    // Editor-only: these callbacks compile out of player builds entirely.
    public static event Action Changed;
    private void OnValidate() { Changed?.Invoke(); }
    private void OnEnable() { Changed?.Invoke(); }
    private void OnDisable() { Changed?.Invoke(); }
    private void OnDestroy() { Changed?.Invoke(); }
#endif
}
}
