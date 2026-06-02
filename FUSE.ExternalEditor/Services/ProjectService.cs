using Fuse.Core.Model;
using Fuse.Core.Serialization;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Default <see cref="IProjectService"/> backed by <see cref="FuseCoreSerializer"/>,
/// so the external editor reads/writes exactly the same <c>*.fuse.json</c>
/// contract the in-game stack consumes.
/// </summary>
public sealed class ProjectService : IProjectService
{
    public FuseModDefinition Load(string path) => FuseCoreSerializer.Load(path);

    public void Save(FuseModDefinition definition, string path) => FuseCoreSerializer.SaveJson(definition, path);
}
