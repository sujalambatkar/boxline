using BoxLine.Core.Bookings;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class BookingWorkflowTests
{
    public static TheoryData<BookingStatus, BookingStatus, bool> AllTransitions()
    {
        var data = new TheoryData<BookingStatus, BookingStatus, bool>();
        foreach (var from in Enum.GetValues<BookingStatus>())
        {
            foreach (var to in Enum.GetValues<BookingStatus>())
            {
                var allowed = (from, to) is (BookingStatus.Draft, BookingStatus.Confirmed)
                    or (BookingStatus.Confirmed, BookingStatus.Cancelled);
                data.Add(from, to, allowed);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void CanTransition_AllowsOnlyDraftToConfirmedAndConfirmedToCancelled(BookingStatus from, BookingStatus to, bool expected)
    {
        BookingWorkflow.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void AllTransitionsAreCovered() => AllTransitions().Should().HaveCount(9);

    [Theory]
    [InlineData(BookingStatus.Draft, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Draft, BookingStatus.Draft)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Confirmed)]
    public void EnsureCanTransition_ThrowsConflictForDisallowedMoves(BookingStatus from, BookingStatus to)
    {
        var act = () => BookingWorkflow.EnsureCanTransition(from, to);

        act.Should().Throw<ConflictException>().WithMessage($"A {from} booking cannot move to {to}.");
    }

    [Fact]
    public void EnsureCanTransition_DoesNotThrowForAllowedMoves()
    {
        BookingWorkflow.EnsureCanTransition(BookingStatus.Draft, BookingStatus.Confirmed);
        BookingWorkflow.EnsureCanTransition(BookingStatus.Confirmed, BookingStatus.Cancelled);
    }

    [Fact]
    public void NextStatuses_ListsWhereABookingCanGo()
    {
        BookingWorkflow.NextStatuses(BookingStatus.Draft).Should().Equal(BookingStatus.Confirmed);
        BookingWorkflow.NextStatuses(BookingStatus.Confirmed).Should().Equal(BookingStatus.Cancelled);
        BookingWorkflow.NextStatuses(BookingStatus.Cancelled).Should().BeEmpty();
    }
}
