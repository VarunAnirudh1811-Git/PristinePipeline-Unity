using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Inline create/edit UI for FBXImportProfile assets.
    /// Rendered by FBXImporterTab when in create or edit mode — not a separate window.
    /// Owns no persistent state beyond what the user is currently editing.
    /// 
    /// Includes full texture naming patterns UI matching FBXImportProfileEditor.
    /// </summary>
    public class FBXImportProfileCreatorTab
    {
        // ── State ────────────────────────────────────────────────────────────────

        private string _profileName = "";
        private string _description = "";
        private bool _enforceNaming = false;
        private bool _enableEmission = false;
        private bool _enableAO = true;
        private List<string> _validPrefixes = new();
        private FBXImportPreset _defaultPreset;
        private List<FBXImportRule> _rules = new();

        // Reorderable lists
        private ReorderableList _prefixReorderable;
        private ReorderableList _rulesReorderable;

        // UI state
        private bool _isEditMode = false;
        private FBXImportProfile _editTarget = null;
        private bool _isDirty = false;
        private bool _defaultPresetFoldout = true;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Opens the creator in new-profile mode.</summary>
        public void BeginCreate()
        {
            _isEditMode = false;
            _editTarget = null;
            _profileName = "";
            _description = "";
            _enforceNaming = false;
            _enableEmission = false;
            _enableAO = true;
            _validPrefixes = new List<string> { "SM_", "SK_", "P_" };
            _defaultPreset = new FBXImportPreset { presetName = "Default" };
            _rules = new List<FBXImportRule>();
            _isDirty = false;
            _defaultPresetFoldout = true;
            BuildReorderableLists();
        }

        /// <summary>Opens the creator loaded with an existing profile for editing.</summary>
        public void BeginEdit(FBXImportProfile profile)
        {
            _isEditMode = true;
            _editTarget = profile;
            _profileName = profile.profileName;
            _description = profile.description;
            _enforceNaming = profile.enforceNamingConvention;
            _enableEmission = profile.enableEmission;
            _enableAO = profile.enableAmbientOcclusion;
            _validPrefixes = new List<string>(profile.validPrefixes);
            _defaultPreset = DeepClonePreset(profile.defaultPreset);
            _rules = new List<FBXImportRule>(profile.Rules);
            _isDirty = false;
            _defaultPresetFoldout = true;
            BuildReorderableLists();
        }

        /// <summary>
        /// Draws the creator UI. Returns true when the user confirms a save,
        /// signalling FBXImporterTab to return to list mode and refresh.
        /// Returns false when the user presses Back (with or without a discard confirmation).
        /// The out parameter carries the saved profile on success, or null on back/cancel.
        /// </summary>
        public bool Draw(out FBXImportProfile savedProfile, out bool wantsBack)
        {
            savedProfile = null;

            DrawCreatorHeader(out wantsBack);
            if (wantsBack) return false;

            PristinePipelineWindow.DrawDivider();

            DrawNameAndDescription();
            PristinePipelineWindow.DrawDivider();

            DrawNamingConventionSettings();
            PristinePipelineWindow.DrawDivider();

            DrawProfileLevelToggles();
            PristinePipelineWindow.DrawDivider();

            DrawDefaultPreset();
            PristinePipelineWindow.DrawDivider();

            DrawRulesList();
            EditorGUILayout.Space(10);

            PristinePipelineWindow.DrawDivider();

            if (DrawSaveButton(out savedProfile))
                return true;

            EditorGUILayout.Space(8);
            return false;
        }

        // ── Header ───────────────────────────────────────────────────────────────

        private void DrawCreatorHeader(out bool wantsBack)
        {
            wantsBack = false;

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← Back", GUILayout.Width(70)))
                {
                    if (_isDirty)
                    {
                        bool discard = EditorUtility.DisplayDialog(
                            "Unsaved Changes",
                            "You have unsaved changes. Go back and discard them?",
                            "Discard", "Keep Editing");

                        if (discard)
                            wantsBack = true;
                    }
                    else
                    {
                        wantsBack = true;
                    }
                }

                GUIStyle headingStyle = new(EditorStyles.boldLabel) { fontSize = 13 };
                string heading = _isEditMode ? $"Edit  —  {_editTarget.profileName}" : "New Profile";
                EditorGUILayout.LabelField(heading, headingStyle);
            }

            EditorGUILayout.Space(6);
        }

        // ── Name and description ─────────────────────────────────────────────────

        private void DrawNameAndDescription()
        {
            EditorGUI.BeginChangeCheck();

            _profileName = EditorGUILayout.TextField(
                new GUIContent("Profile Name", "Used as the asset filename."),
                _profileName);

            if (string.IsNullOrWhiteSpace(_profileName))
                EditorGUILayout.HelpBox("Profile name cannot be empty.", MessageType.Error);

            _description = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the profile selector."),
                _description);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

        // ── Naming convention settings ───────────────────────────────────────────

        private void DrawNamingConventionSettings()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Naming Convention", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();

            _enforceNaming = EditorGUILayout.Toggle(
                new GUIContent("Enforce",
                    "When on, FBX files that don't match a valid prefix are blocked at import. " +
                    "When off, a warning is logged but the import continues."),
                _enforceNaming);

            if (EditorGUI.EndChangeCheck()) _isDirty = true;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Valid Prefixes", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2);
            _prefixReorderable?.DoLayoutList();
        }

        // ── Profile level toggles ────────────────────────────────────────────────

        private void DrawProfileLevelToggles()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Texture Channels", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These toggles apply to all presets in this profile.", MessageType.None);
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();

            _enableEmission = EditorGUILayout.Toggle(
                new GUIContent("Enable Emission",
                    "Search and assign _E / _Emissive textures. " +
                    "Also enables the Emission keyword on created materials."),
                _enableEmission);

            _enableAO = EditorGUILayout.Toggle(
                new GUIContent("Enable Ambient Occlusion",
                    "Search and assign _AO textures to the Occlusion Map slot. " +
                    "Has no effect when an ORM texture is found."),
                _enableAO);

            if (EditorGUI.EndChangeCheck()) _isDirty = true;
        }

        // ── Default preset ───────────────────────────────────────────────────────

        private void DrawDefaultPreset()
        {
            EditorGUILayout.Space(4);

            _defaultPresetFoldout = EditorGUILayout.Foldout(
                _defaultPresetFoldout, "Default Preset", true, EditorStyles.foldoutHeader);

            if (!_defaultPresetFoldout) return;

            EditorGUILayout.HelpBox(
                "Applied to any FBX that doesn't match a named rule. A warning is always logged.",
                MessageType.None);
            EditorGUILayout.Space(2);

            DrawPresetFields(_defaultPreset, isDefaultPreset: true);
        }

        // ── Preset fields (shared between default preset and rule presets) ────────

        private void DrawPresetFields(FBXImportPreset preset, bool isDefaultPreset = false)
        {
            if (preset == null) return;

            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.IndentLevelScope(1))
            {
                // Basic settings
                if (!isDefaultPreset)
                {
                    preset.presetName = EditorGUILayout.TextField(
                        new GUIContent("Preset Name", "Display name for this preset."),
                        preset.presetName);
                }

                preset.scaleFactor = EditorGUILayout.FloatField(
                    new GUIContent("Scale Factor", "Uniform scale on import. Maya = 0.01, Blender = 1.0."),
                    preset.scaleFactor);

                if (preset.scaleFactor <= 0f)
                    EditorGUILayout.HelpBox("Scale factor must be greater than zero.", MessageType.Error);

                preset.SetMeshCompression(
                    (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(
                        new GUIContent("Mesh Compression", "Reduces mesh size. Off preserves precision."),
                        preset.MeshCompression()));

                preset.readWriteEnabled = EditorGUILayout.Toggle(
                    new GUIContent("Read/Write Enabled", "Allows CPU-side mesh access at runtime."),
                    preset.readWriteEnabled);

                preset.optimizeMesh = EditorGUILayout.Toggle(
                    new GUIContent("Optimize Mesh", "Reorders vertices for GPU cache performance."),
                    preset.optimizeMesh);

                preset.generateLightmapUVs = EditorGUILayout.Toggle(
                    new GUIContent("Generate Lightmap UVs", "Creates a second UV channel for lightmap baking."),
                    preset.generateLightmapUVs);

                preset.SetNormals(
                    (ModelImporterNormals)EditorGUILayout.EnumPopup(
                        new GUIContent("Normals", "Import preserves artist intent. Calculate recomputes from geometry."),
                        preset.Normals()));

                preset.SetTangents(
                    (ModelImporterTangents)EditorGUILayout.EnumPopup(
                        new GUIContent("Tangents", "Calculate Mikkt Space matches most bakers."),
                        preset.Tangents()));

                preset.swapUVs = EditorGUILayout.Toggle(
                    new GUIContent("Swap UVs", "Swaps UV channel 0 and 1."),
                    preset.swapUVs);

                // Material & Texture section
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Material & Texture", EditorStyles.miniBoldLabel);

                preset.materialPrefix = EditorGUILayout.TextField(
                    new GUIContent("Material Prefix", "Prepended to created material names."),
                    preset.materialPrefix);

                preset.materialsFolder = EditorGUILayout.TextField(
                    new GUIContent("Materials Folder", "Path relative to Active Root — e.g. Art/Materials."),
                    preset.materialsFolder);

                ValidateRelativePath(preset.materialsFolder, "Materials Folder");

                preset.texturesFolder = EditorGUILayout.TextField(
                    new GUIContent("Textures Folder", "Path relative to Active Root — e.g. Art/Textures."),
                    preset.texturesFolder);

                ValidateRelativePath(preset.texturesFolder, "Textures Folder");

                // Texture Naming Patterns
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Texture Naming Patterns", EditorStyles.miniBoldLabel);

                DrawPatternList(preset.baseColorPatterns, "Base Color Patterns", "T_*_B", "T_*_BC");
                DrawPatternList(preset.normalPatterns, "Normal Patterns", "T_*_N");
                DrawPatternList(preset.metallicPatterns, "Metallic Patterns", "T_*_MS", "T_*_M");
                DrawPatternList(preset.ormPatterns, "ORM Patterns", "T_*_ORM");
                DrawPatternList(preset.emissivePatterns, "Emissive Patterns", "T_*_E");
                DrawPatternList(preset.aoPatterns, "AO Patterns", "T_*_AO");

                // Prefab section
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Prefab", EditorStyles.miniBoldLabel);

                preset.prefabsFolder = EditorGUILayout.TextField(
                    new GUIContent("Prefabs Folder", "Path relative to Active Root — e.g. Level/Prefabs."),
                    preset.prefabsFolder);

                ValidateRelativePath(preset.prefabsFolder, "Prefabs Folder");

                preset.generatePrefab = EditorGUILayout.Toggle(
                    new GUIContent("Auto-Generate Prefab",
                        "When on, a prefab is generated automatically on import."),
                    preset.generatePrefab);

                preset.lightmapStatic = EditorGUILayout.Toggle(
                    new GUIContent("Lightmap Static", "Marks the generated prefab root as ContributeGI."),
                    preset.lightmapStatic);
            }

            if (EditorGUI.EndChangeCheck()) _isDirty = true;
        }

        // ── Pattern list helper ──────────────────────────────────────────────────

        private void DrawPatternList(List<string> patterns, string label, params string[] defaults)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            // Simple list display - could be enhanced with ReorderableList
            for (int i = 0; i < patterns.Count; i++)
            {
                EditorGUI.BeginChangeCheck();
                string newValue = EditorGUILayout.TextField(patterns[i]);
                if (EditorGUI.EndChangeCheck())
                {
                    patterns[i] = newValue;
                    _isDirty = true;
                }
            }

            // Add button
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Add Pattern", GUILayout.Width(100)))
                {
                    patterns.Add("*");
                    _isDirty = true;
                }

                // Restore defaults button (only show if list is empty)
                if (patterns.Count == 0 && defaults.Length > 0)
                {
                    if (GUILayout.Button("Restore Defaults", GUILayout.Width(100)))
                    {
                        patterns.Clear();
                        patterns.AddRange(defaults);
                        _isDirty = true;
                    }
                }
            }

            // Warning if empty
            if (patterns.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"At least one {label} pattern is required. Click 'Restore Defaults' to add default patterns.",
                    MessageType.Warning);
            }
        }

        // ── Rules list ───────────────────────────────────────────────────────────

        private void DrawRulesList()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Rules are evaluated in order — last match wins. " +
                "Drag to reorder. Each rule maps a name pattern to a set of import settings.",
                MessageType.None);
            EditorGUILayout.Space(2);
            _rulesReorderable?.DoLayoutList();
        }

        private void BuildReorderableLists()
        {
            BuildPrefixList();
            BuildRulesListInternal();
        }

        private void BuildPrefixList()
        {
            _prefixReorderable = new ReorderableList(
                _validPrefixes, typeof(string),
                draggable: true, displayHeader: false,
                displayAddButton: true, displayRemoveButton: true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= _validPrefixes.Count) return;
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.BeginChangeCheck();
                    _validPrefixes[index] = EditorGUI.TextField(rect, _validPrefixes[index]);
                    if (EditorGUI.EndChangeCheck()) _isDirty = true;
                },
                onAddCallback = _ => { _validPrefixes.Add(""); _isDirty = true; },
                onRemoveCallback = _ =>
                {
                    _validPrefixes.RemoveAt(_prefixReorderable.index);
                    _isDirty = true;
                }
            };
        }

        private void BuildRulesListInternal()
        {
            const float lineH = 18f;
            const float spacing = 20f;
            //const int FixedRows = 14; // Increased for pattern lists

            _rulesReorderable = new ReorderableList(
                _rules, typeof(FBXImportRule),
                draggable: true, displayHeader: true,
                displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Pattern → Preset Settings (with Texture Patterns)",
                        EditorStyles.miniBoldLabel),

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= _rules.Count) return;

                    FBXImportRule rule = _rules[index];
                    rule.preset ??= new FBXImportPreset();

                    float y = rect.y + 2f;
                    float w = rect.width;

                    EditorGUI.BeginChangeCheck();

                    // Row 0: pattern + note
                    EditorGUI.LabelField(new Rect(rect.x, y, 88f, lineH), "Pattern");
                    rule.namePattern = EditorGUI.TextField(
                        new Rect(rect.x + 90f, y, w * 0.4f, lineH), rule.namePattern);
                    EditorGUI.LabelField(new Rect(rect.x + w * 0.4f + 94f, y, 34f, lineH), "Note");
                    rule.note = EditorGUI.TextField(
                        new Rect(rect.x + w * 0.4f + 130f, y, w - w * 0.4f - 130f, lineH), rule.note);
                    y += spacing;

                    // Draw full preset fields (reusing the method)
                    // This is simplified - for a full implementation, you'd draw each field manually
                    // similar to FBXImporterTab's original implementation
                    DrawCompactPresetFields(rule.preset, ref y, rect, w, lineH, spacing);

                    if (EditorGUI.EndChangeCheck()) _isDirty = true;

                    var messages = FBXImporterUtility.ValidateRule(rule);
                    foreach (string msg in messages)
                    {
                        EditorGUI.HelpBox(
                            new Rect(rect.x, y, w, lineH + 4), msg, MessageType.Warning);
                        y += lineH + 6f;
                    }
                },

                elementHeightCallback = index =>
                {
                    // Calculate height based on number of fields in preset
                    float height = 14 * spacing + 8f; // Approximate for all preset fields
                    if (index >= 0 && index < _rules.Count)
                    {
                        var messages = FBXImporterUtility.ValidateRule(_rules[index]);
                        height += messages.Count * (lineH + 6f);
                    }
                    return height;
                },

                onAddCallback = _ =>
                {
                    _rules.Add(new FBXImportRule
                    {
                        preset = new FBXImportPreset { presetName = "New Preset" }
                    });
                    _isDirty = true;
                },

                onRemoveCallback = _ =>
                {
                    _rules.RemoveAt(_rulesReorderable.index);
                    _isDirty = true;
                }
            };
        }

        private void DrawCompactPresetFields(FBXImportPreset preset, ref float y, Rect rect, float w, float lineH, float spacing)
        {
            // Preset name
            EditorGUI.LabelField(new Rect(rect.x, y, 88f, lineH), "Preset Name");
            preset.presetName = EditorGUI.TextField(
                new Rect(rect.x + 90f, y, w - 92f, lineH), preset.presetName);
            y += spacing;

            // Scale + Compression
            EditorGUI.LabelField(new Rect(rect.x, y, 48f, lineH), "Scale");
            preset.scaleFactor = EditorGUI.FloatField(
                new Rect(rect.x + 50f, y, 50f, lineH), preset.scaleFactor);
            EditorGUI.LabelField(new Rect(rect.x + 112f, y, 88f, lineH), "Compression");
            preset.SetMeshCompression(
                (ModelImporterMeshCompression)EditorGUI.EnumPopup(
                    new Rect(rect.x + 202f, y, w - 204f, lineH),
                    preset.MeshCompression()));
            y += spacing;

            // Toggles
            preset.readWriteEnabled = EditorGUI.ToggleLeft(
                new Rect(rect.x, y, 110f, lineH), "Read/Write", preset.readWriteEnabled);
            preset.optimizeMesh = EditorGUI.ToggleLeft(
                new Rect(rect.x + 114f, y, 120f, lineH), "Optimize Mesh", preset.optimizeMesh);
            preset.generateLightmapUVs = EditorGUI.ToggleLeft(
                new Rect(rect.x + 238f, y, w - 240f, lineH), "Lightmap UVs", preset.generateLightmapUVs);
            y += spacing;

            // Normals + Tangents
            EditorGUI.LabelField(new Rect(rect.x, y, 56f, lineH), "Normals");
            preset.SetNormals(
                (ModelImporterNormals)EditorGUI.EnumPopup(
                    new Rect(rect.x + 58f, y, 130f, lineH), preset.Normals()));
            EditorGUI.LabelField(new Rect(rect.x + 198f, y, 58f, lineH), "Tangents");
            preset.SetTangents(
                (ModelImporterTangents)EditorGUI.EnumPopup(
                    new Rect(rect.x + 258f, y, w - 260f, lineH), preset.Tangents()));
            y += spacing;

            // Swap UVs
            preset.swapUVs = EditorGUI.ToggleLeft(
                new Rect(rect.x, y, 130f, lineH), "Swap UVs", preset.swapUVs);
            y += spacing;

            // Material Prefix
            EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Mat Prefix");
            preset.materialPrefix = EditorGUI.TextField(
                new Rect(rect.x + 102f, y, w - 104f, lineH), preset.materialPrefix);
            y += spacing;

            // Materials Folder
            EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Materials Folder");
            preset.materialsFolder = EditorGUI.TextField(
                new Rect(rect.x + 102f, y, w - 104f, lineH), preset.materialsFolder);
            y += spacing;

            // Textures Folder
            EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Textures Folder");
            preset.texturesFolder = EditorGUI.TextField(
                new Rect(rect.x + 102f, y, w - 104f, lineH), preset.texturesFolder);
            y += spacing;

            // Texture Patterns headers (simplified - just show count)
            EditorGUI.LabelField(new Rect(rect.x, y, w, lineH),
                $"Patterns: BC:{preset.baseColorPatterns?.Count ?? 0} " +
                $"N:{preset.normalPatterns?.Count ?? 0} " +
                $"M:{preset.metallicPatterns?.Count ?? 0} " +
                $"ORM:{preset.ormPatterns?.Count ?? 0} " +
                $"E:{preset.emissivePatterns?.Count ?? 0} " +
                $"AO:{preset.aoPatterns?.Count ?? 0}",
                EditorStyles.miniLabel);
            y += spacing;

            // Prefabs Folder
            EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Prefabs Folder");
            preset.prefabsFolder = EditorGUI.TextField(
                new Rect(rect.x + 102f, y, w - 104f, lineH), preset.prefabsFolder);
            y += spacing;

            // Generate Prefab + Lightmap Static
            preset.generatePrefab = EditorGUI.ToggleLeft(
                new Rect(rect.x, y, 160f, lineH), "Auto-Generate Prefab", preset.generatePrefab);
            preset.lightmapStatic = EditorGUI.ToggleLeft(
                new Rect(rect.x + 164f, y, w - 166f, lineH), "Lightmap Static", preset.lightmapStatic);
            y += spacing;
        }

        // ── Validation helpers ───────────────────────────────────────────────────

        private void ValidateRelativePath(string path, string fieldName)
        {
            if (!string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/"))
            {
                EditorGUILayout.HelpBox(
                    $"{fieldName} must be a relative path — do not start with \"Assets/\".",
                    MessageType.Warning);
            }
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        private bool DrawSaveButton(out FBXImportProfile savedProfile)
        {
            savedProfile = null;

            if (GUILayout.Button("Save Profile", GUILayout.Height(30)))
            {
                if (!ValidateBeforeSave())
                    return false;

                FBXImportProfile profile = BuildOrUpdateProfile();
                FBXImporterUtility.SaveProfile(profile);
                savedProfile = profile;
                _isDirty = false;
                return true;
            }

            return false;
        }

        private bool ValidateBeforeSave()
        {
            if (string.IsNullOrWhiteSpace(_profileName))
            {
                EditorUtility.DisplayDialog("Cannot Save", "Profile name cannot be empty.", "OK");
                return false;
            }

            if (_defaultPreset.scaleFactor <= 0f)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Save",
                    "Default preset scale factor must be greater than zero.", "OK");
                return false;
            }

            // Validate texture patterns are not empty
            if (_defaultPreset.baseColorPatterns.Count == 0 ||
                _defaultPreset.normalPatterns.Count == 0 ||
                _defaultPreset.metallicPatterns.Count == 0 ||
                _defaultPreset.ormPatterns.Count == 0 ||
                _defaultPreset.emissivePatterns.Count == 0 ||
                _defaultPreset.aoPatterns.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Save",
                    "All texture pattern lists must have at least one pattern. Check the Default Preset.",
                    "OK");
                return false;
            }

            // Conflict check for new profiles only
            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.FBX_ProfileSavePath,
                    _profileName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<FBXImportProfile>(assetPath) != null)
                {
                    EditorUtility.DisplayDialog(
                        "Duplicate Profile",
                        $"A profile named '{_profileName}' already exists. Choose a different name.",
                        "OK");
                    return false;
                }
            }

            return true;
        }

        private FBXImportProfile BuildOrUpdateProfile()
        {
            if (_isEditMode && _editTarget != null)
            {
                _editTarget.profileName = _profileName.Trim();
                _editTarget.description = _description.Trim();
                _editTarget.enforceNamingConvention = _enforceNaming;
                _editTarget.enableEmission = _enableEmission;
                _editTarget.enableAmbientOcclusion = _enableAO;
                _editTarget.validPrefixes = new List<string>(_validPrefixes);
                _editTarget.defaultPreset = _defaultPreset;
                _editTarget.SetRules(_rules);
                return _editTarget;
            }

            var p = ScriptableObject.CreateInstance<FBXImportProfile>();
            p.profileName = _profileName.Trim();
            p.description = _description.Trim();
            p.enforceNamingConvention = _enforceNaming;
            p.enableEmission = _enableEmission;
            p.enableAmbientOcclusion = _enableAO;
            p.validPrefixes = new List<string>(_validPrefixes);
            p.defaultPreset = _defaultPreset;
            p.SetRules(_rules);
            return p;
        }

        private FBXImportPreset DeepClonePreset(FBXImportPreset source)
        {
            if (source == null) return new FBXImportPreset { presetName = "Default" };
            return JsonUtility.FromJson<FBXImportPreset>(JsonUtility.ToJson(source));
        }
    }
}