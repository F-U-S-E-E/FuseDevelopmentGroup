using Fuse.Core.Model;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Loads and saves FUSE packages (<c>*.fuse.json</c> / <c>.bson</c>) for the
/// editor. Abstracted so view models stay testable with a fake implementation.
/// </summary>
public interface IProjectService
{
    FuseModDefinition Load(string path);

    void Save(FuseModDefinition definition, string path);
}
