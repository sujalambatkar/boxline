using BoxLine.Core.Entities;

namespace BoxLine.Core.Bookings;

/// <summary>Filters and paging for the booking list. Dates are inclusive UTC calendar days.</summary>
public sealed record BookingQuery(
    BookingStatus? Status = null,
    int? PolId = null,
    int? PodId = null,
    int? ShipperId = null,
    DateOnly? CreatedFrom = null,
    DateOnly? CreatedTo = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);
