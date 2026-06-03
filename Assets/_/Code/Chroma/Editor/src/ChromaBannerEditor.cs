using UnityEditor;
using UnityEngine;

namespace Chroma.Editor
{
// Grouped Inspector for ChromaBanner: Background (toggle gates the colors), Font, Title.
// Edits flow through OnValidate -> ChromaBanner.Changed, so the Hierarchy refreshes live.
[CustomEditor(typeof(ChromaBanner))]
[CanEditMultipleObjects]
public class ChromaBannerEditor : UnityEditor.Editor
{
    #region Unity API

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Background", EditorStyles.boldLabel);
        SerializedProperty bg = serializedObject.FindProperty("m_background");
        EditorGUILayout.PropertyField(bg, new GUIContent("Enabled"));
        using (new EditorGUI.DisabledScope(!bg.boolValue))
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_color"), new GUIContent("Color"));
            SerializedProperty grad = serializedObject.FindProperty("m_gradient");
            EditorGUILayout.PropertyField(grad, new GUIContent("Gradient"));
            using (new EditorGUI.DisabledScope(!grad.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_color2"), new GUIContent("Color 2"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_vertical"), new GUIContent("Vertical"));
            }
            EditorGUI.indentLevel--;
        }
        if (!bg.boolValue)
            EditorGUILayout.LabelField("Text-only label (no colored block).", EditorStyles.miniLabel);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Font", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_textColor"), new GUIContent("Text color"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_align"), new GUIContent("Alignment"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_fontStyle"), new GUIContent("Style"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_fontSize"), new GUIContent("Size (0 = default)"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Title", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_title"),
            new GUIContent("Title", "Empty = use the GameObject's name."));

        serializedObject.ApplyModifiedProperties();
    }

    #endregion
}
}
