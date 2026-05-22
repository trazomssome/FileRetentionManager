using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Rules;

namespace FileRetentionManager.Tests.Domain;

public sealed class CompositeRetentionRuleTests
{
    [Fact]
    public void IsMatch_ReturnsTrue_WhenAllActiveConditionsMatch()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new FileMetadata(
            @"C:\Temp\archive.tmp",
            "archive.tmp",
            2 * 1024 * 1024,
            now.AddDays(-45),
            now.AddDays(-40));
        var criteria = new RetentionCriteria(
            now.AddDays(-30),
            1024 * 1024,
            ["*.tmp"]);

        var result = CompositeRetentionRule.Default.IsMatch(file, criteria);

        Assert.True(result);
    }

    [Fact]
    public void IsMatch_ReturnsFalse_WhenNoConditionsAreActive()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new FileMetadata(
            @"C:\Temp\archive.tmp",
            "archive.tmp",
            2 * 1024 * 1024,
            now.AddDays(-45),
            now.AddDays(-40));
        var criteria = new RetentionCriteria(null, null, []);

        var result = CompositeRetentionRule.Default.IsMatch(file, criteria);

        Assert.False(result);
    }

    [Fact]
    public void IsMatch_ReturnsFalse_WhenPatternDoesNotMatch()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new FileMetadata(
            @"C:\Temp\archive.tmp",
            "archive.tmp",
            2 * 1024 * 1024,
            now.AddDays(-45),
            now.AddDays(-40));
        var criteria = new RetentionCriteria(
            now.AddDays(-30),
            1024,
            ["*.log"]);

        var result = CompositeRetentionRule.Default.IsMatch(file, criteria);

        Assert.False(result);
    }

    [Fact]
    public void IsMatch_ReturnsTrue_WhenOrConditionMatches()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new FileMetadata(
            @"C:\Temp\fresh.tmp",
            "fresh.tmp",
            1024,
            now.AddDays(-2),
            now.AddDays(-1));
        var criteria = new RetentionCriteria(
            now.AddDays(-30),
            null,
            ["*.tmp"],
            [@"C:\Temp"],
            true,
            ConditionJoinMode.Or);

        var result = CompositeRetentionRule.Default.IsMatch(file, criteria);

        Assert.True(result);
    }
}
