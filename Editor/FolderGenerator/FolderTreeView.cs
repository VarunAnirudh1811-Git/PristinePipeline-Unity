using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Displays a FolderTemplate's paths as a collapsible folder hierarchy.
    /// Display only — no selection, no editing.
    /// Fully expanded by default whenever backing data changes.
    /// </summary>
    public class FolderTreeView : TreeView<int>
    {
        // ── FolderItem ───────────────────────────────────────────────────────────

        private sealed class FolderItem : TreeViewItem<int>
        {
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
        private bool _reloaded = false;

        // ── Construction ─────────────────────────────────────────────────────────

        public FolderTreeView(TreeViewState<int> state) : base(state)
        {
            rowHeight = 18f;
            showBorder = true;
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the tree from the given template and root path.
        /// Must be called at least once before Draw().
        /// ExpandAll() is called here — AFTER base.Reload() completes —
        /// because ExpandAll() requires the tree's internal controller to be
        /// fully initialised, which only happens once Reload() returns.
        /// Calling ExpandAll() inside BuildRoot() causes a NullReferenceException.
        /// </summary>
        public void Reload(FolderTemplate template, string rootPath)
        {
            _template = template;
            _rootPath = string.IsNullOrEmpty(rootPath) ? "Assets" : rootPath;
            _reloaded = false;

            // base.Reload() calls BuildRoot() internally and constructs the item
            // list. By the time this line returns the tree has valid data.
            Reload();

            // ExpandAll() is safe here — the controller is fully initialised
            // after Reload() completes. Never call it inside BuildRoot().
            ExpandAll();

            _reloaded = true;
        }

        /// <summary>
        /// Renders the tree into the given rect.
        /// The rect must come from EditorGUILayout.GetControlRect().
        /// </summary>
        public void Draw(Rect rect)
        {
            if (!_reloaded)
                return;

            EventType type = Event.current.type;
            if (type != EventType.Repaint &&
                type != EventType.Layout &&
                type != EventType.MouseDown &&
                type != EventType.MouseUp &&
                type != EventType.ScrollWheel &&
                type != EventType.KeyDown &&
                type != EventType.KeyUp)
                return;

            OnGUI(rect);
        }

        // ── TreeView<int> overrides ──────────────────────────────────────────────

        protected override TreeViewItem<int> BuildRoot()
        {
            var root = new TreeViewItem<int> { id = 0, depth = -1, displayName = "root" };

            if (_template == null || _template.FolderPaths.Count == 0)
            {
                root.AddChild(new TreeViewItem<int>
                {
                    id = 1,
                    depth = 0,
                    displayName = "(no paths)"
                });

                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            var trieRoot = new TrieNode("root", _rootPath);
            int nextId = 1;

            foreach (string folder in _template.FolderPaths)
            {
                string normalized = FolderGeneratorUtility.NormalizePath(folder);
                if (string.IsNullOrEmpty(normalized)) continue;

                string[] segments = normalized.Split('/');
                trieRoot.Insert(segments, _rootPath, ref nextId);
            }

            foreach (TrieNode child in trieRoot.Children.Values.OrderBy(n => n.Name))
                BuildItemsRecursive(root, child, 0);

            // Always call this at the end of BuildRoot — recalculates every
            // item's depth field from the parent-child relationships we built.
            SetupDepthsFromParentsAndChildren(root);

            // NOTE: ExpandAll() is intentionally NOT called here.
            // It must be called after base.Reload() returns in the public
            // Reload(template, rootPath) method above.

            return root;
        }

        protected override void RowGUI(TreeView<int>.RowGUIArgs args)
        {
            float indent = GetContentIndent(args.item);
            Rect labelRect = new (
                args.rowRect.x + indent,
                args.rowRect.y,
                args.rowRect.width - indent,
                args.rowRect.height);

            Rect iconRect = new (labelRect.x, labelRect.y + 1f, 16f, 16f);
            GUI.DrawTexture(
                iconRect,
                EditorGUIUtility.IconContent("Folder Icon").image as Texture2D,
                ScaleMode.ScaleToFit);

            Rect textRect = new (
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
                GUIStyle muted = new (EditorStyles.label)
                { normal = { textColor = Color.gray } };
                EditorGUI.LabelField(textRect, args.item.displayName, muted);
            }
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;
        protected override bool CanBeParent(TreeViewItem<int> item) => true;

        // ── Trie ─────────────────────────────────────────────────────────────────

        private sealed class TrieNode
        {
            public string Name { get; }
            public string FullPath { get; }
            public int Id { get; set; }
            public Dictionary<string, TrieNode> Children { get; }
                = new Dictionary<string, TrieNode>();

            public TrieNode(string name, string fullPath)
            {
                Name = name;
                FullPath = fullPath;
            }

            public void Insert(string[] segments, string parentPath, ref int nextId)
            {
                if (segments.Length == 0) return;

                string seg = segments[0];
                string fullPath = parentPath + "/" + seg;

                if (!Children.TryGetValue(seg, out TrieNode child))
                {
                    child = new TrieNode(seg, fullPath) { Id = nextId++ };
                    Children[seg] = child;
                }

                child.Insert(segments.Skip(1).ToArray(), fullPath, ref nextId);
            }
        }

        private static void BuildItemsRecursive(
            TreeViewItem<int> parent, TrieNode node, int depth)
        {
            var item = new FolderItem(node.Id, depth, node.Name, node.FullPath);
            parent.AddChild(item);

            foreach (TrieNode child in node.Children.Values.OrderBy(n => n.Name))
                BuildItemsRecursive(item, child, depth + 1);
        }
    }
}