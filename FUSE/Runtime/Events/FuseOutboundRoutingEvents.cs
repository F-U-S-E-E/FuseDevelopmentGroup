using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using Model.Ops;

namespace FUSE.Runtime.Events
{
    public sealed class FuseOutboundRoutingCandidate
    {
        public FuseOutboundRoutingCandidate(
            IndustryComponent component,
            float weight,
            float? proposedPayment = null,
            int? proposedGraceDays = null)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Weight = weight;
            ProposedPayment = proposedPayment;
            ProposedGraceDays = proposedGraceDays;
        }

        public IndustryComponent Component { get; }
        public float Weight { get; set; }
        public float? ProposedPayment { get; set; }
        public int? ProposedGraceDays { get; set; }
    }

    public sealed class FuseOutboundRoutingContext
    {
        internal FuseOutboundRoutingContext(
            IOpsCar car,
            OpsCarPosition origin,
            bool loaded,
            IList<FuseOutboundRoutingCandidate> candidates)
        {
            Car = car;
            Origin = origin;
            IsLoaded = loaded;
            Candidates = candidates;
        }

        public IOpsCar Car { get; }
        public OpsCarPosition Origin { get; }
        public bool IsLoaded { get; }
        public IList<FuseOutboundRoutingCandidate> Candidates { get; }
    }

    public static class FuseOutboundRoutingEvents
    {
        public static event Action<FuseOutboundRoutingContext> Preparing;

        internal static void RaisePreparing(FuseOutboundRoutingContext context)
        {
            var handlers = Preparing;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<FuseOutboundRoutingContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        "FUSE contained an outbound-routing extension error; " +
                        $"other extensions and base routing remain available: {ex.GetBaseException().Message}");
                }
            }
        }

        internal static void Reset()
        {
            Preparing = null;
        }
    }
}
