using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Shared editor utilities used across FBX Importer, Folder Generator, 
    /// and Asset Organizer tools.
    /// </summary>
    public class PristinePipelineUtility
    {
        // ── Path Helpers ──────────────────────────────────────────   

        /// <summary>
        /// Returns the absolute filesystem path to the Unity project root.
        /// </summary>
        public static string ProjectRoot =>
            Application.dataPath[..^"Assets".Length];

        /// <summary>
        /// Converts a Unity asset path (e.g., "Assets/...") to absolute filesystem path.
        /// </summary>
        public static string ToAbsolutePath(string unityAssetPath) =>
            Path.Combine(ProjectRoot, unityAssetPath).Replace("\\", "/");

        /// <summary>
        /// Strips leading "Assets/" from a path.
        /// </summary>
        public static string StripAssetsPrefix(string path)
        {
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return path["Assets/".Length..];
            return path;
        }

        /// <summary>
        /// Ensures a physical directory exists for a Unity asset path.
        /// </summary>
        public static void EnsureDirectoryExists(string unityAssetPath)
        {
            string absolutePath = ToAbsolutePath(unityAssetPath);
            if (!Directory.Exists(absolutePath))
                Directory.CreateDirectory(absolutePath);
        }

        /// <summary>
        /// Ensures a Unity asset folder exists, creating missing segments with AssetDatabase.
        /// Accepts full Unity asset paths (starting with "Assets/").
        /// </summary>
        public static void EnsureAssetFolderExists(string folderPath)
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

        /// <summary>
        /// Generates a unique asset path with numeric suffix if needed.
        /// </summary>
        public static string BuildUniqueAssetPath(string folderPath, string baseName)
        {
            string candidate = Path.Combine(folderPath, baseName + ".asset").Replace("\\", "/");
            int counter = 1;

            while (File.Exists(ToAbsolutePath(candidate)))
            {
                candidate = Path.Combine(folderPath, $"{baseName}{counter}.asset").Replace("\\", "/");
                counter++;
            }

            return candidate;
        }

        /// <summary>
        /// Returns relative path to active path.
        /// </summary>
        public static string ResolveRelativeToActiveRoot(string relativePath)
        {
            string root = ToolSettings.ActiveRootPath.TrimEnd('/');
            return string.IsNullOrWhiteSpace(relativePath)
                ? root
                : root + "/" + relativePath.Trim('/');
        }

        // ── Wildcard Matching ────────────────────────────────────

        /// <summary>
        /// Wildcard pattern matching — * (any sequence) and ? (any single char).
        /// Case-insensitive. Dynamic programming, handles all edge cases.
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

        // ── Generic Asset Loading ────────────────────────────────

        /// <summary>
        /// Generic method to load assets of type T from a folder path.
        /// Sets isBuiltIn flag if path starts with "Packages/".
        /// </summary>
        public static List<T> LoadAssetsFromPath<T>(
            string folderPath,
            List<T> results,
            Func<T, string, bool> onLoaded = null) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);

                if (asset != null && !results.Contains(asset))
                {
                    results.Add(asset);
                    onLoaded?.Invoke(asset, path);
                }
            }

            return results;
        }

        /// <summary>
        /// Generic display name builder for assets with isBuiltIn flag.
        /// Built-in templates are suffixed with " (Built-in)" in the dropdown.
        /// </summary>
        public static string[] GetAssetDisplayNames<T>(List<T> assets, Func<T, string> getName, Func<T, bool> isBuiltIn) where T : class
        {
            return assets
                .Select(a => isBuiltIn(a) ? $"{getName(a)} (Built-in)" : getName(a))
                .ToArray();
        }

        // ── Asset Editing Batches ─────────────────────────────────

        /// <summary>
        /// Executes an action within a StartAssetEditing/StopAssetEditing scope.
        /// </summary>
        public static void BatchAssetEditing(Action action, bool refreshOnComplete = true, bool saveAssets = true)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                action?.Invoke();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (saveAssets) AssetDatabase.SaveAssets();
                if (refreshOnComplete) AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Validates that a folder path is relative (does not start with "Assets/").
        /// </summary>
        public static bool IsValidRelativePath(string path, string fieldName, List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add($"{fieldName} must be a relative path — do not start with \"Assets/\".");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalizes a path (backslashes to forward slashes, trims).
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace("\\", "/").Trim().Trim('/');
        }

        // ── Profile Management ────────────────────────────────────

        /// <summary>
        /// Saves a profile asset. Updates in place if exists, creates new if not.
        /// </summary>
        public static void SaveProfile<T>(T profile, string savePath,
            Func<T, string> getName, Action<T> onBeforeSave = null) where T : ScriptableObject
        {
            if (profile == null) return;

            EnsureDirectoryExists(savePath);
            onBeforeSave?.Invoke(profile);

            if (AssetDatabase.Contains(profile))
            {
                // Update existing asset
                string existingPath = AssetDatabase.GetAssetPath(profile);
                string expectedFile = getName(profile) + ".asset";

                if (Path.GetFileName(existingPath) != expectedFile)
                {
                    string renameError = AssetDatabase.RenameAsset(existingPath, getName(profile));
                    if (!string.IsNullOrEmpty(renameError))
                        Debug.LogWarning($"{ToolInfo.LogPrefix} Could not rename: {renameError}");
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            else
            {
                // Create new asset
                string assetPath = Path.Combine(savePath, getName(profile) + ".asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(profile, assetPath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"{ToolInfo.LogPrefix} Saved: {getName(profile)}");
        }

        /// <summary>
        /// Deletes a profile asset. Skips if null or built-in.
        /// </summary>
        public static void DeleteProfile<T>(T profile, Func<T, bool> isBuiltIn,
            Func<T, string> getName) where T : ScriptableObject
        {
            if (profile == null) return;
            if (isBuiltIn?.Invoke(profile) == true)
            {
                Debug.LogWarning($"{ToolInfo.LogPrefix} Cannot delete built-in profile: {getName(profile)}");
                return;
            }

            string path = AssetDatabase.GetAssetPath(profile);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
                Debug.Log($"{ToolInfo.LogPrefix} Deleted: {getName(profile)}");
            }
        }

        /// <summary>
        /// Creates a clone of a profile with automatic naming (_Copy, _Copy2, etc.).
        /// </summary>
        public static T CloneProfile<T>(T source, string savePath,
            Func<T, string> getName, Func<T, T> createClone) where T : ScriptableObject
        {
            if (source == null) return null;

            EnsureDirectoryExists(savePath);

            string cloneName = GetUniqueCloneName(savePath, getName(source));
            string assetPath = Path.Combine(savePath, cloneName + ".asset").Replace("\\", "/");

            T clone = createClone(source);

            // Set clone name via reflection
            SetNameProperty(clone, cloneName);

            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.Refresh();

            Debug.Log($"{ToolInfo.LogPrefix} Cloned '{getName(source)}' → '{cloneName}'");
            return clone;
        }

        /// <summary>
        /// Exports a profile to JSON file.
        /// </summary>
        public static void ExportProfile<T>(T profile, Func<T, object> toSerializableData,
            Func<T, string> getName)
        {
            if (profile == null) return;

            string path = EditorUtility.SaveFilePanel(
                "Export Profile", "", getName(profile), "json");

            if (string.IsNullOrEmpty(path)) return;

            var data = toSerializableData(profile);
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(path, json);

            Debug.Log($"{ToolInfo.LogPrefix} Exported to: {path}");
        }

        /// <summary>
        /// Imports a profile from JSON file.
        /// </summary>
        public static T ImportProfile<T>(
            string savePath,
            string fileTypeDescription,
            Func<string, T> fromJsonData,
            Action<T> onPostImport = null) where T : ScriptableObject
        {
            string path = EditorUtility.OpenFilePanel($"Import {fileTypeDescription}", "", "json");
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                T profile = fromJsonData(json);

                if (profile == null)
                {
                    EditorUtility.DisplayDialog(
                        "Import Failed",
                        $"The selected file is not a valid {fileTypeDescription}.",
                        "OK");
                    return null;
                }

                EnsureDirectoryExists(savePath);

                // Get name from profile (assumes it has profileName/templateName)
                string profileName = GetNameProperty(profile);
                string assetPath = BuildUniqueAssetPath(savePath, profileName);

                // Update name to match asset path if needed
                string finalName = Path.GetFileNameWithoutExtension(assetPath);
                SetNameProperty(profile, finalName);

                AssetDatabase.CreateAsset(profile, assetPath);
                AssetDatabase.Refresh();

                onPostImport?.Invoke(profile);

                Debug.Log($"{ToolInfo.LogPrefix} Imported: {finalName}");
                return profile;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ToolInfo.LogPrefix} Import failed: {ex.Message}");
                EditorUtility.DisplayDialog("Import Failed", "An error occurred while reading the file.", "OK");
                return null;
            }
        }

        /// <summary>
        /// Generic method to load all profiles of type T from built-in and user paths.
        /// </summary>
        public static List<T> LoadAllProfiles<T>(
            string userPath,
            string builtInPath,
            Action<T, string> onLoaded = null) where T : ScriptableObject
        {
            var profiles = new List<T>();

            if (AssetDatabase.IsValidFolder(builtInPath))
                LoadProfilesFromPath(builtInPath, profiles, onLoaded, isBuiltIn: true);

            if (!string.IsNullOrWhiteSpace(userPath))
            {
                EnsureAssetFolderExists(userPath);
                LoadProfilesFromPath(userPath, profiles, onLoaded, isBuiltIn: false);
            }

            return profiles;
        }

        /// <summary>
        /// Creates a new instance of a profile type with default values.
        /// Assumes the profile has a profileName/templateName field.
        /// </summary>
        public static T CreateNewProfile<T>(string defaultName) where T : ScriptableObject
        {
            T profile = ScriptableObject.CreateInstance<T>();
            SetNameProperty(profile, defaultName);
            return profile;
        }

        /// <summary>
        /// Updates a profile with data from an imported JSON object.
        /// Uses reflection to copy matching fields.
        /// </summary>
        public static void UpdateProfileFromData<T>(T target, object sourceData)
        {
            var targetFields = typeof(T).GetFields();
            var sourceFields = sourceData.GetType().GetFields();

            foreach (var sourceField in sourceFields)
            {
                // Find matching field by name and type
                var targetField = Array.Find(targetFields, f => f.Name == sourceField.Name);
                if (targetField != null && targetField.FieldType == sourceField.FieldType)
                {
                    targetField.SetValue(target, sourceField.GetValue(sourceData));
                }
            }
        }

        // ── Private Profile Helpers ─────────────────────────────────

        private static void LoadProfilesFromPath<T>(string folderPath, List<T> results,
            Action<T, string> onLoaded, bool isBuiltIn) where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<T>(path);

                if (profile != null)
                {
                    // Check if already in list (avoid duplicates)
                    bool alreadyExists = false;
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (results[i] == profile)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (!alreadyExists)
                    {
                        results.Add(profile);
                        onLoaded?.Invoke(profile, path);

                        // Set isBuiltIn flag if profile has that property
                        SetIsBuiltInProperty(profile, isBuiltIn);
                    }
                }
            }
        }

        private static string GetUniqueCloneName(string savePath, string baseName)
        {
            string candidate = baseName + "_Copy";
            int counter = 1;

            while (File.Exists(Path.Combine(ToAbsolutePath(savePath), candidate + ".asset")))
            {
                candidate = $"{baseName}_Copy{counter}";
                counter++;
            }

            return candidate;
        }

        // Reflection helpers for common profile properties
        private static void SetNameProperty(object obj, string name)
        {
            var field = obj.GetType().GetField("profileName") ??
                        obj.GetType().GetField("templateName");
            if (field != null && field.FieldType == typeof(string))
                field.SetValue(obj, name);
        }

        private static string GetNameProperty(object obj)
        {
            var field = obj.GetType().GetField("profileName") ??
                        obj.GetType().GetField("templateName");
            if (field != null && field.GetValue(obj) is string name)
                return name;
            return "Untitled";
        }

        private static void SetIsBuiltInProperty(object obj, bool isBuiltIn)
        {
            var field = obj.GetType().GetField("isBuiltIn");
            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(obj, isBuiltIn);
        }
    }
}