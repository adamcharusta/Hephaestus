using Hephaestus.Core.Signals;
using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Tests.Windows;

/// <summary>Story 3.14, FR-16: the record of a window, the names of how it ended, and the Signals that ask for one.</summary>
public class WindowTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_is_open_until_it_is_closed()
    {
        var window = new Window(Guid.CreateVersion7(), 101, Noon);

        Assert.True(window.IsOpen);
        Assert.False((window with { ClosedAt = Noon.AddHours(1), End = WindowEnd.Drained }).IsOpen);
    }

    [Fact]
    public void The_ways_a_window_ends_have_names_that_are_read_back()
    {
        Assert.Equal(["stopped", "drained"], Enum.GetValues<WindowEnd>().Select(end => end.Tag()));
        Assert.All(Enum.GetValues<WindowEnd>(), end =>
        {
            Assert.True(WindowEnds.TryParse(end.Tag(), out var parsed));
            Assert.Equal(end, parsed);
        });
        Assert.False(WindowEnds.TryParse("finished", out _));
        Assert.False(WindowEnds.TryParse(null, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((WindowEnd)9).Tag());
    }

    [Fact]
    public void A_request_of_the_Owner_is_a_Signal_of_its_own_from_the_control_API_that_names_the_Project_and_no_issue_and_no_actor()
    {
        var id = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");

        var start = Signal.Start(101, id, Noon);
        var stop = Signal.Stop(101, id, Noon);

        Assert.Equal((Signal.ControlSource, "018f4b6e-0000-7000-8000-000000000001", SignalKind.StartRequested, 101L, 0, null), (start.Source, start.SourceEventId, start.Kind, start.RepositoryId, start.IssueNumber, start.ActorId));
        Assert.Equal((SignalKind.StopRequested, "stop-requested"), (stop.Kind, stop.Kind.Tag()));
        Assert.Equal("start-requested", start.Kind.Tag());
        Assert.True(SignalKinds.TryParse("start-requested", out var kind));
        Assert.Equal(SignalKind.StartRequested, kind);
    }

    [Fact]
    public void The_notes_of_an_applied_Signal_are_machine_values()
    {
        Assert.Equal(("window-already-open", "window-not-open"), (SignalNotes.WindowAlreadyOpen, SignalNotes.WindowNotOpen));
    }
}
