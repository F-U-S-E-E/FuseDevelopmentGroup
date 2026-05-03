using FUSE.Data;

namespace FUSE.Loading
{
    public sealed class FuseLoadedMod
    {
        public FuseLoadedMod(string folderPath, string definitionPath, FuseModDefinition definition)
        {
            FolderPath = folderPath ?? string.Empty;
            DefinitionPath = definitionPath ?? string.Empty;
            Definition = definition;
        }

        public string FolderPath { get; }
        public string DefinitionPath { get; }
        public FuseModDefinition Definition { get; }
    }
}
