using FUSE.Validation;

namespace FUSE.Editor
{
    public interface IFuseEditorProvider
    {
        void OnValidationCompleted(string objectId, ValidationResult result);
    }
}
