using System.Text.RegularExpressions;
using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Rules;

public sealed class FileNamePatternRetentionRule : IRetentionRule
{
    public bool IsMatch(FileMetadata file, RetentionCriteria criteria)
    {
        if (criteria.NamePatterns.Count == 0)
        {
            return true;
        }

        return criteria.NamePatterns.Any(pattern => IsWildcardMatch(file.Name, pattern));
    }

    private static bool IsWildcardMatch(string value, string pattern)
    {
        var expression = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(
            value,
            expression,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}
