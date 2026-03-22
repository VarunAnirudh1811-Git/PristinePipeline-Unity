using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Intercepts FBX imports via Unity's AssetPostprocessor.OnPreprocessModel.
    /// Applies the active FBXImportProfile's settings to the ModelImporter
    /// before Unity processes the file — this is the correct hook for
    /// overriding import settings programmatically.
    ///
    /// Only runs when FBX_Enabled is true and an active profile is set.
    /// Naming convention enforcement (block vs warn) is controlled by
    /// profile.enforceNamingConvention.
    /// </summary>
    public class FBXImporterProcessor : AssetPostprocessor
    {
        // OnPreprocessModel fires before Unity processes a model file.
        // At this point the ModelImporter is writable — any settings we apply
        // here will be used for this import pass.
        // This is the correct hook for programmatic import settings.
        // OnPostprocessModel fires after processing — too late to change settings.
        private void OnPreprocessModel()
        {
            // Only process FBX files
            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                return;

            // Bail if the importer is disabled
            if (!ToolSettings.FBX_Enabled) return;

            FBXImportProfile profile = ProfileRegistry.GetActiveImportProfile();

            if (profile == null)
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} FBX Importer is enabled but no Import Profile " +
                    $"is set. Assign one in the FBX Importer tab.");
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            // ── Naming convention validation ─────────────────────────────────────

            bool nameIsValid = FBXImporterUtility.ValidateName(profile, assetPath);

            if (!nameIsValid)
            {
                if (profile.enforceNamingConvention)
                {
                    // Logs error with details about the expected naming convention based on profile.validPrefixes
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Import blocked — '{fileName}' does not match " +
                        $"any valid prefix in profile '{profile.profileName}'. " +
                        $"Valid prefixes: {string.Join(", ", profile.validPrefixes)}");
                    return;
                }

                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Naming convention warning — '{fileName}' does not " +
                    $"match any valid prefix in profile '{profile.profileName}'.");
            }

            // ── Find matching preset and apply settings ───────────────────────────

            FBXImportPreset preset = FBXImporterUtility.FindMatchingPreset(profile, assetPath);

            if (preset == null) return;

            ModelImporter importer = assetImporter as ModelImporter;
            if (importer == null) return;

            FBXImporterUtility.ApplyPreset(importer, preset);

            Debug.Log(
                $"{ToolInfo.LogPrefix} Applied preset '{preset.presetName}' to '{fileName}'.");
        }
    }
}