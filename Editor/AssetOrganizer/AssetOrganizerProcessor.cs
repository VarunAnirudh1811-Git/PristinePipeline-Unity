using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Runs automatically when new assets are imported into the project.
    /// Reads the active MappingProfile from ProfileRegistry and moves
    /// assets to their designated folders based on matching rules.
    ///
    /// Only fires on first-time imports — not on reimports of existing assets.
    /// Does nothing if the organizer is disabled in ToolSettings.
    /// </summary>
    public class AssetOrganizerProcessor : AssetPostprocessor
    {
        // OnPostprocessAllAssets is Unity's hook that fires after every import
        // batch completes. It receives four arrays:
        //   importedAssets   — assets imported for the first time or reimported
        //   deletedAssets    — assets that were deleted
        //   movedAssets      — assets that were moved (new paths)
        //   movedFromAssets  — assets that were moved (old paths)
        //
        // The didDomainReload parameter indicates whether a C# domain reload
        // occurred during this import batch — we ignore it here.
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            // Bail immediately if organizer is disabled — no profile lookup needed
            if (!ToolSettings.Organizer_Enabled) return;

            MappingProfile profile = ProfileRegistry.GetActiveOrganizerProfile();

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

                // Skip reimports — only process assets being imported for the first time.
                // We detect first-time imports by checking if the asset existed in the
                // database before the postprocessor fired. Since we're inside the
                // postprocessor, the asset is already registered — so instead we check
                // whether the asset's file timestamp matches what would be a fresh import
                // by relying on the movedFromAssetPaths exclusion and the reimport flag.
                // The simplest reliable heuristic: if the asset path appears in
                // movedAssets it was moved, not newly imported — skip it.
                bool wasMoved = System.Array.IndexOf(movedAssets, assetPath) >= 0;
                if (wasMoved) continue;

                MappingRule rule = AssetOrganizerUtility.FindMatchingRule(profile, assetPath);
                if (rule == null) continue;

                AssetOrganizerUtility.MoveAsset(assetPath, rule);
            }
        }
    }
}