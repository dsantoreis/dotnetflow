using DotnetFlow.Api.Services;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class EventTriggerServiceTests
{
    [Fact]
    public void MatchesFilter_NullFilter_ReturnsTrue()
    {
        Assert.True(EventTriggerService.MatchesFilter(null, "any payload"));
    }

    [Fact]
    public void MatchesFilter_EmptyFilter_ReturnsTrue()
    {
        Assert.True(EventTriggerService.MatchesFilter("", "any payload"));
    }

    [Fact]
    public void MatchesFilter_MatchingPayload_ReturnsTrue()
    {
        Assert.True(EventTriggerService.MatchesFilter("order", "new order placed"));
    }

    [Fact]
    public void MatchesFilter_NonMatchingPayload_ReturnsFalse()
    {
        Assert.False(EventTriggerService.MatchesFilter("invoice", "new order placed"));
    }

    [Fact]
    public void MatchesFilter_CaseInsensitive()
    {
        Assert.True(EventTriggerService.MatchesFilter("ORDER", "new order placed"));
    }
}
