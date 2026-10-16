using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ProjectTime.Server.Data;

namespace ProjectTime.Server.Services;

/// <summary>Avgör vilket projekt en aktivitet hör till utifrån reglerna. Ren logik utan databas.</summary>
public sealed class RuleMatcher
{
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();
    private readonly List<Rule> _rules;

    public RuleMatcher(IEnumerable<Rule> rules)
    {
        _rules = rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Pattern))
                      .OrderByDescending(r => r.Priority).ThenBy(r => r.Id).ToList();
    }

    public int? Resolve(string appName, string windowTitle, string? processPath)
    {
        foreach (var r in _rules)
            if (Matches(r, appName, windowTitle, processPath)) return r.ProjectId;
        return null;
    }

    public static bool Matches(Rule r, string appName, string windowTitle, string? processPath)
    {
        return r.Field switch
        {
            RuleField.App => Test(r, appName),
            RuleField.Title => Test(r, windowTitle),
            RuleField.Path => Test(r, processPath ?? ""),
            _ => Test(r, appName) || Test(r, windowTitle) || Test(r, processPath ?? ""),
        };
    }

    private static bool Test(Rule r, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        switch (r.Match)
        {
            case RuleMatch.Contains: return value.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase);
            case RuleMatch.StartsWith: return value.StartsWith(r.Pattern, StringComparison.OrdinalIgnoreCase);
            case RuleMatch.Equals: return value.Equals(r.Pattern, StringComparison.OrdinalIgnoreCase);
            case RuleMatch.Regex:
                var rx = RegexCache.GetOrAdd(r.Pattern, p =>
                {
                    try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
                    catch (ArgumentException) { return null; }
                });
                try { return rx?.IsMatch(value) ?? false; }
                catch (RegexMatchTimeoutException) { return false; }
            default: return false;
        }
    }

    public static bool IsValidPattern(RuleMatch match, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (match != RuleMatch.Regex) return true;
        try { _ = new Regex(pattern); return true; } catch (ArgumentException) { return false; }
    }
}
