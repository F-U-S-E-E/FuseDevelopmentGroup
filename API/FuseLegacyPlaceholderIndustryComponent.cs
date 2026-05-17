using Model.Ops;
using Model.Ops.Definition;

namespace FUSE.API
{
    public sealed class FuseLegacyPlaceholderIndustryComponent : IndustryComponent
    {
        public override bool IsVisible => false;

        protected override void ValidateIndustryComponent()
        {
        }

        public override void Service(IIndustryContext ctx)
        {
        }

        public override void OrderCars(IIndustryContext ctx)
        {
        }
    }
}
