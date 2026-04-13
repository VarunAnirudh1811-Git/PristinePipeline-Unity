using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Centralized EditorPrefs wrapper for all tools under Pristine Pipeline.
    /// All keys are namespaced under ToolInfo.SettingsPrefix to avoid collisions.
    /// No tool should call EditorPrefs directly — go through this class.
    /// </summary>
    public static class ToolSettings
    {
        // ── Key helpers ──────────────────────────────────────────────────────────
        ///<summary>
        ///Creates a clear, standardized format for keys by centralizing how they’re constructed. 
        ///This improves readability by making keys predictable, reduces duplication of string logic across the codebase, 
        ///and makes future changes (like renaming the prefix) easy to manage in one place.
        ///</summary>
        private static string Key(string suffix) => ToolInfo.SettingsPrefix + "." + suffix;

        // ── Active tab ───────────────────────────────────────────────────────────

        private const string ActiveTabKey = "ActiveTab";

        public static int ActiveTab
        {
            get => EditorPrefs.GetInt(Key(ActiveTabKey), 0);
            set => EditorPrefs.SetInt(Key(ActiveTabKey), value);
        }

        // ── Active root ───────────────────────────────────────────────────────────


        private const string ActiveRootKey = "ActiveRoot";

        /// <summary>
        /// The root Unity asset path that all tools operate under.
        /// Defaults to "Assets". All tools resolve their working paths relative to this.
        /// Never null or empty — validated on set.
        /// </summary>
        public static string ActiveRootPath
        {
            get
            {
                string value = EditorPrefs.GetString(Key(ActiveRootKey), "Assets");
                return ValidateRoot(value);
            }
            set
            {
                string validated = ValidateRoot(value);
                EditorPrefs.SetString(Key(ActiveRootKey), validated);
            }
        }

        private static string ValidateRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Assets";

            path = path.Replace("\\", "/").Trim().TrimEnd('/');

            if (!path.StartsWith("Assets"))
                return "Assets";

            return path;
        }

        // ── Folder Generator ─────────────────────────────────────────────────────

        private const string FolderGen_ActiveTemplateGuidKey = "FolderGen.ActiveTemplateGuid";
        private const string FolderGen_TemplateSavePathKey = "FolderGen.TemplateSavePath";
        private const string FolderGen_AddKeepFilesKey = "FolderGen.AddKeepFiles";

        public static bool FolderGen_AddKeepFiles
        {
            get => EditorPrefs.GetBool(Key(FolderGen_AddKeepFilesKey), true);
            set => EditorPrefs.SetBool(Key(FolderGen_AddKeepFilesKey), value);
        }

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
        private const string Organizer_ProfileSavePathKey = "Organizer.ProfileSavePath";

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

        public static string Organizer_ProfileSavePath
        {
            get => EditorPrefs.GetString(Key(Organizer_ProfileSavePathKey), ToolInfo.DefaultAssetMappingProfileSavePath);
            set => EditorPrefs.SetString(Key(Organizer_ProfileSavePathKey), value);
        }

        // ── FBX Importer ─────────────────────────────────────────────────────────

        private const string FBX_EnabledKey = "FBX.Enabled";
        private const string FBX_ActiveProfileGuidKey = "FBX.ActiveProfileGuid";
        private const string FBX_ProfileSavePathKey = "FBX.ProfileSavePath";

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

        public static string FBX_ProfileSavePath
        {
            get => EditorPrefs.GetString(Key(FBX_ProfileSavePathKey), ToolInfo.DefaultFBXImportProfileSavePath);
            set => EditorPrefs.SetString(Key(FBX_ProfileSavePathKey), value);
        }

        // ── Utilities ────────────────────────────────────────────────────────────


        public static string ResolveRelativeToActiveRoot(string relativePath)
        {
            string root = ActiveRootPath.TrimEnd('/');
            return string.IsNullOrWhiteSpace(relativePath)
                ? root
                : root + "/" + relativePath.Trim('/');
        }

        // ── ADDITIONS TO ToolSettings.cs ────────────────────────────────────────────
        //
        // Add the following block inside the Asset Organizer section, after
        // Organizer_ProfileSavePath.
        //
        // Also add the DeleteKey call shown at the bottom to ResetAll().
        // ─────────────────────────────────────────────────────────────────────────────

        // ── Asset Organizer — additional scope paths ─────────────────────────────
        // Stored as a JSON array in a single EditorPrefs key so the list survives
        // Unity restarts without needing a ScriptableObject. The getter returns an
        // empty list (never null) when the key is absent or the JSON is malformed.

        private const string Organizer_AdditionalScopePathsKey = "Organizer.AdditionalScopePaths";

        public static List<string> Organizer_AdditionalScopePaths
        {
            get
            {
                string json = EditorPrefs.GetString(Key(Organizer_AdditionalScopePathsKey), "");
                if (string.IsNullOrEmpty(json)) return new List<string>();
                try
                {
                    return JsonUtility.FromJson<StringListWrapper>(json)?.items
                           ?? new List<string>();
                }
                catch { return new List<string>(); }
            }
            set
            {
                var wrapper = new StringListWrapper { items = value ?? new List<string>() };
                EditorPrefs.SetString(Key(Organizer_AdditionalScopePathsKey),
                    JsonUtility.ToJson(wrapper));
            }
        }

        // Helper — JsonUtility cannot serialise a bare List<string>,
        // so we wrap it in a class. Lives here alongside its key.
        [System.Serializable]
        private class StringListWrapper
        {
            public List<string> items = new();
        }


        /// <summary>
        /// Wipes all Pristine Pipeline EditorPrefs entries.
        /// Exposed for the Settings tab reset button.
        /// </summary>
        public static void ResetAll()
        {
            EditorPrefs.DeleteKey(Key(ActiveTabKey));
            EditorPrefs.DeleteKey(Key(ActiveRootKey));
            EditorPrefs.DeleteKey(Key(FolderGen_ActiveTemplateGuidKey));
            EditorPrefs.DeleteKey(Key(FolderGen_TemplateSavePathKey));
            EditorPrefs.DeleteKey(Key(FolderGen_AddKeepFilesKey));
            EditorPrefs.DeleteKey(Key(Organizer_EnabledKey));
            EditorPrefs.DeleteKey(Key(Organizer_ActiveProfileGuidKey));
            EditorPrefs.DeleteKey(Key(Organizer_ProfileSavePathKey));
            EditorPrefs.DeleteKey(Key(FBX_EnabledKey));
            EditorPrefs.DeleteKey(Key(FBX_ActiveProfileGuidKey));
            EditorPrefs.DeleteKey(Key(FBX_ProfileSavePathKey));
            EditorPrefs.DeleteKey(Key(Organizer_AdditionalScopePathsKey));
        }        

    }
}