using HvacConnect.Core.Domain.Enums;

namespace HvacConnect.Core.Services;

public class SlaCalculator
{
    // Default SLA durations as per Section 7.2
    public static (TimeSpan response, TimeSpan resolution) GetDefaultDurations(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Critical => (TimeSpan.FromHours(2), TimeSpan.FromHours(4)),
            TicketPriority.High     => (TimeSpan.FromHours(4), TimeSpan.FromHours(8)),
            TicketPriority.Medium   => (TimeSpan.FromHours(8), TimeSpan.FromHours(24)),
            TicketPriority.Low      => (TimeSpan.FromHours(24), TimeSpan.FromHours(48)),
            _ => (TimeSpan.FromHours(8), TimeSpan.FromHours(24))
        };
    }

    public static SLAState ComputeState(DateTime createdAt, DateTime targetResolutionAt, DateTime? resolvedAt, DateTime nowUtc)
    {
        // SLA-RUL-005: The SLA clock stops at ResolvedAt. The final state is frozen.
        DateTime effectiveNow = resolvedAt ?? nowUtc;

        if (effectiveNow > targetResolutionAt)
        {
            return SLAState.Breached; // SLA-RUL-004
        }

        TimeSpan totalWindow = targetResolutionAt - createdAt;
        TimeSpan remaining = targetResolutionAt - effectiveNow;

        // SLA-RUL-003: AtRisk: 25% or less of window remains, or fewer than 2 hours.
        if (remaining.TotalHours <= 2 || remaining.TotalSeconds <= (totalWindow.TotalSeconds * 0.25))
        {
            return SLAState.AtRisk;
        }

        return SLAState.WithinSLA; // SLA-RUL-002
    }
}

