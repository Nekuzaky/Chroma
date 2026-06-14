CHROMA 0.3.0
================================================================================

Editor-only Unity extension that color-codes your Hierarchy and Project folders,
and enforces your team's scene conventions — with zero runtime cost.

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

FEATURES
--------
• Colored Banners — Turn any GameObject into a colored header:
  - By name: rename like "#1f6feb center bold=Title" (solid color or gradients)
  - By component: add ChromaBanner component (keeps the GameObject name clean)

• Convention Linter — Enforce team conventions: shared rules (scope + assertion +
  severity + message) flag violations inline in the Hierarchy and in a Lint tab
  (HasBanner, NameRegex, NoEmpty, NoMissingScript, RequiredParent, MaxDepth,
  NoDefaultName). Per-user ignores + a Next-Violation shortcut.

• Row Widgets — Always-visible active toggle (SetActive + Undo) and component icons

• Selection Accent — Theme-colored tint on the selected row (visible on banners too)

• Separators — Create visual dividers by naming objects "---" or "___"

• Custom Banner Font — Use a Font asset or any installed system font
  (Sans / Serif / Mono / Comic quick-picks) for banner & separator text

• Tree Guide Lines — File explorer style connector lines in the indent gutter

• Auto-Color Rules — Tint rows by Tag, Layer, name prefix, or regex pattern

• Child Color Inheritance — Children inherit colors from parent banners (flat or depth-fade)

• Display Extras — Child count "(N)", zebra striping, missing-script warnings,
  bookmarks (jump & reorder)

• Project Window — Color folders in the Project window

• Scene View — Opt-in floating colored labels + wireframe markers for banner-colored
  objects, a "Chroma Bookmarks" overlay, and a Set Scene Icon command

• RGB Mode — Animate rows through rainbow colors (~30fps), 14 themes
  (Classic, Halloween, Christmas, Valentine, Matrix, Corrupted, Funny, Fast Food,
  Candy, Police, Fire, Ice, Toxic, Rave)

• Themes — 16 color schemes with palette previews, incl. colorblind-safe
  Okabe-Ito & IBM. Animated window UI.

• Build Stripping — Banners are removed from built scenes (zero runtime footprint)


INSTALLATION
------------
Copy Assets/_/Code/Chroma into your project's Assets folder if you want.
Self-contained with assembly definitions (Chroma.Runtime, Chroma.Editor).

INSTALLATION VIA GIT (RECOMMENDED)
-----------------------------------
Install via Unity Package Manager:
  1. Window > Package Manager > + button
  2. Select "Add package from git URL..."
  3. Paste this URL:
     https://github.com/Nekuzaky/Chroma.git?path=Assets/_/Code/Chroma
  4. Click "Add"

This will install Chroma as a package in your project's Packages folder.


FILE STRUCTURE
--------------
Assets/_/Code/Chroma/
  package.json  UPM package manifest (com.nekuzaky.chroma)
  Runtime/      ChromaBanner component (all platforms, Editor-only at runtime)
  Editor/       Hierarchy/Project drawers, window, config (Editor-only)
  Tests/        EditMode tests


DOCUMENTATION
-------------
For detailed information on banner syntax, presets, and configuration,
see the README.md file or visit the Chroma settings panel in Unity.


IF YOU FIND A BUG
-----------------
Please report it on contact@nekuzaky.com or https://www.nekuzaky.com/contact

================================================================================
