using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
/// <summary>
/// Automatically creates the ChromaConfig asset when the project loads,
/// ensuring it exists in Assets/Chroma before any Chroma functionality runs.
/// </summary>
[InitializeOnLoad]
public static class ChromaInitializer
{
    static ChromaInitializer()
    {
        // Create or load the ChromaConfig asset on domain load (editor startup, script recompile, etc.)
        ChromaConfig.GetOrCreate();
    }
}
}
