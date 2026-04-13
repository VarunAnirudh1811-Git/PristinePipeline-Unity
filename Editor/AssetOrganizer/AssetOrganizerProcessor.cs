using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Runs automatically when new assets are imported into the project.
    /// Reads the active MappingProfile from ProfileRegistry and moves
    /// assets to their designated folders based on matching rules.
    ///
    /// Only fires on first-time imports — not on reimports of existing assets.
    /// Does nothing if the organizer is disabled in ToolSettings.
    /// </summary>
    /// v1.2.1 - Scope control:
    /// Only processes assets that pass AssetOrganizerUtility.IsInScope.
    /// See that method's documentation for the full scope definition.
    /// In short: Assets/ top-level files, anything under the Active Root,
    /// and anything under a user-defined additional scope path are processed.
    /// Everything else is silently ignored.
    public class AssetOrganizerProcessor : AssetPostprocessor
    {
        /// <summary>
        // OnPostprocessAllAssets is Unity's hook that fires after every import
        // batch completes. It receives four arrays:
        //   importedAssets   — assets imported for the first time or reimported
        //   deletedAssets    — assets that were deleted
        //   movedAssets      — assets that were moved (new paths)
        //   movedFromAssets  — assets that were moved (old paths)
        //
        // The didDomainReload parameter indicates whether a C# domain reload
        // occurred during this import batch — we ignore it here.
        /// <summary>
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            // Bail immediately if organizer is disabled — no profile lookup needed
            if (!ToolSettings.Organizer_Enabled) return;

            AssetMappingProfile profile = ProfileRegistry.GetActiveOrganizerProfile();

            if (profile == null)
            {
                // Organizer is enabled but no profile is set — warn once
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Asset Organizer is enabled but no " +
                    $"Mapping Profile is set. Assign one in the Asset Organizer tab.");
                return;
            }

            // We only process first-time imports — not reimports of existing assets.
            // Unity doesn't distinguish these natively in the imported array, so we
            // check whether the asset existed before this import by attempting to
            // load it. If it loads successfully it was already in the database
            // (reimport) — we skip it. If it's null, this is a first-time import.
            //
            // Note: we process one asset at a time without StartAssetEditing/
            // StopAssetEditing here because MoveAsset itself calls AssetDatabase
            // methods that must not be called inside a StartAssetEditing block
            // during a postprocessor callback.
            foreach (string assetPath in importedAssets)
            {
                // Skip folders and meta files
                if (AssetDatabase.IsValidFolder(assetPath)) continue;
                if (assetPath.EndsWith(".meta")) continue;
                if (!assetPath.StartsWith("Assets/")) continue;

                // Skip assets that were moved rather than freshly imported -
                // they already live somewhere intentional.
                if (System.Array.IndexOf(movedAssets, assetPath) >= 0) continue;

                // Skip assets outside the defined scope. This is the primary safety
                // boundary that prevents plugins and third-party assets from being
                // reorganised without the user's explicit consent.
                if (!AssetOrganizerUtility.IsInScope(assetPath)) continue;

                MappingRule rule = AssetOrganizerUtility.FindMatchingRule(profile, assetPath);
                if (rule == null) continue;

                // MoveAsset always resolves the destination relative to Active Root,
                // so a file adopted from Assets/ top-level is pulled into the root tree.
                AssetOrganizerUtility.MoveAsset(assetPath, rule);
            }
        }
    }
}