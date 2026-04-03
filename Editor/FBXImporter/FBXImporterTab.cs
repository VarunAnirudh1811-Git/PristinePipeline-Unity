using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Draws the FBX Importer tab inside PristinePipelineWindow.
    ///
    /// UX changes (v1.1):
    ///   - Primary actions (Reprocess + Generate Prefabs) promoted to the top of
    ///     the tab, immediately below the enable toggle.
    ///   - Rules preview is now wrapped in a fixed-height scroll view.
    ///   - Profile management section is collapsible to reduce noise once profiles
    ///     are configured.
    ///   - "Select Asset in Project" moved inline with the profile dropdown.
    ///   - Enable toggle shows coloured status pill.
    ///   - Reprocess and Generate Prefabs actions have clear visual hierarchy
    ///     (primary vs secondary).
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

        private bool _profileManagementExpanded = false;

        // ── Rules preview scroll ─────────────────────────────────────────────────

        private Vector2 _rulesScrollPos;
        private const float RulesPreviewMaxHeight = 180f;

        // ── Create / Edit state ──────────────────────────────────────────────────

        private FBXImportProfile _editTarget = null;
        private bool _isEditMode = false;
        private string _editName = "";
        private string _editDescription = "";
        private bool _editEnforce = false;
        private bool _editEmission = false;
        private bool _editAO = true;
        private List<string> _editPrefixes = new();
        private FBXImportPreset _editDefaultPreset;
        private List<FBXImportRule> _editRules = new();
        private ReorderableList _rulesReorderable;
        private ReorderableList _prefixReorderable;
        private bool _isDirty = false;
        private bool _defaultPresetFoldout = true;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void OnEnable() => RefreshProfiles();
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
            // 1 — Status + primary actions (always visible, no scroll needed)
            DrawEnableToggleAndPrimaryActions();
            PristinePipelineWindow.DrawDivider();

            // 2 — Profile selector (compact, always visible)
            DrawProfileSelectorCompact();
            PristinePipelineWindow.DrawDivider();

            // 3 — Rules preview (scrollable)
            DrawActiveProfileSummary();

            // 4 — Profile management (collapsible)
            PristinePipelineWindow.DrawDivider();
            DrawProfileManagementCollapsible(parentWindow);
        }

        // ── Enable toggle + primary actions ──────────────────────────────────────

        private void DrawEnableToggleAndPrimaryActions()
        {
            EditorGUILayout.Space(6);

            // ── Status row ───────────────────────────────────────────────────────
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
                        $"{ToolInfo.LogPrefix} FBX Importer {(updated ? "enabled" : "disabled")}.");
                }

                // Coloured status pill
                Color pillColor = ToolSettings.FBX_Enabled
                    ? new Color(0.2f, 0.75f, 0.35f)
                    : new Color(0.55f, 0.55f, 0.55f);
                Color prev = GUI.color;
                GUI.color = pillColor;
                EditorGUILayout.LabelField(
                    ToolSettings.FBX_Enabled ? "● Active" : "● Off",
                    EditorStyles.miniLabel,
                    GUILayout.Width(52));
                GUI.color = prev;
            }

            EditorGUILayout.Space(6);

            bool canAct = ActiveProfile != null;
            GUI.enabled = canAct;

            // ── Primary action — Reprocess ───────────────────────────────────────
            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = canAct ? new Color(0.25f, 0.65f, 1f) : bg;

            if (GUILayout.Button(
                canAct
                    ? $"▶  Reprocess All FBX Files  ({ActiveProfile.profileName})"
                    : "▶  Reprocess All FBX Files",
                GUILayout.Height(34)))
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
                        $"{count} FBX file(s) reprocessed.", "OK");
                }
            }

            GUI.backgroundColor = bg;
            EditorGUILayout.Space(4);

            // ── Secondary action — Generate Prefabs ──────────────────────────────
            GUI.backgroundColor = canAct ? new Color(0.45f, 0.78f, 0.45f) : bg;

            if (GUILayout.Button(
                "  Generate Prefabs Now",
                GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog(
                    "Generate Prefabs",
                    $"Generate prefabs for all FBX files using the " +
                    $"'{ActiveProfile.profileName}' profile.\n" +
                    $"Existing prefabs at the destination will not be overwritten.\n\nContinue?",
                    "Generate", "Cancel"))
                {
                    int count = GeneratePrefabsForAll(ActiveProfile);
                    EditorUtility.DisplayDialog(
                        "Generate Complete",
                        $"{count} prefab(s) generated.", "OK");
                }
            }

            GUI.backgroundColor = bg;
            GUI.enabled = true;

            if (!canAct)
                EditorGUILayout.HelpBox(
                    "Select or create a profile to enable actions.",
                    MessageType.Info);

            EditorGUILayout.Space(2);
        }

        // ── Compact profile selector ─────────────────────────────────────────────

        private void DrawProfileSelectorCompact()
        {
            EditorGUILayout.Space(4);

            if (_profiles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No profiles found. Use 'Manage Profiles' below to create one.",
                    MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent("Profile", "The active FBX import profile."),
                    GUILayout.Width(100));

                int newIndex = EditorGUILayout.Popup(_selectedIndex, _profileNames);
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    ProfileRegistry.SetActiveImportProfile(ActiveProfile);
                }

                GUI.enabled = ActiveProfile != null;
                if (GUILayout.Button(
                    new GUIContent("◉", "Select asset in Project window"),
                    GUILayout.Width(30), GUILayout.Height(18)))
                    EditorGUIUtility.PingObject(ActiveProfile);
                GUI.enabled = true;
            }

            if (ActiveProfile != null && !string.IsNullOrWhiteSpace(ActiveProfile.description))
                EditorGUILayout.LabelField(ActiveProfile.description, EditorStyles.helpBox);

            EditorGUILayout.Space(2);
        }

        // ── Active profile summary ───────────────────────────────────────────────

        private void DrawActiveProfileSummary()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Active Rules", EditorStyles.boldLabel);
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
                    EditorGUILayout.LabelField("Pattern", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Preset", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                    EditorGUILayout.LabelField("Note", EditorStyles.miniBoldLabel);
                }

                PristinePipelineWindow.DrawDivider();

                // Scrollable rule rows
                _rulesScrollPos = EditorGUILayout.BeginScrollView(
                    _rulesScrollPos,
                    GUILayout.MaxHeight(RulesPreviewMaxHeight));

                foreach (FBXImportRule rule in ActiveProfile.Rules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(rule.namePattern, GUILayout.Width(100));
                        EditorGUILayout.LabelField(
                            rule.preset?.presetName ?? "—", GUILayout.Width(110));
                        EditorGUILayout.LabelField(rule.note, EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(2);

                // Profile-level metadata
                EditorGUILayout.LabelField(
                    "Naming convention: " + (ActiveProfile.enforceNamingConvention
                        ? "Enforced (blocks on violation)"
                        : "Warn only"),
                    EditorStyles.miniLabel);

                EditorGUILayout.LabelField(
                    $"Emission: {(ActiveProfile.enableEmission ? "On" : "Off")}  " +
                    $"AO: {(ActiveProfile.enableAmbientOcclusion ? "On" : "Off")}",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(2);
            }
        }

        // ── Collapsible profile management ───────────────────────────────────────

        private void DrawProfileManagementCollapsible(EditorWindow parentWindow)
        {
            _profileManagementExpanded = EditorGUILayout.Foldout(
                _profileManagementExpanded,
                "Manage Profiles",
                true,
                EditorStyles.foldoutHeader);

            if (!_profileManagementExpanded) return;

            EditorGUILayout.Space(4);
            DrawProfileManagementButtons(ActiveProfile, parentWindow);
        }

        private void DrawProfileManagementButtons(
            FBXImportProfile selected, EditorWindow parentWindow)
        {
            float halfWidth = (EditorGUIUtility.currentViewWidth - 9f) / 2f;

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

            EditorGUILayout.Space(4);
        }

        // ── Generate Prefabs helper ──────────────────────────────────────────────

        private static int GeneratePrefabsForAll(FBXImportProfile profile)
        {
            if (profile == null) return 0;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            int count = 0;

            foreach (string path in allAssets)
            {
                if (!path.StartsWith("Assets/")) continue;
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                FBXImportPreset preset = FBXImporterUtility.FindMatchingPreset(profile, path);
                if (preset == null) continue;

                string result = FBXImporterUtility.GeneratePrefab(path, preset);
                if (result != null) count++;
            }

            AssetDatabase.Refresh();
            return count;
        }

        // ── Create / Edit mode ───────────────────────────────────────────────────

        private void BeginCreate()
        {
            _isEditMode = false;
            _editTarget = null;
            _editName = "";
            _editDescription = "";
            _editEnforce = false;
            _editEmission = false;
            _editAO = true;
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
            _editEmission = profile.enableEmission;
            _editAO = profile.enableAmbientOcclusion;
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
            DrawProfileLevelToggles();
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

                var heading = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
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
                new GUIContent("Profile Name", "Used as the asset filename."), _editName);

            if (string.IsNullOrWhiteSpace(_editName))
                EditorGUILayout.HelpBox("Profile name cannot be empty.", MessageType.Error);

            _editDescription = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the profile selector."),
                _editDescription);

            if (EditorGUI.EndChangeCheck()) _isDirty = true;
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

            if (EditorGUI.EndChangeCheck()) _isDirty = true;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Valid Prefixes", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2);
            _prefixReorderable?.DoLayoutList();
        }

        private void DrawProfileLevelToggles()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Texture Channels", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These toggles apply to all presets in this profile.", MessageType.None);
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();

            _editEmission = EditorGUILayout.Toggle(
                new GUIContent("Enable Emission",
                    "Search and assign _E / _Emissive textures. " +
                    "Also enables the Emission keyword on created materials."),
                _editEmission);

            _editAO = EditorGUILayout.Toggle(
                new GUIContent("Enable Ambient Occlusion",
                    "Search and assign _AO textures to the Occlusion Map slot. " +
                    "Has no effect when an ORM texture is found."),
                _editAO);

            if (EditorGUI.EndChangeCheck()) _isDirty = true;
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

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Material & Texture", EditorStyles.miniBoldLabel);

                preset.materialPrefix = EditorGUILayout.TextField(
                    new GUIContent("Material Prefix", "Prepended to created material names."),
                    preset.materialPrefix);

                preset.materialsFolder = EditorGUILayout.TextField(
                    new GUIContent("Materials Folder", "Unity asset path where materials are saved."),
                    preset.materialsFolder);

                if (!string.IsNullOrWhiteSpace(preset.materialsFolder)
                    && !preset.materialsFolder.StartsWith("Assets/"))
                    EditorGUILayout.HelpBox("Materials folder must start with Assets/.", MessageType.Warning);

                preset.texturesFolder = EditorGUILayout.TextField(
                    new GUIContent("Textures Folder", "Unity asset path searched for matching textures."),
                    preset.texturesFolder);

                if (!string.IsNullOrWhiteSpace(preset.texturesFolder)
                    && !preset.texturesFolder.StartsWith("Assets/"))
                    EditorGUILayout.HelpBox("Textures folder must start with Assets/.", MessageType.Warning);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Prefab", EditorStyles.miniBoldLabel);

                preset.prefabsFolder = EditorGUILayout.TextField(
                    new GUIContent("Prefabs Folder", "Unity asset path where generated prefabs are saved."),
                    preset.prefabsFolder);

                if (!string.IsNullOrWhiteSpace(preset.prefabsFolder)
                    && !preset.prefabsFolder.StartsWith("Assets/"))
                    EditorGUILayout.HelpBox("Prefabs folder must start with Assets/.", MessageType.Warning);

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
            BuildRulesList_Internal();
        }

        private void BuildPrefixList()
        {
            _prefixReorderable = new ReorderableList(
                _editPrefixes, typeof(string),
                draggable: true, displayHeader: false,
                displayAddButton: true, displayRemoveButton: true)
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

        private void BuildRulesList_Internal()
        {
            const float lineH = 18f;
            const float spacing = 20f;
            const int FixedRows = 12;

            _rulesReorderable = new ReorderableList(
                _editRules, typeof(FBXImportRule),
                draggable: true, displayHeader: true,
                displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Pattern → Preset Settings",
                        EditorStyles.miniBoldLabel),

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= _editRules.Count) return;

                    FBXImportRule rule = _editRules[index];
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

                    // Row 1: preset name
                    EditorGUI.LabelField(new Rect(rect.x, y, 88f, lineH), "Preset Name");
                    rule.preset.presetName = EditorGUI.TextField(
                        new Rect(rect.x + 90f, y, w - 92f, lineH), rule.preset.presetName);
                    y += spacing;

                    // Row 2: scale + compression
                    EditorGUI.LabelField(new Rect(rect.x, y, 48f, lineH), "Scale");
                    rule.preset.scaleFactor = EditorGUI.FloatField(
                        new Rect(rect.x + 50f, y, 50f, lineH), rule.preset.scaleFactor);
                    EditorGUI.LabelField(new Rect(rect.x + 112f, y, 88f, lineH), "Compression");
                    rule.preset.SetMeshCompression(
                        (ModelImporterMeshCompression)EditorGUI.EnumPopup(
                            new Rect(rect.x + 202f, y, w - 204f, lineH),
                            rule.preset.MeshCompression()));
                    y += spacing;

                    // Row 3: toggles
                    rule.preset.readWriteEnabled = EditorGUI.ToggleLeft(
                        new Rect(rect.x, y, 110f, lineH), "Read/Write", rule.preset.readWriteEnabled);
                    rule.preset.optimizeMesh = EditorGUI.ToggleLeft(
                        new Rect(rect.x + 114f, y, 120f, lineH), "Optimize Mesh", rule.preset.optimizeMesh);
                    rule.preset.generateLightmapUVs = EditorGUI.ToggleLeft(
                        new Rect(rect.x + 238f, y, w - 240f, lineH), "Lightmap UVs", rule.preset.generateLightmapUVs);
                    y += spacing;

                    // Row 4: normals + tangents
                    EditorGUI.LabelField(new Rect(rect.x, y, 56f, lineH), "Normals");
                    rule.preset.SetNormals(
                        (ModelImporterNormals)EditorGUI.EnumPopup(
                            new Rect(rect.x + 58f, y, 130f, lineH), rule.preset.Normals()));
                    EditorGUI.LabelField(new Rect(rect.x + 198f, y, 58f, lineH), "Tangents");
                    rule.preset.SetTangents(
                        (ModelImporterTangents)EditorGUI.EnumPopup(
                            new Rect(rect.x + 258f, y, w - 260f, lineH), rule.preset.Tangents()));
                    y += spacing;

                    // Row 5: swap UVs
                    rule.preset.swapUVs = EditorGUI.ToggleLeft(
                        new Rect(rect.x, y, 130f, lineH), "Swap UVs", rule.preset.swapUVs);
                    y += spacing;

                    // Row 6: material prefix
                    EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Mat Prefix");
                    rule.preset.materialPrefix = EditorGUI.TextField(
                        new Rect(rect.x + 102f, y, w - 104f, lineH), rule.preset.materialPrefix);
                    y += spacing;

                    // Row 7: materials folder
                    EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Materials Folder");
                    rule.preset.materialsFolder = EditorGUI.TextField(
                        new Rect(rect.x + 102f, y, w - 104f, lineH), rule.preset.materialsFolder);
                    y += spacing;

                    // Row 8: textures folder
                    EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Textures Folder");
                    rule.preset.texturesFolder = EditorGUI.TextField(
                        new Rect(rect.x + 102f, y, w - 104f, lineH), rule.preset.texturesFolder);
                    y += spacing;

                    // Row 9: prefabs folder
                    EditorGUI.LabelField(new Rect(rect.x, y, 100f, lineH), "Prefabs Folder");
                    rule.preset.prefabsFolder = EditorGUI.TextField(
                        new Rect(rect.x + 102f, y, w - 104f, lineH), rule.preset.prefabsFolder);
                    y += spacing;

                    // Row 10: generate prefab + lightmap static
                    rule.preset.generatePrefab = EditorGUI.ToggleLeft(
                        new Rect(rect.x, y, 160f, lineH), "Auto-Generate Prefab", rule.preset.generatePrefab);
                    rule.preset.lightmapStatic = EditorGUI.ToggleLeft(
                        new Rect(rect.x + 164f, y, w - 166f, lineH), "Lightmap Static", rule.preset.lightmapStatic);
                    y += spacing;

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
                    float height = FixedRows * spacing + 8f;
                    if (index >= 0 && index < _editRules.Count)
                    {
                        var messages = FBXImporterUtility.ValidateRule(_editRules[index]);
                        height += messages.Count * (lineH + 6f);
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
                _editTarget.enableEmission = _editEmission;
                _editTarget.enableAmbientOcclusion = _editAO;
                _editTarget.validPrefixes = new List<string>(_editPrefixes);
                _editTarget.defaultPreset = _editDefaultPreset;
                _editTarget.SetRules(_editRules);
                return _editTarget;
            }

            var p = ScriptableObject.CreateInstance<FBXImportProfile>();
            p.profileName = _editName.Trim();
            p.description = _editDescription.Trim();
            p.enforceNamingConvention = _editEnforce;
            p.enableEmission = _editEmission;
            p.enableAmbientOcclusion = _editAO;
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