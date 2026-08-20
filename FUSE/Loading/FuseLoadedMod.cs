using FUSE.Authoring.Data;

namespace FUSE.Loading
{
    public sealed class FuseLoadedMod
    {
        public FuseLoadedMod(
            string folderPath,
            string definitionPath,
            FuseModDefinition definition,
            FuseModDefinition sourceDefinition = null,
            FuseFeatureEvaluation featureEvaluation = null)
        {
            FolderPath = folderPath ?? string.Empty;
            DefinitionPath = definitionPath ?? string.Empty;
            Definition = definition;
            SourceDefinition = sourceDefinition ?? definition;
            FeatureEvaluation = featureEvaluation ?? new FuseFeatureEvaluation();
        }

        public string FolderPath { get; }
        public string DefinitionPath { get; }
        public FuseModDefinition Definition { get; }
        public FuseModDefinition SourceDefinition { get; }
        public FuseFeatureEvaluation FeatureEvaluation { get; }
    }
}
