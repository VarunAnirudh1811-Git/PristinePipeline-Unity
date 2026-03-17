using UnityEditor;

namespace GlyphLabs
{
    /// <summary>
    /// Centralized EditorPrefs wrapper for all tools under Pristine Pipeline.
    /// All keys are namespaced under ToolInfo.SettingsPrefix to avoid collisions.
    /// No tool should call EditorPrefs directly — go through this class.
    /// </summary>
    public static class ToolSettings
    {
        // ── Key helpers ──────────────────────────────────────────────────────────

        private static string Key(string suffix) => ToolInfo.SettingsPrefix + "." + suffix;

        // ── Active tab ───────────────────────────────────────────────────────────

        private const string ActiveTabKey = "ActiveTab";

        public static int ActiveTab
        {
            get => EditorPrefs.GetInt(Key(ActiveTabKey), 0);
            set => EditorPrefs.SetInt(Key(ActiveTabKey), value);
        }

        // ── Folder Generator ─────────────────────────────────────────────────────

        private const string FolderGen_ActiveTemplateGuidKey = "FolderGen.ActiveTemplateGuid";
        private const string FolderGen_TemplateSavePathKey = "FolderGen.TemplateSavePath";

        public static string FolderGen_ActiveTemplateGuid
        {
            get => EditorPrefs.GetString(Key(FolderGen_ActiveTemplateGuidKey), string.Empty);
            set => EditorPrefs.SetString(Key(FolderGen_ActiveTemplateGuidKey), value);
        }

        /// <summary>
        /// Where user-created templates are saved. Defaults to ToolInfo.DefaultTemplateSavePath.
        /// Configurable from the Settings tab.
        /// </summary>
        public static string FolderGen_TemplateSavePath
        {
            get => EditorPrefs.GetString(Key(FolderGen_TemplateSavePathKey), ToolInfo.DefaultTemplateSavePath);
            set => EditorPrefs.SetString(Key(FolderGen_TemplateSavePathKey), value);
        }

        // ── Asset Organizer ──────────────────────────────────────────────────────

        private const string Organizer_EnabledKey = "Organizer.Enabled";
        private const string Organizer_ActiveProfileGuidKey = "Organizer.ActiveProfileGuid";

        public static bool Organizer_Enabled
        {
            get => EditorPrefs.GetBool(Key(Organizer_EnabledKey), true);
            set => EditorPrefs.SetBool(Key(Organizer_EnabledKey), value);
        }

        public static string Organizer_ActiveProfileGuid
        {
            get => EditorPrefs.GetString(Key(Organizer_ActiveProfileGuidKey), string.Empty);
            set => EditorPrefs.SetString(Key(Organizer_ActiveProfileGuidKey), value);
        }

        // ── FBX Importer ─────────────────────────────────────────────────────────

        private const string FBX_EnabledKey           = "FBX.Enabled";
        private const string FBX_ActiveProfileGuidKey = "FBX.ActiveProfileGuid";

        public static bool FBX_Enabled
        {
            get => EditorPrefs.GetBool(Key(FBX_EnabledKey), false);
            set => EditorPrefs.SetBool(Key(FBX_EnabledKey), value);
        }

        public static string FBX_ActiveProfileGuid
        {
            get => EditorPrefs.GetString(Key(FBX_ActiveProfileGuidKey), string.Empty);
            set => EditorPrefs.SetString(Key(FBX_ActiveProfileGuidKey), value);
        }

        // ── Utilities ────────────────────────────────────────────────────────────

        /// <summary>
        /// Wipes all Pristine Pipeline EditorPrefs entries.
        /// Exposed for the Settings tab reset button.
        /// </summary>
        public static void ResetAll()
        {
            EditorPrefs.DeleteKey(Key(ActiveTabKey));
            EditorPrefs.DeleteKey(Key(FolderGen_ActiveTemplateGuidKey));
            EditorPrefs.DeleteKey(Key(FolderGen_TemplateSavePathKey));
            EditorPrefs.DeleteKey(Key(Organizer_EnabledKey));
            EditorPrefs.DeleteKey(Key(Organizer_ActiveProfileGuidKey));
            EditorPrefs.DeleteKey(Key(FBX_EnabledKey));
            EditorPrefs.DeleteKey(Key(FBX_ActiveProfileGuidKey));
        }
    }
}