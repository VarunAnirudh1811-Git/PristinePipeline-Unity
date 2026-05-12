using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Collects FBX post-import jobs enqueued during OnPostprocessModel
    /// and drains them incrementally after the import lock releases.
    ///
    /// Flow:
    ///   1. OnPostprocessModel calls Enqueue() — resets the debounce timer.
    ///   2. EditorApplication.update fires every frame.
    ///   3. Once the debounce window expires with no new enqueues, the queue
    ///      snapshots all pending jobs into an active batch and begins draining.
    ///   4. Each drain call advances one step: Materials → Textures → Prefabs → Done.
    ///   5. A single AssetDatabase.Refresh() fires at the end of Done.
    ///
    /// Domain reload safety:
    ///   Before assembly reload, the pending queue is written to SessionState so
    ///   jobs are not lost if a script is saved mid-import. On [InitializeOnLoad]
    ///   the queue checks SessionState and restores any saved jobs.
    /// </summary>
    [InitializeOnLoad]
    public static class FBXPostImportQueue
    {
        // ── Session state key ────────────────────────────────────────────────────

        private const string SessionKey = "GlyphLabs.FBXPostImportQueue.Pending";

        // ── Queue and drain state ────────────────────────────────────────────────

        private static readonly Queue<PostImportJob> _pending = new ();
        private static List<PostImportJob> _activeBatch = new ();

        private static bool _draining = false;
        private static bool _editingStarted = false;
        private static double _lastEnqueueTime = -1.0;

        private enum DrainStep { Materials, Remap, Textures, Prefabs, Done }
        private static DrainStep _currentStep;

        // ── Initialisation ───────────────────────────────────────────────────────

        static FBXPostImportQueue()
        {
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            RestoreFromSessionState();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a job to the pending queue and resets the debounce timer.
        /// Safe to call from inside OnPostprocessModel.
        /// </summary>
        public static void Enqueue(PostImportJob job)
        {
            if (job == null) return;
            _pending.Enqueue(job);
            _lastEnqueueTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Number of jobs currently waiting to be processed.
        /// </summary>
        public static int PendingCount => _pending.Count;

        // ── Editor update loop ───────────────────────────────────────────────────

        private static void OnEditorUpdate()
        {
            if (_draining)
            {
                AdvanceDrain();
                return;
            }

            if (_pending.Count == 0) return;

            double elapsed = EditorApplication.timeSinceStartup - _lastEnqueueTime;
            if (elapsed < ToolSettings.FBX_PostImportDebounceSeconds) return;

            BeginDrain();
        }

        // ── Drain lifecycle ──────────────────────────────────────────────────────

        private static void BeginDrain()
        {
            _activeBatch = new List<PostImportJob>(_pending);
            _pending.Clear();

            // Clear session state — jobs are now in the active batch
            SessionState.EraseString(SessionKey);

            _currentStep = DrainStep.Materials;
            _draining = true;

            AssetDatabase.StartAssetEditing();
            _editingStarted = true;

            Debug.Log(
                $"{ToolInfo.LogPrefix} Post-import queue draining " +
                $"{_activeBatch.Count} job(s).");
        }

        private static void AdvanceDrain()
        {
            switch (_currentStep)
            {
                case DrainStep.Materials:
                    RunMaterials();
                    // Flush so material assets are registered before remap tries to load them
                    SafeStopEditing();
                    AssetDatabase.StartAssetEditing();
                    _editingStarted = true;
                    _currentStep = DrainStep.Remap;
                    break;

                case DrainStep.Remap:
                    RunRemap();
                    // Flush again so SaveAndReimport's binding changes are visible
                    // before texture assignment loads the materials
                    SafeStopEditing();
                    AssetDatabase.StartAssetEditing();
                    _editingStarted = true;
                    _currentStep = DrainStep.Textures;
                    break;

                case DrainStep.Textures:
                    RunTextures();
                    _currentStep = DrainStep.Prefabs;
                    break;

                case DrainStep.Prefabs:
                    RunPrefabs();
                    _currentStep = DrainStep.Done;
                    break;

                case DrainStep.Done:
                    SafeStopEditing();
                    AssetDatabase.Refresh();
                    LogBatchResults();

                    _activeBatch.Clear();
                    _draining = false;
                    break;
            }
        }

        // ── Step runners ─────────────────────────────────────────────────────────

        private static void RunMaterials()
        {
            foreach (PostImportJob job in _activeBatch)
            {
                try { FBXImporterUtility.CreateMaterialsForJob(job); }
                catch (System.Exception ex)
                {
                    job.result.errors.Add($"Material step: {ex.Message}");
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Material creation failed for " +
                        $"'{job.assetPath}': {ex.Message}");
                }
            }
        }

        private static void RunRemap()
        {
            foreach (PostImportJob job in _activeBatch)
            {
                try { FBXImporterUtility.RemapMaterialsForJob(job); }
                catch (System.Exception ex)
                {
                    job.result.errors.Add($"Remap step: {ex.Message}");
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Material remap failed for " +
                        $"'{job.assetPath}': {ex.Message}");
                }
            }
        }

        private static void RunTextures()
        {
            foreach (PostImportJob job in _activeBatch)
            {
                try { FBXImporterUtility.AssignTexturesForJob(job); }
                catch (System.Exception ex)
                {
                    job.result.errors.Add($"Texture step: {ex.Message}");
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Texture assignment failed for " +
                        $"'{job.assetPath}': {ex.Message}");
                }
            }
        }

        private static void RunPrefabs()
        {
            foreach (PostImportJob job in _activeBatch)
            {
                try { FBXImporterUtility.GeneratePrefabForJob(job); }
                catch (System.Exception ex)
                {
                    job.result.errors.Add($"Prefab step: {ex.Message}");
                    Debug.LogError(
                        $"{ToolInfo.LogPrefix} Prefab generation failed for " +
                        $"'{job.assetPath}': {ex.Message}");
                }
            }
        }

        // ── Domain reload safety ─────────────────────────────────────────────────

        private static void OnBeforeAssemblyReload()
        {
            // Persist any unprocessed pending jobs to SessionState so they
            // survive the domain reload and are picked up on re-initialisation.
            if (_pending.Count > 0)
            {
                var wrapper = new PostImportJobListWrapper
                {
                    jobs = new List<PostImportJob>(_pending)
                };
                SessionState.SetString(SessionKey, JsonUtility.ToJson(wrapper));
            }

            // Always stop editing if we were mid-drain to avoid a stuck editing scope.
            if (_draining)
                SafeStopEditing();
        }

        private static void RestoreFromSessionState()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var wrapper = JsonUtility.FromJson<PostImportJobListWrapper>(json);
                if (wrapper?.jobs == null) return;

                foreach (PostImportJob job in wrapper.jobs)
                    _pending.Enqueue(job);

                // Trigger drain immediately — debounce already expired
                _lastEnqueueTime = EditorApplication.timeSinceStartup - 999.0;

                SessionState.EraseString(SessionKey);

                Debug.Log(
                    $"{ToolInfo.LogPrefix} Restored {wrapper.jobs.Count} " +
                    $"post-import job(s) after domain reload.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"{ToolInfo.LogPrefix} Could not restore post-import queue " +
                    $"from session state: {ex.Message}");
                SessionState.EraseString(SessionKey);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void SafeStopEditing()
        {
            if (!_editingStarted) return;
            AssetDatabase.StopAssetEditing();
            _editingStarted = false;
        }

        private static void LogBatchResults()
        {
            int success = 0, failed = 0;

            foreach (PostImportJob job in _activeBatch)
            {
                if (job.result.HasErrors) failed++;
                else success++;
            }

            string summary = $"{ToolInfo.LogPrefix} Post-import complete — " +
                             $"{success} succeeded, {failed} failed.";

            if (failed > 0) Debug.LogWarning(summary);
            else Debug.Log(summary);
        }
    }
}