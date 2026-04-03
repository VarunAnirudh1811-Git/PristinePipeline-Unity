using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Stateless utility methods for the Folder Generator.
    /// No EditorWindow dependency — safe to call from any editor context.
    /// All operations that touch the AssetDatabase or filesystem live here,
    /// keeping FolderGeneratorTab and TemplateCreatorTab focused on UI only.
    /// </summary>
    public static class FolderGeneratorUtility
    {
        private static string ProjectRoot =>
            Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);

        // ── Template loading ─────────────────────────────────────────────────────

        /// <summary>
        /// Loads all FolderTemplate assets visible to the project.
        /// Built-in templates (from the package) are loaded first, then user templates
        /// from ToolSettings.FolderGen_TemplateSavePath. Duplicates are excluded.
        /// </summary>
        public static List<FolderTemplate> LoadAllTemplates()
        {
            var templates = new List<FolderTemplate>();
            string builtInPath = ToolInfo.BuiltInTemplatePath;
            string userPath = ToolSettings.FolderGen_TemplateSavePath;

            if (AssetDatabase.IsValidFolder(builtInPath))
            {
                LoadTemplatesFromPath(builtInPath, templates);
            }
                        

            if (AssetDatabase.IsValidFolder(userPath))
            {
                LoadTemplatesFromPath(userPath, templates);
            }

            return templates;
        }

        private static void LoadTemplatesFromPath(string folderPath, List<FolderTemplate> results)
        {
            string[] guids = AssetDatabase.FindAssets("t:FolderTemplate", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var template = AssetDatabase.LoadAssetAtPath<FolderTemplate>(path);

                if (template != null && !results.Contains(template))
                {
                    results.Add(template);
                    template.isBuiltIn = path.StartsWith("Packages/");
                }
            }
        }

        /// <summary>
        /// Returns an array of display names for a list of templates.
        /// Built-in templates are suffixed with " (Built-in)" in the dropdown.
        /// </summary>
        public static string[] GetTemplateDisplayNames(List<FolderTemplate> templates)
        {
            return templates
                .Select(t => t.isBuiltIn ? $"{t.templateName} (Built-in)" : t.templateName)
                .ToArray();
        }

        // ── Template persistence ─────────────────────────────────────────────────

        /// <summary>
        /// Saves a FolderTemplate asset. If the asset already exists in the database
        /// it is updated in place (rename handled if name changed). If it is new,
        /// it is created at the user template save path.
        /// </summary>
        public static void SaveTemplate(FolderTemplate template)
        {
            if (template == null) return;

            EnsureDirectoryExists(ToolSettings.FolderGen_TemplateSavePath);

            if (AssetDatabase.Contains(template))
            {
                // Rename the asset file if the templateName changed
                string existingPath = AssetDatabase.GetAssetPath(template);
                string expectedFile = template.templateName + ".asset";

                if (Path.GetFileName(existingPath) != expectedFile)
                {
                    string renameError = AssetDatabase.RenameAsset(existingPath, template.templateName);
                    if (!string.IsNullOrEmpty(renameError))
                        Debug.LogWarning($"{ToolInfo.LogPrefix} Could not rename template asset: {renameError}");
                }

                EditorUtility.SetDirty(template);
                AssetDatabase.SaveAssets();
            }
            else
            {
                string assetPath = Path.Combine(
                    ToolSettings.FolderGen_TemplateSavePath,
                    template.templateName + ".asset").Replace("\\", "/");

                AssetDatabase.CreateAsset(template, assetPath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"{ToolInfo.LogPrefix} Template saved: {template.templateName}");
        }

        /// <summary>
        /// Creates a duplicate of the given template and saves it to the user path.
        /// The clone name is suffixed with _Copy, or _Copy1, _Copy2, etc. if copies exist.
        /// </summary>
        public static FolderTemplate CloneTemplate(FolderTemplate source)
        {
            if (source == null) return null;

            EnsureDirectoryExists(ToolSettings.FolderGen_TemplateSavePath);

            string cloneName = source.templateName + "_Copy";
            string assetPath = BuildUniqueAssetPath(ToolSettings.FolderGen_TemplateSavePath, cloneName);

            var clone = ScriptableObject.CreateInstance<FolderTemplate>();
            clone.templateName = Path.GetFileNameWithoutExtension(assetPath);
            clone.description = source.description;
            clone.isBuiltIn = false;
            clone.SetFolderPaths(new List<string>(source.FolderPaths));

            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.Refresh();

            Debug.Log($"{ToolInfo.LogPrefix} Cloned '{source.templateName}' → '{clone.templateName}'");
            return clone;
        }

        /// <summary>
        /// Deletes a user template asset. Built-in templates are silently ignored.
        /// </summary>
        public static void DeleteTemplate(FolderTemplate template)
        {
            if (template == null || template.isBuiltIn) return;

            string path = AssetDatabase.GetAssetPath(template);

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
                Debug.Log($"{ToolInfo.LogPrefix} Deleted template: {template.templateName}");
            }
        }

        // ── Template import / export ─────────────────────────────────────────────

        /// <summary>
        /// Exports a FolderTemplate to a JSON file via a save panel dialog.
        /// </summary>
        public static void ExportTemplate(FolderTemplate template)
        {
            if (template == null) return;

            string path = EditorUtility.SaveFilePanel(
                "Export Template",
                "",
                template.templateName,
                "json");

            if (string.IsNullOrEmpty(path)) return;

            var data = new FolderTemplateData
            {
                templateName = template.templateName,
                description = template.description,
                folderPaths = new List<string>(template.FolderPaths)
            };

            File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"{ToolInfo.LogPrefix} Exported template to: {path}");
        }

        /// <summary>
        /// Imports a FolderTemplate from a JSON file via an open panel dialog.
        /// Returns the imported template, or null if the operation was cancelled or failed.
        /// </summary>
        public static FolderTemplate ImportTemplate()
        {
            string path = EditorUtility.OpenFilePanel("Import Template", "", "json");
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<FolderTemplateData>(json);

                if (data == null || string.IsNullOrWhiteSpace(data.templateName))
                {
                    EditorUtility.DisplayDialog(
                        "Import Failed",
                        "The selected file is not a valid Folder Template.",
                        "OK");
                    return null;
                }

                EnsureDirectoryExists(ToolSettings.FolderGen_TemplateSavePath);

                string assetPath = BuildUniqueAssetPath(ToolSettings.FolderGen_TemplateSavePath, data.templateName);

                var template = ScriptableObject.CreateInstance<FolderTemplate>();
                template.templateName = Path.GetFileNameWithoutExtension(assetPath);
                template.description = data.description;
                template.SetFolderPaths(data.folderPaths ?? new List<string>());

                AssetDatabase.CreateAsset(template, assetPath);
                AssetDatabase.Refresh();

                Debug.Log($"{ToolInfo.LogPrefix} Imported template: {template.templateName}");
                return template;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ToolInfo.LogPrefix} Import failed: {ex.Message}");
                EditorUtility.DisplayDialog("Import Failed", "An error occurred while reading the file.", "OK");
                return null;
            }
        }

        // ── Folder creation ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates the folder structure defined by the template under rootPath.
        /// Optionally writes a .keep file into each empty folder.
        /// </summary>
        public static void CreateFolders(FolderTemplate template, string rootPath, bool addKeepFiles)
        {
            if (template == null) return;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string folder in template.FolderPaths)
                {
                    string normalized = NormalizePath(folder);
                    if (string.IsNullOrEmpty(normalized)) continue;

                    string assetRelativePath = Path.Combine(rootPath, normalized).Replace("\\", "/");
                    string fullPath = ToAbsolutePath(assetRelativePath);

                    try
                    {
                        if (!Directory.Exists(fullPath))
                        {
                            Directory.CreateDirectory(fullPath);
                            Debug.Log($"{ToolInfo.LogPrefix} Created folder: {fullPath}");
                        }

                        if (addKeepFiles && IsDirectoryEmpty(fullPath))
                            File.WriteAllText(
                                Path.Combine(fullPath, ".keep"),
                                "# This file ensures the folder is tracked by version control.\n");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"{ToolInfo.LogPrefix} Failed to create '{fullPath}': {ex.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        // ── Path resolution ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the root path for folder generation.
        /// If useProjectRoot is true and projectName is provided, nests under Assets/ProjectName.
        /// Otherwise returns "Assets".
        /// </summary>
        public static string ResolveRootPath(bool useProjectRoot, string projectName)
        {
            if (!useProjectRoot || string.IsNullOrWhiteSpace(projectName))
                return "Assets";

            return Path.Combine("Assets", projectName.Trim().Replace(" ", "")).Replace("\\", "/");
        }

        /// <summary>
        /// Strips characters that are invalid in folder names (preserving / and \).
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars()
                .Except(new[] { '/', '\\' })
                .ToArray();

            string cleaned = new (path.Where(c => !invalid.Contains(c)).ToArray());
            return cleaned.Replace("\\", "/").Trim().Trim('/');
        }

        /// <summary>
        /// Returns true if the given path contains characters invalid for a folder name.
        /// </summary>
        public static bool HasInvalidCharacters(string path)
        {
            char[] invalid = Path.GetInvalidFileNameChars()
                .Except(new[] { '/', '\\' })
                .ToArray();

            return path.IndexOfAny(invalid) >= 0;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static bool IsDirectoryEmpty(string path) =>
            Directory.GetFiles(path).Length == 0 &&
            Directory.GetDirectories(path).Length == 0;

        private static string ToAbsolutePath(string unityAssetPath) =>
            Path.Combine(ProjectRoot, unityAssetPath).Replace("\\", "/");

        private static void EnsureDirectoryExists(string unityAssetPath)
        {
            string absolutePath = ToAbsolutePath(unityAssetPath);
            if (!Directory.Exists(absolutePath))
                Directory.CreateDirectory(absolutePath);
        }

        private static string BuildUniqueAssetPath(string folderPath, string baseName)
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
    }

    // ── JSON serialization container ─────────────────────────────────────────────

    [Serializable]
    public class FolderTemplateData
    {
        public string templateName;
        public string description;
        public List<string> folderPaths;
    }
}