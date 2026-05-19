using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Draws the Asset Organizer tab inside PristinePipelineWindow. 
    /// </summary>
    ///  UX changes (v1.1):
    ///   - Primary action button ("Organize Project Now") promoted to the top of
    ///     the tab so it is visible without scrolling.
    ///   - Rules preview is now wrapped in a fixed-height scroll view.
    ///   - Profile management section is collapsible to reduce visual noise for
    ///     studios where profiles are set once and rarely changed.
    ///   - "Select Asset in Project" moved inside the profile selector row for
    ///     better spatial grouping.
    ///   - Enable toggle now shows a coloured status pill instead of a plain label.
    ///   v1.2.1 — Scope control:
    ///   Added a "Scope" section in list mode that shows the three zones the
    ///   organizer will process and lets the user manage additional opt-in paths.
    ///   The additional paths list is global (shared across profiles) and persisted
    ///   in ToolSettings.Organizer_AdditionalScopePaths.
    
    public class AssetOrganizerTab
    {
        // ── Mode ─────────────────────────────────────────────────────────────────

        private enum Mode { List, CreateEdit }
        private Mode _mode = Mode.List;

        // ── List mode state ──────────────────────────────────────────────────────

        private List<AssetMappingProfile> _profiles = new();
        private string[] _profileNames = new string[0];
        private int _selectedIndex = 0;

        private bool _profileManagementExpanded = false;
        private bool _scopeExpanded = false;

        // ── Rules preview scroll ─────────────────────────────────────────────────

        private Vector2 _rulesScrollPos;
        private const float RulesPreviewMaxHeight = 180f;

        // ── Scope — additional paths list ────────────────────────────────────────

        // Local copy of the additional paths kept in sync with ToolSettings.
        // We work on a local list so the ReorderableList has a stable reference,
        // then flush back to ToolSettings whenever a change is made.
        private List<string> _additionalPaths;
        private ReorderableList _additionalPathsList;

        // ── Creator state ────────────────────────────────────────────────────────

        private readonly AssetMappingProfileCreatorTab _creator = new();

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void OnEnable()
        {
            RefreshProfiles();
            LoadAdditionalPaths();
            BuildAdditionalPathsList();
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
            // 1 — Status + primary action
            DrawEnableToggleAndPrimaryAction();
            PristinePipelineWindow.DrawDivider();

            // 2 — Profile selector
            DrawProfileSelectorCompact();
            PristinePipelineWindow.DrawDivider();

            // 3 — Rules preview
            DrawRulesPreview();

            // 4 — Scope (collapsible)
            PristinePipelineWindow.DrawDivider();
            DrawScopeSection();

            // 5 — Profile management (collapsible)
            PristinePipelineWindow.DrawDivider();
            DrawProfileManagementCollapsible(parentWindow);
        }

        // ── Enable toggle + primary action ───────────────────────────────────────

        private void DrawEnableToggleAndPrimaryAction()
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
                        $"{ToolInfo.LogPrefix} Asset Organizer {(updated ? "enabled" : "disabled")}.");
                }

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
                    $"This will move assets inside the defined scope using the " +
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
                    new GUIContent("Profile",
                        "The active mapping profile used during organize passes."),
                    GUILayout.Width(100));

                int newIndex = EditorGUILayout.Popup(_selectedIndex, _profileNames);
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    ProfileRegistry.SetActiveOrganizerProfile(ActiveProfile);
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

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Extension", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                    EditorGUILayout.LabelField("Pattern", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Destination", EditorStyles.miniBoldLabel);
                }

                PristinePipelineWindow.DrawDivider();

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

        // ── Scope section ────────────────────────────────────────────────────────

        private void DrawScopeSection()
        {
            _scopeExpanded = EditorGUILayout.Foldout(
                _scopeExpanded, "Scope", true, EditorStyles.foldoutHeader);

            if (!_scopeExpanded) return;

            EditorGUILayout.Space(4);

            // ── Always-on zones (read-only display) ───────────────────────────────
            EditorGUILayout.LabelField("Always included", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Zone A
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent("●", "Files dropped directly into the Assets/ folder " +
                            "via the Unity menu or Project window root. Non-recursive."),
                        GUILayout.Width(14));
                    EditorGUILayout.LabelField("Assets/  (top level only)",
                        EditorStyles.miniLabel);
                }

                // Zone B
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent("●", "Your project's working directory. " +
                            "Set this in the Active Root bar at the top of the window."),
                        GUILayout.Width(14));

                    string root = ToolSettings.ActiveRootPath;
                    EditorGUILayout.LabelField(
                        $"{root}/  (Active Root, recursive)",
                        EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(6);

            // ── Additional opt-in paths ───────────────────────────────────────────
            EditorGUILayout.LabelField(
                new GUIContent("Additional folders",
                    "Opt-in folders processed recursively. " +
                    "Use this for content you own that lives outside the Active Root."),
                EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2);

            _additionalPathsList?.DoLayoutList();

            EditorGUILayout.Space(2);
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
                    AssetMappingProfile clone = AssetOrganizerUtility.CloneProfile(selected);
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

        private void DrawCreateEditMode(EditorWindow parentWindow)
        {
            bool saved = _creator.Draw(out AssetMappingProfile savedProfile, out bool wantsBack);

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
        
        // ── Additional scope paths helpers ────────────────────────────────────────

        private void LoadAdditionalPaths()
        {
            _additionalPaths = ToolSettings.Organizer_AdditionalScopePaths;
        }

        private void FlushAdditionalPaths()
        {
            ToolSettings.Organizer_AdditionalScopePaths = _additionalPaths;
        }

        private void BuildAdditionalPathsList()
        {
            _additionalPathsList = new ReorderableList(
                _additionalPaths, typeof(string),
                draggable: true,
                displayHeader: false,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= _additionalPaths.Count) return;

                    rect.y += 2;
                    float lineH = EditorGUIUtility.singleLineHeight;

                    // Reserve space on the right for the folder-picker button
                    float btnW = 26f;
                    float gap = 4f;
                    float fieldW = rect.width - btnW - gap;

                    EditorGUI.BeginChangeCheck();

                    _additionalPaths[index] = EditorGUI.TextField(
                        new Rect(rect.x, rect.y, fieldW, lineH),
                        _additionalPaths[index]);

                    // Folder-picker button — opens the OS folder panel and converts
                    // the result to a Unity asset path automatically.
                    if (GUI.Button(new Rect(rect.x + fieldW + gap, rect.y, btnW, lineH),
                        new GUIContent("…", "Pick a folder inside this project")))
                    {
                        string picked = EditorUtility.OpenFolderPanel(
                            "Select Scope Folder",
                            Application.dataPath,
                            "");

                        if (!string.IsNullOrEmpty(picked))
                        {
                            string projectRoot = Application.dataPath[..^"Assets".Length];
                            picked = picked.Replace("\\", "/");

                            if (picked.StartsWith(projectRoot))
                            {
                                string relative = picked[projectRoot.Length..].TrimStart('/');
                                if (relative.StartsWith("Assets"))
                                {
                                    _additionalPaths[index] = relative;
                                    GUI.changed = true;
                                }
                                else
                                {
                                    EditorUtility.DisplayDialog(
                                        "Invalid Folder",
                                        "The selected folder must be inside Assets/.",
                                        "OK");
                                }
                            }
                            else
                            {
                                EditorUtility.DisplayDialog(
                                    "Invalid Folder",
                                    "The selected folder must be inside this Unity project.",
                                    "OK");
                            }
                        }
                    }

                    // Warn when path looks invalid
                    bool pathInvalid = !string.IsNullOrWhiteSpace(_additionalPaths[index])
                        && !_additionalPaths[index].StartsWith("Assets/",
                            System.StringComparison.OrdinalIgnoreCase);

                    if (pathInvalid)
                    {
                        float warnY = rect.y + lineH + 2f;
                        EditorGUI.HelpBox(
                            new Rect(rect.x, warnY, rect.width, lineH + 4),
                            "Path must start with Assets/",
                            MessageType.Warning);
                    }

                    if (EditorGUI.EndChangeCheck())
                        FlushAdditionalPaths();
                },

                elementHeightCallback = index =>
                {
                    float height = EditorGUIUtility.singleLineHeight + 6f;
                    if (index >= 0 && index < _additionalPaths.Count)
                    {
                        bool pathInvalid = !string.IsNullOrWhiteSpace(_additionalPaths[index])
                            && !_additionalPaths[index].StartsWith("Assets/",
                                System.StringComparison.OrdinalIgnoreCase);
                        if (pathInvalid)
                            height += EditorGUIUtility.singleLineHeight + 6f;
                    }
                    return height;
                },

                onAddCallback = _ =>
                {
                    _additionalPaths.Add("");
                    FlushAdditionalPaths();
                },

                onRemoveCallback = list =>
                {
                    _additionalPaths.RemoveAt(list.index);
                    FlushAdditionalPaths();
                },

                onReorderCallback = _ => FlushAdditionalPaths()
            };
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