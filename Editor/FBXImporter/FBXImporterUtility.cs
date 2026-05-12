using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Stateless utility methods for the FBX Importer.
    /// No EditorWindow dependency — safe to call from any editor context.
    ///
    /// v1.2 — Active Root refactor:
    ///   All folder paths on FBXImportPreset (materialsFolder, texturesFolder,
    ///   prefabsFolder) are now stored as paths relative to the Active Root
    ///   (ToolSettings.ActiveRootPath). They must NOT start with "Assets/".
    ///   This utility resolves them to full Unity asset paths via ResolveFolder()
    ///   at every point of use. Validation rejects paths that start with "Assets/".
    /// </summary>
    public static class FBXImporterUtility
    {
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

        public static string[] GetProfileDisplayNames(List<FBXImportProfile> profiles)
        {
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

        // ── Profile import / export ──────────────────────────────────────────────

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
                    if (rule?.preset != null) SanitizePreset(rule.preset);
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

        /// <summary>
        /// Sanitizes a preset loaded from JSON. Strips any "Assets/" prefix that may
        /// exist in data exported before v1.2 so old profiles remain usable.
        /// Also resets any blank or zero fields to safe defaults.
        /// </summary>
        private static void SanitizePreset(FBXImportPreset preset)
        {
            if (preset == null) return;

            if (string.IsNullOrWhiteSpace(preset.materialPrefix))
                preset.materialPrefix = "M_";

            preset.materialsFolder = StripAssetsPrefix(
                string.IsNullOrWhiteSpace(preset.materialsFolder) ? "Art/Materials" : preset.materialsFolder);

            preset.texturesFolder = StripAssetsPrefix(
                string.IsNullOrWhiteSpace(preset.texturesFolder) ? "Art/Textures" : preset.texturesFolder);

            preset.prefabsFolder = StripAssetsPrefix(
                string.IsNullOrWhiteSpace(preset.prefabsFolder) ? "Level/Prefabs" : preset.prefabsFolder);

            if (preset.scaleFactor <= 0f)
                preset.scaleFactor = 1.0f;

            // ── Texture pattern defaults ───────────────────────────────────────

            preset.baseColorPatterns ??= new List<string>();
            preset.normalPatterns ??= new List<string>();
            preset.ormPatterns ??= new List<string>();
            preset.metallicPatterns ??= new List<string>();
            preset.emissivePatterns ??= new List<string>();
            preset.aoPatterns ??= new List<string>();

            if (preset.baseColorPatterns.Count == 0)
            {
                preset.baseColorPatterns.Add("T_*_B");
                preset.baseColorPatterns.Add("T_*_BC");
            }

            if (preset.normalPatterns.Count == 0)
            {
                preset.normalPatterns.Add("T_*_N");
            }

            if (preset.metallicPatterns.Count == 0)
            {
                preset.metallicPatterns.Add("T_*_MS");
                preset.metallicPatterns.Add("T_*_M");
            }

            if (preset.ormPatterns.Count == 0)
            {
                preset.ormPatterns.Add("T_*_ORM");
            }

            if (preset.emissivePatterns.Count == 0)
            {
                preset.emissivePatterns.Add("T_*_E");
            }

            if (preset.aoPatterns.Count == 0)
            {
                preset.aoPatterns.Add("T_*_AO");
            }
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

            if (lastMatch != null) return lastMatch;

            Debug.LogWarning(
                $"{ToolInfo.LogPrefix} No rule matched '{Path.GetFileName(assetPath)}'. " +
                $"Applying default preset from profile '{profile.profileName}'.");

            return profile.defaultPreset;
        }

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

        // ── ModelImporter configuration ──────────────────────────────────────────

        /// <summary>
        /// Applies all preset settings to a ModelImporter.
        /// Must be called from OnPreprocessModel — the importer has not run yet at that point.
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

            // Tell Unity to use external materials so RemapMaterial calls take effect.
            // Without this the model ignores the remap table entirely.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;
        }

        // ── Material creation ────────────────────────────────────────────────────

        /// <summary>
        /// Creates materials for each embedded material slot in the imported FBX.
        /// Uses URP Lit shader (falls back to Standard).
        /// Skips creation if a material already exists at the expected path.
        /// Returns the list of materials that were created or found.
        ///
        /// preset.materialsFolder is resolved relative to ActiveRootPath.
        /// </summary>
        public static List<Material> CreateMaterials(
            GameObject importedModel,
            FBXImportPreset preset,
            string fbxAssetPath)
        {
            if (importedModel == null || preset == null) return new List<Material>();
            if (string.IsNullOrWhiteSpace(preset.materialsFolder)) return new List<Material>();

            string folder = ToolSettings.ResolveRelativeToActiveRoot(preset.materialsFolder);
            EnsureAssetFolderExists(folder);

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
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogError(
                    $"{ToolInfo.LogPrefix} Could not find URP Lit or Standard shader. " +
                    $"Material creation skipped for '{Path.GetFileName(fbxAssetPath)}'.");
                return new List<Material>();
            }

            if (shader.name == "Standard")
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} URP Lit shader not found — falling back to Standard. Is URP installed?");

            var created = new List<Material>();

            foreach (string rawName in materialNames)
            {
                string baseName = StripAssetsPrefix(rawName);
                string matName = string.IsNullOrWhiteSpace(preset.materialPrefix)
                    ? baseName
                    : preset.materialPrefix + baseName;

                string matPath = folder + "/" + matName + ".mat";

                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing != null) { created.Add(existing); continue; }

                var mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, matPath);
                created.Add(mat);

                Debug.Log($"{ToolInfo.LogPrefix} Created material: {matPath}");
            }

            return created;
        }

        // ── Texture assignment ───────────────────────────────────────────────────

        /// <summary>
        /// Attempts texture assignment for one material using the given base name.
        /// Returns true if at least one texture slot was filled.
        /// Handles all suffix searching, keyword enabling, and SetDirty internally.
        /// </summary>
        private static bool AssignTextures(
            Material material,
            string baseName,
            FBXImportPreset preset,
            FBXImportProfile profile)
        {
            if (material == null || preset == null || profile == null) return false;

            string folder = ToolSettings.ResolveRelativeToActiveRoot(preset.texturesFolder);

            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Textures folder not found: '{folder}'. " +
                    $"Skipping texture assignment for '{baseName}'.");
                return false;
            }

            // Strip known material prefixes so the search key matches texture file names.
            // e.g. "M_SM_Rock" → "SM_Rock" → matches "SM_Rock_BC.png"
            string searchName = StripAssetsPrefix(baseName);
            bool dirty = false;

            var baseColor = FindTextureFromPatterns(folder, searchName, preset.baseColorPatterns);
            if (baseColor != null)
            {
                material.SetTexture("_BaseMap", baseColor);
                material.SetTexture("_MainTex", baseColor);
                dirty = true;
            }

            var normal = FindTextureFromPatterns(folder, searchName, preset.normalPatterns);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                dirty = true;
            }

            var orm = FindTextureFromPatterns(folder, searchName, preset.ormPatterns);
            if (orm != null)
            {
                material.SetTexture("_MetallicGlossMap", orm);
                material.SetTexture("_OcclusionMap", orm);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                dirty = true;
            }
            else
            {
                var metallic = FindTextureFromPatterns(folder, searchName, preset.metallicPatterns);
                if (metallic != null)
                {
                    material.SetTexture("_MetallicGlossMap", metallic);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    dirty = true;
                }

                if (profile.enableAmbientOcclusion)
                {
                    var ao = FindTextureFromPatterns(folder, searchName, preset.aoPatterns);
                    if (ao != null)
                    {
                        material.SetTexture("_OcclusionMap", ao);
                        dirty = true;
                    }
                }
            }

            if (profile.enableEmission)
            {
                var emissive = FindTextureFromPatterns(folder, searchName, preset.emissivePatterns);
                if (emissive != null)
                {
                    material.SetTexture("_EmissionMap", emissive);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.white);
                    dirty = true;
                }
            }

            if (dirty) EditorUtility.SetDirty(material);
            return dirty;
        }

        // ── Prefab generation ────────────────────────────────────────────────────

        /// <summary>
        /// Generates a prefab from the imported FBX asset.
        /// preset.prefabsFolder is resolved relative to ActiveRootPath.
        /// Skips creation if a prefab already exists at the destination path.
        /// Returns the path of the prefab asset, or null if generation was skipped or failed.
        /// </summary>
        public static string GeneratePrefab(string fbxAssetPath, FBXImportPreset preset)
        {
            if (string.IsNullOrEmpty(fbxAssetPath) || preset == null) return null;

            string folder = ToolSettings.ResolveRelativeToActiveRoot(preset.prefabsFolder);
            string baseName = Path.GetFileNameWithoutExtension(fbxAssetPath);
            string prefabPath = folder + "/" + baseName + ".prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                Debug.Log($"{ToolInfo.LogPrefix} Prefab already exists at '{prefabPath}' — skipping.");
                return prefabPath;
            }

            var sourceMesh = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (sourceMesh == null)
            {
                Debug.LogError(
                    $"{ToolInfo.LogPrefix} Could not load mesh asset at '{fbxAssetPath}'. " +
                    $"Prefab generation skipped.");
                return null;
            }

            EnsureAssetFolderExists(folder);

            var instance = UnityEngine.Object.Instantiate(sourceMesh);
            instance.name = baseName;

            if (preset.lightmapStatic)
                GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.ContributeGI);

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

        // ── Reprocess ────────────────────────────────────────────────────────────

        /// <summary>
        /// Forces a reimport of a single FBX asset, triggering OnPreprocessModel.
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
        /// </summary>
        /// <remarks>
        /// Deprecated in favour of the job queue system (FBXPostImportQueue +
        /// CreateMaterialsForJob / AssignTexturesForJob / GeneratePrefabForJob).
        /// This method remains for use by ReprocessAll until that is updated
        /// to use the queue in a follow-up change.
        /// </remarks>
        [System.Obsolete(
            "Use FBXPostImportQueue with the three job-step methods instead. " +
            "This method calls AssetDatabase.Refresh internally and is unsafe " +
            "to call from within OnPostprocessModel.")]
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
                    $"{ToolInfo.LogPrefix} RunPostImportSteps: could not load '{fbxAssetPath}' as GameObject.");
                return;
            }

            var materials = CreateMaterials(model, preset, fbxAssetPath);

            foreach (var mat in materials)
                AssignTextures(mat, mat.name, preset, profile);

            if (preset.generatePrefab)
                GeneratePrefab(fbxAssetPath, preset);


            AssetDatabase.SaveAssets();
        }

        // ── Job-oriented step methods (called by FBXPostImportQueue) ─────────────────

        /// <summary>
        /// Step 1 of 3. Creates materials for all renderer slots in the imported model.
        /// Writes created material asset paths into job.result.materialsCreated.
        /// Called from within StartAssetEditing scope — do not call Refresh here.
        /// </summary>
        public static void CreateMaterialsForJob(PostImportJob job)
        {
            if (job == null) return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(job.assetPath);
            if (model == null)
            {
                job.result.errors.Add("Could not load model asset.");
                return;
            }

            List<Material> materials = CreateMaterials(model, job.preset, job.assetPath);
            foreach (Material mat in materials)
            {
                string matPath = AssetDatabase.GetAssetPath(mat);
                if (!string.IsNullOrEmpty(matPath))
                    job.result.materialsCreated.Add(matPath);
            }
        }

        /// <summary>
        /// Step 2 of 4. Remaps the FBX model's internal material slots to point at
        /// the external material assets created in Step 1.
        ///
        /// Must be called AFTER CreateMaterialsForJob has flushed assets to disk
        /// (i.e. after the StartAssetEditing/StopAssetEditing boundary in the queue).
        /// Calls SaveAndReimport() internally — this is a lightweight remap-only
        /// reimport, not a full mesh reimport, so it does not retrigger OnPostprocessModel.
        /// </summary>
        public static void RemapMaterialsForJob(PostImportJob job)
        {
            if (job == null) return;
            if (job.result.materialsCreated.Count == 0) return;

            var importer = AssetImporter.GetAtPath(job.assetPath) as ModelImporter;
            if (importer == null)
            {
                job.result.errors.Add("Could not load ModelImporter for remap.");
                return;
            }

            // Build lookup: internal embedded material name → external material asset.
            // Strip our prefix so the key matches what Unity stored internally.
            var remapTable = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

            foreach (string matPath in job.result.materialsCreated)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                string internalName = string.IsNullOrWhiteSpace(job.preset.materialPrefix)
                    ? mat.name
                    : mat.name.StartsWith(job.preset.materialPrefix,
                        StringComparison.OrdinalIgnoreCase)
                        ? mat.name[job.preset.materialPrefix.Length..]
                        : mat.name;

                remapTable[internalName] = mat;
            }

            // Discover embedded material identifiers from the asset itself.
            // LoadAllAssetsAtPath returns all sub-objects including embedded materials.
            // These are the actual slot identifiers the importer uses internally.
            UnityEngine.Object[] embeddedAssets = AssetDatabase.LoadAllAssetsAtPath(job.assetPath);

            bool anyRemapped = false;

            foreach (UnityEngine.Object embedded in embeddedAssets)
            {
                if (embedded == null) continue;
                if (embedded is not Material embeddedMat) continue;

                string slotName = embeddedMat.name;

                // Exact match first
                if (!remapTable.TryGetValue(slotName, out Material target))
                {
                    // Fallback: some FBX exporters prefix slot names with the mesh
                    // name e.g. "Cube:Material" — try matching the part after ':'
                    int colonIdx = slotName.IndexOf(':');
                    string shortName = colonIdx >= 0
                        ? slotName[(colonIdx + 1)..]
                        : slotName;

                    remapTable.TryGetValue(shortName, out target);
                }

                if (target == null) continue;

                var identifier = new AssetImporter.SourceAssetIdentifier(embedded);
                importer.AddRemap(identifier, target);
                anyRemapped = true;

                Debug.Log(
                    $"{ToolInfo.LogPrefix} Remapped '{slotName}' -> " +
                    $"'{AssetDatabase.GetAssetPath(target)}'");
            }

            if (anyRemapped)
            {
                importer.SaveAndReimport();
            }
            else
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} No material slots remapped for " +
                    $"'{System.IO.Path.GetFileName(job.assetPath)}'. " +
                    $"Embedded material names: " +
                    $"[{string.Join(", ", embeddedAssets.OfType<Material>().Select(m => m.name))}]. " +
                    $"Remap table keys: [{string.Join(", ", remapTable.Keys)}].");
            }
        }

        /// <summary>
        /// Step 3 of 4. Assigns textures to each material created in Step 1.
        /// Relies on job.result.materialsCreated being populated by CreateMaterialsForJob.
        /// Must be called after a StartAssetEditing/StopAssetEditing flush so the
        /// material assets written in Step 1 are visible to AssetDatabase.LoadAssetAtPath.
        /// </summary>
        public static void AssignTexturesForJob(PostImportJob job)
        {
            if (job == null) return;
            if (job.result.materialsCreated.Count == 0) return;

            // Mesh base name is a reliable fallback search key when texture files
            // are named after the FBX (e.g. SM_Rock_BC.png) rather than the material.
            string meshBaseName = Path.GetFileNameWithoutExtension(job.assetPath);

            int assigned = 0;

            foreach (string matPath in job.result.materialsCreated)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    job.result.errors.Add($"Could not load material at '{matPath}'.");
                    continue;
                }

                // Pass 1 — search by material name (handles M_Rock → strips to Rock)
                bool found = AssignTextures(mat, mat.name, job.preset, job.profile);

                // Pass 2 — search by mesh file name (handles SM_Rock naming convention)
                if (!found)
                    found = AssignTextures(mat, meshBaseName, job.preset, job.profile);

                if (found) assigned++;
            }

            job.result.texturesAssigned = assigned > 0;

            if (!job.result.texturesAssigned)
                Debug.Log(
                    $"{ToolInfo.LogPrefix} No textures found for " +
                    $"'{Path.GetFileName(job.assetPath)}' at import time — " +
                    $"run 'Reassign Textures' after importing textures.");
        }

        /// <summary>
        /// Step 4 of 4. Generates a prefab for the imported FBX if the preset requests it.
        /// Skips silently if preset.generatePrefab is false.
        /// Writes the generated prefab's asset path into job.result.prefabPath.
        /// </summary>
        public static void GeneratePrefabForJob(PostImportJob job)
        {
            if (job == null) return;
            if (!job.preset.generatePrefab) return;

            job.result.prefabPath = GeneratePrefab(job.assetPath, job.preset);
        }

        /// <summary>
        /// Runs a manual reprocess pass over all FBX files in the project.
        /// Triggers reimport then runs post-import steps.
        /// Returns the count of assets processed.
        /// </summary>
        public static int ReprocessAll(FBXImportProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning($"{ToolInfo.LogPrefix} ReprocessAll: no profile provided.");
                return 0;
            }

            string rootPath = ToolSettings.ActiveRootPath;
            string[] allAssets = AssetDatabase.GetAllAssetPaths();

            int enqueued = 0;

            foreach (string path in allAssets)
            {
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                // Scope filter — Active Root only (fixes the second loop bug from the audit)
                if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) continue;

                FBXImportPreset preset = FindMatchingPreset(profile, path);
                if (preset == null) continue;

                FBXPostImportQueue.Enqueue(new PostImportJob
                {
                    assetPath = path,
                    preset = preset,
                    profile = profile
                });

                enqueued++;
            }

            Debug.Log(
                $"{ToolInfo.LogPrefix} ReprocessAll enqueued {enqueued} FBX file(s) " +
                $"under '{rootPath}'.");
            return enqueued;
        }

        /// <summary>
        /// Scans all material assets under the Active Root and re-runs texture
        /// assignment for each one using the given profile's preset settings.
        ///
        /// Intended for workflows where textures arrive after FBX import has completed.
        /// Does not reimport any FBX files — only updates material texture slots.
        /// Returns the number of materials that had at least one texture slot updated.
        /// </summary>
        public static int ReassignTexturesForProfile(FBXImportProfile profile)
        {
            if (profile == null) return 0;

            string root = ToolSettings.ActiveRootPath;
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { root });

            if (guids.Length == 0)
            {
                Debug.Log(
                    $"{ToolInfo.LogPrefix} ReassignTextures: no materials found under '{root}'.");
                return 0;
            }

            int updated = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string guid in guids)
                {
                    string matPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null) continue;

                    // Match the best preset for this material by testing rule patterns.
                    // Falls back to the profile's default preset if no rule matches.
                    FBXImportPreset preset =
                        FindPresetForMaterial(profile, mat.name) ?? profile.defaultPreset;

                    if (string.IsNullOrWhiteSpace(preset.texturesFolder)) continue;

                    // Pass 1 — material name (M_SM_Rock → strips prefix → SM_Rock)
                    bool assigned = AssignTextures(mat, mat.name, preset, profile);

                    // Pass 2 — name after stripping prefix, in case pass 1 found nothing
                    if (!assigned)
                    {
                        string stripped = StripAssetsPrefix(mat.name);
                        if (!string.Equals(stripped, mat.name, StringComparison.OrdinalIgnoreCase))
                            assigned = AssignTextures(mat, stripped, preset, profile);
                    }

                    if (assigned)
                    {
                        updated++;
                        Debug.Log(
                            $"{ToolInfo.LogPrefix} Reassigned textures → '{mat.name}'.");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"{ToolInfo.LogPrefix} ReassignTextures complete — " +
                $"{updated}/{guids.Length} material(s) updated.");

            return updated;
        }

        /// <summary>
        /// Finds the best matching FBXImportPreset for a material by testing the
        /// material name against each rule's name pattern. Last match wins —
        /// consistent with FindMatchingPreset. Returns null if no rule matches.
        /// </summary>
        private static FBXImportPreset FindPresetForMaterial(
            FBXImportProfile profile, string materialName)
        {
            if (profile == null || string.IsNullOrWhiteSpace(materialName)) return null;

            FBXImportPreset lastMatch = null;

            foreach (FBXImportRule rule in profile.Rules)
            {
                if (!rule.HasNamePattern) continue;
                if (MatchesWildcard(materialName, rule.namePattern))
                    lastMatch = rule.preset;
            }

            return lastMatch;
        }

        // ── Rule validation ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns validation messages for a single FBX import rule.
        /// Folder paths on the preset must be relative — they must NOT start with "Assets/".
        /// </summary>
        public static List<string> ValidateRule(FBXImportRule rule)
        {
            var messages = new List<string>();

            if (!rule.HasNamePattern)
                messages.Add("Name pattern cannot be empty.");

            if (rule.preset == null)
            {
                messages.Add("Preset is null — this should not happen.");
                return messages;
            }

            if (rule.preset.scaleFactor <= 0f)
                messages.Add("Scale factor must be greater than zero.");

            if (!string.IsNullOrWhiteSpace(rule.preset.materialsFolder)
                && rule.preset.materialsFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                messages.Add("Materials folder must be a relative path — do not start with \"Assets/\".");

            if (!string.IsNullOrWhiteSpace(rule.preset.texturesFolder)
                && rule.preset.texturesFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                messages.Add("Textures folder must be a relative path — do not start with \"Assets/\".");

            if (!string.IsNullOrWhiteSpace(rule.preset.prefabsFolder)
                && rule.preset.prefabsFolder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                messages.Add("Prefabs folder must be a relative path — do not start with \"Assets/\".");

            // Texture maps validation.
            ValidatePatternList(
                rule.preset.baseColorPatterns,
                "Base Color",
                messages);

            ValidatePatternList(
                rule.preset.normalPatterns,
                "Normal",
                messages);

            ValidatePatternList(
                 rule.preset.metallicPatterns,
                "Metallic",
                messages);

            ValidatePatternList(
                 rule.preset.ormPatterns,
                "ORM",
                messages);

            ValidatePatternList(
                 rule.preset.emissivePatterns,
                "Emissive",
                messages);

            ValidatePatternList(
                 rule.preset.aoPatterns,
                "AO",
                messages);

            return messages;
        }

        private static void ValidatePatternList(
            List<string> patterns,
            string label,
            List<string> messages)
        {
            if (patterns == null || patterns.Count == 0)
            {
                messages.Add($"{label} pattern list cannot be empty.");
                return;
            }

            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    messages.Add($"{label} contains an empty pattern.");
                    continue;
                }

                if (!pattern.Contains("*"))
                {
                    messages.Add(
                        $"{label} pattern '{pattern}' must contain '*' wildcard.");
                }
            }
        }

        // ── Path resolution ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a folder path relative to the Active Root into a full Unity asset path.
        /// Examples (Active Root = "Assets/GameA"):
        ///   "Art/Materials"  → "Assets/GameA/Art/Materials"
        ///   "Level/Prefabs"  → "Assets/GameA/Level/Prefabs"
        /// </summary>


        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Generates exact texture candidate names from wildcard patterns
        /// and searches for matching Texture2D assets.
        ///
        /// Example:
        ///     Pattern:  T_*_B
        ///     BaseName: Object
        ///     Result:   T_Object_B
        ///
        /// Matching is exact-file-name only to avoid accidental partial matches.
        /// </summary>
        private static Texture2D FindTextureFromPatterns(
            string folder,
            string baseName,
            List<string> patterns)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            if (string.IsNullOrWhiteSpace(baseName))
                return null;

            if (patterns == null || patterns.Count == 0)
                return null;

            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                string candidateName = pattern.Replace("*", baseName);

                string[] guids = AssetDatabase.FindAssets(
                    candidateName + " t:Texture2D",
                    new[] { folder });

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    string fileName =
                        Path.GetFileNameWithoutExtension(path);

                    // Exact name match only
                    if (!string.Equals(
                            fileName,
                            candidateName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Texture2D tex =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                    if (tex != null)
                        return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// Strips a leading "Assets/" from a path, returning just the relative portion.
        /// Used when sanitizing profiles exported before v1.2.
        /// </summary>
        private static string StripAssetsPrefix(string path)
        {
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return path["Assets/".Length..];
            return path;
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
                candidate = Path.Combine(folderPath, $"{baseName}{counter}.asset").Replace("\\", "/");
                counter++;
            }

            return candidate;
        }
    }
}