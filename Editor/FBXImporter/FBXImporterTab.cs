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

        // ── Creator state ────────────────────────────────────────────────────────

        private readonly FBXImportProfileCreatorTab _creator = new();

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

            float buttonWidth = (EditorGUIUtility.currentViewWidth - 8f) * 0.5f;

            EditorGUILayout.BeginHorizontal();

            // ── Secondary action — Generate Prefabs ──────────────────────────────
            GUI.backgroundColor = canAct ? new Color(0.45f, 0.78f, 0.45f) : bg;

            if (GUILayout.Button(
                "  Generate Prefabs Now",
                GUILayout.Height(26), GUILayout.Width(buttonWidth)))
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

            // ── Tertiary action — Reassign Textures ──────────────────────────────────
            GUI.backgroundColor = canAct ? new Color(0.75f, 0.65f, 0.95f) : bg;

            if (GUILayout.Button(
                "  Reassign Textures",
                GUILayout.Height(26), GUILayout.Width(buttonWidth)))
            {
                if (EditorUtility.DisplayDialog(
                    "Reassign Textures",
                    $"Re-scan all materials under '{ToolSettings.ActiveRootPath}' and assign " +
                    $"matching textures using the '{ActiveProfile.profileName}' profile.\n\n" +
                    $"Existing texture assignments will be overwritten if a matching " +
                    $"texture is found. Continue?",
                    "Reassign", "Cancel"))
                {
                    int count = FBXImporterUtility.ReassignTexturesForProfile(ActiveProfile);
                    EditorUtility.DisplayDialog(
                        "Reassign Complete",
                        $"{count} material(s) updated.",
                        "OK");
                }
            }

            GUI.backgroundColor = bg;

            EditorGUILayout.EndHorizontal();

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
                    _creator.BeginCreate();
                    _mode = Mode.CreateEdit;
                    parentWindow.Repaint();
                }

                // Edit button - disabled for built-in profiles
                GUI.enabled = selected != null && !selected.isBuiltIn;
                if (GUILayout.Button("Edit Profile", GUILayout.Width(halfWidth)))
                {
                    _creator.BeginEdit(selected);
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

                // Delete button - disabled for built-in profiles
                GUI.enabled = selected != null && !selected.isBuiltIn;

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

        private void DrawCreateEditMode(EditorWindow parentWindow)
        {
            bool saved = _creator.Draw(out FBXImportProfile savedProfile, out bool wantsBack);

            if (saved && savedProfile != null)
            {
                RefreshProfiles();
                int idx = _profiles.IndexOf(savedProfile);
                if (idx >= 0) _selectedIndex = idx;
                _mode = Mode.List;
                parentWindow.Repaint();
            }
            else if (wantsBack)
            {
                _mode = Mode.List;
                parentWindow.Repaint();
            }
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