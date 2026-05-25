using FUSE.Authoring.Validation;

namespace FUSE.Authoring.Editor
{
    public interface IFuseEditorProvider
    {
        void OnValidationCompleted(string objectId, ValidationResult result);
    }
}
