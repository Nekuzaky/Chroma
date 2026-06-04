using System;
using UnityEngine;

namespace Chroma
{
/// <summary>Text alignment for banner text: left, center, or right.</summary>
public enum ChromaAlign { Center, Left, Right }

/// <summary>Font style for banner text: normal, bold, italic, or bold italic.</summary>
public enum ChromaFontStyle { Bold, Normal, Italic, BoldItalic }

/// <summary>
/// Editor-only component to color a GameObject's Hierarchy row with a colored banner.
/// Keeps the GameObject name clean (vs. embedding specs in the name).
/// The hierarchy drawer (ChromaHeaders) reads these fields.
/// Removed from built scenes; zero runtime footprint. Disable the component to hide the banner.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Chroma/Chroma Banner")]
public class ChromaBanner : MonoBehaviour
{
    #region Public

    [Header("Background")]
    [Tooltip("Enable/disable the colored banner background")]
    public bool m_background = true;
    [Tooltip("Banner background color")]
    public Color m_color = new Color(0.15f, 0.45f, 0.90f);
    [Tooltip("Draw a gradient background instead of solid color")]
    public bool m_gradient = false;
    [Tooltip("Gradient end color (if gradient is enabled)")]
    public Color m_color2 = new Color(0.48f, 0.18f, 0.91f);
    [Tooltip("Gradient direction: false = horizontal, true = vertical")]
    public bool m_vertical = false;

    [Space(150), Header("Font")]
    [Tooltip("Banner text color")]
    public Color m_textColor = Color.white;
    [Tooltip("Banner text alignment: Left, Center, or Right")]
    public ChromaAlign m_align = ChromaAlign.Center;
    [Tooltip("Banner text style: Bold, Normal, Italic, or BoldItalic")]
    public ChromaFontStyle m_fontStyle = ChromaFontStyle.Bold;
    [Tooltip("Font size in pixels (0 = default size)")]
    public int m_fontSize = 0;

    [Space(150), Header("Title")]
    [Tooltip("Banner text label. If empty, the GameObject's name is used")]
    public string m_title = "";

    #endregion


    #region Unity API

#if UNITY_EDITOR
    /// <summary>Editor-only event: fired when a ChromaBanner is added, edited, enabled/disabled, or destroyed. Notifies the Hierarchy drawer to refresh its cache.</summary>
    public static event Action Changed;

    private void OnValidate() => Changed?.Invoke();
    private void OnEnable() => Changed?.Invoke();
    private void OnDisable() => Changed?.Invoke();
    private void OnDestroy() => Changed?.Invoke();
#endif

    #endregion
}
}
