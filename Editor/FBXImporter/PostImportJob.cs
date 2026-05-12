using System;
using System.Collections.Generic;

namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Describes a single FBX post-import work item.
    /// Enqueued by FBXImporterProcessor during OnPostprocessModel,
    /// consumed by FBXPostImportQueue after the import lock releases.
    /// </summary>
    [Serializable]
    public sealed class PostImportJob
    {
        public string assetPath;
        public FBXImportPreset preset;
        public FBXImportProfile profile;
        public JobResult result = new ();
    }

    /// <summary>
    /// Per-job outcome written by FBXImporterUtility during each drain step.
    /// Used for Console logging and future UI progress display.
    /// </summary>
    [Serializable]
    public sealed class JobResult
    {
        public List<string> materialsCreated = new ();
        public bool texturesAssigned;
        public string prefabPath;
        public List<string> errors = new ();

        public bool HasErrors => errors.Count > 0;
    }

    /// <summary>
    /// Wrapper for JsonUtility serialization of a job list.
    /// JsonUtility cannot serialize a bare List — this wrapper is
    /// used when persisting the queue to SessionState before a domain reload.
    /// </summary>
    [Serializable]
    internal sealed class PostImportJobListWrapper
    {
        public List<PostImportJob> jobs = new ();
    }
}