using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
/// <summary>Shows patch notes popup when Chroma is updated. Runs automatically on Unity startup.</summary>
[InitializeOnLoad]
public static class ChromaUpdateNotifier
{
    private const string PREF_KEY = "Chroma_LastSeenVersion";
    private const string CURRENT_VERSION = "0.3.0";

    static ChromaUpdateNotifier()
    {
        EditorApplication.delayCall += CheckAndShowPatchNotes;
    }

    private static void CheckAndShowPatchNotes()
    {
        string lastSeen = EditorPrefs.GetString(PREF_KEY, "");

        // First time or new version
        if (lastSeen != CURRENT_VERSION)
        {
            EditorPrefs.SetString(PREF_KEY, CURRENT_VERSION);
            ShowPatchNotes(lastSeen);
            Debug.Log($"[Chroma] Showing patch notes. Previous: {lastSeen}, Current: {CURRENT_VERSION}");
        }
    }

    private static void ShowPatchNotes(string previousVersion)
    {
        string title = $"Chroma Updated to {CURRENT_VERSION}";
        string message = GetPatchNotes(previousVersion);

        EditorUtility.DisplayDialog(title, message, "OK");
    }

    /// <summary>Get patch notes for the current version. Add new entries as you release versions.</summary>
    private static string GetPatchNotes(string from)
    {
        return $@"Welcome to Chroma {CURRENT_VERSION}! 🎉

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🚨 NEW: CONVENTION LINTER
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Chroma now ENFORCES your team's scene conventions:
define rules (scope + assertion + severity) and offending
rows get an inline severity icon with the rule's message.

✓ 7 assertions: HasBanner, NameRegex, NoEmpty,
   NoMissingScript, RequiredParent, MaxDepth, NoDefaultName
✓ New ""Lint"" tab: live violation list, jump / select /
   ignore, team rule editor, ready-made rulesets
✓ Rules live in the shared config — committed via git,
   the whole team stays aligned
✓ Per-user ignores + ""Next Lint Violation"" shortcut
✓ Debounced scans, O(1) per-row lookups, paused in play


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🧩 NEW: ROW WIDGETS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ Active toggle — always-visible checkbox on every row
   (click = SetActive with full Undo support)
✓ Component icons — see each object's components at a
   glance (cached, capped, configurable)


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🎨 NEW: COLORBLIND-SAFE THEMES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Two CVD-safe palettes in Settings > Themes:
✓ Okabe-Ito (deuteranopia / protanopia / tritanopia safe)
✓ IBM Design Library


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🐛 FIXES & POLISH
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ Missing-script icon now shows its tooltip
✓ Config schema version separated from the edit counter
   (real migrations are now possible)
✓ Component caches refresh on component add/remove


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📖 RESOURCES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🌐 GitHub: https://github.com/Nekuzaky/Chroma
📧 Support: contact@nekuzaky.com
🌍 Website: https://www.nekuzaky.com/contact

Questions? Found a bug? Reach out anytime!
";
    }
}
}
