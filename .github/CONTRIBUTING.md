# Contributing to Chroma

First off, thanks for taking the time to contribute! This document explains how to report issues, propose changes, and submit pull requests for Chroma.

## Code of Conduct

This project and everyone participating in it is governed by the [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold it.

## How Can I Contribute?

### Reporting Bugs

Before opening a bug report, please search [existing issues](https://github.com/Nekuzaky/Chroma/issues) to avoid duplicates. When filing a bug, use the bug report template and include:

- Your Unity version (e.g. 2021.3.40f1, Unity 6).
- The Chroma version or commit you are on.
- Clear steps to reproduce the problem.
- What you expected to happen versus what actually happened.
- Screenshots or a short clip if the issue is visual (Chroma is a Hierarchy tool, so this helps a lot).

### Suggesting Enhancements

Feature ideas are welcome. Open an issue using the feature request template and describe the problem you are trying to solve, not just the solution you have in mind. That context helps us find the best fit for the tool.

### Pull Requests

1. Fork the repository and create your branch from `develop` (this is the working branch; `main` is the distribution branch).
2. Name your branch descriptively, e.g. `feature/folder-presets` or `fix/banner-gradient-parse`.
3. Keep changes focused — one logical change per pull request.
4. Make sure the project still compiles in the Editor and that there are no new console warnings.
5. Update the [README](../README.md) and [CHANGELOG](../CHANGELOG.md) when your change is user-facing.
6. Open the pull request against `develop` and fill out the pull request template.

## Branching Model

- `main` — stable, distributable code. Releases are tagged here.
- `develop` — active development; target your pull requests here.

## Commit Messages

We loosely follow [Conventional Commits](https://www.conventionalcommits.org/). Prefixes such as `feat:`, `fix:`, `docs:`, `refactor:`, and `chore:` keep history readable and make changelog generation easier.

## Coding Style

- Chroma is Editor-only — keep runtime code free of any dependency on the tool.
- Match the existing C# conventions in the codebase (naming, spacing, file layout).
- Prefer clear, self-documenting code over comments where possible.

## Questions?

If you are unsure about anything, open a [discussion or issue](https://github.com/Nekuzaky/Chroma/issues), or reach out via [nekuzaky.com/contact](https://www.nekuzaky.com/contact). Thanks again for contributing!
