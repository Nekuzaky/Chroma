CHROMA 0.0.2
================================================================================

Editor-only Unity extension that color-codes your Hierarchy and Project folders
so large scenes stay readable — with zero runtime cost.


FEATURES
--------
• Colored Banners — Turn any GameObject into a colored header:
  - By name: rename like "#1f6feb center bold=Title" (solid color or gradients)
  - By component: add ChromaBanner component (keeps the GameObject name clean)

• Separators — Create visual dividers by naming objects "---" or "___"

• Tree Guide Lines — File explorer style connector lines in the indent gutter

• Auto-Color Rules — Tint rows by Tag, Layer, name prefix, or regex pattern

• Child Color Inheritance — Children inherit colors from parent banners (flat or depth-fade)

• Display Extras — Child count "(N)", zebra striping, bookmarks (jump & reorder)

• Project Window — Color folders in the Project window

• RGB Mode — Animate hierarchy rows through rainbow colors (~30fps)

• Themes — Quick preset color schemes (Minimal, Vibrant, Soft, High-Contrast)

• Build Stripping — Banners are removed from built scenes (zero runtime footprint)


QUICK START
-----------
1. Open Tools > Chroma

2. Select a GameObject and:
   - Choose a color and style in the panel
   - Click "Apply banner" (stores in name) or "Add component"

3. See it colored in the Hierarchy instantly

Done! Customize more in Settings tab (tree lines, separators, themes, etc.)


REQUIREMENTS
------------
• Unity 2021.3 LTS or newer (developed on Unity 6)
• Editor-only — no runtime impact


INSTALLATION
------------
Copy Assets/_/Code/Chroma into your project's Assets folder.
Self-contained with assembly definitions (Chroma.Runtime, Chroma.Editor).


FILE STRUCTURE
--------------
Assets/_/Code/Chroma/
  Runtime/   ChromaBanner component (all platforms, Editor-only at runtime)
  Editor/    Hierarchy/Project drawers, window, config (Editor-only)
  Tests/     EditMode tests


UPDATING CHROMA
---------------
Your config is ALWAYS preserved when updating. Here's how:

1. AUTOMATIC MIGRATION (Safe)
   - Each new version runs automatic migrations on old configs
   - Your settings, presets, folder colors, and rules stay intact
   - New features get sensible defaults, old ones never disappear

2. BACKUP YOUR CONFIG (Optional)
   - Tools > Chroma > Settings tab > Export config...
   - Saves as chroma-config.json (human-readable)
   - Keep this file safe before major updates

3. RESTORE IF NEEDED
   - Tools > Chroma > Settings tab > Import config...
   - Select the exported JSON file
   - Your old config is restored instantly

WHAT NEVER BREAKS
- Your colored banners (names and components)
- Your folder colors
- Your auto-color rules
- Your custom presets
- Your theme choices

TIP: If something looks wrong after an update, check the console for warnings.


DOCUMENTATION
-------------
For detailed information on banner syntax, presets, and configuration,
see the README.md file or visit the Chroma settings panel in Unity.


LICENSE
-------
No license file yet — all rights reserved by the repository owner.

================================================================================
