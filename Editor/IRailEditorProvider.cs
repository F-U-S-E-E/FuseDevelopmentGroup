using RAIL.Validation;

namespace RAIL.Editor
{
    public interface IRailEditorProvider
    {
        void OnValidationCompleted(string objectId, ValidationResult result);
    }
}
