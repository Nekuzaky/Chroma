using NUnit.Framework;
using UnityEngine;

namespace Chromarchy.Editor.Tests
{
// EditMode tests for Chromarchy's name parsing. Run via Window > General > Test Runner (EditMode).
// These exercise the internal parsing helpers and intentionally avoid specs whose tokens collide
// with default preset keys (h1/h2/h3/cat/grad), so results don't depend on the live config asset.
public class ChromarchyParsingTests
{
    #region TryStripName

    [Test]
    public void StripName_SolidBanner_ReturnsTitle()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("#1f6feb center bold=Player", out string cleaned));
        Assert.AreEqual("Player", cleaned);
    }

    [Test]
    public void StripName_NamedColorBanner_ReturnsTitle()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("blue left=Enemies", out string cleaned));
        Assert.AreEqual("Enemies", cleaned);
    }

    [Test]
    public void StripName_GradientBanner_ReturnsTitle()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("#1f6feb>#ff8800 center=Grad", out string cleaned));
        Assert.AreEqual("Grad", cleaned);
    }

    [Test]
    public void StripName_TitleWithEquals_KeepsEverythingAfterFirstEquals()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("blue=A=B", out string cleaned));
        Assert.AreEqual("A=B", cleaned);
    }

    [Test]
    public void StripName_SeparatorWithCaption_ReturnsCaption()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("--- Spawners ---", out string cleaned));
        Assert.AreEqual("Spawners", cleaned);
    }

    [Test]
    public void StripName_BareSeparator_ReturnsEmpty()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("---", out string cleaned));
        Assert.AreEqual("", cleaned);
    }

    [Test]
    public void StripName_UnderscoreSeparator_IsSeparator()
    {
        Assert.IsTrue(ChromarchyHeaders.TryStripName("___ Section ___", out string cleaned));
        Assert.AreEqual("Section", cleaned);
    }

    [Test]
    public void StripName_PlainName_ReturnsFalse()
    {
        Assert.IsFalse(ChromarchyHeaders.TryStripName("Player Camera", out _));
    }

    [Test]
    public void StripName_UnknownColorToken_ReturnsFalse()
    {
        // "notacolor" is not a recognized color/keyword, so the spec is not a banner.
        Assert.IsFalse(ChromarchyHeaders.TryStripName("notacolor center=Title", out _));
    }

    [Test]
    public void StripName_MalformedHex_ReturnsFalse()
    {
        Assert.IsFalse(ChromarchyHeaders.TryStripName("#zzzzzz center=Title", out _));
    }

    [Test]
    public void StripName_NoColorOnlyOptions_ReturnsFalse()
    {
        // Options but no background color => not a banner (nothing to draw).
        Assert.IsFalse(ChromarchyHeaders.TryStripName("center bold=Title", out _));
    }

    [Test]
    public void StripName_NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(ChromarchyHeaders.TryStripName(null, out _));
        Assert.IsFalse(ChromarchyHeaders.TryStripName("", out _));
    }

    #endregion


    #region TryGetColor

    [Test]
    public void GetColor_NamedColor_Parses()
    {
        Assert.IsTrue(ChromarchyHeaders.TryGetColor("blue", out _));
        Assert.IsTrue(ChromarchyHeaders.TryGetColor("grey", out _));
        Assert.IsTrue(ChromarchyHeaders.TryGetColor("orange", out _));
    }

    [Test]
    public void GetColor_HexWithHash_Parses()
    {
        Assert.IsTrue(ChromarchyHeaders.TryGetColor("#FF8800", out Color c));
        Assert.AreEqual(1f, c.r, 0.01f);
        Assert.AreEqual(0f, c.b, 0.01f);
    }

    [Test]
    public void GetColor_HexWithoutHash_Parses()
    {
        Assert.IsTrue(ChromarchyHeaders.TryGetColor("f80", out _));
    }

    [Test]
    public void GetColor_Garbage_ReturnsFalse()
    {
        Assert.IsFalse(ChromarchyHeaders.TryGetColor("notacolor", out _));
        Assert.IsFalse(ChromarchyHeaders.TryGetColor("", out _));
        Assert.IsFalse(ChromarchyHeaders.TryGetColor("   ", out _));
    }

    #endregion


    #region TryGetPreviewColor

    [Test]
    public void PreviewColor_LiteralColor_ReturnsThatColor()
    {
        Assert.IsTrue(ChromarchyHeaders.TryGetPreviewColor("#ff8800 center bold", out Color c));
        Assert.AreEqual(1f, c.r, 0.01f);
        Assert.AreEqual(0f, c.b, 0.01f);
    }

    [Test]
    public void PreviewColor_SkipsTextColorToken()
    {
        // text:white comes first but must be skipped; the red background is the preview color.
        Assert.IsTrue(ChromarchyHeaders.TryGetPreviewColor("text:white #ff0000", out Color c));
        Assert.AreEqual(1f, c.r, 0.01f);
        Assert.Less(c.g, 0.5f);
    }

    [Test]
    public void PreviewColor_GradientTakesFirstStop()
    {
        Assert.IsTrue(ChromarchyHeaders.TryGetPreviewColor("#ff0000>#0000ff center", out Color c));
        Assert.AreEqual(1f, c.r, 0.01f);
        Assert.AreEqual(0f, c.b, 0.01f);
    }

    [Test]
    public void PreviewColor_OptionsOnly_ReturnsFalse()
    {
        Assert.IsFalse(ChromarchyHeaders.TryGetPreviewColor("left bold s12", out _));
    }

    #endregion
}
}
