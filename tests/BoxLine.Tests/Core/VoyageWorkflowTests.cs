using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class VoyageWorkflowTests
{
    public static TheoryData<VoyageStatus, VoyageStatus, bool> AllTransitions()
    {
        var data = new TheoryData<VoyageStatus, VoyageStatus, bool>();
        foreach (var from in Enum.GetValues<VoyageStatus>())
        {
            foreach (var to in Enum.GetValues<VoyageStatus>())
            {
                var allowed = (from, to) is (VoyageStatus.Scheduled, VoyageStatus.Underway)
                    or (VoyageStatus.Scheduled, VoyageStatus.Cancelled)
                    or (VoyageStatus.Underway, VoyageStatus.Completed)
                    or (VoyageStatus.Underway, VoyageStatus.Cancelled);
                data.Add(from, to, allowed);
            }
        }

        return data;
    }

    [Fact]
    public void AllTransitions_CoversSixteenPairs() => AllTransitions().Should().HaveCount(16);

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void CanTransition_MatchesTheLifecycle(VoyageStatus from, VoyageStatus to, bool expected) =>
        VoyageWorkflow.CanTransition(from, to).Should().Be(expected);

    [Theory]
    [InlineData(VoyageStatus.Scheduled, VoyageStatus.Completed)]
    [InlineData(VoyageStatus.Completed, VoyageStatus.Underway)]
    [InlineData(VoyageStatus.Cancelled, VoyageStatus.Scheduled)]
    [InlineData(VoyageStatus.Underway, VoyageStatus.Scheduled)]
    public void EnsureCanTransition_ThrowsConflictForForbiddenMoves(VoyageStatus from, VoyageStatus to)
    {
        var act = () => VoyageWorkflow.EnsureCanTransition(from, to);

        act.Should().Throw<ConflictException>().WithMessage($"A {from} voyage cannot move to {to}.");
    }

    [Fact]
    public void FinishedVoyages_GoNowhere()
    {
        VoyageWorkflow.NextStatuses(VoyageStatus.Completed).Should().BeEmpty();
        VoyageWorkflow.NextStatuses(VoyageStatus.Cancelled).Should().BeEmpty();
    }
}
