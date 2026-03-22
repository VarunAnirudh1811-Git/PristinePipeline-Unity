using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Draws the Asset Organizer tab inside PristinePipelineWindow.
    /// Owns the tab's UI state and mode transitions (list ↔ create/edit).
    /// All rule matching and asset operations are delegated to AssetOrganizerUtility.
    /// </summary>
    public class AssetOrganizerTab
    {
        // ── Mode ─────────────────────────────────────────────────────────────────

        private enum Mode { List, CreateEdit }
        private Mode _mode = Mode.List;

        // ── List mode state ──────────────────────────────────────────────────────

        private List<AssetMappingProfile> _profiles = new();
        private string[] _profileNames = new string[0];
        private int _selectedIndex = 0;

        // ── Create / Edit state ──────────────────────────────────────────────────

        // Inline editor state — no separate window
        private AssetMappingProfile _editTarget = null;
        private bool _isEditMode = false;
        private string _editName = "";
        private string _editDescription = "";
        private List<MappingRule> _editRules = new();
        private ReorderableList _reorderableList;
        private bool _isDirty = false;

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
            DrawRulesPreview();
            PristinePipelineWindow.DrawDivider();
            DrawManualOrganize();
        }

        // ── Enable toggle ────────────────────────────────────────────────────────

        private void DrawEnableToggle()
        {
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Asset Organizer", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                bool enabled = ToolSettings.Organizer_Enabled;
                bool updated = EditorGUILayout.Toggle(enabled, GUILayout.Width(20));

                if (updated != enabled)
                {
                    ToolSettings.Organizer_Enabled = updated;
                    Debug.Log(
                        $"{ToolInfo.LogPrefix} Asset Organizer " +
                        $"{(updated ? "enabled" : "disabled")}.");
                }

                EditorGUILayout.LabelField(
                    ToolSettings.Organizer_Enabled ? "Enabled" : "Disabled",
                    GUILayout.Width(56));
            }

            if (ToolSettings.Organizer_Enabled)
            {
                EditorGUILayout.HelpBox(
                    "Organizer is active. New assets will be moved automatically on import.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Organizer is disabled. Enable it to automatically move imported assets.",
                    MessageType.None);
            }

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
                ProfileRegistry.SetActiveOrganizerProfile(ActiveProfile);
            }

            EditorGUILayout.Space(2);

            if (ActiveProfile != null)
                EditorGUILayout.LabelField(ActiveProfile.description, EditorStyles.helpBox);

            EditorGUILayout.Space(6);
            DrawProfileManagementButtons(ActiveProfile, parentWindow);
        }

        private void DrawProfileManagementButtons(AssetMappingProfile selected, EditorWindow parentWindow)
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
                    AssetMappingProfile clone = AssetOrganizerUtility.CloneProfile(selected);
                    RefreshProfiles();
                    int cloneIndex = _profiles.IndexOf(clone);
                    if (cloneIndex >= 0) _selectedIndex = cloneIndex;
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
                        AssetOrganizerUtility.DeleteProfile(selected);
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
                    AssetMappingProfile imported = AssetOrganizerUtility.ImportProfile();
                    if (imported != null)
                    {
                        RefreshProfiles();
                        int cloneIndex = _profiles.IndexOf(imported);
                        if (cloneIndex >= 0) _selectedIndex = cloneIndex;
                    }
                }

                GUI.enabled = selected != null;

                if (GUILayout.Button("Export JSON", GUILayout.Width(halfWidth)))
                    AssetOrganizerUtility.ExportProfile(selected);


                GUI.enabled = true;
            }

            EditorGUILayout.Space(2);

            if (selected != null)
            {
                if (GUILayout.Button("Select Asset in Project"))
                    EditorGUIUtility.PingObject(selected);
            }
        }

        // ── Rules preview ────────────────────────────────────────────────────────

        private void DrawRulesPreview()
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
                    EditorGUILayout.LabelField("Extension", EditorStyles.miniBoldLabel,
                        GUILayout.Width(70));
                    EditorGUILayout.LabelField("Pattern", EditorStyles.miniBoldLabel,
                        GUILayout.Width(100));
                    EditorGUILayout.LabelField("Destination", EditorStyles.miniBoldLabel);
                }

                PristinePipelineWindow.DrawDivider();

                foreach (MappingRule rule in ActiveProfile.Rules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            "." + rule.extension, GUILayout.Width(70));
                        EditorGUILayout.LabelField(
                            rule.HasNamePattern ? rule.namePattern : "—",
                            GUILayout.Width(100));
                        EditorGUILayout.LabelField(
                            rule.destinationFolder,
                            EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.Space(2);
            }
        }

        // ── Manual organize ──────────────────────────────────────────────────────

        private void DrawManualOrganize()
        {
            EditorGUILayout.Space(4);

            bool canOrganize = ActiveProfile != null;

            GUI.enabled = canOrganize;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = canOrganize
                ? new Color(0.3f, 0.6f, 1f)
                : GUI.backgroundColor;

            if (GUILayout.Button("Organize Project Now", GUILayout.Height(36)))
            {
                if (EditorUtility.DisplayDialog(
                    "Organize Project",
                    $"This will move assets across your project using the '{ActiveProfile.profileName}' profile rules.\n\nThis cannot be undone. Continue?",
                    "Organize", "Cancel"))
                {
                    int moved = AssetOrganizerUtility.OrganizeAll(ActiveProfile, out int skipped);
                    EditorUtility.DisplayDialog(
                        "Organize Complete",
                        $"{moved} asset(s) moved, {skipped} skipped.",
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
            _editRules = new List<MappingRule>();
            _isDirty = false;
            BuildReorderableList();
        }

        private void BeginEdit(AssetMappingProfile profile)
        {
            _isEditMode = true;
            _editTarget = profile;
            _editName = profile.profileName;
            _editDescription = profile.description;
            _editRules = new List<MappingRule>(profile.Rules);
            _isDirty = false;
            BuildReorderableList();
        }

        private void DrawCreateEditMode(EditorWindow parentWindow)
        {
            if (!DrawCreateEditHeader(parentWindow)) return;
            PristinePipelineWindow.DrawDivider();
            DrawCreateEditFields();
            EditorGUILayout.Space(6);
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
                string label = _isEditMode
                    ? $"Edit  —  {_editTarget.profileName}"
                    : "New Profile";

                EditorGUILayout.LabelField(label, heading);
            }

            EditorGUILayout.Space(6);
            return true;
        }

        private void DrawCreateEditFields()
        {
            EditorGUI.BeginChangeCheck();

            _editName = EditorGUILayout.TextField(
                new GUIContent("Profile Name", "Used as the asset filename."),
                _editName);

            if (string.IsNullOrWhiteSpace(_editName))
                EditorGUILayout.HelpBox("Profile name cannot be empty.", MessageType.Error);

            EditorGUILayout.Space(2);

            _editDescription = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the profile selector."),
                _editDescription);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

        private void DrawRulesList()
        {
            EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _reorderableList?.DoLayoutList();

            if (_editRules.Count == 0)
                EditorGUILayout.HelpBox("Add at least one rule.", MessageType.Warning);
        }

        private void BuildReorderableList()
        {
            _reorderableList = new ReorderableList(
                _editRules, typeof(MappingRule),
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                {
                    float w = rect.width;
                    float x = rect.x;
                    float ext = 60f;
                    float pat = 110f;
                    float del = 40f;
                    float dst = w - ext - pat - del - 12f;

                    EditorGUI.LabelField(new Rect(x, rect.y, ext, rect.height),
                        "Ext", EditorStyles.miniBoldLabel);
                    EditorGUI.LabelField(new Rect(x + ext + 4, rect.y, pat, rect.height),
                        "Name pattern", EditorStyles.miniBoldLabel);
                    EditorGUI.LabelField(new Rect(x + ext + pat + 8, rect.y, dst, rect.height),
                        "Destination", EditorStyles.miniBoldLabel);
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index < 0 || index >= _editRules.Count) return;

                        MappingRule rule = _editRules[index];
                        rect.y += 2;
                        rect.height = EditorGUIUtility.singleLineHeight;

                        float w = rect.width;
                        float ext = 60f;
                        float pat = 110f;
                        float del = 40f;
                        float dst = w - ext - pat - del - 12f;

                        EditorGUI.BeginChangeCheck();

                        // Extension field — strip leading dot if user types one
                        string rawExt = EditorGUI.TextField(
                            new Rect(rect.x, rect.y, ext, rect.height),
                            rule.extension);
                        rule.extension = rawExt.TrimStart('.').ToLowerInvariant();

                        // Name pattern field
                        rule.namePattern = EditorGUI.TextField(
                            new Rect(rect.x + ext + 4, rect.y, pat, rect.height),
                            rule.namePattern);

                        // Destination folder field
                        rule.destinationFolder = EditorGUI.TextField(
                            new Rect(rect.x + ext + pat + 8, rect.y, dst, rect.height),
                            rule.destinationFolder);

                        if (EditorGUI.EndChangeCheck())
                            _isDirty = true;

                        // Validation messages below the row
                        var messages = AssetOrganizerUtility.ValidateRule(rule);
                        float warningY = rect.y + EditorGUIUtility.singleLineHeight + 2;

                        foreach (string msg in messages)
                        {
                            EditorGUI.HelpBox(
                                new Rect(rect.x, warningY, rect.width,
                                    EditorGUIUtility.singleLineHeight + 4),
                                msg, MessageType.Warning);
                            warningY += EditorGUIUtility.singleLineHeight + 6;
                        }
                    },

                elementHeightCallback = index =>
                    {
                        float height = EditorGUIUtility.singleLineHeight + 4;
                        if (index < 0 || index >= _editRules.Count) return height;

                        var messages = AssetOrganizerUtility.ValidateRule(_editRules[index]);
                        height += messages.Count * (EditorGUIUtility.singleLineHeight + 6);
                        return height;
                    },

                onAddCallback = list =>
                    {
                        _editRules.Add(new MappingRule());
                        _isDirty = true;
                    },

                onRemoveCallback = list =>
                    {
                        _editRules.RemoveAt(list.index);
                        _isDirty = true;
                    }
            };
        }

        private void DrawSaveButton(EditorWindow parentWindow)
        {
            if (GUILayout.Button("Save Profile", GUILayout.Height(30)))
            {
                if (!ValidateBeforeSave()) return;

                AssetMappingProfile profile = BuildOrUpdateProfile();
                AssetOrganizerUtility.SaveProfile(profile);

                RefreshProfiles();
                int idx = _profiles.IndexOf(profile);
                if (idx >= 0) _selectedIndex = idx;

                _isDirty = false;
                _mode = Mode.List;
                parentWindow.Repaint();
            }
        }

        private bool ValidateBeforeSave()
        {
            if (string.IsNullOrWhiteSpace(_editName))
            {
                EditorUtility.DisplayDialog("Cannot Save", "Profile name cannot be empty.", "OK");
                return false;
            }

            if (_editRules.Count == 0)
            {
                EditorUtility.DisplayDialog("Cannot Save", "Add at least one rule.", "OK");
                return false;
            }

            // Check for duplicate name on new profiles
            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.Organizer_ProfileSavePath,
                    _editName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<AssetMappingProfile>(assetPath) != null)
                {
                    EditorUtility.DisplayDialog(
                        "Duplicate Profile",
                        $"A profile named '{_editName}' already exists.",
                        "OK");
                    return false;
                }
            }

            return true;
        }

        private AssetMappingProfile BuildOrUpdateProfile()
        {
            if (_isEditMode && _editTarget != null)
            {
                _editTarget.profileName = _editName.Trim();
                _editTarget.description = _editDescription.Trim();
                _editTarget.SetRules(_editRules);
                return _editTarget;
            }

            var p = ScriptableObject.CreateInstance<AssetMappingProfile>();
            p.profileName = _editName.Trim();
            p.description = _editDescription.Trim();
            p.SetRules(_editRules);
            return p;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private AssetMappingProfile ActiveProfile =>
            (_profiles.Count > 0 && _selectedIndex < _profiles.Count)
                ? _profiles[_selectedIndex]
                : null;

        private void RefreshProfiles()
        {
            _profiles = AssetOrganizerUtility.LoadAllProfiles();
            _profileNames = AssetOrganizerUtility.GetProfileDisplayNames(_profiles);

            AssetMappingProfile active = ProfileRegistry.GetActiveOrganizerProfile();
            int idx = active != null ? _profiles.IndexOf(active) : -1;
            _selectedIndex = idx >= 0 ? idx : 0;

            if (ActiveProfile != null)
                ProfileRegistry.SetActiveOrganizerProfile(ActiveProfile);
        }
    }
}