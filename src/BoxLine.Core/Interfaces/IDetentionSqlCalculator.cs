using BoxLine.Core.Detention;

namespace BoxLine.Core.Interfaces;

/// <summary>
/// The detention calculation as run by the database (<c>usp_CalculateDetention</c>). It must give exactly the
/// same answers, and raise the same errors, as <see cref="DetentionCalculator"/>.
/// </summary>
public interface IDetentionSqlCalculator
{
    Task<DetentionBreakdown> CalculateAsync(
        int tariffId, DateOnly outDate, DateOnly returnDate, bool countWeekendsAsChargeable, CancellationToken cancellationToken);
}
