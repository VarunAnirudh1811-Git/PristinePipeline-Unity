using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Draws the Asset Organizer tab inside PristinePipelineWindow.
    ///
    /// UX changes (v1.1):
    ///   - Primary action button ("Organize Project Now") promoted to the top of
    ///     the tab so it is visible without scrolling.
    ///   - Rules preview is now wrapped in a fixed-height scroll view.
    ///   - Profile management section is collapsible to reduce visual noise for
    ///     studios where profiles are set once and rarely changed.
    ///   - "Select Asset in Project" moved inside the profile selector row for
    ///     better spatial grouping.
    ///   - Enable toggle now shows a coloured status pill instead of a plain label.
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

        // Profile management panel is collapsed by default — studios set profiles
        // once; the daily workflow is just hitting the action button.
        private bool _profileManagementExpanded = false;

        // ── Rules preview scroll ─────────────────────────────────────────────────

        private Vector2 _rulesScrollPos;
        private const float RulesPreviewMaxHeight = 180f;

        // ── Create / Edit state ──────────────────────────────────────────────────

        private AssetMappingProfile _editTarget = null;
        private bool _isEditMode = false;
        private string _editName = "";
        private string _editDescription = "";
        private List<MappingRule> _editRules = new();
        private ReorderableList _reorderableList;
        private bool _isDirty = false;

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
            // 1 — Status + primary action (always visible, no scroll needed)
            DrawEnableToggleAndPrimaryAction();
            PristinePipelineWindow.DrawDivider();

            // 2 — Profile selector (always visible, compact)
            DrawProfileSelectorCompact();
            PristinePipelineWindow.DrawDivider();

            // 3 — Rules preview (scrollable)
            DrawRulesPreview();

            // 4 — Profile management (collapsible, non-critical)
            PristinePipelineWindow.DrawDivider();
            DrawProfileManagementCollapsible(parentWindow);
        }

        // ── Enable toggle + primary action ───────────────────────────────────────

        private void DrawEnableToggleAndPrimaryAction()
        {
            EditorGUILayout.Space(6);

            // ── Status row ───────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Asset Organizer", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                bool enabled = ToolSettings.Organizer_Enabled;
                bool updated = EditorGUILayout.Toggle(enabled, GUILayout.Width(20));
                if (updated != enabled)
                {
                    ToolSettings.Organizer_Enabled = updated;
                    Debug.Log($"{ToolInfo.LogPrefix} Asset Organizer {(updated ? "enabled" : "disabled")}.");
                }

                // Coloured status pill
                Color pillColor = ToolSettings.Organizer_Enabled
                    ? new Color(0.2f, 0.75f, 0.35f)
                    : new Color(0.55f, 0.55f, 0.55f);

                Color prev = GUI.color;
                GUI.color = pillColor;
                EditorGUILayout.LabelField(
                    ToolSettings.Organizer_Enabled ? "● Active" : "● Off",
                    EditorStyles.miniLabel,
                    GUILayout.Width(52));
                GUI.color = prev;
            }

            EditorGUILayout.Space(6);

            // ── Primary action — promoted to top ─────────────────────────────────
            bool canOrganize = ActiveProfile != null;
            GUI.enabled = canOrganize;

            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = canOrganize ? new Color(0.25f, 0.65f, 1f) : bg;

            if (GUILayout.Button(
                canOrganize
                    ? $"▶  Organize Project Now  ({ActiveProfile.profileName})"
                    : "▶  Organize Project Now",
                GUILayout.Height(34)))
            {
                if (EditorUtility.DisplayDialog(
                    "Organize Project",
                    $"This will move assets across your project using the " +
                    $"'{ActiveProfile.profileName}' profile rules.\n\nThis cannot be undone. Continue?",
                    "Organize", "Cancel"))
                {
                    int moved = AssetOrganizerUtility.OrganizeAll(ActiveProfile, out int skipped);
                    EditorUtility.DisplayDialog(
                        "Organize Complete",
                        $"{moved} asset(s) moved, {skipped} skipped.",
                        "OK");
                }
            }

            GUI.backgroundColor = bg;
            GUI.enabled = true;

            if (!canOrganize)
                EditorGUILayout.HelpBox(
                    "Select or create a profile to enable organizing.",
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
                    new GUIContent("Profile", "The active mapping profile used during organize passes."),
                    GUILayout.Width(100));

                int newIndex = EditorGUILayout.Popup(_selectedIndex, _profileNames);
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    ProfileRegistry.SetActiveOrganizerProfile(ActiveProfile);
                }

                // Ping button — inline, space-efficient
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

        // ── Rules preview ────────────────────────────────────────────────────────

        private void DrawRulesPreview()
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
                    EditorGUILayout.LabelField("Extension", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                    EditorGUILayout.LabelField("Pattern", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Destination", EditorStyles.miniBoldLabel);
                }

                PristinePipelineWindow.DrawDivider();

                // Scrollable rule rows
                _rulesScrollPos = EditorGUILayout.BeginScrollView(
                    _rulesScrollPos,
                    GUILayout.MaxHeight(RulesPreviewMaxHeight));

                foreach (MappingRule rule in ActiveProfile.Rules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("." + rule.extension, GUILayout.Width(70));
                        EditorGUILayout.LabelField(
                            rule.HasNamePattern ? rule.namePattern : "—",
                            GUILayout.Width(100));
                        EditorGUILayout.LabelField(rule.destinationFolder, EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndScrollView();
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
            AssetMappingProfile selected, EditorWindow parentWindow)
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
                        AssetOrganizerUtility.DeleteProfile(selected);
                        RefreshProfiles();
                    }
                }

                GUI.backgroundColor = prev;
                GUI.enabled = true;
            }

            EditorGUILayout.Space(2);

            // Row 3 — Import / Export JSON
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import JSON", GUILayout.Width(halfWidth)))
                {
                    AssetMappingProfile imported = AssetOrganizerUtility.ImportProfile();
                    if (imported != null)
                    {
                        RefreshProfiles();
                        int idx = _profiles.IndexOf(imported);
                        if (idx >= 0) _selectedIndex = idx;
                    }
                }

                GUI.enabled = selected != null;
                if (GUILayout.Button("Export JSON", GUILayout.Width(halfWidth)))
                    AssetOrganizerUtility.ExportProfile(selected);
                GUI.enabled = true;
            }

            EditorGUILayout.Space(4);
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
            EditorGUILayout.HelpBox(
                "Rules are evaluated in order — last match wins.",
                MessageType.None);
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

                    string rawExt = EditorGUI.TextField(
                        new Rect(rect.x, rect.y, ext, rect.height), rule.extension);
                    rule.extension = rawExt.TrimStart('.').ToLowerInvariant();

                    rule.namePattern = EditorGUI.TextField(
                        new Rect(rect.x + ext + 4, rect.y, pat, rect.height), rule.namePattern);

                    rule.destinationFolder = EditorGUI.TextField(
                        new Rect(rect.x + ext + pat + 8, rect.y, dst, rect.height),
                        rule.destinationFolder);

                    if (EditorGUI.EndChangeCheck()) _isDirty = true;

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

                onAddCallback = _ => { _editRules.Add(new MappingRule()); _isDirty = true; },
                onRemoveCallback = list => { _editRules.RemoveAt(list.index); _isDirty = true; }
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

            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.Organizer_ProfileSavePath,
                    _editName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<AssetMappingProfile>(assetPath) != null)
                {
                    EditorUtility.DisplayDialog(
                        "Duplicate Profile",
                        $"A profile named '{_editName}' already exists.", "OK");
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