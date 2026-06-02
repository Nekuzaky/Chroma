# Chroma

Chroma is an **Editor-only** Unity extension that color-codes your **Hierarchy** (and Project-window
folders) so large scenes stay readable — colored banners, gradients, separators, guide lines,
auto-coloring rules and more, with **zero runtime cost**.

## Features

- **Banners** — turn any row into a colored header, two ways:
  - **By name**: rename a GameObject like `#1f6feb center bold=Title` — solid color or `a>b` gradient,
    alignment, style, size, text color, or `nobg` for a text-only label.
  - **By component**: add a `ChromaBanner` component (separate Background / Font / Title fields) so the
    GameObject keeps its real name.
- **Separators** — name an object `--- Label` (solid / dashed / dotted / double line styles).
- **Tree guide lines** in the indent gutter.
- **Auto-color rules** — tint rows by Tag, Layer, name prefix or regex.
- **Child-color inheritance** (flat or depth-fade).
- **Child count `(N)`**, **zebra striping**, and **bookmarks** (jump & reorder).
- **Animated RGB mode** for hierarchy rows and Project-window folders.
- **Project-window folder colors**.
- **Themes** and reusable **banner presets**.
- **Build stripping** — banner name specs and `ChromaBanner` components are removed from built scenes,
  so there is **zero runtime footprint**.
- **EditMode tests** for the name parser.

## Requirements

- Unity 2021.3 LTS or newer (developed on Unity 6). Editor-only — works in any project regardless of
  the render pipeline.

## Installation

Copy the `Assets/_/Codes/Chroma` folder into your project's `Assets`. It is self-contained, with its own
assembly definitions (`Chroma.Runtime`, `Chroma.Editor`).

## Usage

- Open the panel via **Tools ▸ Chroma**.
- **Selection** tab: choose colors/style with a live preview, then **Apply banner** (store it in the
  name) or **Add component**. **Apply title only** renames a banner while keeping its colors.
- **Settings** tab: display toggles, tree lines, separators, inheritance, auto-color rules, RGB mode,
  folder colors, themes, presets and build options.
- Right-click a GameObject ▸ **Chroma** for quick actions (bookmark, copy/paste style, strip).
- Right-click a folder ▸ **Chroma ▸ Folder Color** to color it in the Project window.

## Project structure

```
Assets/_/Codes/Chroma/
  Runtime/   # ChromaBanner component        (Chroma.Runtime asmdef, all platforms)
  Editor/    # Hierarchy & Project drawers, window, config (Chroma.Editor asmdef, Editor-only)
  Tests/     # EditMode tests                 (Chroma.Editor.Tests asmdef)
```

## License

No license file yet — all rights reserved by the repository owner until one is added.
