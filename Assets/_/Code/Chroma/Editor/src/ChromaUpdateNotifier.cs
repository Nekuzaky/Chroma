using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
/// <summary>Shows patch notes popup when Chroma is updated. Runs automatically on Unity startup.</summary>
[InitializeOnLoad]
public static class ChromaUpdateNotifier
{
    private const string PREF_KEY = "Chroma_LastSeenVersion";
    private const string CURRENT_VERSION = "0.2.0";

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
🎨 MAJOR: RGB THEMES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Three new animated color themes for the Hierarchy:

🎃 Halloween
   → Orange, Violet, Black cycling

🎄 Christmas
   → Red, Green, Gold cycling

💝 Valentine
   → Deep Red, Hot Pink, Magenta cycling

All themes respect speed/spread settings and work on both
Hierarchy and Project window folders.

Access: Settings > RGB mode > Theme dropdown


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🔐 SECURITY HARDENING
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ ReDoS Protection
   Regex patterns now have 500ms timeout to prevent
   Editor freezes on complex patterns

✓ Safe Deserialization
   Config imports are validated and clamped to safe ranges:
   - Regex patterns: max 100 chars
   - Numeric values: clamped to valid ranges
   - String lengths: limited to prevent memory issues

✓ Path Traversal Prevention
   Config imports restricted to project folder only

✓ Editor-Only Safeguards
   Prevents accidental use of Editor-only code in builds

✓ Event Deduplication
   Fixed event subscription accumulation on assembly reload


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🐛 BUG FIXES & IMPROVEMENTS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ Substring Parsing
   Fixed edge cases with ""text:"" and ""t:"" specs

✓ Config Validation
   New ValidateAndClamp() function ensures all imported
   configurations are safe and within valid ranges

✓ Performance
   RGB themed modes run at 30% slower speed for smoother
   color transitions (configurable via Speed slider)

✓ Auto-Migration System
   Old configs automatically upgrade to new versions
   without losing user settings


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
