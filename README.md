# Chroma

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-100000?style=flat&logo=unity&logoColor=white)](https://unity.com/)
[![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=csharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Editor Only](https://img.shields.io/badge/Editor--only-zero%20runtime%20cost-brightgreen)](#requirements)

Editor-only Unity extension that color-codes your **Hierarchy** and **Project-window folders** to keep large scenes readable — with **zero runtime cost**.

## Table of Contents

- [Features](#features)
- [How to Use](#how-to-use)
- [Installation](#installation)
- [Requirements](#requirements)
- [Contributing](#contributing)
- [Found a Bug?](#found-a-bug)
- [Changelog](#changelog)
- [License](#license)

## Features

### Colored Banners
Create colored headers in the Hierarchy using two methods:

**By name** — Rename a GameObject with a spec like:
```
#1f6feb center bold=Title
```
- Color: hex codes or color names (red, blue, green, etc.)
- Gradient: `#color1>color2` (e.g., `#ff0000>0000ff`)
- Alignment: `left` / `center` / `right`
- Style: `bold` / `italic` / `bolditalic` / `normal`
- Size: `s12` (font size in pixels)
- Text color: `text:#ffffff` or `t:#fff`
- Text-only: `nobg` (no background, just colored text)

**By component** — Add a `ChromaBanner` component to keep the real GameObject name clean, with separate fields for color, gradient, text, alignment, and style.

### Separators
Create visual dividers by naming objects `---` or `___`:
- Solid, Dashed, Dotted, or Double line styles
- Optional centered caption: `--- My Section`

### Tree Guide Lines
File-explorer style connector lines in the Hierarchy indent gutter.

### Auto-Color Rules
Automatically tint rows by:
- **Tag** — Match by GameObject tag
- **Layer** — Match by Layer
- **Name prefix** — Match by name start (e.g., "Enemy_")
- **Regex** — Match by regex pattern (with ReDoS protection)

### Child Color Inheritance
Children inherit parent banner colors:
- **Flat** — constant opacity
- **DepthFade** — fades per nesting level

### Display Extras
- Child count: show `(N)` next to each GameObject
- Zebra striping: alternate row colors for readability
- Missing script warnings: warning icon on GameObjects with deleted scripts
- Bookmarks: mark, jump to, and reorder GameObjects

### Project Window Colors
Color folders in the Project window.

### RGB Mode
Animate Hierarchy rows and Project-window folders through rainbow colors (~30fps).

### Themes & Presets
Quick color schemes (Minimal, Vibrant, Soft, High-Contrast) and reusable banner presets.

### Custom Banner Font
Use a Font asset or any installed system font (Sans / Serif / Mono / Comic).

### Build Stripping
Banner specs and `ChromaBanner` components are automatically removed from built scenes.

## How to Use

### Open Chroma
Go to **Tools ▸ Chroma** to open the settings panel.

### Selection Tab
1. Select a GameObject in the Hierarchy
2. Choose a color and style
3. **Apply banner** — stores the spec in the GameObject name
4. **Add component** — adds a `ChromaBanner` component instead
5. **Apply title only** — renames a banner while keeping its colors

### Settings Tab
Configure:
- Display toggles (banners, separators, tree lines, etc.)
- Tree line color
- Separator style and colors
- Child color inheritance (flat or depth-fade)
- Auto-color rules (Tag, Layer, prefix, regex)
- RGB mode speed, saturation, brightness
- Folder colors in the Project window
- Themes and presets
- Build stripping options

### Quick Actions
- Right-click a GameObject ▸ **Chroma** — bookmark, copy/paste style, strip
- Right-click a folder ▸ **Chroma ▸ Folder Color** — color the folder in the Project window

## Installation

### Via Package Manager (recommended)
1. In Unity, go to **Window ▸ Package Manager**
2. Click **+ ▸ Add package from git URL…**
3. Paste: `https://github.com/Nekuzaky/Chroma.git?path=Assets/_/Code/Chroma`
4. Click **Add**

### Manual
Copy `Assets/_/Code/Chroma` into your project's `Assets` folder.

## Requirements

- Unity 2021.3 LTS or newer (works on Unity 6)
- Editor-only — no impact on runtime or builds

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](.github/CONTRIBUTING.md) for guidelines on reporting issues, proposing features, and submitting pull requests. By participating, you agree to abide by our [Code of Conduct](.github/CODE_OF_CONDUCT.md).

## Found a Bug?

Open an [issue](https://github.com/Nekuzaky/Chroma/issues) or reach out:
- **Email**: contact@nekuzaky.com
- **Website**: https://www.nekuzaky.com/contact

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

## License

MIT License — see [LICENSE](LICENSE) file for details.

You are free to use, modify, and distribute this software for any purpose.
