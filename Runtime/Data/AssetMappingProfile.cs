using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// A single extension → folder mapping rule.
    /// Extension is stored without the dot (e.g. "png" not ".png") for
    /// consistent comparison. NamePattern is an optional wildcard string —
    /// if empty the rule matches any file with the given extension.
    /// </summary>
    [Serializable]
    public class MappingRule
    {
        [Tooltip("File extension without the dot — e.g. png, fbx, wav")]
        public string extension = "";

        [Tooltip("Optional wildcard name pattern — e.g. T_* or *_BaseColor. Leave empty to match any name.")]
        public string namePattern = "";

        [Tooltip("Destination folder as a Unity asset path — e.g. Assets/Art/Textures")]
        public string destinationFolder = "";

        [Tooltip("Human-readable note about what this rule does. Not used at runtime.")]
        public string note = "";

        /// <summary>True when this rule has a name pattern set.</summary>
        public bool HasNamePattern => !string.IsNullOrWhiteSpace(namePattern);
    }

    /// <summary>
    /// ScriptableObject that holds a list of MappingRules for the Asset Organizer.
    /// Lives in Runtime so its type is accessible outside editor-only code.
    /// Created via Right Click > Create > GlyphLabs > Mapping Profile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MappingProfile",
        menuName = "GlyphLabs/Mapping Profile")]
    public class AssetMappingProfile : ScriptableObject
    {
        public string profileName = "New Profile";
        public string description = "";

        [SerializeField]
        private List<MappingRule> rules = new List<MappingRule>();

        // ── API ──────────────────────────────────────────────────────────────────

        /// <summary>Read-only view of the rules list.</summary>
        public IReadOnlyList<MappingRule> Rules => rules;

        public void SetRules(List<MappingRule> newRules)
        {
            rules = new List<MappingRule>(newRules);
        }

        public void AddRule(MappingRule rule)
        {
            rules.Add(rule);
        }

        public void RemoveRuleAt(int index)
        {
            if (index >= 0 && index < rules.Count)
                rules.RemoveAt(index);
        }
    }
}