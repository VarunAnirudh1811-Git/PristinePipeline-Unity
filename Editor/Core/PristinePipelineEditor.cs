using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// The main Editor window for Pristine Pipeline.
    /// This is the shell only — tab content is delegated to each tool's own DrawTab() method.
    /// Adding a new tool = add a TabID entry, a label, and a DrawTab() call. Nothing else changes.
    /// </summary>
    public class PristinePipelineWindow : EditorWindow
    {
        // ── Tab registry ─────────────────────────────────────────────────────────

        private static class TabID
        {
            public const int FolderGenerator = 0;
            public const int AssetOrganizer = 1;
            public const int FBXImporter = 2;
            public const int Settings = 3;
            public const int Count = 4;
        }

        private static readonly string[] TabLabels =
        {
            "Folder Generator",
            "Asset Organizer",
            "FBX Importer",
            "Settings"
        };

        // ── State ────────────────────────────────────────────────────────────────

        private int _activeTab;
        private Vector2 _scrollPosition;
        private FolderGeneratorTab _folderGeneratorTab;

        // ── Entry point ──────────────────────────────────────────────────────────

        [MenuItem(ToolInfo.MenuRoot)]
        public static void Open()
        {
            PristinePipelineWindow window = GetWindow<PristinePipelineWindow>();
            window.titleContent = new GUIContent(ToolInfo.ToolName);
            window.minSize = new Vector2(440, 360);
            window.Show();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _activeTab = ToolSettings.ActiveTab;
            _folderGeneratorTab = new FolderGeneratorTab();
            _folderGeneratorTab.OnEnable();
        }

        private void OnDisable()
        {
            ToolSettings.ActiveTab = _activeTab;
            _folderGeneratorTab.OnDisable();
        }

        // ── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawHeader();
            DrawTabBar();
            DrawDivider();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawActiveTab();
            EditorGUILayout.EndScrollView();
        }

        // ── Header ───────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIStyle titleStyle = new GUIStyle(EditorStyles.largeLabel)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 24
                };

                EditorGUILayout.LabelField(ToolInfo.ToolName, titleStyle);
                GUILayout.FlexibleSpace();

                GUIStyle versionStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };

                EditorGUILayout.LabelField(
                    $"v{ToolInfo.Version}  ·  {ToolInfo.Author}",
                    versionStyle,
                    GUILayout.Width(160));
            }

            EditorGUILayout.Space(4);
        }

        // ── Tab bar ──────────────────────────────────────────────────────────────

        private void DrawTabBar()
        {
            int selected = GUILayout.Toolbar(_activeTab, TabLabels);

            if (selected != _activeTab)
            {
                _activeTab = selected;
                _scrollPosition = Vector2.zero;
            }
        }

        // ── Divider ──────────────────────────────────────────────────────────────
        // Called from other classes, hence public

        public static void DrawDivider()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
            EditorGUILayout.Space(6);
        }

        // ── Tab dispatch ─────────────────────────────────────────────────────────

        private void DrawActiveTab()
        {
            switch (_activeTab)
            {
                case TabID.FolderGenerator: _folderGeneratorTab.Draw(this);               break;
                case TabID.AssetOrganizer: DrawPlaceholder("Asset Organizer", "Phase 3"); break;
                case TabID.FBXImporter: DrawPlaceholder("FBX Importer", "Phase 4");       break;
                case TabID.Settings: DrawSettingsTab();                                   break;
            }
        }

        // ── Placeholder (removed once a phase is implemented) ────────────────────

        private void DrawPlaceholder(string toolName, string phase)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.HelpBox(
                $"{toolName} — coming in {phase}.",
                MessageType.Info);
        }

        // ── Settings tab ─────────────────────────────────────────────────────────

        private void DrawSettingsTab()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUILayout.LabelField("Tool", ToolInfo.ToolName, EditorStyles.label);
                EditorGUILayout.LabelField("Version", ToolInfo.Version, EditorStyles.label);
                EditorGUILayout.LabelField("Author", ToolInfo.Author, EditorStyles.label);
            }

            DrawDivider();

            EditorGUILayout.LabelField("Folder Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUILayout.LabelField(
                    new GUIContent("Template Save Path", "Where user-created templates are saved."),
                    EditorStyles.label);

                string current = ToolSettings.FolderGen_TemplateSavePath;
                string updated = EditorGUILayout.TextField(current);

                if (updated != current)
                    ToolSettings.FolderGen_TemplateSavePath = updated;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                        ToolSettings.FolderGen_TemplateSavePath = ToolInfo.DefaultTemplateSavePath;
                }
            }

            DrawDivider();

            EditorGUILayout.LabelField("Danger Zone", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Reset All clears every Pristine Pipeline EditorPrefs entry on this machine. " +
                "ScriptableObject profiles and templates in your project are not affected.",
                MessageType.Warning);

            EditorGUILayout.Space(4);

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);

            if (GUILayout.Button("Reset All Settings", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog(
                    "Reset All Settings",
                    $"Clear all {ToolInfo.ToolName} EditorPrefs on this machine?",
                    "Reset", "Cancel"))
                {
                    ToolSettings.ResetAll();
                    _activeTab = 0;
                    Debug.Log($"{ToolInfo.LogPrefix} All settings reset.");
                }
            }

            GUI.backgroundColor = prev;
            EditorGUILayout.Space(8);
        }
    }
}