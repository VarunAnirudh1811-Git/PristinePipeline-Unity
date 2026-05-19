using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Stateless utility methods for the Folder Generator.
    /// No EditorWindow dependency — safe to call from any editor context.
    /// All operations that touch the AssetDatabase or filesystem live here,
    /// keeping FolderGeneratorTab and FolderGeneratorTemplateCreatorTab focused on UI only.
    /// </summary>
    /// v1.2 — Active Root refactor:
    ///   - CreateFolders no longer accepts a rootPath parameter. It reads
    ///     ToolSettings.ActiveRootPath directly, making the Active Root the single
    ///     source of truth for all folder creation.
    ///   - ResolveRootPath removed — callers that need the current root for display
    ///     or preview should read ToolSettings.ActiveRootPath directly.
    public static class FolderGeneratorUtility
    {
        // ── Template loading ─────────────────────────────────────────────────────
        /// <summary>
        /// Loads all FolderTemplate assets visible to the project.
        /// Built-in templates (from the package) are loaded first, then user templates
        /// from ToolSettings.FolderGen_TemplateSavePath. Duplicates are excluded.
        /// </summary>
        public static List<FolderTemplate> LoadAllTemplates()
        {
            return PristinePipelineUtility.LoadAllProfiles<FolderTemplate>(
                ToolSettings.FolderGen_TemplateSavePath,
                ToolInfo.BuiltInTemplatePath,
                onLoaded: (template, path) => template.isBuiltIn = path.StartsWith("Packages/")
            );
        }

        public static string[] GetTemplateDisplayNames(List<FolderTemplate> templates)
        {
            return PristinePipelineUtility.GetAssetDisplayNames(
                templates,
                t => t.templateName,
                t => t.isBuiltIn
            );
        }

        // ── Template persistence ─────────────────────────────────────────────────
        /// <summary>
        /// Saves a FolderTemplate asset. If the asset already exists in the database
        /// it is updated in place (rename handled if name changed). If it is new,
        /// it is created at the user template save path.
        /// </summary>
        public static void SaveTemplate(FolderTemplate template)
        {
            PristinePipelineUtility.SaveProfile(
                template,
                ToolSettings.FolderGen_TemplateSavePath,
                t => t.templateName
            );
        }

        /// <summary>
        /// Creates a duplicate of the given template and saves it to the user path.
        /// The clone name is suffixed with _Copy, or _Copy1, _Copy2, etc. if copies exist.
        /// </summary>
        public static FolderTemplate CloneTemplate(FolderTemplate source)
        {
            return PristinePipelineUtility.CloneProfile(
                source,
                ToolSettings.FolderGen_TemplateSavePath,
                t => t.templateName,
                src =>
                {
                    var clone = ScriptableObject.CreateInstance<FolderTemplate>();
                    clone.templateName = src.templateName + "_Copy";
                    clone.description = src.description;
                    clone.isBuiltIn = false;
                    clone.SetFolderPaths(new List<string>(src.FolderPaths));
                    return clone;
                }
            );
        }

        /// <summary>
        /// Deletes a user template asset. Built-in templates are silently ignored.
        /// </summary>
        public static void DeleteTemplate(FolderTemplate template)
        {
            PristinePipelineUtility.DeleteProfile(
                template,
                t => t.isBuiltIn,
                t => t.templateName
            );
        }

        // ── Template import / export ─────────────────────────────────────────────

        /// <summary>
        /// Exports a FolderTemplate to a JSON file via a save panel dialog.
        /// </summary>
        public static void ExportTemplate(FolderTemplate template)
        {
            PristinePipelineUtility.ExportProfile(
                template,
                t => new FolderTemplateData
                {
                    templateName = t.templateName,
                    description = t.description,
                    folderPaths = new List<string>(t.FolderPaths)
                },
                t => t.templateName
            );
        }

        /// <summary>
        /// Imports a FolderTemplate from a JSON file via an open panel dialog.
        /// Returns the imported template, or null if the operation was cancelled or failed.
        /// </summary>
        public static FolderTemplate ImportTemplate()
        {
            return PristinePipelineUtility.ImportProfile<FolderTemplate>(
                ToolSettings.FolderGen_TemplateSavePath,
                "Folder Template",
                json =>
                {
                    var data = JsonUtility.FromJson<FolderTemplateData>(json);
                    if (data == null || string.IsNullOrWhiteSpace(data.templateName))
                        return null;

                    var template = ScriptableObject.CreateInstance<FolderTemplate>();
                    template.templateName = data.templateName;
                    template.description = data.description;
                    template.SetFolderPaths(data.folderPaths ?? new List<string>());
                    return template;
                }
            );
        }

        // ── Folder creation ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates the folder structure defined by the template under ActiveRoot.
        /// Optionally writes a .keep file into each empty folder.
        /// </summary>
        public static void CreateFolders(FolderTemplate template, bool addKeepFiles)
        {
            if (template == null) return;

            string root = ToolSettings.ActiveRootPath;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string folder in template.FolderPaths)
                {
                    string normalized = NormalizePath(folder);
                    if (string.IsNullOrEmpty(normalized)) continue;

                    string assetRelativePath = root + "/" + normalized;
                    string fullPath = PristinePipelineUtility.ToAbsolutePath(assetRelativePath);

                    try
                    {
                        if (!Directory.Exists(fullPath))
                        {
                            Directory.CreateDirectory(fullPath);
                            Debug.Log($"{ToolInfo.LogPrefix} Created folder: {assetRelativePath}");
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