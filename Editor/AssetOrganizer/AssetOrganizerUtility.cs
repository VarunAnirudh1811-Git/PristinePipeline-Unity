using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Stateless utility methods for the Asset Organizer.
    /// No EditorWindow dependency — safe to call from any editor context.
    /// All rule matching, asset moving, and profile operations live here.
    /// AssetOrganizerTab and AssetOrganizerProcessor delegate to this class.
    /// </summary>
    public static class AssetOrganizerUtility
    {
        // ── Profile loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads all MappingProfile assets visible to the project from the
        /// user profile save path. Returns an empty list if none exist.
        /// </summary>
        public static List<MappingProfile> LoadAllProfiles()
        {
            var profiles = new List<MappingProfile>();
            string userPath = ToolSettings.Organizer_ProfileSavePath;

            if (!string.IsNullOrWhiteSpace(userPath) && AssetDatabase.IsValidFolder(userPath))
                LoadProfilesFromPath(userPath, profiles);

            return profiles;
        }

        private static void LoadProfilesFromPath(string folderPath, List<MappingProfile> results)
        {
            string[] guids = AssetDatabase.FindAssets("t:MappingProfile", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<MappingProfile>(path);

                if (profile != null && !results.Contains(profile))
                    results.Add(profile);
            }
        }

        /// <summary>
        /// Returns display names for a list of profiles.
        /// </summary>
        public static string[] GetProfileDisplayNames(List<MappingProfile> profiles)
        {
            return profiles.Select(p => p.profileName).ToArray();
        }

        // ── Profile persistence ──────────────────────────────────────────────────

        /// <summary>
        /// Saves a MappingProfile asset. Updates in place if it already exists,
        /// creates a new asset at the profile save path if it does not.
        /// </summary>
        public static void SaveProfile(MappingProfile profile)
        {
            if (profile == null) return;

            EnsureDirectoryExists(ToolSettings.Organizer_ProfileSavePath);

            if (AssetDatabase.Contains(profile))
            {
                string existingPath = AssetDatabase.GetAssetPath(profile);
                string expectedFile = profile.profileName + ".asset";

                if (Path.GetFileName(existingPath) != expectedFile)
                {
                    string renameError = AssetDatabase.RenameAsset(existingPath, profile.profileName);
                    if (!string.IsNullOrEmpty(renameError))
                        Debug.LogWarning(
                            $"{ToolInfo.LogPrefix} Could not rename profile asset: {renameError}");
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            else
            {
                string assetPath = Path.Combine(
                    ToolSettings.Organizer_ProfileSavePath,
                    profile.profileName + ".asset").Replace("\\", "/");

                AssetDatabase.CreateAsset(profile, assetPath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"{ToolInfo.LogPrefix} Profile saved: {profile.profileName}");
        }

        /// <summary>
        /// Deletes a MappingProfile asset.
        /// </summary>
        public static void DeleteProfile(MappingProfile profile)
        {
            if (profile == null) return;

            string path = AssetDatabase.GetAssetPath(profile);

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
                Debug.Log($"{ToolInfo.LogPrefix} Deleted profile: {profile.profileName}");
            }
        }

        /// <summary>
        /// Creates a duplicate of the given profile at the save path.
        /// </summary>
        public static MappingProfile CloneProfile(MappingProfile source)
        {
            if (source == null) return null;

            EnsureDirectoryExists(ToolSettings.Organizer_ProfileSavePath);

            string cloneName = source.profileName + "_Copy";
            string assetPath = BuildUniqueAssetPath(
                ToolSettings.Organizer_ProfileSavePath, cloneName);

            var clone = UnityEngine.Object.Instantiate(source);
            clone.profileName = Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"{ToolInfo.LogPrefix} Cloned '{source.profileName}' → '{clone.profileName}'");
            return clone;
        }

        // ── Rule matching ────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the best matching rule for the given asset path from the profile.
        ///
        /// Evaluation strategy — all rules evaluated, last match wins:
        /// Rules are checked in order. Every rule that matches updates the result.
        /// This means rules lower in the list take priority over rules higher up,
        /// giving the user explicit control over precedence by reordering rules.
        /// A rule with a name pattern is not automatically higher priority than
        /// an extension-only rule — position in the list is what matters.
        ///
        /// Returns null if no rule matches.
        /// </summary>
        public static MappingRule FindMatchingRule(MappingProfile profile, string assetPath)
        {
            if (profile == null) return null;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string extension = Path.GetExtension(assetPath).TrimStart('.').ToLowerInvariant();
            MappingRule lastMatch = null;

            foreach (MappingRule rule in profile.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.extension)) continue;
                if (string.IsNullOrWhiteSpace(rule.destinationFolder)) continue;

                // Extension must match (case-insensitive, stored without dot)
                if (!rule.extension.TrimStart('.').ToLowerInvariant().Equals(extension))
                    continue;

                // If the rule has a name pattern, it must also match
                if (rule.HasNamePattern && !MatchesWildcard(fileName, rule.namePattern))
                    continue;

                // This rule matches — update lastMatch so the last match wins
                lastMatch = rule;
            }

            return lastMatch;
        }

        /// <summary>
        /// Wildcard pattern matching supporting * (any sequence) and ? (any single char).
        /// Case-insensitive. Used for optional name pattern rules.
        ///
        /// Examples:
        ///   MatchesWildcard("T_Hero_BC",    "T_*")          → true
        ///   MatchesWildcard("T_Hero_BC",    "*_BC")         → true
        ///   MatchesWildcard("T_Hero_BC",    "T_*_BC")       → true
        ///   MatchesWildcard("SM_Rock",      "T_*")          → false
        ///   MatchesWildcard("T_A",          "T_?")          → true
        ///   MatchesWildcard("T_AB",         "T_?")          → false
        ///
        /// Architecture note: this method is the only place wildcard logic lives.
        /// To upgrade to regex in a future release, replace the body of this method
        /// with a Regex.IsMatch call — the rest of the system is unaffected.
        /// </summary>
        public static bool MatchesWildcard(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(input)) return false;

            // Normalise to lowercase for case-insensitive matching
            input = input.ToLowerInvariant();
            pattern = pattern.ToLowerInvariant();

            // dp[i][j] = true if input[0..i-1] matches pattern[0..j-1]
            // Dynamic programming approach handles all * and ? combinations
            // correctly including multiple * wildcards in one pattern.
            int inputLen = input.Length;
            int patternLen = pattern.Length;

            bool[,] dp = new bool[inputLen + 1, patternLen + 1];
            dp[0, 0] = true;

            // A pattern of all * characters matches an empty input
            for (int j = 1; j <= patternLen; j++)
                if (pattern[j - 1] == '*') dp[0, j] = dp[0, j - 1];

            for (int i = 1; i <= inputLen; i++)
            {
                for (int j = 1; j <= patternLen; j++)
                {
                    if (pattern[j - 1] == '*')
                    {
                        // * matches zero characters (dp[i][j-1])
                        // or one more character (dp[i-1][j])
                        dp[i, j] = dp[i, j - 1] || dp[i - 1, j];
                    }
                    else if (pattern[j - 1] == '?' || pattern[j - 1] == input[i - 1])
                    {
                        // ? matches any single char, or exact char match
                        dp[i, j] = dp[i - 1, j - 1];
                    }
                }
            }

            return dp[inputLen, patternLen];
        }

        // ── Asset moving ─────────────────────────────────────────────────────────

        /// <summary>
        /// Moves an asset to the destination folder defined by the matching rule.
        /// Creates the destination folder if it does not exist.
        /// Returns true if the move succeeded, false if it was skipped or failed.
        /// </summary>
        public static bool MoveAsset(string assetPath, MappingRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.destinationFolder))
                return false;

            string destination = rule.destinationFolder.TrimEnd('/');
            string fileName = Path.GetFileName(assetPath);
            string targetPath = destination + "/" + fileName;

            // Asset is already in the correct folder — nothing to do
            string currentFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.Equals(currentFolder, destination, StringComparison.OrdinalIgnoreCase))
                return false;

            // Ensure the destination folder exists — create it if needed
            EnsureAssetFolderExists(destination);

            // Check for a name collision at the destination
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Skipped move — asset already exists at '{targetPath}'.");
                return false;
            }

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
        /// Runs a full organize pass over all assets in the project using the
        /// given profile. Used by the Manual Organize button in the tab UI.
        /// Returns the number of assets successfully moved.
        /// </summary>
        public static int OrganizeAll(MappingProfile profile)
        {
            if (profile == null) return 0;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            int moved = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string assetPath in allAssets)
                {
                    // Skip folders, meta files, and anything outside Assets/
                    if (!assetPath.StartsWith("Assets/")) continue;
                    if (AssetDatabase.IsValidFolder(assetPath)) continue;

                    MappingRule rule = FindMatchingRule(profile, assetPath);
                    if (rule == null) continue;

                    if (MoveAsset(assetPath, rule))
                        moved++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"{ToolInfo.LogPrefix} Organize complete — {moved} asset(s) moved.");
            return moved;
        }

        // ── Rule validation ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a list of validation messages for a single rule.
        /// Empty list means the rule is valid.
        /// </summary>
        public static List<string> ValidateRule(MappingRule rule)
        {
            var messages = new List<string>();

            if (string.IsNullOrWhiteSpace(rule.extension))
                messages.Add("Extension cannot be empty.");

            if (string.IsNullOrWhiteSpace(rule.destinationFolder))
                messages.Add("Destination folder cannot be empty.");
            else if (!rule.destinationFolder.StartsWith("Assets/"))
                messages.Add("Destination folder must start with Assets/.");

            return messages;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string ProjectRoot =>
            Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);

        private static string ToAbsolutePath(string unityAssetPath) =>
            Path.Combine(ProjectRoot, unityAssetPath).Replace("\\", "/");

        private static void EnsureDirectoryExists(string unityAssetPath)
        {
            string absolutePath = ToAbsolutePath(unityAssetPath);
            if (!Directory.Exists(absolutePath))
                Directory.CreateDirectory(absolutePath);
        }

        /// <summary>
        /// Ensures a folder exists in the AssetDatabase, creating each missing
        /// segment of the path using AssetDatabase.CreateFolder so Unity tracks it.
        /// </summary>
        private static void EnsureAssetFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            // Walk the path segment by segment and create any missing folders
            string[] segments = folderPath.Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];

                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);

                current = next;
            }
        }

        private static string BuildUniqueAssetPath(string folderPath, string baseName)
        {
            string candidate = Path.Combine(folderPath, baseName + ".asset").Replace("\\", "/");
            int counter = 1;

            while (File.Exists(ToAbsolutePath(candidate)))
            {
                candidate = Path.Combine(
                    folderPath, $"{baseName}{counter}.asset").Replace("\\", "/");
                counter++;
            }

            return candidate;
        }
    }
}