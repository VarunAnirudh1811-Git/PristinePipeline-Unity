using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Custom Inspector for FBXImportProfile ScriptableObject assets.
    ///
    /// Why this exists:
    ///   FBXImportPreset stores ModelImporterMeshCompression, ModelImporterNormals,
    ///   and ModelImporterTangents as plain ints so the Runtime assembly has no
    ///   UnityEditor dependency. Those fields are [HideInInspector] to prevent Unity
    ///   drawing them as raw number fields. This custom editor replaces them with
    ///   EnumPopup dropdowns so the asset Inspector remains fully usable.
    ///
    ///   All other fields are drawn via SerializedProperty so undo, prefab override
    ///   indicators, and multi-edit work correctly.
    ///
    /// v1.2 — Active Root refactor:
    ///   Folder path validation now rejects paths that START with "Assets/" — paths
    ///   must be relative to the Active Root (e.g. "Art/Materials", not
    ///   "Assets/Art/Materials").
    /// </summary>
    [CustomEditor(typeof(FBXImportProfile))]
    public class FBXImportProfileEditor : Editor
    {
        // ── SerializedProperty cache ─────────────────────────────────────────────

        private SerializedProperty _profileName;
        private SerializedProperty _description;
        private SerializedProperty _enforceNaming;
        private SerializedProperty _validPrefixes;
        private SerializedProperty _enableEmission;
        private SerializedProperty _enableAO;
        private SerializedProperty _defaultPreset;
        private SerializedProperty _rules;

        // Default preset children
        private SerializedProperty _dp_presetName;
        private SerializedProperty _dp_scaleFactor;
        private SerializedProperty _dp_readWrite;
        private SerializedProperty _dp_optimizeMesh;
        private SerializedProperty _dp_lightmapUVs;
        private SerializedProperty _dp_swapUVs;
        private SerializedProperty _dp_materialPrefix;
        private SerializedProperty _dp_materialsFolder;
        private SerializedProperty _dp_texturesFolder;
        private SerializedProperty _dp_prefabsFolder;
        private SerializedProperty _dp_generatePrefab;
        private SerializedProperty _dp_lightmapStatic;
        // int-backed enum fields — drawn manually as EnumPopup
        private SerializedProperty _dp_meshCompressionInt;
        private SerializedProperty _dp_normalsInt;
        private SerializedProperty _dp_tangentsInt;

        private bool _defaultPresetFoldout = true;

        private void OnEnable()
        {
            _profileName = serializedObject.FindProperty("profileName");
            _description = serializedObject.FindProperty("description");
            _enforceNaming = serializedObject.FindProperty("enforceNamingConvention");
            _validPrefixes = serializedObject.FindProperty("validPrefixes");
            _enableEmission = serializedObject.FindProperty("enableEmission");
            _enableAO = serializedObject.FindProperty("enableAmbientOcclusion");
            _defaultPreset = serializedObject.FindProperty("defaultPreset");
            _rules = serializedObject.FindProperty("rules");

            CacheDefaultPresetChildren();
        }

        private void CacheDefaultPresetChildren()
        {
            if (_defaultPreset == null) return;

            _dp_presetName = _defaultPreset.FindPropertyRelative("presetName");
            _dp_scaleFactor = _defaultPreset.FindPropertyRelative("scaleFactor");
            _dp_meshCompressionInt = _defaultPreset.FindPropertyRelative("meshCompressionInt");
            _dp_readWrite = _defaultPreset.FindPropertyRelative("readWriteEnabled");
            _dp_optimizeMesh = _defaultPreset.FindPropertyRelative("optimizeMesh");
            _dp_lightmapUVs = _defaultPreset.FindPropertyRelative("generateLightmapUVs");
            _dp_normalsInt = _defaultPreset.FindPropertyRelative("normalsInt");
            _dp_tangentsInt = _defaultPreset.FindPropertyRelative("tangentsInt");
            _dp_swapUVs = _defaultPreset.FindPropertyRelative("swapUVs");
            _dp_materialPrefix = _defaultPreset.FindPropertyRelative("materialPrefix");
            _dp_materialsFolder = _defaultPreset.FindPropertyRelative("materialsFolder");
            _dp_texturesFolder = _defaultPreset.FindPropertyRelative("texturesFolder");
            _dp_prefabsFolder = _defaultPreset.FindPropertyRelative("prefabsFolder");
            _dp_generatePrefab = _defaultPreset.FindPropertyRelative("generatePrefab");
            _dp_lightmapStatic = _defaultPreset.FindPropertyRelative("lightmapStatic");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── Identity ─────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_profileName, new GUIContent("Profile Name"));
            EditorGUILayout.PropertyField(_description, new GUIContent("Description"));
            EditorGUILayout.Space(6);

            // ── Naming convention ─────────────────────────────────────────────────
            EditorGUILayout.LabelField("Naming Convention", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enforceNaming,
                new GUIContent("Enforce",
                    "When on, FBX files that don't match a valid prefix are blocked at import."));
            EditorGUILayout.PropertyField(_validPrefixes, new GUIContent("Valid Prefixes"));
            EditorGUILayout.Space(6);

            // ── Texture channels ──────────────────────────────────────────────────
            EditorGUILayout.LabelField("Texture Channels", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableEmission,
                new GUIContent("Enable Emission",
                    "Search and assign _E / _Emissive textures to all materials."));
            EditorGUILayout.PropertyField(_enableAO,
                new GUIContent("Enable Ambient Occlusion",
                    "Search and assign _AO textures. No effect when an ORM map is found."));
            EditorGUILayout.Space(6);

            // ── Default preset ────────────────────────────────────────────────────
            _defaultPresetFoldout = EditorGUILayout.Foldout(
                _defaultPresetFoldout, "Default Preset", true, EditorStyles.foldoutHeader);

            if (_defaultPresetFoldout)
            {
                EditorGUILayout.HelpBox(
                    "Applied to any FBX that doesn't match a named rule. A warning is always logged.",
                    MessageType.None);
                EditorGUILayout.Space(2);
                DrawPresetProperties(
                    _dp_presetName, _dp_scaleFactor,
                    _dp_meshCompressionInt, _dp_readWrite, _dp_optimizeMesh,
                    _dp_lightmapUVs, _dp_normalsInt, _dp_tangentsInt, _dp_swapUVs,
                    _dp_materialPrefix, _dp_materialsFolder, _dp_texturesFolder,
                    _dp_prefabsFolder, _dp_generatePrefab, _dp_lightmapStatic);
            }

            EditorGUILayout.Space(6);

            // ── Rules ─────────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Rules are evaluated in order — last match wins.", MessageType.None);

            DrawRulesList();

            serializedObject.ApplyModifiedProperties();
        }

        // ── Shared preset property drawer ────────────────────────────────────────

        private static void DrawPresetProperties(
            SerializedProperty presetName,
            SerializedProperty scaleFactor,
            SerializedProperty meshCompressionInt,
            SerializedProperty readWrite,
            SerializedProperty optimizeMesh,
            SerializedProperty lightmapUVs,
            SerializedProperty normalsInt,
            SerializedProperty tangentsInt,
            SerializedProperty swapUVs,
            SerializedProperty materialPrefix,
            SerializedProperty materialsFolder,
            SerializedProperty texturesFolder,
            SerializedProperty prefabsFolder,
            SerializedProperty generatePrefab,
            SerializedProperty lightmapStatic)
        {
            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUILayout.PropertyField(presetName, new GUIContent("Preset Name"));

                EditorGUILayout.PropertyField(scaleFactor,
                    new GUIContent("Scale Factor",
                        "Uniform scale on import. Maya = 0.01, Blender = 1.0."));

                if (scaleFactor.floatValue <= 0f)
                    EditorGUILayout.HelpBox("Scale factor must be greater than zero.", MessageType.Error);

                meshCompressionInt.intValue = (int)(ModelImporterMeshCompression)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Mesh Compression",
                            "Reduces mesh size on disk and in memory."),
                        (ModelImporterMeshCompression)meshCompressionInt.intValue);

                EditorGUILayout.PropertyField(readWrite,
                    new GUIContent("Read/Write Enabled",
                        "Allows CPU-side mesh access at runtime."));

                EditorGUILayout.PropertyField(optimizeMesh,
                    new GUIContent("Optimize Mesh",
                        "Reorders vertices for GPU cache performance."));

                EditorGUILayout.PropertyField(lightmapUVs,
                    new GUIContent("Generate Lightmap UVs",
                        "Creates a second UV channel for lightmap baking."));

                normalsInt.intValue = (int)(ModelImporterNormals)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Normals",
                            "Import preserves artist intent. Calculate recomputes from geometry."),
                        (ModelImporterNormals)normalsInt.intValue);

                tangentsInt.intValue = (int)(ModelImporterTangents)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Tangents",
                            "Calculate Mikkt Space matches most bakers."),
                        (ModelImporterTangents)tangentsInt.intValue);

                EditorGUILayout.PropertyField(swapUVs,
                    new GUIContent("Swap UVs",
                        "Swaps UV channel 0 and 1."));

                // ── Material & Texture ───────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Material & Texture", EditorStyles.miniBoldLabel);

                EditorGUILayout.PropertyField(materialPrefix,
                    new GUIContent("Material Prefix",
                        "Prepended to created material names — e.g. M_ produces M_Rock."));

                EditorGUILayout.PropertyField(materialsFolder,
                    new GUIContent("Materials Folder",
                        "Path relative to Active Root — e.g. Art/Materials."));

                if (!string.IsNullOrWhiteSpace(materialsFolder.stringValue)
                    && materialsFolder.stringValue.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                    EditorGUILayout.HelpBox(
                        "Must be a relative path — do not start with \"Assets/\".",
                        MessageType.Warning);

                EditorGUILayout.PropertyField(texturesFolder,
                    new GUIContent("Textures Folder",
                        "Path relative to Active Root — e.g. Art/Textures."));

                if (!string.IsNullOrWhiteSpace(texturesFolder.stringValue)
                    && texturesFolder.stringValue.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                    EditorGUILayout.HelpBox(
                        "Must be a relative path — do not start with \"Assets/\".",
                        MessageType.Warning);

                // ── Prefab ───────────────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Prefab", EditorStyles.miniBoldLabel);

                EditorGUILayout.PropertyField(prefabsFolder,
                    new GUIContent("Prefabs Folder",
                        "Path relative to Active Root — e.g. Level/Prefabs."));

                if (!string.IsNullOrWhiteSpace(prefabsFolder.stringValue)
                    && prefabsFolder.stringValue.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                    EditorGUILayout.HelpBox(
                        "Must be a relative path — do not start with \"Assets/\".",
                        MessageType.Warning);

                EditorGUILayout.PropertyField(generatePrefab,
                    new GUIContent("Auto-Generate Prefab",
                        "When on, a prefab is generated automatically on import."));

                EditorGUILayout.PropertyField(lightmapStatic,
                    new GUIContent("Lightmap Static",
                        "Marks the generated prefab root as ContributeGI."));
            }
        }

        // ── Rules list ───────────────────────────────────────────────────────────

        private void DrawRulesList()
        {
            int ruleCount = _rules.arraySize;

            for (int i = 0; i < ruleCount; i++)
            {
                SerializedProperty rule = _rules.GetArrayElementAtIndex(i);
                SerializedProperty namePattern = rule.FindPropertyRelative("namePattern");
                SerializedProperty note = rule.FindPropertyRelative("note");
                SerializedProperty preset = rule.FindPropertyRelative("preset");

                SerializedProperty rp_presetName = preset.FindPropertyRelative("presetName");
                SerializedProperty rp_scaleFactor = preset.FindPropertyRelative("scaleFactor");
                SerializedProperty rp_meshCompressionInt = preset.FindPropertyRelative("meshCompressionInt");
                SerializedProperty rp_readWrite = preset.FindPropertyRelative("readWriteEnabled");
                SerializedProperty rp_optimizeMesh = preset.FindPropertyRelative("optimizeMesh");
                SerializedProperty rp_lightmapUVs = preset.FindPropertyRelative("generateLightmapUVs");
                SerializedProperty rp_normalsInt = preset.FindPropertyRelative("normalsInt");
                SerializedProperty rp_tangentsInt = preset.FindPropertyRelative("tangentsInt");
                SerializedProperty rp_swapUVs = preset.FindPropertyRelative("swapUVs");
                SerializedProperty rp_materialPrefix = preset.FindPropertyRelative("materialPrefix");
                SerializedProperty rp_materialsFolder = preset.FindPropertyRelative("materialsFolder");
                SerializedProperty rp_texturesFolder = preset.FindPropertyRelative("texturesFolder");
                SerializedProperty rp_prefabsFolder = preset.FindPropertyRelative("prefabsFolder");
                SerializedProperty rp_generatePrefab = preset.FindPropertyRelative("generatePrefab");
                SerializedProperty rp_lightmapStatic = preset.FindPropertyRelative("lightmapStatic");

                string foldoutLabel = string.IsNullOrWhiteSpace(namePattern.stringValue)
                    ? $"Rule {i + 1}"
                    : $"Rule {i + 1}  —  {namePattern.stringValue}";

                rule.isExpanded = EditorGUILayout.Foldout(rule.isExpanded, foldoutLabel, true);

                if (rule.isExpanded)
                {
                    using (new EditorGUI.IndentLevelScope(1))
                    {
                        EditorGUILayout.PropertyField(namePattern,
                            new GUIContent("Name Pattern", "Wildcard — e.g. SM_*, SK_*. Case-insensitive."));
                        EditorGUILayout.PropertyField(note, new GUIContent("Note"));
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField("Preset", EditorStyles.miniBoldLabel);

                        DrawPresetProperties(
                            rp_presetName, rp_scaleFactor,
                            rp_meshCompressionInt, rp_readWrite, rp_optimizeMesh,
                            rp_lightmapUVs, rp_normalsInt, rp_tangentsInt, rp_swapUVs,
                            rp_materialPrefix, rp_materialsFolder, rp_texturesFolder,
                            rp_prefabsFolder, rp_generatePrefab, rp_lightmapStatic);
                    }
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("Remove Rule", GUILayout.Width(100)))
                {
                    _rules.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                GUI.backgroundColor = prev;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
            }

            if (GUILayout.Button("+ Add Rule"))
            {
                _rules.InsertArrayElementAtIndex(ruleCount);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}