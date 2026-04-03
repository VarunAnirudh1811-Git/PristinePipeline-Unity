using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Stateless utility methods for the FBX Importer.
    /// No EditorWindow dependency — safe to call from any editor context.
    ///
    /// Phase 4 methods: profile persistence, naming validation, wildcard matching,
    /// FindMatchingPreset, ApplyPreset (ModelImporter settings), ReprocessAll.
    ///
    /// Phase 5 additions: CreateMaterials, FindAndAssignTextures, GeneratePrefab.
    /// These run after Unity has finished processing the model (post-import).
    /// They are called from FBXImporterProcessor.OnPostprocessModel and can also
    /// be triggered manually from FBXImporterTab.
    ///
    /// Enum fields on FBXImportPreset are accessed via FBXImportPresetEditorExtensions
    /// so the Runtime type carries no UnityEditor dependency.
    /// </summary>
    public static class FBXImporterUtility
    {
        // ── Texture suffix maps ──────────────────────────────────────────────────
        // These are the canonical suffix sets searched when assigning textures.
        // Order within each array does not matter — all are searched.

        private static readonly string[] BaseColorSuffixes = { "_BC", "_BaseColor", "_Albedo", "_D", "_Diffuse" };
        private static readonly string[] NormalSuffixes = { "_N", "_Normal", "_NRM" };
        private static readonly string[] OrmSuffixes = { "_ORM" };
        private static readonly string[] MetallicSuffixes = { "_M", "_Metallic", "_MetallicSmoothness" };
        private static readonly string[] EmissiveSuffixes = { "_E", "_Emissive", "_EM" };
        private static readonly string[] AoSuffixes = { "_AO", "_AmbientOcclusion" };

        // ── Profile loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads all FBXImportProfile assets from the configured save paths.
        /// Built-in profiles (package Resources) are listed first, then user profiles.
        /// </summary>
        public static List<FBXImportProfile> LoadAllProfiles()
        {
            var profiles = new List<FBXImportProfile>();
            string userPath = ToolSettings.FBX_ProfileSavePath;
            string builtInPath = ToolInfo.BuiltInImporterProfilePath;

            if (AssetDatabase.IsValidFolder(builtInPath))
                LoadProfilesFromPath(builtInPath, profiles);

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
                var profile = AssetDatabase.LoadAssetAtPath<FBXImportProfile>(path);

                if (profile != null && !results.Contains(profile))
                {
                    results.Add(profile);
                    profile.isBuiltIn = path.StartsWith("Packages/");
                }
            }
        }

        public static string[] GetProfileDisplayNames(List<FBXImportProfile> profiles) {
           return profiles
                .Select(p => p.isBuiltIn ? $"{p.profileName} (Built-in)" : p.profileName)
                .ToArray();
        }

        // ── Profile persistence ──────────────────────────────────────────────────

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
                    string renameError = AssetDatabase.RenameAsset(existingPath, profile.profileName);
                    if (!string.IsNullOrEmpty(renameError))
                        Debug.LogWarning($"{ToolInfo.LogPrefix} Could not rename profile: {renameError}");
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

        public static void DeleteProfile(FBXImportProfile profile)
        {
            if (profile == null) return;

            string path = AssetDatabase.GetAssetPath(profile);

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.Refresh();
                Debug.Log($"{ToolInfo.LogPrefix} Deleted FBX Import Profile: {profile.profileName}");
            }
        }

        public static FBXImportProfile CloneProfile(FBXImportProfile source)
        {
            if (source == null) return null;

            EnsureDirectoryExists(ToolSettings.FBX_ProfileSavePath);

            string cloneName = source.profileName + "_Copy";
            string assetPath = BuildUniqueAssetPath(ToolSettings.FBX_ProfileSavePath, cloneName);

            var clone = UnityEngine.Object.Instantiate(source);
            clone.profileName = Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.Refresh();

            Debug.Log($"{ToolInfo.LogPrefix} Cloned '{source.profileName}' → '{clone.profileName}'");
            return clone;
        }

        // ── Profile import / export ─────────────────────────────────────────────

        public static void ExportProfile(FBXImportProfile profile)
        {
            if (profile == null) return;

            string path = EditorUtility.SaveFilePanel(
                "Export FBX Import Profile", "", profile.profileName, "json");

            if (string.IsNullOrEmpty(path)) return;

            var data = new FBXImportProfileData
            {
                profileName = profile.profileName,
                description = profile.description,
                enforceNamingConvention = profile.enforceNamingConvention,
                validPrefixes = new List<string>(profile.validPrefixes),
                defaultPreset = profile.defaultPreset,
                rules = new List<FBXImportRule>(profile.Rules),
                enableEmission = profile.enableEmission,
                enableAmbientOcclusion = profile.enableAmbientOcclusion
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
                string assetPath = BuildUniqueAssetPath(ToolSettings.FBX_ProfileSavePath, data.profileName);

                var profile = ScriptableObject.CreateInstance<FBXImportProfile>();
                profile.profileName = Path.GetFileNameWithoutExtension(assetPath);
                profile.description = data.description;
                profile.enforceNamingConvention = data.enforceNamingConvention;
                profile.validPrefixes = data.validPrefixes ?? new List<string>();
                profile.defaultPreset = data.defaultPreset ?? new FBXImportPreset { presetName = "Default" };
                SanitizePreset(profile.defaultPreset);
                profile.enableEmission = data.enableEmission;
                profile.enableAmbientOcclusion = data.enableAmbientOcclusion;

                var rules = data.rules ?? new List<FBXImportRule>();

                foreach (var rule in rules)
                {
                    if (rule?.preset != null)
                        SanitizePreset(rule.preset);
                }
                profile.SetRules(rules);

                AssetDatabase.CreateAsset(profile, assetPath);
                AssetDatabase.Refresh();

                Debug.Log($"{ToolInfo.LogPrefix} Imported FBX Import Profile: {profile.profileName}");
                return profile;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{ToolInfo.LogPrefix} Import failed: {ex.Message}");
                EditorUtility.DisplayDialog("Import Failed", "An error occurred while reading the file.", "OK");
                return null;
            }
        }

        private static void SanitizePreset(FBXImportPreset preset)
        {
            if (preset == null) return;

            if (string.IsNullOrWhiteSpace(preset.materialPrefix))
                preset.materialPrefix = "M_";

            if (string.IsNullOrWhiteSpace(preset.materialsFolder))
                preset.materialsFolder = "Assets/Art/Materials";

            if (string.IsNullOrWhiteSpace(preset.texturesFolder))
                preset.texturesFolder = "Assets/Art/Textures";

            if (string.IsNullOrWhiteSpace(preset.prefabsFolder))
                preset.prefabsFolder = "Assets/Art/Prefabs";

            if (preset.scaleFactor <= 0f)
                preset.scaleFactor = 1.0f;
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
            public bool enableEmission;
            public bool enableAmbientOcclusion;
        }

        // ── Naming validation ────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the FBX filename starts with at least one valid prefix
        /// from the profile, or when the profile's prefix list is empty.
        /// The caller decides whether a false result blocks or warns.
        /// </summary>
        public static bool ValidateName(FBXImportProfile profile, string assetPath)
        {
            if (profile == null) return true;
            if (profile.validPrefixes.Count == 0) return true;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            return profile.validPrefixes
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        // ── Rule matching ────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the best matching FBXImportPreset for the given asset path.
        /// All rules are evaluated in list order — last match wins. If no rule
        /// matches, the profile's defaultPreset is returned with a warning.
        /// </summary>
        public static FBXImportPreset FindMatchingPreset(FBXImportProfile profile, string assetPath)
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

            Debug.LogWarning(
                $"{ToolInfo.LogPrefix} No rule matched '{Path.GetFileName(assetPath)}'. " +
                $"Applying default preset from profile '{profile.profileName}'.");

            return profile.defaultPreset;
        }

        /// <summary>
        /// Wildcard pattern matching — * (any sequence) and ? (any single char).
        /// Case-insensitive. Dynamic programming, handles all edge cases.
        /// Replace this method body with Regex.IsMatch to upgrade in future.
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

        // ── ModelImporter configuration (Phase 4) ────────────────────────────────

        /// <summary>
        /// Applies all preset settings to a ModelImporter.
        /// Must be called from OnPreprocessModel — importer has not run yet at that point.
        /// Uses FBXImportPresetEditorExtensions for the enum fields.
        /// </summary>
        public static void ApplyPreset(ModelImporter importer, FBXImportPreset preset)
        {
            if (importer == null || preset == null) return;

            importer.globalScale = preset.scaleFactor;
            importer.meshCompression = preset.MeshCompression();
            importer.isReadable = preset.readWriteEnabled;
            importer.optimizeMeshPolygons = preset.optimizeMesh;
            importer.optimizeMeshVertices = preset.optimizeMesh;
            importer.generateSecondaryUV = preset.generateLightmapUVs;
            importer.importNormals = preset.Normals();
            importer.importTangents = preset.Tangents();
            importer.swapUVChannels = preset.swapUVs;
        }

        // ── Phase 5 — material creation ──────────────────────────────────────────

        /// <summary>
        /// Creates materials for each embedded material slot in the imported FBX.
        /// Uses URP Lit shader. Skips creation if a material already exists at the
        /// expected path. Returns the list of materials that were created or found.
        ///
        /// Called from FBXImporterProcessor.OnPostprocessModel after Unity has
        /// extracted mesh data and material names from the file.
        /// </summary>
        public static List<Material> CreateMaterials(
            GameObject importedModel,
            FBXImportPreset preset,
            string fbxAssetPath)
        {
            if (importedModel == null || preset == null) return new List<Material>();

            if (string.IsNullOrWhiteSpace(preset.materialsFolder))
                return new List<Material>();

            string folder = preset.materialsFolder.TrimEnd('/');
            EnsureAssetFolderExists(folder);

            // Collect all unique material names from the imported hierarchy
            var renderers = importedModel.GetComponentsInChildren<Renderer>(includeInactive: true);
            var materialNames = renderers
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Select(m => m.name)
                .Distinct()
                .ToList();

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                // Fallback — Standard works in non-URP projects
                shader = Shader.Find("Standard");
                if (shader == null)
                {
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Could not find URP Lit or Standard shader. " +
                        $"Material creation skipped for '{Path.GetFileName(fbxAssetPath)}'.");
                    return new List<Material>();
                }

                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} URP Lit shader not found — falling back to Standard. " +
                    $"Is URP installed?");
            }

            var created = new List<Material>();

            foreach (string rawName in materialNames)
            {
                // Strip any existing prefix the DCC tool may have embedded
                string baseName = StripKnownPrefixes(rawName);
                string matName = string.IsNullOrWhiteSpace(preset.materialPrefix)
                    ? baseName
                    : preset.materialPrefix + baseName;

                string matPath = Path.Combine(folder, matName + ".mat").Replace("\\", "/");

                // Load existing — never overwrite
                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing != null)
                {
                    created.Add(existing);
                    continue;
                }

                var mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, matPath);
                created.Add(mat);

                Debug.Log($"{ToolInfo.LogPrefix} Created material: {matPath}");
            }

            if (created.Count > 0)
                AssetDatabase.Refresh();

            return created;
        }

        /// <summary>
        /// Searches the textures folder for textures matching the given material name
        /// and assigns them to the correct slots on the material.
        ///
        /// Search strategy:
        ///   Strip the material prefix from matName → get baseName.
        ///   Look for files named baseName + suffix + any image extension.
        ///   Search is not recursive — only the top level of texturesFolder is searched.
        ///
        /// Suffix mapping (all configurable via the suffix arrays at the top of this file):
        ///   _BC / _BaseColor  → _BaseMap / _MainTex
        ///   _N / _Normal      → _BumpMap
        ///   _ORM              → packed: R=AO, G=Roughness, B=Metallic
        ///   _M / _Metallic    → _MetallicGlossMap
        ///   _E / _Emissive    → _EmissionMap  (only when profile.enableEmission is true)
        ///   _AO               → _OcclusionMap (only when profile.enableAmbientOcclusion is true)
        /// </summary>
        public static void FindAndAssignTextures(
            Material material,
            string materialName,
            FBXImportPreset preset,
            FBXImportProfile profile)
        {
            if (material == null || preset == null || profile == null) return;

            string folder = preset.texturesFolder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Textures folder not found: '{folder}'. " +
                    $"Skipping texture assignment for '{materialName}'.");
                return;
            }

            // Derive the base name used in texture file names by stripping the material prefix
            string baseName = string.IsNullOrWhiteSpace(preset.materialPrefix)
                ? materialName
                : materialName.StartsWith(preset.materialPrefix, StringComparison.OrdinalIgnoreCase)
                    ? materialName[preset.materialPrefix.Length..]
                    : materialName;

            bool dirty = false;

            // Base Color
            var baseColor = FindTexture(folder, baseName, BaseColorSuffixes);
            if (baseColor != null)
            {
                material.SetTexture("_BaseMap", baseColor);
                material.SetTexture("_MainTex", baseColor);   // Built-in compat
                dirty = true;
            }

            // Normal Map
            var normal = FindTexture(folder, baseName, NormalSuffixes);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                dirty = true;
            }

            // ORM (packed Occlusion/Roughness/Metallic)
            var orm = FindTexture(folder, baseName, OrmSuffixes);
            if (orm != null)
            {
                // In URP Lit: R channel → Metallic, G channel → Smoothness (inverted roughness),
                // packed via the same map approach Unity uses for MetallicGlossMap.
                // The shader reads all three channels from this texture.
                material.SetTexture("_MetallicGlossMap", orm);
                material.SetTexture("_OcclusionMap", orm);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                dirty = true;
            }
            else
            {
                // Individual Metallic/Smoothness
                var metallic = FindTexture(folder, baseName, MetallicSuffixes);
                if (metallic != null)
                {
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    dirty = true;
                }

                // AO (only if no ORM)
                if (profile.enableAmbientOcclusion)
                {
                    var ao = FindTexture(folder, baseName, AoSuffixes);
                    if (ao != null)
                    {
                        material.SetTexture("_OcclusionMap", ao);
                        dirty = true;
                    }
                }
            }

            // Emission
            if (profile.enableEmission)
            {
                var emissive = FindTexture(folder, baseName, EmissiveSuffixes);
                if (emissive != null)
                {
                    material.SetTexture("_EmissionMap", emissive);
                    material.EnableKeyword("_EMISSION");
                    // Default emission color to white so the map is visible
                    material.SetColor("_EmissionColor", Color.white);
                    dirty = true;
                }
            }

            if (dirty)
                EditorUtility.SetDirty(material);
        }

        // ── Phase 5 — prefab generation ──────────────────────────────────────────

        /// <summary>
        /// Generates a prefab from the imported FBX asset.
        /// Skips creation if a prefab already exists at the destination path.
        /// When preset.lightmapStatic is true, marks the prefab root ContributeGI.
        ///
        /// Returns the path of the prefab asset, or null if generation was skipped
        /// or failed.
        ///
        /// Called from FBXImporterProcessor after materials have been applied, or
        /// directly from FBXImporterTab via the manual Generate button.
        /// </summary>
        public static string GeneratePrefab(string fbxAssetPath, FBXImportPreset preset)
        {
            if (string.IsNullOrEmpty(fbxAssetPath) || preset == null) return null;

            string folder = preset.prefabsFolder.TrimEnd('/');
            string baseName = Path.GetFileNameWithoutExtension(fbxAssetPath);
            string prefabPath = Path.Combine(folder, baseName + ".prefab").Replace("\\", "/");

            // Never overwrite an existing prefab
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                Debug.Log(
                    $"{ToolInfo.LogPrefix} Prefab already exists at '{prefabPath}' — skipping.");
                return prefabPath;
            }

            // Load the imported mesh asset
            var sourceMesh = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (sourceMesh == null)
            {
                Debug.LogError(
                    $"{ToolInfo.LogPrefix} Could not load mesh asset at '{fbxAssetPath}'. " +
                    $"Prefab generation skipped.");
                return null;
            }

            EnsureAssetFolderExists(folder);

            // Instantiate, configure, save as prefab, then destroy the scene instance
            var instance = UnityEngine.Object.Instantiate(sourceMesh);
            instance.name = baseName;

            if (preset.lightmapStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    instance,
                    StaticEditorFlags.ContributeGI);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);

            if (prefab == null)
            {
                Debug.LogError(
                    $"{ToolInfo.LogPrefix} PrefabUtility.SaveAsPrefabAsset failed for '{prefabPath}'.");
                return null;
            }

            Debug.Log($"{ToolInfo.LogPrefix} Generated prefab: {prefabPath}");
            return prefabPath;
        }

        // ── Manual reprocess ─────────────────────────────────────────────────────

        /// <summary>
        /// Forces a reimport of a single FBX asset. This triggers OnPreprocessModel
        /// in FBXImporterProcessor so mesh settings are reapplied.
        /// Material and prefab steps require post-import access and are NOT re-run
        /// by this method alone — use ReprocessAssetFull for a complete pass.
        /// </summary>
        public static bool ReprocessAsset(string assetPath, FBXImportProfile profile)
        {
            if (string.IsNullOrEmpty(assetPath) || profile == null) return false;
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} ReprocessAsset skipped — not an FBX: {assetPath}");
                return false;
            }

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

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"{ToolInfo.LogPrefix} Reprocessed: {Path.GetFileName(assetPath)}");
            return true;
        }

        /// <summary>
        /// Full post-import pass for a single already-imported FBX:
        /// creates materials, assigns textures, and generates a prefab if configured.
        /// Call this after the asset has been imported (not from OnPreprocessModel).
        /// </summary>
        public static void RunPostImportSteps(
            string fbxAssetPath,
            FBXImportPreset preset,
            FBXImportProfile profile)
        {
            if (string.IsNullOrEmpty(fbxAssetPath) || preset == null || profile == null) return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (model == null)
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} RunPostImportSteps: could not load " +
                    $"'{fbxAssetPath}' as GameObject.");
                return;
            }

            // 1 — Create materials
            var materials = CreateMaterials(model, preset, fbxAssetPath);

            // 2 — Assign textures to each created material
            foreach (var mat in materials)
                FindAndAssignTextures(mat, mat.name, preset, profile);

            // 3 — Save all material changes
            AssetDatabase.SaveAssets();

            // 4 — Generate prefab (if toggled or called explicitly)
            if (preset.generatePrefab)
                GeneratePrefab(fbxAssetPath, preset);

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Runs a manual reprocess pass over all FBX files in the project.
        /// Triggers reimport (which fires OnPreprocessModel) then runs post-import steps.
        /// Returns the count of assets processed.
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
                    if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                    if (ReprocessAsset(path, profile))
                        count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            // Post-import steps run after the batch reimport completes
            // so the asset database is in a consistent state
            foreach (string path in allAssets)
            {
                if (!path.StartsWith("Assets/")) continue;
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                FBXImportPreset preset = FindMatchingPreset(profile, path);
                if (preset != null)
                    RunPostImportSteps(path, preset, profile);
            }

            Debug.Log(
                $"{ToolInfo.LogPrefix} Manual reprocess complete — {count} FBX file(s) processed.");

            return count;
        }

        // ── Rule validation ──────────────────────────────────────────────────────

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

        /// <summary>
        /// Searches the given Unity asset folder (non-recursive) for a texture file
        /// whose name matches baseName + one of the provided suffixes.
        /// Returns the first match found, or null.
        /// Supported image extensions: png, tga, jpg, jpeg, exr, hdr, psd.
        /// </summary>
        private static Texture2D FindTexture(string folder, string baseName, string[] suffixes)
        {
            string[] imageExtensions = { "png", "tga", "jpg", "jpeg", "exr", "hdr", "psd" };

            foreach (string suffix in suffixes)
            {
                foreach (string ext in imageExtensions)
                {
                    string path = $"{folder}/{baseName}{suffix}.{ext}";
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                        return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// Strips known material name prefixes (M_, MI_, MAT_) that DCC tools
        /// sometimes embed in exported material names to get to the base name.
        /// </summary>
        private static string StripKnownPrefixes(string name)
        {
            string[] knownPrefixes = { "M_", "MI_", "MAT_", "Mat_", "mat_" };
            foreach (string prefix in knownPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return name[prefix.Length..];
            }
            return name;
        }

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
            string candidate = Path.Combine(folderPath, baseName + ".asset").Replace("\\", "/");
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