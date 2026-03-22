using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Stateless utility methods for the FBX Importer.
    /// No EditorWindow dependency — safe to call from any editor context.
    /// All profile loading, rule matching, naming validation, and
    /// ModelImporter configuration live here.
    /// FBXImporterTab and FBXImporterProcessor both delegate to this class.
    /// </summary>
    public static class FBXImporterUtility
    {
        // ── Profile loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads all FBXImportProfile assets from the configured save path.
        /// </summary>
        public static List<FBXImportProfile> LoadAllProfiles()
        {
            var profiles = new List<FBXImportProfile>();
            string userPath = ToolSettings.FBX_ProfileSavePath;
            string builtInPath = ToolInfo.BuiltInProfilePath;

            if (AssetDatabase.IsValidFolder(builtInPath))
            {
                LoadProfilesFromPath(builtInPath, profiles);
            }

            if (!string.IsNullOrWhiteSpace(userPath))
            {
                EnsureAssetFolderExists(userPath);
                LoadProfilesFromPath(userPath, profiles);
            }

            return profiles;
        }

        private static void LoadProfilesFromPath(string folderPath, List<FBXImportProfile> results)
        {
            string[] guids = AssetDatabase.FindAssets("t:FBXImportProfile", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                FBXImportProfile profile = AssetDatabase.LoadAssetAtPath<FBXImportProfile>(path);

                if (profile != null && !results.Contains(profile))
                    results.Add(profile);
            }
        }

        /// <summary>
        /// Returns display names for a list of profiles.
        /// </summary>
        public static string[] GetProfileDisplayNames(List<FBXImportProfile> profiles)
        {
            return profiles.Select(p => p.profileName).ToArray();
        }

        // ── Profile persistence ──────────────────────────────────────────────────

        /// <summary>
        /// Saves an FBXImportProfile asset. Updates in place if it already exists,
        /// creates a new asset at the profile save path if it does not.
        /// </summary>
        public static void SaveProfile(FBXImportProfile profile)
        {
            if (profile == null) return;

            EnsureDirectoryExists(ToolSettings.FBX_ProfileSavePath);

            if (AssetDatabase.Contains(profile))
            {
                string existingPath = AssetDatabase.GetAssetPath(profile);
                string expectedFile = profile.profileName + ".asset";

                if (Path.GetFileName(existingPath) != expectedFile)
                {
                    string renameError = AssetDatabase.RenameAsset(
                        existingPath, profile.profileName);

                    if (!string.IsNullOrEmpty(renameError))
                        Debug.LogWarning(
                            $"{ToolInfo.LogPrefix} Could not rename profile: {renameError}");
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            else
            {
                string assetPath = Path.Combine(
                    ToolSettings.FBX_ProfileSavePath,
                    profile.profileName + ".asset").Replace("\\", "/");

                AssetDatabase.CreateAsset(profile, assetPath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"{ToolInfo.LogPrefix} FBX Import Profile saved: {profile.profileName}");
        }

        /// <summary>
        /// Deletes an FBXImportProfile asset.
        /// </summary>
        public static void DeleteProfile(FBXImportProfile profile)
        {
            if (profile == null) return;

            string path = AssetDatabase.GetAssetPath(profile);

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
                Debug.Log(
                    $"{ToolInfo.LogPrefix} Deleted FBX Import Profile: {profile.profileName}");
            }
        }

        // ── Profile import / export ─────────────────────────────────────────────

        /// <summary>
        /// Exports an FBXImportProfile to a JSON file via a save panel dialog.
        /// </summary>
        public static void ExportProfile(FBXImportProfile profile)
        {
            if (profile == null) return;

            string path = EditorUtility.SaveFilePanel(
                "Export FBX Import Profile",
                "",
                profile.profileName,
                "json");

            if (string.IsNullOrEmpty(path)) return;

            var data = new FBXImportProfileData
            {
                profileName = profile.profileName,
                description = profile.description,
                enforceNamingConvention = profile.enforceNamingConvention,
                validPrefixes = new List<string>(profile.validPrefixes),
                defaultPreset = profile.defaultPreset,
                rules = new List<FBXImportRule>(profile.Rules)
            };

            File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"{ToolInfo.LogPrefix} Exported FBX Import Profile to: {path}");
        }

        public static FBXImportProfile ImportProfile()
        {
            string path = EditorUtility.OpenFilePanel("Import FBX Import Profile", "", "json");
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<FBXImportProfileData>(json);

                if (data == null || string.IsNullOrWhiteSpace(data.profileName))
                {
                    EditorUtility.DisplayDialog(
                        "Import Failed",
                        "The selected file is not a valid FBX Import Profile.",
                        "OK");
                    return null;
                }

                EnsureDirectoryExists(ToolSettings.FBX_ProfileSavePath);

                string assetPath = BuildUniqueAssetPath(
                    ToolSettings.FBX_ProfileSavePath, data.profileName);

                var profile = ScriptableObject.CreateInstance<FBXImportProfile>();
                profile.profileName = Path.GetFileNameWithoutExtension(assetPath);
                profile.description = data.description;
                profile.enforceNamingConvention = data.enforceNamingConvention;
                profile.validPrefixes = data.validPrefixes ?? new List<string>();
                profile.defaultPreset = data.defaultPreset ?? new FBXImportPreset { presetName = "Default" };
                profile.SetRules(data.rules ?? new List<FBXImportRule>());

                AssetDatabase.CreateAsset(profile, assetPath);
                AssetDatabase.Refresh();

                Debug.Log($"{ToolInfo.LogPrefix} Imported FBX Import Profile: {profile.profileName}");
                return profile;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ToolInfo.LogPrefix} Import failed: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "Import Failed",
                    "An error occurred while reading the file.",
                    "OK");
                return null;
            }
        }

        [Serializable]
        public class FBXImportProfileData
        {
            public string profileName;
            public string description;
            public bool enforceNamingConvention;
            public List<string> validPrefixes;
            public FBXImportPreset defaultPreset;
            public List<FBXImportRule> rules;
        }

        /// <summary>
        /// Creates a duplicate of the given profile at the save path.
        /// </summary>
        public static FBXImportProfile CloneProfile(FBXImportProfile source)
        {
            if (source == null) return null;

            EnsureDirectoryExists(ToolSettings.FBX_ProfileSavePath);

            string cloneName = source.profileName + "_Copy";
            string assetPath = BuildUniqueAssetPath(
                ToolSettings.FBX_ProfileSavePath, cloneName);

            FBXImportProfile clone = UnityEngine.Object.Instantiate(source);
            clone.profileName = Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"{ToolInfo.LogPrefix} Cloned '{source.profileName}' → '{clone.profileName}'");
            return clone;
        }

        // ── Naming validation ────────────────────────────────────────────────────

        /// <summary>
        /// Validates the FBX filename against the profile's valid prefix list.
        /// Returns true when the name is valid (starts with a known prefix).
        /// Returns false when no prefix matches.
        ///
        /// The caller decides what to do on failure — block or warn — based on
        /// profile.enforceNamingConvention.
        /// </summary>
        public static bool ValidateName(FBXImportProfile profile, string assetPath)
        {
            if (profile == null) return true;
            if (profile.validPrefixes.Count == 0) return true;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            return profile.validPrefixes
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Any(p => fileName.StartsWith(p, System.StringComparison.OrdinalIgnoreCase));
        }

        // ── Rule matching ────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the best matching FBXImportPreset for the given asset path.
        ///
        /// Evaluation strategy — all rules evaluated, last match wins:
        /// Rules are checked in list order. Every rule whose pattern matches
        /// the filename updates the result. Rules lower in the list therefore
        /// have higher priority. If no rule matches, the profile's defaultPreset
        /// is returned and a warning is logged.
        /// </summary>
        public static FBXImportPreset FindMatchingPreset(
            FBXImportProfile profile, string assetPath)
        {
            if (profile == null) return null;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            FBXImportPreset lastMatch = null;

            foreach (FBXImportRule rule in profile.Rules)
            {
                if (!rule.HasNamePattern) continue;
                if (!MatchesWildcard(fileName, rule.namePattern)) continue;

                lastMatch = rule.preset;
            }

            if (lastMatch != null)
                return lastMatch;

            // No rule matched — fall back to default preset with a warning
            Debug.LogWarning(
                $"{ToolInfo.LogPrefix} No rule matched '{Path.GetFileName(assetPath)}'. " +
                $"Applying default preset from profile '{profile.profileName}'.");

            return profile.defaultPreset;
        }

        /// <summary>
        /// Wildcard pattern matching supporting * (any sequence) and ? (any single char).
        /// Case-insensitive. Dynamic programming approach handles all edge cases
        /// including multiple wildcards in one pattern.
        ///
        /// To upgrade to regex in a future release, replace the body of this method
        /// with Regex.IsMatch — the rest of the system is unaffected.
        /// </summary>
        public static bool MatchesWildcard(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(input)) return false;

            input = input.ToLowerInvariant();
            pattern = pattern.ToLowerInvariant();

            int inputLen = input.Length;
            int patternLen = pattern.Length;

            bool[,] dp = new bool[inputLen + 1, patternLen + 1];
            dp[0, 0] = true;

            for (int j = 1; j <= patternLen; j++)
                if (pattern[j - 1] == '*') dp[0, j] = dp[0, j - 1];

            for (int i = 1; i <= inputLen; i++)
            {
                for (int j = 1; j <= patternLen; j++)
                {
                    if (pattern[j - 1] == '*')
                        dp[i, j] = dp[i, j - 1] || dp[i - 1, j];
                    else if (pattern[j - 1] == '?' || pattern[j - 1] == input[i - 1])
                        dp[i, j] = dp[i - 1, j - 1];
                }
            }

            return dp[inputLen, patternLen];
        }

        // ── ModelImporter configuration ──────────────────────────────────────────

        /// <summary>
        /// Applies all settings from an FBXImportPreset to a ModelImporter.
        /// Called from FBXImporterProcessor.OnPreprocessModel — the importer
        /// has not yet run at that point so all settings applied here take
        /// effect on the current import pass.
        /// </summary>
        public static void ApplyPreset(ModelImporter importer, FBXImportPreset preset)
        {
            if (importer == null || preset == null) return;

            importer.globalScale = preset.scaleFactor;
            importer.meshCompression = preset.meshCompression;
            importer.isReadable = preset.readWriteEnabled;
            importer.optimizeMeshPolygons = preset.optimizeMesh;
            importer.optimizeMeshVertices = preset.optimizeMesh;
            importer.generateSecondaryUV = preset.generateLightmapUVs;
            importer.importNormals = preset.normals;
            importer.importTangents = preset.tangents;
            importer.swapUVChannels = preset.swapUVs;
        }

        /// <summary>
        /// Manually reprocesses a single FBX asset using the given profile.
        /// Forces a reimport so OnPreprocessModel fires again with the new settings.
        /// Used by the manual override button in FBXImporterTab.
        /// </summary>
        public static bool ReprocessAsset(string assetPath, FBXImportProfile profile)
        {
            if (string.IsNullOrEmpty(assetPath) || profile == null) return false;

            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} ReprocessAsset skipped — not an FBX: {assetPath}");
                return false;
            }

            // Validate naming before forcing reimport
            if (!ValidateName(profile, assetPath))
            {
                if (profile.enforceNamingConvention)
                {
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Naming convention violation: " +
                        $"'{Path.GetFileName(assetPath)}' does not match any valid prefix. " +
                        $"Reprocess cancelled.");
                    return false;
                }

                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Naming convention warning: " +
                    $"'{Path.GetFileName(assetPath)}' does not match any valid prefix.");
            }

            // Force reimport — this triggers OnPreprocessModel in FBXImporterProcessor
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"{ToolInfo.LogPrefix} Reprocessed: {Path.GetFileName(assetPath)}");
            return true;
        }

        /// <summary>
        /// Runs a manual reprocess pass over all FBX files in the project.
        /// Returns the count of assets reprocessed.
        /// </summary>
        public static int ReprocessAll(FBXImportProfile profile)
        {
            if (profile == null) return 0;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            int count = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string path in allAssets)
                {
                    if (!path.StartsWith("Assets/")) continue;
                    if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                    if (ReprocessAsset(path, profile))
                        count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"{ToolInfo.LogPrefix} Manual reprocess complete — {count} FBX file(s) processed.");

            return count;
        }

        // ── Rule validation ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a list of validation messages for a single rule.
        /// Empty list means the rule is valid.
        /// </summary>
        public static List<string> ValidateRule(FBXImportRule rule)
        {
            var messages = new List<string>();

            if (!rule.HasNamePattern)
                messages.Add("Name pattern cannot be empty.");

            if (rule.preset == null)
                messages.Add("Preset is null — this should not happen.");
            else if (rule.preset.scaleFactor <= 0f)
                messages.Add("Scale factor must be greater than zero.");

            return messages;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string ProjectRoot =>
            Application.dataPath[..^"/Assets".Length];

        private static string ToAbsolutePath(string unityAssetPath) =>
            Path.Combine(ProjectRoot, unityAssetPath).Replace("\\", "/");

        private static void EnsureAssetFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

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

        private static void EnsureDirectoryExists(string unityAssetPath)
        {
            string absolutePath = ToAbsolutePath(unityAssetPath);
            if (!Directory.Exists(absolutePath))
                Directory.CreateDirectory(absolutePath);
        }

        private static string BuildUniqueAssetPath(string folderPath, string baseName)
        {
            string candidate = Path.Combine(
                folderPath, baseName + ".asset").Replace("\\", "/");
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