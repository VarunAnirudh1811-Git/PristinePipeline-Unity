using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Draws the FBX Importer tab inside PristinePipelineWindow.
    /// Owns the tab's UI state and mode transitions (list ↔ create/edit).
    /// All profile operations and import logic are delegated to FBXImporterUtility.
    /// </summary>
    public class FBXImporterTab
    {
        // ── Mode ─────────────────────────────────────────────────────────────────

        private enum Mode { List, CreateEdit }
        private Mode _mode = Mode.List;

        // ── List mode state ──────────────────────────────────────────────────────

        private List<FBXImportProfile> _profiles = new();
        private string[] _profileNames = new string[0];
        private int _selectedIndex = 0;

        // ── Create / Edit state ──────────────────────────────────────────────────

        private FBXImportProfile _editTarget = null;
        private bool _isEditMode = false;
        private string _editName = "";
        private string _editDescription = "";
        private bool _editEnforce = false;
        private List<string> _editPrefixes = new();
        private FBXImportPreset _editDefaultPreset;
        private List<FBXImportRule> _editRules = new();
        private ReorderableList _rulesReorderable;
        private ReorderableList _prefixReorderable;
        private bool _isDirty = false;

        // Foldout states for default preset inspector
        private bool _defaultPresetFoldout = true;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void OnEnable()
        {
            RefreshProfiles();
        }

        public void OnDisable() { }

        // ── Entry point ──────────────────────────────────────────────────────────

        public void Draw(EditorWindow parentWindow)
        {
            switch (_mode)
            {
                case Mode.List: DrawListMode(parentWindow); break;
                case Mode.CreateEdit: DrawCreateEditMode(parentWindow); break;
            }
        }

        // ── List mode ────────────────────────────────────────────────────────────

        private void DrawListMode(EditorWindow parentWindow)
        {
            DrawEnableToggle();
            PristinePipelineWindow.DrawDivider();
            DrawProfileSelector(parentWindow);
            PristinePipelineWindow.DrawDivider();
            DrawActiveProfileSummary();
            PristinePipelineWindow.DrawDivider();
            DrawManualReprocess();
        }

        // ── Enable toggle ────────────────────────────────────────────────────────

        private void DrawEnableToggle()
        {
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("FBX Importer", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                bool enabled = ToolSettings.FBX_Enabled;
                bool updated = EditorGUILayout.Toggle(enabled, GUILayout.Width(20));

                if (updated != enabled)
                {
                    ToolSettings.FBX_Enabled = updated;
                    Debug.Log(
                        $"{ToolInfo.LogPrefix} FBX Importer " +
                        $"{(updated ? "enabled" : "disabled")}.");
                }

                EditorGUILayout.LabelField(
                    ToolSettings.FBX_Enabled ? "Enabled" : "Disabled",
                    GUILayout.Width(56));
            }

            EditorGUILayout.HelpBox(
                ToolSettings.FBX_Enabled
                    ? "FBX Importer is active. Import settings will be applied automatically."
                    : "FBX Importer is disabled. Enable it to apply import settings automatically.",
                ToolSettings.FBX_Enabled ? MessageType.Info : MessageType.None);

            EditorGUILayout.Space(4);
        }

        // ── Profile selector ─────────────────────────────────────────────────────

        private void DrawProfileSelector(EditorWindow parentWindow)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_profiles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No profiles found. Create one below or check the profile save path in Settings.",
                    MessageType.Warning);

                EditorGUILayout.Space(4);
                DrawProfileManagementButtons(null, parentWindow);
                return;
            }

            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Active Profile"),
                _selectedIndex,
                _profileNames);

            if (newIndex != _selectedIndex)
            {
                _selectedIndex = newIndex;
                ProfileRegistry.SetActiveImportProfile(ActiveProfile);
            }

            EditorGUILayout.Space(2);

            if (ActiveProfile != null)
                EditorGUILayout.LabelField(ActiveProfile.description, EditorStyles.helpBox);

            EditorGUILayout.Space(6);
            DrawProfileManagementButtons(ActiveProfile, parentWindow);
        }

        private void DrawProfileManagementButtons(
            FBXImportProfile selected, EditorWindow parentWindow)
        {
            float halfWidth = (EditorGUIUtility.currentViewWidth - 9f) / 2f;

            // Row 1 — New / Edit
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Profile", GUILayout.Width(halfWidth)))
                {
                    BeginCreate();
                    _mode = Mode.CreateEdit;
                    parentWindow.Repaint();
                }

                GUI.enabled = selected != null;

                if (GUILayout.Button("Edit Profile", GUILayout.Width(halfWidth)))
                {
                    BeginEdit(selected);
                    _mode = Mode.CreateEdit;
                    parentWindow.Repaint();
                }

                GUI.enabled = true;
            }

            EditorGUILayout.Space(4);

            // Row 2 — Duplicate / Delete
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selected != null;

                if (GUILayout.Button("Duplicate", GUILayout.Width(halfWidth)))
                {
                    FBXImportProfile clone = FBXImporterUtility.CloneProfile(selected);
                    RefreshProfiles();
                    int idx = _profiles.IndexOf(clone);
                    if (idx >= 0) _selectedIndex = idx;
                }

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

                if (GUILayout.Button("Delete", GUILayout.Width(halfWidth)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Delete Profile",
                        $"Delete '{selected.profileName}'? This cannot be undone.",
                        "Delete", "Cancel"))
                    {
                        FBXImporterUtility.DeleteProfile(selected);
                        RefreshProfiles();
                    }
                }

                GUI.backgroundColor = prev;
                GUI.enabled = true;
            }

            EditorGUILayout.Space(2);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import JSON", GUILayout.Width(halfWidth)))
                {
                    FBXImportProfile imported = FBXImporterUtility.ImportProfile();
                    if (imported != null)
                    {
                        RefreshProfiles();
                        int idx = _profiles.IndexOf(imported);
                        if (idx >= 0) _selectedIndex = idx;
                    }
                }

                GUI.enabled = selected != null;

                if (GUILayout.Button("Export JSON", GUILayout.Width(halfWidth)))
                    FBXImporterUtility.ExportProfile(selected);

                GUI.enabled = true;
            }

            EditorGUILayout.Space(2);

            if (selected != null)
            {
                if (GUILayout.Button("Select Asset in Project"))
                    EditorGUIUtility.PingObject(selected);
            }
        }

        // ── Active profile summary ───────────────────────────────────────────────

        private void DrawActiveProfileSummary()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (ActiveProfile == null || ActiveProfile.Rules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No profile selected or profile has no rules.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Space(2);

                // Column headers
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Pattern",
                        EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Preset",
                        EditorStyles.miniBoldLabel, GUILayout.Width(110));
                    EditorGUILayout.LabelField("Note",
                        EditorStyles.miniBoldLabel);
                }

                PristinePipelineWindow.DrawDivider();

                foreach (FBXImportRule rule in ActiveProfile.Rules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            rule.namePattern, GUILayout.Width(100));
                        EditorGUILayout.LabelField(
                            rule.preset?.presetName ?? "—", GUILayout.Width(110));
                        EditorGUILayout.LabelField(
                            rule.note, EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.Space(2);

                // Naming convention status
                EditorGUILayout.LabelField(
                    "Naming convention: " + (ActiveProfile.enforceNamingConvention
                        ? "Enforced (imports blocked on violation)"
                        : "Warn only"),
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(2);
            }
        }

        // ── Manual reprocess ─────────────────────────────────────────────────────

        private void DrawManualReprocess()
        {
            EditorGUILayout.Space(4);

            bool canReprocess = ActiveProfile != null;
            GUI.enabled = canReprocess;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = canReprocess
                ? new Color(0.3f, 0.6f, 1f)
                : GUI.backgroundColor;

            if (GUILayout.Button("Reprocess All FBX Files", GUILayout.Height(36)))
            {
                if (EditorUtility.DisplayDialog(
                    "Reprocess All FBX Files",
                    $"This will reimport all FBX files in the project using the " +
                    $"'{ActiveProfile.profileName}' profile.\n\nContinue?",
                    "Reprocess", "Cancel"))
                {
                    int count = FBXImporterUtility.ReprocessAll(ActiveProfile);
                    EditorUtility.DisplayDialog(
                        "Reprocess Complete",
                        $"{count} FBX file(s) reprocessed.",
                        "OK");
                }
            }

            GUI.backgroundColor = prev;
            GUI.enabled = true;
            EditorGUILayout.Space(8);
        }

        // ── Create / Edit mode ───────────────────────────────────────────────────

        private void BeginCreate()
        {
            _isEditMode = false;
            _editTarget = null;
            _editName = "";
            _editDescription = "";
            _editEnforce = false;
            _editPrefixes = new List<string> { "SM_", "SK_", "P_" };
            _editDefaultPreset = new FBXImportPreset { presetName = "Default" };
            _editRules = new List<FBXImportRule>();
            _isDirty = false;
            _defaultPresetFoldout = true;
            BuildReorderableLists();
        }

        private void BeginEdit(FBXImportProfile profile)
        {
            _isEditMode = true;
            _editTarget = profile;
            _editName = profile.profileName;
            _editDescription = profile.description;
            _editEnforce = profile.enforceNamingConvention;
            _editPrefixes = new List<string>(profile.validPrefixes);
            _editDefaultPreset = JsonUtility.FromJson<FBXImportPreset>(
                JsonUtility.ToJson(profile.defaultPreset));
            _editRules = new List<FBXImportRule>(profile.Rules);
            _isDirty = false;
            _defaultPresetFoldout = true;
            BuildReorderableLists();
        }

        private void DrawCreateEditMode(EditorWindow parentWindow)
        {
            if (!DrawCreateEditHeader(parentWindow)) return;
            PristinePipelineWindow.DrawDivider();
            DrawNameAndDescription();
            PristinePipelineWindow.DrawDivider();
            DrawNamingConventionSettings();
            PristinePipelineWindow.DrawDivider();
            DrawDefaultPreset();
            PristinePipelineWindow.DrawDivider();
            DrawRulesList();
            EditorGUILayout.Space(10);
            PristinePipelineWindow.DrawDivider();
            DrawSaveButton(parentWindow);
            EditorGUILayout.Space(8);
        }

        private bool DrawCreateEditHeader(EditorWindow parentWindow)
        {
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

                        if (!discard) return false;
                    }

                    _mode = Mode.List;
                    parentWindow.Repaint();
                    return false;
                }

                GUIStyle heading = new(EditorStyles.boldLabel) { fontSize = 13 };
                EditorGUILayout.LabelField(
                    _isEditMode ? $"Edit  —  {_editTarget.profileName}" : "New Profile",
                    heading);
            }

            EditorGUILayout.Space(6);
            return true;
        }

        private void DrawNameAndDescription()
        {
            EditorGUI.BeginChangeCheck();

            _editName = EditorGUILayout.TextField(
                new GUIContent("Profile Name", "Used as the asset filename."),
                _editName);

            if (string.IsNullOrWhiteSpace(_editName))
                EditorGUILayout.HelpBox("Profile name cannot be empty.", MessageType.Error);

            _editDescription = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the profile selector."),
                _editDescription);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

        private void DrawNamingConventionSettings()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Naming Convention", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();

            _editEnforce = EditorGUILayout.Toggle(
                new GUIContent("Enforce",
                    "When on, FBX files that don't match a valid prefix are blocked at import. " +
                    "When off, a warning is logged but the import continues."),
                _editEnforce);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Valid Prefixes", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2);

            _prefixReorderable?.DoLayoutList();
        }

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
            DrawPresetFields(_editDefaultPreset);
        }

        private void DrawPresetFields(FBXImportPreset preset)
        {
            if (preset == null) return;

            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.IndentLevelScope(1))
            {
                preset.scaleFactor = EditorGUILayout.FloatField(
                    new GUIContent("Scale Factor",
                        "Uniform scale on import. Maya = 0.01, Blender = 1.0."),
                    preset.scaleFactor);

                if (preset.scaleFactor <= 0f)
                    EditorGUILayout.HelpBox("Scale factor must be greater than zero.",
                        MessageType.Error);

                preset.meshCompression = (UnityEditor.ModelImporterMeshCompression)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Mesh Compression",
                            "Reduces mesh size. Off preserves precision."),
                        preset.meshCompression);

                preset.readWriteEnabled = EditorGUILayout.Toggle(
                    new GUIContent("Read/Write Enabled",
                        "Allows CPU-side mesh access at runtime."),
                    preset.readWriteEnabled);

                preset.optimizeMesh = EditorGUILayout.Toggle(
                    new GUIContent("Optimize Mesh",
                        "Reorders vertices for GPU cache performance. Recommended on."),
                    preset.optimizeMesh);

                preset.generateLightmapUVs = EditorGUILayout.Toggle(
                    new GUIContent("Generate Lightmap UVs",
                        "Creates a second UV channel for lightmap baking."),
                    preset.generateLightmapUVs);

                preset.normals = (UnityEditor.ModelImporterNormals)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Normals",
                            "Import preserves artist intent. Calculate recomputes from geometry."),
                        preset.normals);

                preset.tangents = (UnityEditor.ModelImporterTangents)
                    EditorGUILayout.EnumPopup(
                        new GUIContent("Tangents",
                            "Calculate Mikkt Space matches most bakers."),
                        preset.tangents);

                preset.swapUVs = EditorGUILayout.Toggle(
                    new GUIContent("Swap UVs",
                        "Swaps UV channel 0 and 1. Fixes inverted UV order from some exporters."),
                    preset.swapUVs);
            }

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

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

        private void DrawSaveButton(EditorWindow parentWindow)
        {
            if (GUILayout.Button("Save Profile", GUILayout.Height(30)))
            {
                if (!ValidateBeforeSave()) return;

                FBXImportProfile profile = BuildOrUpdateProfile();
                FBXImporterUtility.SaveProfile(profile);

                RefreshProfiles();
                int idx = _profiles.IndexOf(profile);
                if (idx >= 0) _selectedIndex = idx;

                _isDirty = false;
                _mode = Mode.List;
                parentWindow.Repaint();
            }
        }

        // ── ReorderableList builders ─────────────────────────────────────────────

        private void BuildReorderableLists()
        {
            BuildPrefixList();
            BuildRulesList();
        }

        private void BuildPrefixList()
        {
            _prefixReorderable = new ReorderableList(
                _editPrefixes, typeof(string),
                draggable: true,
                displayHeader: false,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= _editPrefixes.Count) return;
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;

                    EditorGUI.BeginChangeCheck();
                    _editPrefixes[index] = EditorGUI.TextField(rect, _editPrefixes[index]);
                    if (EditorGUI.EndChangeCheck()) _isDirty = true;
                },

                onAddCallback = _ => { _editPrefixes.Add(""); _isDirty = true; }
            };
            _prefixReorderable.onRemoveCallback = _ =>
            {
                _editPrefixes.RemoveAt(_prefixReorderable.index);
                _isDirty = true;
            };
        }

        private void BuildRulesList()
        {
            _rulesReorderable = new ReorderableList(
                _editRules, typeof(FBXImportRule),
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Pattern → Preset Settings",
                        EditorStyles.miniBoldLabel),

                drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index < 0 || index >= _editRules.Count) return;

                        FBXImportRule rule = _editRules[index];
                        float y = rect.y + 2f;
                        float lineH = EditorGUIUtility.singleLineHeight;
                        float spacing = lineH + 2f;

                        // Name pattern + note on first line
                        EditorGUI.BeginChangeCheck();

                        EditorGUI.LabelField(
                            new Rect(rect.x, y, 90f, lineH), "Pattern");
                        rule.namePattern = EditorGUI.TextField(
                            new Rect(rect.x + 92f, y, rect.width * 0.4f, lineH),
                            rule.namePattern);

                        EditorGUI.LabelField(
                            new Rect(rect.x + rect.width * 0.4f + 96f, y, 36f, lineH), "Note");
                        rule.note = EditorGUI.TextField(
                            new Rect(rect.x + rect.width * 0.4f + 134f, y,
                                rect.width - rect.width * 0.4f - 134f, lineH),
                            rule.note);

                        y += spacing;

                        // Preset name
                        EditorGUI.LabelField(new Rect(rect.x, y, 90f, lineH), "Preset Name");
                        rule.preset.presetName = EditorGUI.TextField(
                            new Rect(rect.x + 92f, y, rect.width - 94f, lineH),
                            rule.preset.presetName);

                        y += spacing;

                        // Scale + compression on one row
                        EditorGUI.LabelField(new Rect(rect.x, y, 80f, lineH), "Scale");
                        rule.preset.scaleFactor = EditorGUI.FloatField(
                            new Rect(rect.x + 82f, y, 50f, lineH),
                            rule.preset.scaleFactor);

                        EditorGUI.LabelField(new Rect(rect.x + 144f, y, 90f, lineH), "Compression");
                        rule.preset.meshCompression =
                            (UnityEditor.ModelImporterMeshCompression)EditorGUI.EnumPopup(
                                new Rect(rect.x + 236f, y, rect.width - 238f, lineH),
                                rule.preset.meshCompression);

                        y += spacing;

                        // Toggle row 1
                        rule.preset.readWriteEnabled = EditorGUI.ToggleLeft(
                            new Rect(rect.x, y, 130f, lineH),
                            "Read/Write", rule.preset.readWriteEnabled);
                        rule.preset.optimizeMesh = EditorGUI.ToggleLeft(
                            new Rect(rect.x + 134f, y, 130f, lineH),
                            "Optimize Mesh", rule.preset.optimizeMesh);
                        rule.preset.generateLightmapUVs = EditorGUI.ToggleLeft(
                            new Rect(rect.x + 268f, y, rect.width - 270f, lineH),
                            "Lightmap UVs", rule.preset.generateLightmapUVs);

                        y += spacing;

                        // Normals + Tangents
                        EditorGUI.LabelField(new Rect(rect.x, y, 60f, lineH), "Normals");
                        rule.preset.normals = (UnityEditor.ModelImporterNormals)EditorGUI.EnumPopup(
                            new Rect(rect.x + 62f, y, 130f, lineH),
                            rule.preset.normals);

                        EditorGUI.LabelField(new Rect(rect.x + 202f, y, 60f, lineH), "Tangents");
                        rule.preset.tangents = (UnityEditor.ModelImporterTangents)EditorGUI.EnumPopup(
                            new Rect(rect.x + 264f, y, rect.width - 266f, lineH),
                            rule.preset.tangents);

                        y += spacing;

                        // Swap UVs
                        rule.preset.swapUVs = EditorGUI.ToggleLeft(
                            new Rect(rect.x, y, 130f, lineH),
                            "Swap UVs", rule.preset.swapUVs);

                        if (EditorGUI.EndChangeCheck()) _isDirty = true;

                        // Validation
                        var messages = FBXImporterUtility.ValidateRule(rule);
                        y += spacing;
                        foreach (string msg in messages)
                        {
                            EditorGUI.HelpBox(
                                new Rect(rect.x, y, rect.width, lineH + 4), msg, MessageType.Warning);
                            y += lineH + 6f;
                        }
                    },

                elementHeightCallback = index =>
                    {
                        // pattern row + preset name + scale/compression + toggles + normals/tangents
                        // + swap uvs = 6 rows
                        float height = (EditorGUIUtility.singleLineHeight + 2f) * 7f + 8f;

                        if (index >= 0 && index < _editRules.Count)
                        {
                            var messages = FBXImporterUtility.ValidateRule(_editRules[index]);
                            height += messages.Count * (EditorGUIUtility.singleLineHeight + 6f);
                        }

                        return height;
                    },

                onAddCallback = _ =>
                    {
                        _editRules.Add(new FBXImportRule
                        {
                            preset = new FBXImportPreset { presetName = "New Preset" }
                        });
                        _isDirty = true;
                    }
            };

            _rulesReorderable.onRemoveCallback = _ =>
            {
                _editRules.RemoveAt(_rulesReorderable.index);
                _isDirty = true;
            };
        }

        // ── Validation and save ──────────────────────────────────────────────────

        private bool ValidateBeforeSave()
        {
            if (string.IsNullOrWhiteSpace(_editName))
            {
                EditorUtility.DisplayDialog("Cannot Save", "Profile name cannot be empty.", "OK");
                return false;
            }

            if (_editDefaultPreset.scaleFactor <= 0f)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Save",
                    "Default preset scale factor must be greater than zero.", "OK");
                return false;
            }

            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.FBX_ProfileSavePath,
                    _editName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<FBXImportProfile>(assetPath) != null)
                {
                    EditorUtility.DisplayDialog(
                        "Duplicate Profile",
                        $"A profile named '{_editName}' already exists.", "OK");
                    return false;
                }
            }

            return true;
        }

        private FBXImportProfile BuildOrUpdateProfile()
        {
            if (_isEditMode && _editTarget != null)
            {
                _editTarget.profileName = _editName.Trim();
                _editTarget.description = _editDescription.Trim();
                _editTarget.enforceNamingConvention = _editEnforce;
                _editTarget.validPrefixes = new List<string>(_editPrefixes);
                _editTarget.defaultPreset = _editDefaultPreset;
                _editTarget.SetRules(_editRules);
                return _editTarget;
            }

            var p = ScriptableObject.CreateInstance<FBXImportProfile>();
            p.profileName = _editName.Trim();
            p.description = _editDescription.Trim();
            p.enforceNamingConvention = _editEnforce;
            p.validPrefixes = new List<string>(_editPrefixes);
            p.defaultPreset = _editDefaultPreset;
            p.SetRules(_editRules);
            return p;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private FBXImportProfile ActiveProfile =>
            (_profiles.Count > 0 && _selectedIndex < _profiles.Count)
                ? _profiles[_selectedIndex]
                : null;

        private void RefreshProfiles()
        {
            _profiles = FBXImporterUtility.LoadAllProfiles();
            _profileNames = FBXImporterUtility.GetProfileDisplayNames(_profiles);

            FBXImportProfile active = ProfileRegistry.GetActiveImportProfile();
            int idx = active != null ? _profiles.IndexOf(active) : -1;
            _selectedIndex = idx >= 0 ? idx : 0;

            if (ActiveProfile != null)
                ProfileRegistry.SetActiveImportProfile(ActiveProfile);
        }
    }
}