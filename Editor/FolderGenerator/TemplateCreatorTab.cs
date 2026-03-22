using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Inline create/edit UI for FolderTemplate assets.
    /// Rendered by FolderGeneratorTab when in create or edit mode — not a separate window.
    /// Owns no persistent state beyond what the user is currently editing.
    /// </summary>
    public class TemplateCreatorTab
    {
        // ── State ────────────────────────────────────────────────────────────────

        private string _templateName = "";
        private string _templateDescription = "";
        private List<string> _folderPaths = new();
        private ReorderableList _reorderableList;

        private bool _isEditMode = false;
        private FolderTemplate _editTarget = null;

        // Dirty flag — true when there are unsaved changes
        private bool _isDirty = false;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Opens the creator in new-template mode.</summary>
        public void BeginCreate()
        {
            _isEditMode = false;
            _editTarget = null;
            _templateName = "";
            _templateDescription = "";
            _folderPaths = new List<string>();
            _isDirty = false;
            BuildReorderableList();
        }

        /// <summary>Opens the creator loaded with an existing template for editing.</summary>
        public void BeginEdit(FolderTemplate template)
        {
            _isEditMode = true;
            _editTarget = template;
            _templateName = template.templateName;
            _templateDescription = template.description;
            _folderPaths = new List<string>(template.FolderPaths);
            _isDirty = false;
            BuildReorderableList();
        }

        /// <summary>
        /// Draws the creator UI. Returns true when the user confirms a save,
        /// signalling FolderGeneratorTab to return to list mode and refresh.
        /// Returns false when the user presses Back (with or without a discard confirmation).
        /// The out parameter carries the saved template on success, or null on back/cancel.
        /// </summary>
        public bool Draw(out FolderTemplate savedTemplate, out bool wantsBack)
        {
            savedTemplate = null;

            DrawCreatorHeader(out wantsBack);
            if (wantsBack) return false;

            PristinePipelineWindow.DrawDivider();

            DrawNameAndDescription();
            EditorGUILayout.Space(6);

            DrawFolderList();
            EditorGUILayout.Space(10);

            PristinePipelineWindow.DrawDivider();

            if (DrawSaveButton(out savedTemplate))
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
                string heading = _isEditMode ? $"Edit  —  {_editTarget.templateName}" : "New Template";
                EditorGUILayout.LabelField(heading, headingStyle);
            }

            EditorGUILayout.Space(6);
        }

        // ── Name and description ─────────────────────────────────────────────────

        private void DrawNameAndDescription()
        {
            EditorGUI.BeginChangeCheck();

            _templateName = EditorGUILayout.TextField(
                new GUIContent("Template Name", "Used as the asset filename."),
                _templateName);

            if (string.IsNullOrWhiteSpace(_templateName))
                EditorGUILayout.HelpBox("Template name cannot be empty.", MessageType.Error);

            EditorGUILayout.Space(2);

            _templateDescription = EditorGUILayout.TextField(
                new GUIContent("Description", "Short summary shown in the template selector."),
                _templateDescription);

            if (EditorGUI.EndChangeCheck())
                _isDirty = true;
        }

        // ── Folder list ──────────────────────────────────────────────────────────

        private void DrawFolderList()
        {
            EditorGUILayout.LabelField("Folder Paths", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _reorderableList?.DoLayoutList();

            if (_folderPaths.Count == 0)
                EditorGUILayout.HelpBox("Add at least one folder path.", MessageType.Warning);
        }

        private void BuildReorderableList()
        {
            _reorderableList = new ReorderableList(
                _folderPaths, typeof(string),
                draggable: true,
                displayHeader: false,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;

                    EditorGUI.BeginChangeCheck();
                    _folderPaths[index] = EditorGUI.TextField(rect, _folderPaths[index]);
                    if (EditorGUI.EndChangeCheck())
                        _isDirty = true;

                    // Validation warnings drawn below the field
                    bool hasInvalid = FolderGeneratorUtility.HasInvalidCharacters(_folderPaths[index]);
                    bool hasDuplicate = _folderPaths.Count(f => f.Trim() == _folderPaths[index].Trim()) > 1;

                    float warningY = rect.y + EditorGUIUtility.singleLineHeight + 2;

                    if (hasInvalid)
                    {
                        Rect wRect = new(rect.x, warningY, rect.width, EditorGUIUtility.singleLineHeight);
                        EditorGUI.HelpBox(wRect, "Path contains invalid characters.", MessageType.Warning);
                        warningY += EditorGUIUtility.singleLineHeight + 2;
                    }

                    if (hasDuplicate)
                    {
                        Rect wRect = new(rect.x, warningY, rect.width, EditorGUIUtility.singleLineHeight);
                        EditorGUI.HelpBox(wRect, "Duplicate folder path.", MessageType.Warning);
                    }
                },

                elementHeightCallback = index =>
                    {
                        float height = EditorGUIUtility.singleLineHeight + 4;

                        if (index < 0 || index >= _folderPaths.Count) return height;

                        if (FolderGeneratorUtility.HasInvalidCharacters(_folderPaths[index]))
                            height += EditorGUIUtility.singleLineHeight + 4;

                        if (_folderPaths.Count(f => f.Trim() == _folderPaths[index].Trim()) > 1)
                            height += EditorGUIUtility.singleLineHeight + 4;

                        return height;
                    },

                onAddCallback = list => { _folderPaths.Add(""); _isDirty = true; },
                onRemoveCallback = list => { _folderPaths.RemoveAt(list.index); _isDirty = true; }
            };
        }

        // ── Save ─────────────────────────────────────────────────────────────────

        private bool DrawSaveButton(out FolderTemplate savedTemplate)
        {
            savedTemplate = null;

            if (GUILayout.Button("Save Template", GUILayout.Height(30)))
            {
                if (!ValidateBeforeSave())
                    return false;

                FolderTemplate template = BuildOrUpdateTemplate();
                FolderGeneratorUtility.SaveTemplate(template);
                savedTemplate = template;
                _isDirty = false;
                return true;
            }

            return false;
        }

        private bool ValidateBeforeSave()
        {
            if (string.IsNullOrWhiteSpace(_templateName))
            {
                EditorUtility.DisplayDialog("Cannot Save", "Template name cannot be empty.", "OK");
                return false;
            }

            // Conflict check for new templates only
            if (!_isEditMode)
            {
                string assetPath = Path.Combine(
                    ToolSettings.FolderGen_TemplateSavePath,
                    _templateName.Trim() + ".asset").Replace("\\", "/");

                if (AssetDatabase.LoadAssetAtPath<FolderTemplate>(assetPath) != null)
                {
                    EditorUtility.DisplayDialog(
                        "Duplicate Template",
                        $"A template named '{_templateName}' already exists. Choose a different name.",
                        "OK");
                    return false;
                }
            }            

            if (_folderPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Cannot Save", "Add at least one folder path.", "OK");
                return false;
            }            

            return true;
        }

        private FolderTemplate BuildOrUpdateTemplate()
        {
            // Sanitize: remove blanks and duplicates
            var sanitized = _folderPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct()
                .ToList();

            if (_isEditMode && _editTarget != null)
            {
                _editTarget.templateName = _templateName.Trim();
                _editTarget.description = _templateDescription.Trim();
                _editTarget.SetFolderPaths(sanitized);
                return _editTarget;
            }

            var t = ScriptableObject.CreateInstance<FolderTemplate>();
            t.templateName = _templateName.Trim();
            t.description = _templateDescription.Trim();
            t.SetFolderPaths(sanitized);
            return t;
        }
    }
}