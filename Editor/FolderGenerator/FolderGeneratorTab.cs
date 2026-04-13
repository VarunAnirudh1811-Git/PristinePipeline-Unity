using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Draws the Folder Generator tab inside PristinePipelineWindow.
    ///
    /// UX changes (v1.1):
    ///   - "Generate Folder Structure" button promoted to the top of the tab,
    ///     below the template dropdown, so the primary action is immediately visible.
    ///   - Template management buttons (New, Edit, Duplicate, Delete, Import/Export)
    ///     moved into a collapsible "Manage Templates" section.
    ///   - "Select Asset in Project" moved inline with the template dropdown.
    ///   - Generation options kept between the dropdown and the preview so context
    ///     is clear before committing.
    ///   - Preview section unchanged (TreeView with fixed height).
    /// </summary>
    public class FolderGeneratorTab
    {
        // ── Mode ─────────────────────────────────────────────────────────────────

        private enum Mode { List, CreateEdit }
        private Mode _mode = Mode.List;

        // ── List mode state ──────────────────────────────────────────────────────

        private List<FolderTemplate> _templates = new();
        private string[] _templateNames = new string[0];
        private int _selectedIndex = 0;

        private bool _addKeepFiles;

        private bool _templateManagementExpanded = false;

        // ── Tree view ────────────────────────────────────────────────────────────

        private FolderTreeView _treeView;
        private TreeViewState<int> _treeViewState;

        private FolderTemplate _lastTreeTemplate;
        private string _lastTreeRootPath;

        private const float TreeViewHeight = 200f;

        // ── Creator state ────────────────────────────────────────────────────────

        private readonly TemplateCreatorTab _creator = new();

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void OnEnable()
        {
            _addKeepFiles   = ToolSettings.FolderGen_AddKeepFiles;

            _treeViewState = new TreeViewState<int>();
            _treeView      = new FolderTreeView(_treeViewState);

            RefreshTemplates();
            ForceTreeReload();
        }

        public void OnDisable() { }

        // ── Entry point ──────────────────────────────────────────────────────────

        public void Draw(EditorWindow parentWindow)
        {
            switch (_mode)
            {
                case Mode.List:       DrawListMode(parentWindow);       break;
                case Mode.CreateEdit: DrawCreateEditMode(parentWindow); break;
            }
        }

        // ── List mode ────────────────────────────────────────────────────────────

        private void DrawListMode(EditorWindow parentWindow)
        {
            // 1 — Template selector + primary action
            DrawTemplateSelectorAndAction();
            PristinePipelineWindow.DrawDivider();

            // 2 — Generation options (contextual, before preview)
            DrawGenerationOptions();
            PristinePipelineWindow.DrawDivider();

            // 3 — Preview tree
            DrawFolderPreview();

            // 4 — Template management (collapsible, non-critical)
            PristinePipelineWindow.DrawDivider();
            DrawTemplateManagementCollapsible(parentWindow);
        }

        // ── Template selector + primary action ───────────────────────────────────

        private void DrawTemplateSelectorAndAction()
        {
            // ── Primary action — promoted to top ─────────────────────────────────
            bool canGenerate = ActiveTemplate != null;

            GUI.enabled = canGenerate;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = canGenerate
                ? new Color(0.25f, 0.75f, 0.35f)
                : GUI.backgroundColor;

            if (GUILayout.Button(
                canGenerate
                    ? $"▶  Generate Folder Structure  ({ActiveTemplate.templateName})"
                    : "▶  Generate Folder Structure",
                GUILayout.Height(34)))
            {
                FolderGeneratorUtility.CreateFolders(ActiveTemplate, _addKeepFiles);

                EditorUtility.DisplayDialog(
                    "Folders Created",
                    $"Folder structure generated under:\n{ToolSettings.ActiveRootPath}",
                    "OK");
            }

            GUI.backgroundColor = prev;
            GUI.enabled = true;

            EditorGUILayout.Space(6);

            if (_templates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No templates found. Use 'Manage Templates' below to create one.",
                    MessageType.Warning);
                EditorGUILayout.Space(4);
                return;
            }

            // Compact single-row dropdown
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent("Template", "The folder structure template to generate."),
                    GUILayout.Width(100));

                int newIndex = EditorGUILayout.Popup(_selectedIndex, _templateNames);
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    ProfileRegistry.SetActiveFolderTemplate(ActiveTemplate);
                    InvalidateTreeCache();
                }

                // Ping button inline
                GUI.enabled = ActiveTemplate != null && !ActiveTemplate.isBuiltIn;
                if (GUILayout.Button(
                    new GUIContent("◉", "Select asset in Project window"),
                    GUILayout.Width(30), GUILayout.Height(18)))
                    EditorGUIUtility.PingObject(ActiveTemplate);
                GUI.enabled = true;
            }

            if (ActiveTemplate != null && !string.IsNullOrWhiteSpace(ActiveTemplate.description))
                EditorGUILayout.LabelField(ActiveTemplate.description, EditorStyles.helpBox);

            EditorGUILayout.Space(6);

            
        }

        // ── Generation options ───────────────────────────────────────────────────

        private void DrawGenerationOptions()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            bool newAddKeepFiles = EditorGUILayout.Toggle(
                new GUIContent("Add .keep in Empty Folders",
                    "Places a .keep file in empty folders so they are tracked by version control."),
                _addKeepFiles);

            if (newAddKeepFiles != _addKeepFiles)
            {
                _addKeepFiles = newAddKeepFiles;
                ToolSettings.FolderGen_AddKeepFiles = _addKeepFiles;
            }

            EditorGUILayout.Space(2);
        }

        // ── Folder preview (tree view) ───────────────────────────────────────────

        private void DrawFolderPreview()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Expand All",   GUILayout.Width(82))) _treeView.ExpandAll();
                if (GUILayout.Button("Collapse All", GUILayout.Width(86))) _treeView.CollapseAll();
            }

            EditorGUILayout.Space(2);

            if (ActiveTemplate == null || ActiveTemplate.FolderPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("No template selected or template has no paths.", MessageType.Info);
                return;
            }

            // Tree preview reflects the live Active Root
            string currentRoot = ToolSettings.ActiveRootPath;

            if (ActiveTemplate != _lastTreeTemplate || currentRoot != _lastTreeRootPath)
            {
                if (!EditorGUIUtility.editingTextField)
                {
                    _treeView.Reload(ActiveTemplate, currentRoot);

                    _lastTreeTemplate = ActiveTemplate;
                    _lastTreeRootPath = currentRoot;
                }
            }

            Rect treeRect = EditorGUILayout.GetControlRect(false, TreeViewHeight);
            _treeView.Draw(treeRect);
        }

        // ── Collapsible template management ──────────────────────────────────────

        private void DrawTemplateManagementCollapsible(EditorWindow parentWindow)
        {
            _templateManagementExpanded = EditorGUILayout.Foldout(
                _templateManagementExpanded,
                "Manage Templates",
                true,
                EditorStyles.foldoutHeader);

            if (!_templateManagementExpanded) return;

            EditorGUILayout.Space(4);
            DrawTemplateManagementButtons(ActiveTemplate, parentWindow);
        }

        private void DrawTemplateManagementButtons(FolderTemplate selected, EditorWindow parentWindow)
        {
            float halfWidth = (EditorGUIUtility.currentViewWidth - 9f) / 2f;

            // Row 1 — New / Edit
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Template", GUILayout.Width(halfWidth)))
                {
                    _creator.BeginCreate();
                    _mode = Mode.CreateEdit;
                    parentWindow.Repaint();
                }

                GUI.enabled = selected != null && !selected.isBuiltIn;
                if (GUILayout.Button("Edit Template", GUILayout.Width(halfWidth)))
                {
                    _creator.BeginEdit(selected);
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
                    FolderTemplate clone = FolderGeneratorUtility.CloneTemplate(selected);
                    RefreshTemplates();
                    int idx = _templates.IndexOf(clone);
                    if (idx >= 0) _selectedIndex = idx;
                }

                GUI.enabled = selected != null && !selected.isBuiltIn;

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

                if (GUILayout.Button("Delete", GUILayout.Width(halfWidth)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Delete Template",
                        $"Delete '{selected.templateName}'? This cannot be undone.",
                        "Delete", "Cancel"))
                    {
                        FolderGeneratorUtility.DeleteTemplate(selected);
                        RefreshTemplates();
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
                    FolderTemplate imported = FolderGeneratorUtility.ImportTemplate();
                    if (imported != null)
                    {
                        RefreshTemplates();
                        int idx = _templates.IndexOf(imported);
                        if (idx >= 0) _selectedIndex = idx;
                    }
                }

                GUI.enabled = selected != null;
                if (GUILayout.Button("Export JSON", GUILayout.Width(halfWidth)))
                    FolderGeneratorUtility.ExportTemplate(selected);
                GUI.enabled = true;
            }

            EditorGUILayout.Space(4);
        }

        // ── Create / Edit mode ───────────────────────────────────────────────────

        private void DrawCreateEditMode(EditorWindow parentWindow)
        {
            bool saved = _creator.Draw(out FolderTemplate savedTemplate, out bool wantsBack);

            if (saved && savedTemplate != null)
            {
                RefreshTemplates();
                int idx = _templates.IndexOf(savedTemplate);
                if (idx >= 0) _selectedIndex = idx;
                _mode = Mode.List;
                ForceTreeReload();
                parentWindow.Repaint();
            }
            else if (wantsBack)
            {
                _mode = Mode.List;
                parentWindow.Repaint();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private FolderTemplate ActiveTemplate =>
            (_templates.Count > 0 && _selectedIndex < _templates.Count)
                ? _templates[_selectedIndex]
                : null;

        private void RefreshTemplates()
        {
            _templates     = FolderGeneratorUtility.LoadAllTemplates();
            _templateNames = FolderGeneratorUtility.GetTemplateDisplayNames(_templates);

            FolderTemplate active = ProfileRegistry.GetActiveFolderTemplate();
            int idx = active != null ? _templates.IndexOf(active) : -1;
            _selectedIndex = idx >= 0 ? idx : 0;

            if (ActiveTemplate != null)
                ProfileRegistry.SetActiveFolderTemplate(ActiveTemplate);

            InvalidateTreeCache();
        }

        private void ForceTreeReload()
        {            
            _treeView.Reload(ActiveTemplate, ToolSettings.ActiveRootPath);
            _lastTreeTemplate = ActiveTemplate;
            _lastTreeRootPath = ToolSettings.ActiveRootPath;
        }

        private void InvalidateTreeCache()
        {
            _lastTreeTemplate = null;
            _lastTreeRootPath = null;
        }
    }
}