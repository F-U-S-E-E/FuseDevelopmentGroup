using Model.Ops;
using Model.Ops.Definition;

namespace FUSE.Runtime.API
{
    public sealed class FuseLegacyPlaceholderIndustryComponent : IndustryComponent
    {
        protected override void ValidateIndustryComponent()
        {
        }

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            // Confusing Supplements' Empty component is a visible, span-bound
            // destination marker. It performs no service work, but accepts
            // every automatic destination class so authors can reserve/highlight
            // tracks such as "Loaded Coal Output Tracks: KEEP CLEAR!" without
            // creating a loader whose null Load later throws during Industry.Tick.
            return true;
        }

        public override void Service(IIndustryContext ctx)
        {
        }

        public override void OrderCars(IIndustryContext ctx)
        {
        }
    }
}
