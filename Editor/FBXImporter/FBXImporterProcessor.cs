using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Intercepts FBX imports via Unity's AssetPostprocessor.
    ///
    /// OnPreprocessModel (Phase 4)
    ///   Fires BEFORE Unity processes the model. ModelImporter is writable here.
    ///   Applies mesh settings (scale, compression, normals, tangents, etc.)
    ///   from the active FBXImportProfile preset.
    ///   Also enforces or warns on naming convention violations.
    ///
    /// OnPostprocessModel (Phase 5)
    ///   Fires AFTER Unity has processed the model and mesh data is available.
    ///   Delegates material creation, texture assignment, and prefab generation
    ///   to FBXImporterUtility.RunPostImportSteps.
    ///   Skipped if the tool is disabled or no profile is set.
    ///
    /// Neither callback should call AssetDatabase.StartAssetEditing /
    /// StopAssetEditing — Unity manages the asset editing scope around postprocessors.
    /// </summary>
    public class FBXImporterProcessor : AssetPostprocessor
    {
        // ── OnPreprocessModel ────────────────────────────────────────────────────

        private void OnPreprocessModel()
        {
            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
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

            if (!FBXImporterUtility.ValidateName(profile, assetPath))
            {
                if (profile.enforceNamingConvention)
                {
                    // Log error with details then return — this does NOT block the import
                    // at the AssetPostprocessor level (throwing here is unreliable across
                    // Unity versions). The file is still imported but settings are not applied.
                    // The error in the Console is the signal to the user.
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Naming convention violation — '{fileName}' " +
                        $"does not match any valid prefix in profile '{profile.profileName}'. " +
                        $"Valid prefixes: {string.Join(", ", profile.validPrefixes)}. " +
                        $"Import settings were NOT applied.");
                    return;
                }

                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Naming convention warning — '{fileName}' does not " +
                    $"match any valid prefix in profile '{profile.profileName}'.");
            }

            // ── Apply mesh import settings ───────────────────────────────────────

            FBXImportPreset preset = FBXImporterUtility.FindMatchingPreset(profile, assetPath);
            if (preset == null) return;

            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            FBXImporterUtility.ApplyPreset(importer, preset);

            Debug.Log(
                $"{ToolInfo.LogPrefix} Applied preset '{preset.presetName}' to '{fileName}'.");
        }

        // ── OnPostprocessModel ───────────────────────────────────────────────────

        private void OnPostprocessModel(GameObject importedModel)
        {
            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
            if (!ToolSettings.FBX_Enabled) return;

            FBXImportProfile profile = ProfileRegistry.GetActiveImportProfile();
            if (profile == null) return;

            // Skip if naming convention is enforced and the name is invalid —
            // consistent with OnPreprocessModel which also returns early in that case.
            if (!FBXImporterUtility.ValidateName(profile, assetPath)
                && profile.enforceNamingConvention)
                return;

            FBXImportPreset preset = FBXImporterUtility.FindMatchingPreset(profile, assetPath);
            if (preset == null) return;

            // RunPostImportSteps handles material creation, texture assignment, and
            // conditional prefab generation. It calls AssetDatabase.Refresh internally.
            // The importedModel parameter here is the in-memory object — we pass the
            // asset path so the utility can load the on-disk asset for prefab generation.
            FBXImporterUtility.RunPostImportSteps(assetPath, preset, profile);
        }
    }
}