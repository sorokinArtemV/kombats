using Kombats.Battle.Application.UseCases.Turns;
using Xunit;

namespace Kombats.Battle.Application.Tests;

public class TurnDeadlineWorkerTests
{
    [Theory]
    [InlineData(0)] // no skew
    [InlineData(100)] // 100ms skew
    [InlineData(50)] // 50ms skew
    public void ShouldResolve_WhenNowIsBeforeDeadline_ReturnsFalse(int skewMs)
    {
        // Arrange
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deadline = now.AddMilliseconds(200); // deadline is 200ms in the future

        // Act
		var result = TurnDeadlinePolicy.ShouldResolve(now, deadline, skewMs);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(0)] // no skew
    [InlineData(100)] // 100ms skew
    [InlineData(50)] // 50ms skew
    public void ShouldResolve_WhenNowIsAtDeadline_ReturnsFalse(int skewMs)
    {
        // Arrange
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deadline = now; // deadline is exactly now

        // Act
		var result = TurnDeadlinePolicy.ShouldResolve(now, deadline, skewMs);

        // Assert
        if (skewMs == 0)
        {
            Assert.True(result); // at deadline with no skew should resolve
        }
        else
        {
            Assert.False(result); // at deadline but within skew buffer should not resolve
        }
    }

    [Theory]
    [InlineData(0)] // no skew
    [InlineData(100)] // 100ms skew
    [InlineData(50)] // 50ms skew
    public void ShouldResolve_WhenNowIsSlightlyAfterDeadlineButWithinSkew_ReturnsFalse(int skewMs)
    {
        // Arrange
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deadline = now.AddMilliseconds(-skewMs / 2); // deadline was skewMs/2 ago

        // Act
		var result = TurnDeadlinePolicy.ShouldResolve(now, deadline, skewMs);

        // Assert
        if (skewMs == 0)
        {
            Assert.True(result); // with no skew, any past deadline should resolve
        }
        else
        {
            Assert.False(result); // still within skew buffer, should not resolve
        }
    }

    [Theory]
    [InlineData(0, 1)] // no skew, 1ms after
    [InlineData(100, 101)] // 100ms skew, 101ms after
    [InlineData(50, 51)] // 50ms skew, 51ms after
    public void ShouldResolve_WhenNowIsAfterDeadlinePlusSkew_ReturnsTrue(int skewMs, int msAfterDeadline)
    {
        // Arrange
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deadline = now.AddMilliseconds(-msAfterDeadline); // deadline was msAfterDeadline ago

        // Act
		var result = TurnDeadlinePolicy.ShouldResolve(now, deadline, skewMs);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldResolve_WhenNowIsExactlyDeadlinePlusSkew_ReturnsTrue()
    {
        // Arrange
        var skewMs = 100;
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deadline = now.AddMilliseconds(-skewMs); // deadline was exactly skewMs ago

        // Act
		var result = TurnDeadlinePolicy.ShouldResolve(now, deadline, skewMs);

        // Assert
        Assert.True(result); // >= means it should resolve at the boundary
    }
}

