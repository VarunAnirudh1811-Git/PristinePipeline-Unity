using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Draws the Folder Generator tab inside PristinePipelineWindow.
    /// Owns the tab's UI state and mode transitions (list ↔ create/edit).
    /// All actual asset operations are delegated to FolderGeneratorUtility.
    /// </summary>
    public class FolderGeneratorTab
    {
        // ── Mode ─────────────────────────────────────────────────────────────────

        private enum Mode { List, CreateEdit }
        private Mode _mode = Mode.List;

        // ── List mode state ──────────────────────────────────────────────────────

        private List<FolderTemplate> _templates = new List<FolderTemplate>();
        private string[] _templateNames = new string[0];
        private int _selectedIndex = 0;

        private bool _useProjectRoot = false;
        private string _projectName = "";
        private bool _addKeepFiles = true;

        // ── Tree view ────────────────────────────────────────────────────────────

        private FolderTreeView _treeView;
        private TreeViewState<int> _treeViewState;

        // Tracks the last template + root combination loaded into the tree
        // so Reload is only called when something actually changed, not every frame.
        private FolderTemplate _lastTreeTemplate;
        private string _lastTreeRootPath;

        // Fixed height reserved for the tree view rect inside the scroll view.
        private const float TreeViewHeight = 220f;

        // ── Creator state ────────────────────────────────────────────────────────

        private readonly TemplateCreatorTab _creator = new TemplateCreatorTab();

        // ── Lifecycle (called by PristinePipelineWindow) ─────────────────────────

        public void OnEnable()
        {
            // Tree view must be initialized before RefreshTemplates —
            // DrawFolderPreview will call Reload on it immediately after.
            _treeViewState = new TreeViewState<int>();
            _treeView = new FolderTreeView(_treeViewState);
            RefreshTemplates();
        }

        public void OnDisable() { }

        // ── Entry point ──────────────────────────────────────────────────────────

        /// <summary>Called every OnGUI frame by PristinePipelineWindow.</summary>
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
            DrawTemplateSelector(parentWindow);
            PristinePipelineWindow.DrawDivider();
            DrawFolderPreview();
            PristinePipelineWindow.DrawDivider();
            DrawGenerationOptions();
            PristinePipelineWindow.DrawDivider();
            DrawGenerateButton(parentWindow);
        }

        // ── Template selector ────────────────────────────────────────────────────

        private void DrawTemplateSelector(EditorWindow parentWindow)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Template", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_templates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No templates found. Create one below or check the template save path in Settings.",
                    MessageType.Warning);

                EditorGUILayout.Space(4);
                DrawTemplateManagementButtons(null, parentWindow);
                return;
            }

            int newIndex = EditorGUILayout.Popup(
                new GUIContent("Active Template"),
                _selectedIndex,
                _templateNames);

            if (newIndex != _selectedIndex)
            {
                _selectedIndex = newIndex;
                ProfileRegistry.SetActiveFolderTemplate(ActiveTemplate);
                InvalidateTreeCache();
            }

            EditorGUILayout.Space(2);

            if (ActiveTemplate != null)
                EditorGUILayout.LabelField(ActiveTemplate.description, EditorStyles.helpBox);

            EditorGUILayout.Space(6);
            DrawTemplateManagementButtons(ActiveTemplate, parentWindow);
        }

        private void DrawTemplateManagementButtons(FolderTemplate selected, EditorWindow parentWindow)
        {
            float halfWidth = (EditorGUIUtility.currentViewWidth - 24f) / 2f;

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
                    int cloneIndex = _templates.IndexOf(clone);
                    if (cloneIndex >= 0) _selectedIndex = cloneIndex;
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

            EditorGUILayout.Space(4);

            // Row 3 — Import / Export
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

            EditorGUILayout.Space(2);

            if (selected != null && !selected.isBuiltIn)
            {
                if (GUILayout.Button("Select Asset in Project"))
                    EditorGUIUtility.PingObject(selected);
            }
        }

        // ── Folder preview (tree view) ───────────────────────────────────────────

        private void DrawFolderPreview()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                // Guard against _treeView being null before buttons are drawn
                GUI.enabled = _treeView != null;

                if (GUILayout.Button("Expand All", GUILayout.Width(82)))
                    _treeView.ExpandAll();

                if (GUILayout.Button("Collapse All", GUILayout.Width(86)))
                    _treeView.CollapseAll();

                GUI.enabled = true;
            }

            EditorGUILayout.Space(2);

            if (_treeView == null)
            {
                EditorGUILayout.HelpBox("Tree view not initialized.", MessageType.Warning);
                return;
            }

            if (ActiveTemplate == null || ActiveTemplate.FolderPaths.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No template selected or template has no paths.",
                    MessageType.Info);
                return;
            }

            // Reload only when template or root path actually changed
            string currentRoot = FolderGeneratorUtility.ResolveRootPath(_useProjectRoot, _projectName);

            if (ActiveTemplate != _lastTreeTemplate || currentRoot != _lastTreeRootPath)
            {
                _treeView.Reload(ActiveTemplate, currentRoot);
                _lastTreeTemplate = ActiveTemplate;
                _lastTreeRootPath = currentRoot;
            }

            Rect treeRect = EditorGUILayout.GetControlRect(false, TreeViewHeight);
            _treeView.Draw(treeRect);
        }

        // ── Generation options ───────────────────────────────────────────────────

        private void DrawGenerationOptions()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _useProjectRoot = EditorGUILayout.Toggle(
                new GUIContent("Use Project Root Folder",
                    "Nest all folders under Assets/<ProjectName> instead of directly under Assets."),
                _useProjectRoot);

            if (_useProjectRoot)
            {
                EditorGUI.BeginChangeCheck();

                _projectName = EditorGUILayout.TextField(
                    new GUIContent("Project Name", "The subfolder created under Assets/."),
                    _projectName);

                if (EditorGUI.EndChangeCheck())
                    InvalidateTreeCache();

                if (string.IsNullOrWhiteSpace(_projectName))
                    EditorGUILayout.HelpBox(
                        "Project name cannot be empty when using project root.",
                        MessageType.Warning);
            }

            EditorGUILayout.Space(2);

            _addKeepFiles = EditorGUILayout.Toggle(
                new GUIContent("Add .keep in Empty Folders",
                    "Places a .keep file in empty folders so they are tracked by version control."),
                _addKeepFiles);
        }

        // ── Generate button ──────────────────────────────────────────────────────

        private void DrawGenerateButton(EditorWindow parentWindow)
        {
            EditorGUILayout.Space(4);

            bool canGenerate = ActiveTemplate != null &&
                               (!_useProjectRoot || !string.IsNullOrWhiteSpace(_projectName));

            GUI.enabled = canGenerate;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = canGenerate
                ? new Color(0.3f, 0.8f, 0.4f)
                : GUI.backgroundColor;

            if (GUILayout.Button("Generate Folder Structure", GUILayout.Height(36)))
            {
                string rootPath = FolderGeneratorUtility.ResolveRootPath(_useProjectRoot, _projectName);
                FolderGeneratorUtility.CreateFolders(ActiveTemplate, rootPath, _addKeepFiles);

                EditorUtility.DisplayDialog(
                    "Folders Created",
                    $"Folder structure generated under:\n{rootPath}",
                    "OK");
            }

            GUI.backgroundColor = prev;
            GUI.enabled = true;
            EditorGUILayout.Space(8);
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
                InvalidateTreeCache();
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
            _templates = FolderGeneratorUtility.LoadAllTemplates();
            _templateNames = FolderGeneratorUtility.GetTemplateDisplayNames(_templates);

            FolderTemplate active = ProfileRegistry.GetActiveFolderTemplate();
            int idx = active != null ? _templates.IndexOf(active) : -1;
            _selectedIndex = idx >= 0 ? idx : 0;

            if (ActiveTemplate != null)
                ProfileRegistry.SetActiveFolderTemplate(ActiveTemplate);

            InvalidateTreeCache();
        }

        /// <summary>
        /// Clears the cached template/root so the tree view reloads on next draw.
        /// </summary>
        private void InvalidateTreeCache()
        {
            _lastTreeTemplate = null;
            _lastTreeRootPath = null;
        }
    }
}