namespace Fuse.Core.Validation
{
    public interface IValidator<in T>
    {
        ValidationResult Validate(T value);
    }
}
