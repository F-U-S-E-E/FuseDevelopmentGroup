using FUSE.Infrastructure;
using Model.Ops;

namespace FUSE.Runtime.API
{
    public sealed class FuseFormulaicIndustryComponent : FormulaicIndustryComponent
    {
        protected override void ValidateIndustryComponent()
        {
            if ((inputTerms == null || inputTerms.Count == 0) &&
                (outputTerms == null || outputTerms.Count == 0))
            {
                FuseLog.Warning(
                    $"FUSE formulaic industry component '{Identifier}' has no inputs or outputs; " +
                    "it will remain inert until the definition is fixed.");
            }
        }
    }
}
