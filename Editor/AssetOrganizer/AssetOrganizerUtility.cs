using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Stateless utility methods for the Asset Organizer.
    /// No EditorWindow dependency — safe to call from any editor context.
    /// All rule matching, asset moving, and profile operations live here.
    /// AssetOrganizerTab and AssetOrganizerProcessor delegate to this class.
    /// </summary>
    ///   v1.2 — Active Root refactor:
    ///   Rule destination folders are stored as paths relative to the Active Root
    ///   (ToolSettings.ActiveRootPath). They must NOT start with "Assets/". This utility
    ///   resolves them to full Unity asset paths by prepending ActiveRootPath at the
    ///   point of use (MoveAsset, EnsureAssetFolderExists).
    ///    v1.2.1 — Scope control:
    ///   Assets are only processed when they fall inside the defined scope:
    ///     (a) Directly under Assets/ — non-recursive, parent dir == "Assets" exactly.
    ///         This handles the "file dropped via the menu" adoption case.
    ///     (b) Anywhere under the Active Root — recursive.
    ///     (c) Anywhere under a user-defined additional scope path — recursive.
    ///   Everything else (plugins, SDKs, package-imported assets) is never touched.
    ///   Destination always resolves relative to Active Root regardless of where the
    ///   source file was found.
    public static class AssetOrganizerUtility
    {
        // ── Profile loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads all AssetMappingProfile assets visible to the project from the
        /// built-in package path and user profile save path.
        /// Returns an empty list if none exist.
        /// </summary>
        public static List<AssetMappingProfile> LoadAllProfiles()
        {
            return PristinePipelineUtility.LoadAllProfiles<AssetMappingProfile>(
                ToolSettings.Organizer_ProfileSavePath,
                ToolInfo.BuiltInMappingProfilePath,
                onLoaded: (profile, path) => profile.isBuiltIn = path.StartsWith("Packages/")
            );
        }

        public static string[] GetProfileDisplayNames(List<AssetMappingProfile> profiles)
        {
            return PristinePipelineUtility.GetAssetDisplayNames(
                profiles,
                p => p.profileName,
                p => p.isBuiltIn
            );
        }

        // ── Profile persistence ──────────────────────────────────────────────────

        /// <summary>
        /// Saves an AssetMappingProfile asset. Updates in place if it already exists,
        /// creates a new asset at the profile save path if it does not.
        /// </summary>
        public static void SaveProfile(AssetMappingProfile profile)
        {
            PristinePipelineUtility.SaveProfile(
                profile,
                ToolSettings.Organizer_ProfileSavePath,
                p => p.profileName
            );
        }

        /// <summary>
        /// Deletes a MappingProfile asset.
        /// </summary>
        public static void DeleteProfile(AssetMappingProfile profile)
        {
            PristinePipelineUtility.DeleteProfile(
                profile,
                p => p.isBuiltIn,    // Built-in profiles cannot be deleted
                p => p.profileName
            );
        }

        /// <summary>Creates a duplicate of the given profile at the save path.</summary>
        public static AssetMappingProfile CloneProfile(AssetMappingProfile source)
        {
            return PristinePipelineUtility.CloneProfile(
                source,
                ToolSettings.Organizer_ProfileSavePath,
                p => p.profileName,
                src => UnityEngine.Object.Instantiate(src)  // Simple clone for AssetMappingProfile
            );
        }

        // ── Profile import / export ──────────────────────────────────────────────

        /// <summary>Exports an AssetMappingProfile to a JSON file.</summary>
        public static void ExportProfile(AssetMappingProfile profile)
        {
            PristinePipelineUtility.ExportProfile(
                profile,
                p => new AssetMappingProfileData
                {
                    profileName = p.profileName,
                    description = p.description,
                    rules = new List<MappingRule>(p.Rules)
                },
                p => p.profileName
            );
        }

        /// <summary>Imports an AssetMappingProfile from a JSON file.</summary>
        public static AssetMappingProfile ImportProfile()
        {
            return PristinePipelineUtility.ImportProfile<AssetMappingProfile>(
                ToolSettings.Organizer_ProfileSavePath,
                "Asset Mapping Profile",
                json =>
                {
                    var data = JsonUtility.FromJson<AssetMappingProfileData>(json);
                    if (data == null || string.IsNullOrWhiteSpace(data.profileName))
                        return null;

                    var profile = ScriptableObject.CreateInstance<AssetMappingProfile>();
                    profile.profileName = data.profileName;
                    profile.description = data.description;
                    profile.SetRules(data.rules ?? new List<MappingRule>());
                    return profile;
                }
            );
        }

        [Serializable]
        public class AssetMappingProfileData
        {
            public string profileName;
            public string description;
            public List<MappingRule> rules;
        }

        // ── Scope control ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the given Unity asset path falls inside the organizer's
        /// defined scope. Only assets in scope are ever moved, both by OrganizeAll
        /// and by the automatic AssetOrganizerProcessor.
        ///
        /// Scope is defined as the union of three zones:
        ///
        ///   Zone A — Assets/ top level (non-recursive)
        ///     Files whose immediate parent directory is exactly "Assets".
        ///     This is the adoption zone: files dropped via the Unity menu or
        ///     dragged onto the Project window root land here and need to be
        ///     pulled into the Active Root.
        ///     Example: "Assets/SM_Rock.fbx" → in scope
        ///              "Assets/Plugins/SomeTool/mesh.fbx" → NOT in scope
        ///
        ///   Zone B — Active Root (recursive)
        ///     Everything under ToolSettings.ActiveRootPath.
        ///     Example (root = "Assets/GameA"): "Assets/GameA/Art/SM_Rock.fbx" → in scope
        ///
        ///   Zone C — User-defined additional paths (recursive)
        ///     Each path in ToolSettings.Organizer_AdditionalScopePaths.
        ///     Allows opt-in for specific third-party folders the user controls.
        /// </summary>
        public static bool IsInScope(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            // Zone A — non-recursive Assets/ top level
            // Parent is exactly "Assets" when there is no second slash after "Assets/".
            string parentDir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.Equals(parentDir, "Assets", StringComparison.OrdinalIgnoreCase))
                return true;

            // Zone B — Active Root (recursive)
            string root = ToolSettings.ActiveRootPath.TrimEnd('/');
            string rootPrefix = root + "/";
            if (assetPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            // Zone C — additional scope paths (recursive)
            foreach (string extra in ToolSettings.Organizer_AdditionalScopePaths)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                string extraPrefix = extra.TrimEnd('/') + "/";
                if (assetPath.StartsWith(extraPrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ── Rule matching ────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the best matching rule for the given asset path from the profile.
        ///
        /// All rules are evaluated in order — last match wins. A rule with a name
        /// pattern is not automatically higher priority than an extension-only rule;
        /// position in the list determines precedence.
        ///
        /// Returns null if no rule matches.
        /// </summary>
        public static MappingRule FindMatchingRule(AssetMappingProfile profile, string assetPath)
        {
            if (profile == null) return null;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string extension = Path.GetExtension(assetPath).TrimStart('.').ToLowerInvariant();
            MappingRule lastMatch = null;

            foreach (MappingRule rule in profile.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.extension)) continue;
                if (string.IsNullOrWhiteSpace(rule.destinationFolder)) continue;

                if (!rule.extension.TrimStart('.').ToLowerInvariant().Equals(extension))
                    continue;

                if (rule.HasNamePattern && !PristinePipelineUtility.MatchesWildcard(fileName, rule.namePattern))
                    continue;

                lastMatch = rule;
            }

            return lastMatch;
        }

        // ── Asset moving ─────────────────────────────────────────────────────────

        /// <summary>
        /// Moves an asset to the destination folder defined by the matching rule.
        /// rule.destinationFolder is treated as a path relative to the Active Root
        /// (ToolSettings.ActiveRootPath) and resolved to a full Unity asset path here.
        /// Creates the destination folder if it does not exist.
        /// Returns true if the move succeeded, false if it was skipped or failed.
        /// </summary>
        public static bool MoveAsset(string assetPath, MappingRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.destinationFolder))
                return false;

            // Resolve relative destination to a full Unity asset path
            string destination = PristinePipelineUtility.ResolveRelativeToActiveRoot(rule.destinationFolder);
            string fileName = Path.GetFileName(assetPath);
            string targetPath = destination + "/" + fileName;

            // Already in the correct folder — nothing to do
            string currentFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.Equals(currentFolder, destination, StringComparison.OrdinalIgnoreCase))
                return false;

            // Name collision at destination
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Skipped move — asset already exists at '{targetPath}'.");
                return false;
            }

            PristinePipelineUtility.EnsureAssetFolderExists(destination);

            string error = AssetDatabase.MoveAsset(assetPath, targetPath);

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError(
                    $"{ToolInfo.LogPrefix} Failed to move '{assetPath}' → '{targetPath}': {error}");
                return false;
            }

            Debug.Log($"{ToolInfo.LogPrefix} Moved '{fileName}' → '{destination}'");
            return true;
        }

        /// <summary>
        /// Runs a full organize pass using the given profile.
        /// Only assets that pass IsInScope are considered — all others are ignored,
        /// regardless of whether their extension matches a rule.
        /// Returns the number of assets successfully moved.
        /// </summary>
        public static int OrganizeAll(AssetMappingProfile profile, out int skipped)
        {
            skipped = 0;

            if (profile == null) return 0;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            int moved = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string assetPath in allAssets)
                {
                    if (!assetPath.StartsWith("Assets/")) continue;
                    if (AssetDatabase.IsValidFolder(assetPath)) continue;
                    if (!IsInScope(assetPath)) continue;

                    MappingRule rule = FindMatchingRule(profile, assetPath);
                    if (rule == null) continue;

                    if (MoveAsset(assetPath, rule))
                        moved++;
                    else
                        skipped++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"{ToolInfo.LogPrefix} Organize complete — {moved} asset(s) moved, {skipped} skipped.");
            return moved;
        }

        // ── Rule validation ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a list of validation messages for a single rule.
        /// Empty list means the rule is valid.
        /// Destination folders must be relative paths — they must NOT start with "Assets/".
        /// </summary>
        public static List<string> ValidateRule(MappingRule rule)
        {
            var messages = new List<string>();

            if (string.IsNullOrWhiteSpace(rule.extension))
                messages.Add("Extension cannot be empty.");

            if (string.IsNullOrWhiteSpace(rule.destinationFolder))
                messages.Add("Destination folder cannot be empty.");
            else if (rule.destinationFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                messages.Add("Destination folder must be a relative path — do not start with \"Assets/\".");

            return messages;
        }
        
    }
}