using System.Collections.Generic;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Data asset representing a reusable folder structure template.
    /// Lives in Runtime so its type is accessible outside editor-only code.
    /// Created via Right Click > Create > GlyphLabs > Folder Template.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FolderTemplate",
        menuName = "GlyphLabs/Folder Template")]
    public class FolderTemplate : ScriptableObject
    {
        // ── Fields ───────────────────────────────────────────────────────────────

        public string templateName = "New Template";
        public string description = "";

        [SerializeField]
        private List<string> folderPaths = new ();

        /// <summary>
        /// Hidden from the Inspector — set only by GlyphLabs on built-in package templates.
        /// When true, the Folder Generator tab treats this template as read-only.
        /// </summary>
        [HideInInspector]
        public bool isBuiltIn = false;

        // ── API ──────────────────────────────────────────────────────────────────

        /// <summary>Read-only view of the folder paths list.</summary>
        public IReadOnlyList<string> FolderPaths => folderPaths;

        /// <summary>
        /// Replaces the folder paths list with a new set.
        /// Called by FolderGeneratorUtility — not intended for direct use.
        /// </summary>
        public void SetFolderPaths(List<string> paths)
        {
            folderPaths = new List<string>(paths);
        }

        /// <summary>Appends a single path entry.</summary>
        public void AddFolderPath(string path)
        {
            folderPaths.Add(path);
        }

        /// <summary>Removes a path entry at the given index.</summary>
        public void RemoveFolderPathAt(int index)
        {
            if (index >= 0 && index < folderPaths.Count)
                folderPaths.RemoveAt(index);
        }
    }
}