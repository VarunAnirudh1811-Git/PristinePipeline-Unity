namespace GlyphLabs
{
    /// <summary>
    /// Single source of truth for tool identity.
    /// To rename the tool, change ToolName here — every other system reads from this.
    /// </summary>
     

    public static class ToolInfo
    {
        public const string ToolName = "Pristine Pipeline";
        public const string Version = "0.1.0";
        public const string Author = "GlyphLabs";
        public const string MenuRoot = "GlyphLabs/" + ToolName;
        public const string LogPrefix = "[" + ToolName + "]";
        public const string SettingsPrefix = "GlyphLabs.PristinePipeline";
        public const string DefaultTemplateSavePath = "Assets/GlyphLabs/PristinePipeline/Templates";
        public const string BuiltInTemplatePath = "Packages/com.glyphlabs.unity-pristine-pipeline/Resources/Templates";
        public const string DefaultAssetMappingProfileSavePath = "Assets/GlyphLabs/PristinePipeline/AssetMappingProfiles";
        public const string DefaultFBXImportProfileSavePath = "Assets/GlyphLabs/PristinePipeline/FBXImportProfiles";
    }
}