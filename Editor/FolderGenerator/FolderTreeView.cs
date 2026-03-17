using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Displays a FolderTemplate's paths as a collapsible folder hierarchy.
    /// Display only — no selection, no editing. Fully expanded by default
    /// whenever the backing data changes.
    ///
    /// Lifecycle:
    ///   1. Construct once and hold on FolderGeneratorTab.
    ///   2. Call Reload(template, rootPath) whenever the template or root changes.
    ///   3. Call Draw(rect) inside OnGUI to render.
    /// </summary>
    public class FolderTreeView : TreeView<int>
    {
        // ── Tree item ────────────────────────────────────────────────────────────

        private sealed class FolderItem : TreeViewItem<int>
        {
            /// <summary>Full Unity asset path shown as a tooltip.</summary>
            public string FullPath { get; }

            public FolderItem(int id, int depth, string displayName, string fullPath)
                : base(id, depth, displayName)
            {
                FullPath = fullPath;
            }
        }

        // ── State ────────────────────────────────────────────────────────────────

        private FolderTemplate _template;
        private string _rootPath;

        // ── Construction ─────────────────────────────────────────────────────────

        // TreeViewState<int> is the non-deprecated companion to TreeView<int>
        public FolderTreeView(TreeViewState<int> state) : base(state) { }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the tree from the given template and root path, then
        /// expands all nodes. Call whenever the template or root path changes.
        /// Passing null clears the tree.
        /// </summary>
        public void Reload(FolderTemplate template, string rootPath)
        {
            _template = template;
            _rootPath = rootPath;
            Reload();               // triggers BuildRoot → ExpandAll
        }

        /// <summary>Renders the tree view inside the given rect.</summary>
        public void Draw(Rect rect) => OnGUI(rect);

        // ── TreeView<int> overrides ──────────────────────────────────────────────

        protected override TreeViewItem<int> BuildRoot()
        {
            // Hidden root required by TreeView<int> — never rendered
            var root = new TreeViewItem<int> { id = 0, depth = -1, displayName = "root" };

            if (_template == null || _template.FolderPaths.Count == 0)
            {
                // Empty placeholder so TreeView<int> doesn't throw on an empty list
                root.AddChild(new TreeViewItem<int> { id = 1, depth = 0, displayName = "(no paths)" });
                return root;
            }

            // Parse flat path strings into a trie so shared prefixes become
            // shared parent nodes, giving us a true folder hierarchy.
            var trieRoot = new TrieNode("root", _rootPath);
            int nextId = 1;

            foreach (string folder in _template.FolderPaths)
            {
                string normalized = FolderGeneratorUtility.NormalizePath(folder);
                if (string.IsNullOrEmpty(normalized)) continue;

                string[] segments = normalized.Split('/');
                trieRoot.Insert(segments, _rootPath, ref nextId);
            }

            // Walk the trie and build TreeViewItem<int> instances
            foreach (TrieNode child in trieRoot.Children.Values.OrderBy(n => n.Name))
                BuildItemsRecursive(root, child, 0);

            SetupDepthsFromParentsAndChildren(root);

            // Expand all nodes after build
            ExpandAll();

            return root;
        }

        // RowGUIArgs is a non-generic nested struct on TreeView<int> —
        // referenced as TreeView<int>.RowGUIArgs, NOT parameterized as RowGUIArgs<int>
        protected override void RowGUI(TreeView<int>.RowGUIArgs args)
        {
            float indent = GetContentIndent(args.item);
            Rect labelRect = new Rect(
                args.rowRect.x + indent,
                args.rowRect.y,
                args.rowRect.width - indent,
                args.rowRect.height);

            // Folder icon
            Rect iconRect = new Rect(labelRect.x, labelRect.y + 1f, 16f, 16f);
            GUI.DrawTexture(
                iconRect,
                EditorGUIUtility.IconContent("Folder Icon").image as Texture2D,
                ScaleMode.ScaleToFit);

            // Display name — GUIContent tooltip set via property to avoid
            // ambiguity with the (string, Texture) constructor overload
            Rect textRect = new Rect(
                labelRect.x + 20f,
                labelRect.y,
                labelRect.width - 20f,
                labelRect.height);

            if (args.item is FolderItem fi)
            {
                EditorGUI.LabelField(textRect,
                    new GUIContent(fi.displayName) { tooltip = fi.FullPath });
            }
            else
            {
                // Placeholder "(no paths)" row — muted style
                GUIStyle muted = new GUIStyle(EditorStyles.label)
                { normal = { textColor = Color.gray } };
                EditorGUI.LabelField(textRect, args.item.displayName, muted);
            }
        }

        // Display only — no selection, no reparenting
        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
        protected override bool CanBeParent(TreeViewItem<int> item) => true;

        // ── Trie for path parsing ────────────────────────────────────────────────

        /// <summary>
        /// Minimal trie node. Each node represents one path segment.
        /// Shared prefixes across paths collapse into shared parent nodes,
        /// producing the correct folder hierarchy.
        /// </summary>
        private sealed class TrieNode
        {
            public string Name { get; }
            public string FullPath { get; }
            public int Id { get; set; }
            public Dictionary<string, TrieNode> Children { get; } = new Dictionary<string, TrieNode>();

            public TrieNode(string name, string fullPath)
            {
                Name = name;
                FullPath = fullPath;
            }

            /// <summary>
            /// Inserts a path (already split into segments) into the trie.
            /// Creates intermediate nodes as needed.
            /// </summary>
            public void Insert(string[] segments, string parentPath, ref int nextId)
            {
                if (segments.Length == 0) return;

                string seg = segments[0];
                string fullPath = parentPath + "/" + seg;

                if (!Children.TryGetValue(seg, out TrieNode child))
                {
                    child = new TrieNode(seg, fullPath);
                    child.Id = nextId++;
                    Children[seg] = child;
                }

                child.Insert(segments.Skip(1).ToArray(), fullPath, ref nextId);
            }
        }

        private static void BuildItemsRecursive(TreeViewItem<int> parent, TrieNode node, int depth)
        {
            var item = new FolderItem(node.Id, depth, node.Name, node.FullPath);
            parent.AddChild(item);

            foreach (TrieNode child in node.Children.Values.OrderBy(n => n.Name))
                BuildItemsRecursive(item, child, depth + 1);
        }
    }
}