using UnityEditor;

namespace GlyphLabs
{
    /// <summary>
    /// Editor-only typed accessors for the int-backed ModelImporter enum fields
    /// on FBXImportPreset. These live in the Editor assembly so Runtime never
    /// takes a dependency on UnityEditor.
    ///
    /// Usage (Editor code only):
    ///   var c = preset.MeshCompression();                        // read
    ///   preset.SetMeshCompression(ModelImporterMeshCompression.Low); // write
    /// </summary>
    public static class FBXImportPresetEditorExtensions
    {
        // ── ModelImporterMeshCompression ─────────────────────────────────────────

        public static ModelImporterMeshCompression MeshCompression(this FBXImportPreset preset)
            => (ModelImporterMeshCompression)preset.meshCompressionInt;

        public static void SetMeshCompression(
            this FBXImportPreset preset, ModelImporterMeshCompression value)
            => preset.meshCompressionInt = (int)value;

        // ── ModelImporterNormals ─────────────────────────────────────────────────

        public static ModelImporterNormals Normals(this FBXImportPreset preset)
            => (ModelImporterNormals)preset.normalsInt;

        public static void SetNormals(
            this FBXImportPreset preset, ModelImporterNormals value)
            => preset.normalsInt = (int)value;

        // ── ModelImporterTangents ────────────────────────────────────────────────

        public static ModelImporterTangents Tangents(this FBXImportPreset preset)
            => (ModelImporterTangents)preset.tangentsInt;

        public static void SetTangents(
            this FBXImportPreset preset, ModelImporterTangents value)
            => preset.tangentsInt = (int)value;
    }
}