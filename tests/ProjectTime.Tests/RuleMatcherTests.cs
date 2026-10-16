using ProjectTime.Server.Data;
using ProjectTime.Server.Services;

namespace ProjectTime.Tests;

public class RuleMatcherTests
{
    private static Rule R(int project, RuleField field, RuleMatch match, string pattern, int prio = 0, bool enabled = true) =>
        new() { Id = project, ProjectId = project, Field = field, Match = match, Pattern = pattern, Priority = prio, Enabled = enabled };

    [Fact]
    public void Contains_is_case_insensitive()
    {
        var m = new RuleMatcher(new[] { R(1, RuleField.Title, RuleMatch.Contains, "kundportal") });
        Assert.Equal(1, m.Resolve("code", "KundPortal – main.cs", null));
        Assert.Null(m.Resolve("code", "annat", null));
    }

    [Fact]
    public void Higher_priority_wins()
    {
        var m = new RuleMatcher(new[]
        {
            R(1, RuleField.App, RuleMatch.Equals, "code", prio: 0),
            R(2, RuleField.Title, RuleMatch.Contains, "kundportal", prio: 10),
        });
        Assert.Equal(2, m.Resolve("code", "kundportal", null));
        Assert.Equal(1, m.Resolve("code", "annat", null));
    }

    [Fact]
    public void Disabled_rules_are_ignored()
    {
        var m = new RuleMatcher(new[] { R(1, RuleField.App, RuleMatch.Equals, "code", enabled: false) });
        Assert.Null(m.Resolve("code", "x", null));
    }

    [Fact]
    public void Regex_and_path_matching()
    {
        var m = new RuleMatcher(new[]
        {
            R(1, RuleField.Title, RuleMatch.Regex, @"PR #\d+"),
            R(2, RuleField.Path, RuleMatch.StartsWith, @"C:\Work\Acme"),
        });
        Assert.Equal(1, m.Resolve("browser", "PR #42 · GitHub", null));
        Assert.Equal(2, m.Resolve("devenv", "x", @"C:\Work\Acme\src\app.sln"));
        Assert.Null(m.Resolve("devenv", "x", null));
    }

    [Fact]
    public void Invalid_regex_never_matches_and_is_reported()
    {
        Assert.False(RuleMatcher.IsValidPattern(RuleMatch.Regex, "("));
        var m = new RuleMatcher(new[] { R(1, RuleField.Any, RuleMatch.Regex, "(") });
        Assert.Null(m.Resolve("a", "(", "("));
    }

    [Fact]
    public void Any_field_checks_app_title_and_path()
    {
        var m = new RuleMatcher(new[] { R(1, RuleField.Any, RuleMatch.Contains, "acme") });
        Assert.Equal(1, m.Resolve("acme-cli", "", null));
        Assert.Equal(1, m.Resolve("code", "Acme – readme", null));
        Assert.Equal(1, m.Resolve("code", "", "/home/u/acme/x"));
        Assert.Null(m.Resolve("code", "", null));
    }
}
