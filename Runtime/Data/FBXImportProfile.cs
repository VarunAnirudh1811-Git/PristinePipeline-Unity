using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlyphLabs
{
    /// <summary>
    /// Mesh import settings for one named preset.
    /// Holds all ModelImporter-level settings that the FBX Importer controls.
    ///
    /// Editor-typed enum fields (ModelImporterMeshCompression, ModelImporterNormals,
    /// ModelImporterTangents) are stored as plain ints so this class compiles cleanly
    /// in the Runtime assembly without any UnityEditor dependency.
    ///
    /// FBXImporterUtility (Editor assembly) owns the cast back to the editor enums
    /// via FBXImportPresetEditorExtensions. FBXImporterTab reads/writes through the
    /// same extensions so no cast logic is scattered across the codebase.
    ///
    /// Default int values match the enum ordinals in use prior to the split:
    ///   meshCompressionInt  = 0  → ModelImporterMeshCompression.Off
    ///   normalsInt          = 1  → ModelImporterNormals.Import
    ///   tangentsInt         = 3  → ModelImporterTangents.CalculateMikk
    /// </summary>
    [Serializable]
    public class FBXImportPreset
    {
        [Tooltip("Display name — e.g. Static Mesh, Skeletal Mesh, Collision.")]
        public string presetName = "New Preset";

        // ── Mesh settings ────────────────────────────────────────────────────────

        [Tooltip("Uniform scale applied on import. Maya = 0.01, Blender = 1.0.")]
        public float scaleFactor = 1.0f;

        /// <summary>
        /// Serialized as int. Corresponds to ModelImporterMeshCompression ordinal.
        /// 0 = Off, 1 = Low, 2 = Medium, 3 = High.
        /// Use FBXImportPresetEditorExtensions.MeshCompression in Editor code.
        /// Hidden from the default Inspector — drawn as EnumPopup by FBXImportPresetDrawer.
        /// </summary>
        [HideInInspector]
        public int meshCompressionInt = 0;

        [Tooltip("Allows CPU-side mesh access at runtime. Off for most static props.")]
        public bool readWriteEnabled = false;

        [Tooltip("Reorders vertices for better GPU cache performance. Recommended on.")]
        public bool optimizeMesh = true;

        [Tooltip("Generates a second UV channel for lightmap baking. On for static meshes.")]
        public bool generateLightmapUVs = false;

        /// <summary>
        /// Serialized as int. Corresponds to ModelImporterNormals ordinal.
        /// 0 = None, 1 = Import, 2 = Calculate.
        /// Use FBXImportPresetEditorExtensions.Normals in Editor code.
        /// Hidden from the default Inspector — drawn as EnumPopup by FBXImportPresetDrawer.
        /// </summary>
        [HideInInspector]
        public int normalsInt = 1;

        /// <summary>
        /// Serialized as int. Corresponds to ModelImporterTangents ordinal.
        /// 0 = None, 1 = Import, 2 = CalculateLegacy, 3 = CalculateMikk, 4 = CalculateLegacyWithSplitTangents.
        /// Use FBXImportPresetEditorExtensions.Tangents in Editor code.
        /// Hidden from the default Inspector — drawn as EnumPopup by FBXImportPresetDrawer.
        /// </summary>
        [HideInInspector]
        public int tangentsInt = 3;

        [Tooltip("Swaps UV channel 0 and 1. Fixes assets exported with inverted UV order.")]
        public bool swapUVs = false;

        // ── Phase 5 — material, texture, prefab settings ─────────────────────────

        [Tooltip("Prefix prepended to created material names — e.g. M_ produces M_Rock.")]
        public string materialPrefix = "M_";

        [Tooltip("Unity asset path where created materials are saved — e.g. Assets/Art/Materials.")]
        public string materialsFolder = "Assets/Art/Materials";

        [Tooltip("Unity asset path searched for matching textures — e.g. Assets/Art/Textures. Not recursive.")]
        public string texturesFolder = "Assets/Art/Textures";

        [Tooltip("Unity asset path where generated prefabs are saved — e.g. Assets/Prefabs/Props.")]
        public string prefabsFolder = "Assets/Prefabs/Props";

        [Tooltip("When on, a prefab is automatically generated on import. Can always be triggered manually.")]
        public bool generatePrefab = false;

        [Tooltip("When on, the generated prefab root is marked ContributeGI (Lightmap Static).")]
        public bool lightmapStatic = false;
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
        public FBXImportPreset preset = new();

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
    /// Phase 5 adds two profile-level toggles that control texture assignment
    /// for all presets in the profile:
    ///   enableEmission          — whether to search and assign _E / _Emissive textures
    ///   enableAmbientOcclusion  — whether to search and assign _AO textures
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
        public List<string> validPrefixes = new() { "SM_", "SK_", "P_" };

        // ── Default preset ───────────────────────────────────────────────────────

        [Tooltip("Applied to any FBX that doesn't match a named rule. " +
                 "A warning is always logged when this fallback is used.")]
        public FBXImportPreset defaultPreset = new()
        {
            presetName = "Default"
        };

        // ── Phase 5 — profile-level texture toggles ───────────────────────────────

        [Tooltip("When on, textures with _E or _Emissive suffix are searched and assigned " +
                 "to the Emission Map slot. Also enables the Emission keyword on the material.")]
        public bool enableEmission = false;

        [Tooltip("When on, textures with _AO suffix are searched and assigned " +
                 "to the Ambient Occlusion slot.")]
        public bool enableAmbientOcclusion = true;

        // ── Rules ────────────────────────────────────────────────────────────────

        [SerializeField]
        private List<FBXImportRule> rules = new();

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