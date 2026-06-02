using System.Runtime.CompilerServices;

// Lets the EditMode test assembly reach internal parsing helpers
// (TryStripName, TryGetColor, TryGetPreviewColor) without making them public API.
[assembly: InternalsVisibleTo("Chroma.Editor.Tests")]
