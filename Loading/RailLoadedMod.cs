using RAIL.Data;

namespace RAIL.Loading
{
    public sealed class RailLoadedMod
    {
        public RailLoadedMod(string folderPath, string definitionPath, RailModDefinition definition)
        {
            FolderPath = folderPath ?? string.Empty;
            DefinitionPath = definitionPath ?? string.Empty;
            Definition = definition;
        }

        public string FolderPath { get; }
        public string DefinitionPath { get; }
        public RailModDefinition Definition { get; }
    }
}
