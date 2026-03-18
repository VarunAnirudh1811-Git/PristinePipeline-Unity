using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Mesh import settings for one named preset.
    /// Holds all ModelImporter-level settings that the FBX Importer controls.
    /// </summary>
    [Serializable]
    public class FBXImportPreset
    {
        [Tooltip("Display name — e.g. Static Mesh, Skeletal Mesh, Collision.")]
        public string presetName = "New Preset";

        // ── Mesh settings ────────────────────────────────────────────────────────

        [Tooltip("Uniform scale applied on import. Maya = 0.01, Blender = 1.0.")]
        public float scaleFactor = 1.0f;

        [Tooltip("Reduces mesh data size on disk and in memory. Off preserves precision.")]
        public ModelImporterMeshCompression meshCompression = ModelImporterMeshCompression.Off;

        [Tooltip("Allows CPU-side mesh access at runtime. Off for most static props.")]
        public bool readWriteEnabled = false;

        [Tooltip("Reorders vertices for better GPU cache performance. Recommended on.")]
        public bool optimizeMesh = true;

        [Tooltip("Generates a second UV channel for lightmap baking. On for static meshes.")]
        public bool generateLightmapUVs = false;

        [Tooltip("How normals are sourced. Import preserves artist intent from the DCC tool.")]
        public ModelImporterNormals normals = ModelImporterNormals.Import;

        [Tooltip("How tangents are sourced. Calculate Mikkt Space matches most bakers.")]
        public ModelImporterTangents tangents = ModelImporterTangents.CalculateMikk;

        [Tooltip("Swaps UV channel 0 and 1. Fixes assets exported with inverted UV order.")]
        public bool swapUVs = false;
    }

    /// <summary>
    /// Maps a wildcard name pattern to an FBXImportPreset.
    /// When an FBX filename matches the pattern, the linked preset is applied.
    /// Rules are evaluated in order — last match wins.
    /// </summary>
    [Serializable]
    public class FBXImportRule
    {
        [Tooltip("Wildcard name pattern — e.g. SM_*, SK_*, P_*. Case-insensitive.")]
        public string namePattern = "";

        [Tooltip("The import preset applied when this rule matches.")]
        public FBXImportPreset preset = new FBXImportPreset();

        [Tooltip("Optional note describing what this rule is for.")]
        public string note = "";

        /// <summary>True when this rule has a name pattern set.</summary>
        public bool HasNamePattern => !string.IsNullOrWhiteSpace(namePattern);
    }

    /// <summary>
    /// ScriptableObject that defines FBX import behaviour for a project.
    /// Holds a list of FBXImportRules (one per mesh type prefix) and a
    /// default FBXImportPreset applied to unmatched files with a warning.
    ///
    /// Created via Right Click > Create > GlyphLabs > FBX Import Profile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FBXImportProfile",
        menuName = "GlyphLabs/FBX Import Profile")]
    public class FBXImportProfile : ScriptableObject
    {
        // ── Identity ─────────────────────────────────────────────────────────────

        public string profileName = "New FBX Import Profile";
        public string description = "";

        // ── Naming convention ────────────────────────────────────────────────────

        [Tooltip("When on, FBX files that don't match any rule pattern are blocked at import. " +
                 "When off, a warning is logged but the import continues.")]
        public bool enforceNamingConvention = false;

        [Tooltip("Valid name prefixes used for convention validation — e.g. SM_, SK_, P_. " +
                 "Any FBX whose name doesn't start with one of these is flagged.")]
        public List<string> validPrefixes = new List<string> { "SM_", "SK_", "P_" };

        // ── Default preset ───────────────────────────────────────────────────────

        [Tooltip("Applied to any FBX that doesn't match a named rule. " +
                 "A warning is always logged when this fallback is used.")]
        public FBXImportPreset defaultPreset = new FBXImportPreset
        {
            presetName = "Default"
        };

        // ── Rules ────────────────────────────────────────────────────────────────

        [SerializeField]
        private List<FBXImportRule> rules = new List<FBXImportRule>();

        // ── API ──────────────────────────────────────────────────────────────────

        /// <summary>Read-only view of the rules list.</summary>
        public IReadOnlyList<FBXImportRule> Rules => rules;

        public void SetRules(List<FBXImportRule> newRules)
        {
            rules = new List<FBXImportRule>(newRules);
        }

        public void AddRule(FBXImportRule rule)
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