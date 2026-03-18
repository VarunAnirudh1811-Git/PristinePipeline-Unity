using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Resolves the active ScriptableObject profile for each tool.
    /// Sits between ToolSettings (which stores GUIDs) and the processors/windows
    /// (which need actual asset references). Nothing outside this class should
    /// call AssetDatabase.GUIDToAssetPath for profile resolution.
    /// </summary>    
    public static class ProfileRegistry
    {
        // ── Generic helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads a ScriptableObject of type T from a stored GUID.
        /// Returns null cleanly if the GUID is empty or the asset no longer exists.
        /// </summary>
        public static T Load<T>(string guid) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>
        /// Stores the GUID for a ScriptableObject asset into the provided setter.
        /// Passing null clears the stored value.
        /// </summary>
        public static void Save<T>(T asset, System.Action<string> guidSetter) where T : ScriptableObject
        {
            if (asset == null)
            {
                guidSetter(string.Empty);
                return;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(path);
            guidSetter(guid);
        }

        // ── Per-tool convenience accessors ───────────────────────────────────────
        // These are the only methods processors and tabs should call.
        // When a new tool is added, add its pair here and nowhere else.

        // Folder Generator
        public static FolderTemplate  GetActiveFolderTemplate()   => Load<FolderTemplate>(ToolSettings.FolderGen_ActiveTemplateGuid);
        public static void            SetActiveFolderTemplate(FolderTemplate t) => Save(t, g => ToolSettings.FolderGen_ActiveTemplateGuid = g);

        // Asset Organizer 
        public static AssetMappingProfile  GetActiveOrganizerProfile()  => Load<AssetMappingProfile>(ToolSettings.Organizer_ActiveProfileGuid);
        public static void            SetActiveOrganizerProfile(AssetMappingProfile p) => Save(p, g => ToolSettings.Organizer_ActiveProfileGuid = g);

        // FBX Importer — placeholder, uncommented in Phase 4
        public static FBXImportProfile   GetActiveImportProfile()     => Load<FBXImportProfile>(ToolSettings.FBX_ActiveProfileGuid);
        public static void            SetActiveImportProfile(FBXImportProfile p) => Save(p, g => ToolSettings.FBX_ActiveProfileGuid = g);
    }
}