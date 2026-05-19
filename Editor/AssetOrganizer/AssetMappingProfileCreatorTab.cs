using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Inline create/edit UI for AssetMappingProfile assets.
    /// Rendered by AssetOrganizerTab when in create or edit mode — not a separate window.
    /// Owns no persistent state beyond what the user is currently editing.
    /// </summary>
    public class AssetMappingProfileCreatorTab
    {
        // ── State ────────────────────────────────────────────────────────────────

        private string _profileName = "";
        private string _description = "";
        private List<MappingRule> _rules = new();
        private ReorderableList _reorderableList;

        private bool _isEditMode = false;
        private AssetMappingProfile _editTarget = null;

        // Dirty flag — true when there are unsaved changes
        private bool _isDirty = false;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Opens the creator in new-profile mode.</summary>
        public void BeginCreate()
        {
            _isEditMode = false;
            _editTarget = null;
            _profileName = "";
            _description = "";
            _rules = new List<MappingRule>();
            _isDirty = false;
            BuildReorderableList();
        }

        /// <summary>Opens the creator loaded with an existing profile for editing.</summary>
        public void BeginEdit(AssetMappingProfile profile)
        {
            _isEditMode = true;
            _editTarget = profile;
            _profileName = profile.profileName;
            _description = profile.description;
            _rules = new List<MappingRule>(profile.Rules);
            _isDirty = false;
            BuildReorderableList();
        }

        /// <summary>
        /// Draws the creator UI. Returns true when the user confirms a save,
        /// signalling AssetOrganizerTab to return to list mode and refresh.
        /// Returns false when the user presses Back (with or without a discard confirmation).
        /// The out parameter carries the saved profile on success, or null on back/cancel.
        /// </summary>
        public bool Draw(out AssetMappingProfile savedProfile, out bool wantsBack)
        {
            savedProfile = null;

            DrawCreatorHeader(out wantsBack);
            if (wantsBack) return false;

            PristinePipelineWindow.DrawDivider();

            DrawNameAndDescription();
            EditorGUILayout.Space(6);

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

            EditorGUILayout.Space(2);

            _description = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the profile selector."),
                _description);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

        // ── Rules list ───────────────────────────────────────────────────────────

        private void DrawRulesList()
        {
            EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Rules are evaluated in order — last match wins.",
                MessageType.None);
            EditorGUILayout.Space(2);

            _reorderableList?.DoLayoutList();

            if (_rules.Count == 0)
                EditorGUILayout.HelpBox("Add at least one rule.", MessageType.Warning);
        }

        private void BuildReorderableList()
        {
            _reorderableList = new ReorderableList(
                _rules, typeof(MappingRule),
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
                    if (index < 0 || index >= _rules.Count) return;

                    MappingRule rule = _rules[index];
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
                        new Rect(rect.x + ext + 4, rect.y, pat, rect.height),
                        rule.namePattern);

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
                    if (index < 0 || index >= _rules.Count) return height;
                    var messages = AssetOrganizerUtility.ValidateRule(_rules[index]);
                    height += messages.Count * (EditorGUIUtility.singleLineHeight + 6);
                    return height;
                },

                onAddCallback = _ => { _rules.Add(new MappingRule()); _isDirty = true; },
                onRemoveCallback = list => { _rules.RemoveAt(list.index); _isDirty = true; }
            };
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        private bool DrawSaveButton(out AssetMappingProfile savedProfile)
        {
            savedProfile = null;

            if (GUILayout.Button("Save Profile", GUILayout.Height(30)))
            {
                if (!ValidateBeforeSave())
                    return false;

                AssetMappingProfile profile = BuildOrUpdateProfile();
                AssetOrganizerUtility.SaveProfile(profile);
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

            if (_rules.Count == 0)
            {
                EditorUtility.DisplayDialog("Cannot Save", "Add at least one rule.", "OK");
                return false;
            }

            // Conflict check for new profiles only
            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.Organizer_ProfileSavePath,
                    _profileName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<AssetMappingProfile>(assetPath) != null)
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

        private AssetMappingProfile BuildOrUpdateProfile()
        {
            if (_isEditMode && _editTarget != null)
            {
                _editTarget.profileName = _profileName.Trim();
                _editTarget.description = _description.Trim();
                _editTarget.SetRules(_rules);
                return _editTarget;
            }

            var p = ScriptableObject.CreateInstance<AssetMappingProfile>();
            p.profileName = _profileName.Trim();
            p.description = _description.Trim();
            p.SetRules(_rules);
            return p;
        }
    }
}