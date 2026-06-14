# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-06-11

### Added
- **Convention Linter** (flagship): team lint rules in the shared config — scope (All / RootOnly / Tag / Layer / NamePrefix / Regex) + assertion (`HasBanner`, `NameRegex`, `NoEmpty`, `NoMissingScript`, `RequiredParent`, `MaxDepth`, `NoDefaultName`) + severity + message. Violations are flagged inline in the Hierarchy with severity icons and tooltips.
- **Lint tab** in the Chroma window: live violation counts in the tab label, grouped violation list with jump / select-all / ignore, full rule editor, and ready-made starter & strict rulesets.
- **Per-user lint ignores** (`GameObject ▸ Chroma ▸ Lint - Toggle Ignore`) and a bindable `Chroma/Next Lint Violation` shortcut.
- **Row widgets**: always-visible active toggle (click = `SetActive` with Undo) and per-row component icons (cached, configurable cap).
- **Colorblind-safe themes**: Okabe-Ito and IBM palettes (deuteranopia / protanopia friendly) next to the existing themes.
- **10 extended theme palettes**: Synthwave, Neon Noir, Ocean, Autumn, Ember, Solar, Teal Mono, Violet Mono, Warm Gray, Jewel Box (16 themes total). All tuned so white banner text stays legible.
- **Theme palette previews**: each theme in Settings ▸ Themes now shows a live swatch strip of its colors before you apply it.
- **Animated chromatic window header**: scrolling rainbow divider, hue-drifting header, spectrum-distributed section-card accents and active-tab tint — Chroma's RGB signature applied to its own UI.
- EditMode test suite for the linter's rule evaluation (`ChromaLintTests`).

### Changed
- Config schema version is now tracked separately from the edit counter (`m_schemaVersion`), making future data migrations reliable.
- Imported configs validate and clamp the new lint and row-widget settings.

### Fixed
- Missing-script warning icon now actually shows its tooltip.
- Component-related caches refresh when components are added or removed (not only on hierarchy changes).
- README no longer contains a duplicated copy of itself.

## [0.1.0] - 2026-06-04

### Added
- **Auto-creation of ChromaConfig**: The ChromaConfig asset is now automatically created in `Assets/Chroma/` when the project loads, eliminating the need to open Tools > Chroma first.
- **Team-friendly storage**: Config file is stored in Git-friendly YAML format for seamless collaboration — all team members get the same Chroma settings.

[Unreleased]: https://github.com/Nekuzaky/Chroma/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/Nekuzaky/Chroma/compare/v0.1.0...v0.3.0
[0.1.0]: https://github.com/Nekuzaky/Chroma/releases/tag/v0.1.0
