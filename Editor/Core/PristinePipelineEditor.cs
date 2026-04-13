using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
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
        private AssetOrganizerTab _assetOrganizerTab;
        private FBXImporterTab _fbxImporterTab;

        // ── Active Root bar state ────────────────────────────────────────────────

        // When true the bar expands to show the inline new-folder text field.
        private bool _creatingNewRoot = false;
        private string _newRootName = "";

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
            _assetOrganizerTab = new AssetOrganizerTab();
            _assetOrganizerTab.OnEnable();
            _fbxImporterTab = new FBXImporterTab();
            _fbxImporterTab.OnEnable();
        }

        private void OnDisable()
        {
            ToolSettings.ActiveTab = _activeTab;
            _folderGeneratorTab.OnDisable();
            _assetOrganizerTab.OnDisable();
            _fbxImporterTab.OnDisable();
        }

        // ── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawHeader();            
            DrawTabBar();
            if (_activeTab != TabID.Settings)
                DrawActiveRootBar();
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
                GUIStyle titleStyle = new(EditorStyles.largeLabel)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 24
                };

                EditorGUILayout.LabelField(ToolInfo.ToolName, titleStyle);
                GUILayout.FlexibleSpace();

                GUIStyle versionStyle = new(EditorStyles.miniLabel)
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

        // ── Active Root bar ──────────────────────────────────────────────────────

        private void DrawActiveRootBar()
        {
            EditorGUILayout.Space(2);

            string root = ToolSettings.ActiveRootPath;
            bool rootExists = RootFolderExists(root);

            // Auto-recover: if the persisted root has been deleted, fall back silently
            // so every tool keeps working. We do not reset to "Assets" automatically
            // (that would hide the problem); instead we show a warning and let the
            // user decide.
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ── Row 1: label + path display ───────────────────────────────────
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent("Active Root",
                            "All tools operate inside this folder. " +
                            "Folder paths in profiles are resolved relative to this root."),
                        GUILayout.Width(78));

                    // Colour the path red when the folder no longer exists on disk
                    GUIStyle pathStyle = new(EditorStyles.boldLabel);
                    if (!rootExists)
                        pathStyle.normal.textColor = new Color(0.9f, 0.35f, 0.35f);

                    EditorGUILayout.LabelField(root, pathStyle);

                    // Ping — disabled when folder is missing (nothing to ping)
                    GUI.enabled = rootExists;
                    if (GUILayout.Button(
                        new GUIContent("◉", "Select this folder in the Project window"), GUILayout.Width(30), GUILayout.Height(18)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(root);
                        if (obj != null)
                            EditorGUIUtility.PingObject(obj);
                    }
                    GUI.enabled = true;
                }

                // ── Missing-root warning ──────────────────────────────────────────
                if (!rootExists)
                {
                    EditorGUILayout.HelpBox(
                        "The Active Root folder no longer exists on disk. " +
                        "Select an existing folder or reset to Assets.",
                        MessageType.Warning);
                }

                // ── Row 2: action buttons (always have full width) ────────────────
                if (!_creatingNewRoot)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        float buttonWidth = (EditorGUIUtility.currentViewWidth - 20) / 3; // 20 for padding

                        // New Folder — enter inline creation mode
                        if (GUILayout.Button(
                            new GUIContent("New Folder", "Create a new folder and set it as the Active Root"), GUILayout.Width(buttonWidth)))
                        {
                            _creatingNewRoot = true;
                            _newRootName = "";
                        }

                        // Change — open OS folder picker
                        if (GUILayout.Button(
                            new GUIContent("Change", "Pick an existing folder as the Active Root"), GUILayout.Width(buttonWidth)))
                        {
                            string absolute = EditorUtility.OpenFolderPanel(
                                "Select Active Root",
                                Application.dataPath,
                                "");

                            if (!string.IsNullOrEmpty(absolute))
                                TrySetRootFromAbsolutePath(absolute);
                        }
                        
                        // Reset
                        if (GUILayout.Button(
                            new GUIContent("Reset", "Reset Active Root to Assets"), GUILayout.Width(buttonWidth)))
                        {
                            ToolSettings.ActiveRootPath = "Assets";
                            _creatingNewRoot = false;
                        }
                    }
                }
                else
                {
                    // ── Inline new-folder creation ────────────────────────────────
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            new GUIContent("Folder Name",
                                "Name of the new folder created directly inside Assets/"),
                            GUILayout.Width(90));

                        GUI.SetNextControlName("NewRootField");
                        _newRootName = EditorGUILayout.TextField(_newRootName);
                    }

                    bool nameIsValid = IsValidFolderName(_newRootName);

                    if (!nameIsValid && !string.IsNullOrEmpty(_newRootName))
                        EditorGUILayout.HelpBox(
                            "Folder name contains invalid characters.",
                            MessageType.Warning);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = nameIsValid;
                        if (GUILayout.Button("Create & Set"))
                        {
                            string newPath = CreateRootFolder(_newRootName.Trim());
                            if (newPath != null)
                            {
                                ToolSettings.ActiveRootPath = newPath;
                                _creatingNewRoot = false;
                                _newRootName = "";
                            }
                        }
                        GUI.enabled = true;

                        if (GUILayout.Button("Cancel"))
                        {
                            _creatingNewRoot = false;
                            _newRootName = "";
                        }
                    }
                }
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
                case TabID.FolderGenerator: _folderGeneratorTab.Draw(this); break;
                case TabID.AssetOrganizer: _assetOrganizerTab.Draw(this);   break;
                case TabID.FBXImporter: _fbxImporterTab.Draw(this);         break;
                case TabID.Settings: DrawSettingsTab();                     break;
            }
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
                    new GUIContent("Folder Template Save Path",
                        "Where user-created templates are saved."),
                    EditorStyles.label);

                string current = ToolSettings.FolderGen_TemplateSavePath;
                string updated = EditorGUILayout.TextField(current);
                if (updated != current) ToolSettings.FolderGen_TemplateSavePath = updated;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                        ToolSettings.FolderGen_TemplateSavePath = ToolInfo.DefaultTemplateSavePath;
                }
            }

            DrawDivider();

            EditorGUILayout.LabelField("Asset Organizer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUILayout.LabelField(
                    new GUIContent("Asset Profile Map Save Path",
                        "Where user-created profile maps are saved."),
                    EditorStyles.label);

                string current = ToolSettings.Organizer_ProfileSavePath;
                string updated = EditorGUILayout.TextField(current);
                if (updated != current) ToolSettings.Organizer_ProfileSavePath = updated;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                        ToolSettings.Organizer_ProfileSavePath = ToolInfo.DefaultAssetMappingProfileSavePath;
                }
            }

            DrawDivider();

            EditorGUILayout.LabelField("FBX Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUILayout.LabelField(
                    new GUIContent("FBX Import Profile Save Path",
                        "Where user-created import profiles are saved."),
                    EditorStyles.label);

                string current = ToolSettings.FBX_ProfileSavePath;
                string updated = EditorGUILayout.TextField(current);
                if (updated != current) ToolSettings.FBX_ProfileSavePath = updated;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                        ToolSettings.FBX_ProfileSavePath = ToolInfo.DefaultFBXImportProfileSavePath;
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

        // ── Active Root helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Checks whether the Active Root folder actually exists on disk.
        /// Uses Directory.Exists on the absolute filesystem path so that folders
        /// deleted outside Unity (OS file manager, Git clean, etc.) are detected
        /// immediately — without waiting for an AssetDatabase reimport.
        /// </summary>
        private static bool RootFolderExists(string unityAssetPath)
        {
            if (string.IsNullOrWhiteSpace(unityAssetPath)) return false;

            string projectRoot = Application.dataPath[..^"Assets".Length];
            string absolute = Path.Combine(projectRoot, unityAssetPath).Replace("\\", "/");
            return Directory.Exists(absolute);
        }

        /// <summary>
        /// Converts an absolute filesystem path (from OpenFolderPanel) to a Unity
        /// asset path and assigns it to ActiveRootPath if it is inside this project.
        /// Shows a dialog and does nothing if the path is outside the project.
        /// </summary>
        private static void TrySetRootFromAbsolutePath(string absolute)
        {
            string projectRoot = Application.dataPath[..^"Assets".Length];
            absolute = absolute.Replace("\\", "/");

            if (!absolute.StartsWith(projectRoot))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Path",
                    "The selected folder must be inside this Unity project.",
                    "OK");
                return;
            }

            string relative = absolute[projectRoot.Length..].TrimStart('/');

            if (!relative.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Path",
                    "The selected folder must be inside the Assets folder.",
                    "OK");
                return;
            }

            ToolSettings.ActiveRootPath = relative;
        }

        /// <summary>
        /// Creates a new folder directly inside Assets/ using AssetDatabase.CreateFolder,
        /// then registers it with the AssetDatabase. Returns the new Unity asset path
        /// (e.g. "Assets/GameA"), or null if creation failed.
        /// </summary>
        private static string CreateRootFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return null;

            string newPath = "Assets/" + folderName;

            // Already exists — just use it
            if (AssetDatabase.IsValidFolder(newPath))
            {
                Debug.Log($"{ToolInfo.LogPrefix} Folder already exists, setting as root: {newPath}");
                return newPath;
            }

            string guid = AssetDatabase.CreateFolder("Assets", folderName);

            if (string.IsNullOrEmpty(guid))
            {
                EditorUtility.DisplayDialog(
                    "Could Not Create Folder",
                    $"Unity could not create the folder '{folderName}' inside Assets/. " +
                    "Check that the name contains no invalid characters.",
                    "OK");
                return null;
            }

            AssetDatabase.Refresh();
            Debug.Log($"{ToolInfo.LogPrefix} Created root folder: {newPath}");
            return newPath;
        }

        /// <summary>
        /// Returns true when the string is a valid Unity folder name.
        /// Rejects empty strings and names containing characters that are illegal
        /// in both Unity asset paths and common filesystems.
        /// </summary>
        private static bool IsValidFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            foreach (char c in Path.GetInvalidFileNameChars())
                if (name.Contains(c)) return false;

            // Also reject forward-slash — callers should pass a single segment
            if (name.Contains('/') || name.Contains('\\')) return false;

            return true;
        }
    }
}