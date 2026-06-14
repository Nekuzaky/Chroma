using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Chroma.Editor.Tests
{
// EditMode tests for the convention linter's rule evaluation (ChromaLinter.LintObject).
// Each test builds its own in-memory ChromaConfig + GameObjects, so results never depend
// on the project's live config asset or open scenes.
public class ChromaLintTests
{
    private ChromaConfig _cfg;
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<ChromaLinter.Violation> _results = new List<ChromaLinter.Violation>();

    [SetUp]
    public void SetUp()
    {
        _cfg = ScriptableObject.CreateInstance<ChromaConfig>();
        _cfg.m_lintRules = new List<ChromaConfig.LintRule>();
        _results.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in _spawned)
            if (go != null) Object.DestroyImmediate(go);
        _spawned.Clear();
        if (_cfg != null) Object.DestroyImmediate(_cfg);
    }

    private GameObject Spawn(string name)
    {
        var go = new GameObject(name);
        _spawned.Add(go);
        return go;
    }

    private ChromaConfig.LintRule AddRule(LintAssert assert, string assertValue = "",
        LintScope scope = LintScope.All, string scopeValue = "",
        LintSeverity severity = LintSeverity.Warning)
    {
        var rule = new ChromaConfig.LintRule
        {
            m_id = "test-rule",
            m_assert = assert,
            m_assertValue = assertValue,
            m_scope = scope,
            m_scopeValue = scopeValue,
            m_severity = severity,
            m_message = "test message"
        };
        _cfg.m_lintRules.Add(rule);
        return rule;
    }

    private int Lint(GameObject go, int depth = 0)
    {
        _results.Clear();
        ChromaLinter.LintObject(go, depth, _cfg, _results);
        return _results.Count;
    }

    #region NoDefaultName

    [Test]
    public void NoDefaultName_FlagsCube()
    {
        AddRule(LintAssert.NoDefaultName);
        Assert.AreEqual(1, Lint(Spawn("Cube")));
    }

    [Test]
    public void NoDefaultName_FlagsNumberedCopy()
    {
        AddRule(LintAssert.NoDefaultName);
        Assert.AreEqual(1, Lint(Spawn("Cube (3)")));
    }

    [Test]
    public void NoDefaultName_PassesCustomName()
    {
        AddRule(LintAssert.NoDefaultName);
        Assert.AreEqual(0, Lint(Spawn("PlayerRig")));
    }

    [Test]
    public void StripCopySuffix_HandlesEdgeCases()
    {
        Assert.AreEqual("Cube", ChromaLinter.StripCopySuffix("Cube (12)"));
        Assert.AreEqual("Cube (a)", ChromaLinter.StripCopySuffix("Cube (a)"));
        Assert.AreEqual("Cube ()", ChromaLinter.StripCopySuffix("Cube ()"));
        Assert.AreEqual("Cube", ChromaLinter.StripCopySuffix("Cube"));
    }

    #endregion


    #region NoEmpty

    [Test]
    public void NoEmpty_FlagsBareGameObject()
    {
        AddRule(LintAssert.NoEmpty);
        Assert.AreEqual(1, Lint(Spawn("Holder")));
    }

    [Test]
    public void NoEmpty_PassesWithComponent()
    {
        AddRule(LintAssert.NoEmpty);
        GameObject go = Spawn("Collider");
        go.AddComponent<BoxCollider>();
        Assert.AreEqual(0, Lint(go));
    }

    [Test]
    public void NoEmpty_PassesWithChild()
    {
        AddRule(LintAssert.NoEmpty);
        GameObject parent = Spawn("Group");
        GameObject child = Spawn("Child");
        child.transform.SetParent(parent.transform);
        Assert.AreEqual(0, Lint(parent));
    }

    #endregion


    #region NameRegex + scopes

    [Test]
    public void NameRegex_FlagsMismatch_InScope()
    {
        AddRule(LintAssert.NameRegex, "^UI_[A-Z][a-zA-Z0-9]+$", LintScope.NamePrefix, "UI_");
        Assert.AreEqual(1, Lint(Spawn("UI_button")));   // lowercase after UI_ violates
        Assert.AreEqual(0, Lint(Spawn("UI_Button")));   // matches
        Assert.AreEqual(0, Lint(Spawn("Enemy")));       // out of scope entirely
    }

    [Test]
    public void NameRegex_InvalidPattern_NeverFires()
    {
        AddRule(LintAssert.NameRegex, "([unclosed");
        Assert.AreEqual(0, Lint(Spawn("Whatever")));
    }

    [Test]
    public void Scope_RootOnly_SkipsChildren()
    {
        AddRule(LintAssert.NoDefaultName, scope: LintScope.RootOnly);
        GameObject root = Spawn("Cube");
        GameObject child = Spawn("Cube");
        child.transform.SetParent(root.transform);

        Assert.AreEqual(1, Lint(root));
        Assert.AreEqual(0, Lint(child));
    }

    [Test]
    public void Scope_UnknownTag_DoesNotThrow()
    {
        AddRule(LintAssert.NoDefaultName, scope: LintScope.Tag, scopeValue: "ThisTagDoesNotExist_Chroma");
        Assert.DoesNotThrow(() => Lint(Spawn("Cube")));
        Assert.AreEqual(0, _results.Count);
    }

    #endregion


    #region MaxDepth / HasBanner / misc

    [Test]
    public void MaxDepth_FlagsDeepObjects()
    {
        AddRule(LintAssert.MaxDepth, "8");
        GameObject go = Spawn("Deep");
        Assert.AreEqual(1, Lint(go, depth: 9));
        Assert.AreEqual(0, Lint(go, depth: 8));
    }

    [Test]
    public void HasBanner_PassesNameBanner_FlagsPlain()
    {
        AddRule(LintAssert.HasBanner);
        Assert.AreEqual(0, Lint(Spawn("blue=Section")));
        Assert.AreEqual(1, Lint(Spawn("Plain")));
    }

    [Test]
    public void DisabledRule_NeverFires()
    {
        ChromaConfig.LintRule rule = AddRule(LintAssert.NoDefaultName);
        rule.m_enabled = false;
        Assert.AreEqual(0, Lint(Spawn("Cube")));
    }

    [Test]
    public void Violation_CarriesRuleSeverityAndMessage()
    {
        AddRule(LintAssert.NoDefaultName, severity: LintSeverity.Error);
        Lint(Spawn("Cube"));
        Assert.AreEqual(1, _results.Count);
        Assert.AreEqual(LintSeverity.Error, _results[0].m_severity);
        Assert.AreEqual("test message", _results[0].m_message);
        Assert.AreEqual("test-rule", _results[0].m_ruleId);
    }

    #endregion
}
}
