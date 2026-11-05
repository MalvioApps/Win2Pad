using Win2Pad.Core;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class LinkStateTests
{
    [Theory]
    [InlineData(false, 0, false, LinkState.Off)]
    [InlineData(false, 1, true, LinkState.Off)] // stale counters after a stop must not look connected
    [InlineData(true, 0, false, LinkState.Waiting)]
    [InlineData(true, 0, true, LinkState.Waiting)] // redirecting into nothing is still waiting
    [InlineData(true, 1, false, LinkState.Local)]
    [InlineData(true, 2, true, LinkState.Remote)]
    public void State_follows_the_peripheral(bool running, int subscribers, bool redirecting, LinkState expected) =>
        Assert.Equal(expected, LinkStates.From(running, subscribers, redirecting));

    [Fact]
    public void Remote_description_names_the_target() =>
        Assert.Equal("Controlling iPad", LinkStates.Describe(LinkState.Remote, "iPad"));
}
