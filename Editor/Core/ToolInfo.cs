namespace GlyphLabs.PristinePipeline
{
    /// <summary>
    /// Single source of truth for tool identity.
    /// To rename the tool, change ToolName here — every other system reads from this.
    /// </summary>
     

    public static class ToolInfo
    {
        public const string ToolName = "Pristine Pipeline";
        public const string Version = "1.2.2";
        public const string Author = "GlyphLabs";
        public const string MenuRoot = "GlyphLabs/" + ToolName;
        public const string LogPrefix = "[" + ToolName + "]";
        public const string SettingsPrefix = "GlyphLabs.PristinePipeline";
        public const string DefaultTemplateSavePath = "Assets/GlyphLabs/PristinePipeline/Templates";
        public const string BuiltInTemplatePath = "Packages/com.glyphlabs.pristinepipeline/Resources/FolderTemplates";
        public const string DefaultAssetMappingProfileSavePath = "Assets/GlyphLabs/PristinePipeline/AssetMappingProfiles";
        public const string BuiltInMappingProfilePath = "Packages/com.glyphlabs.pristinepipeline/Resources/MappingProfiles";
        public const string DefaultFBXImportProfileSavePath = "Assets/GlyphLabs/PristinePipeline/FBXImportProfiles";
        public const string BuiltInImporterProfilePath = "Packages/com.glyphlabs.pristinepipeline/Resources/ImporterProfiles";
    }
}