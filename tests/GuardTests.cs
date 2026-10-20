public class GuardTests
{
    [Theory]
    [InlineData("../x")]
    [InlineData("/etc/passwd")]
    [InlineData("../workspace2/x")]
    public void Resolve_rejects_paths_outside_the_workspace(string path) =>
        Assert.Throws<ArgumentException>(() => Agent.Resolve(path));

    [Fact]
    public void Resolve_accepts_paths_inside_the_workspace() =>
        Assert.Equal(Path.Combine(Agent.Root, "src", "Program.cs"), Agent.Resolve("src/Program.cs"));

    [Fact]
    public void DotEnv_skips_comments_and_strips_quotes() =>
        Assert.Equal(
            [("A", "1"), ("B", "x=y")],
            DotEnv.Parse(["# ANTHROPIC_API_KEY=old", "A = \"1\"", "B=x=y", "junk"]));
}

public class SeededWorkspaceTests
{
    [Fact]
    public void Fails_on_rounding_then_on_csv_then_passes_once_both_bugs_are_fixed()
    {
        Agent.SeedWorkspace();
        var invoice = Path.Combine(Agent.Root, "src", "Invoice.cs");
        Assert.Contains("FAIL VAT total", Agent.RunTests());

        File.WriteAllText(invoice, File.ReadAllText(invoice).Replace(
            "lines.Sum(l => Math.Round(l.Hours * l.Rate * (1 + VatRate), 2))",
            "Math.Round(lines.Sum(l => l.Hours * l.Rate) * (1 + VatRate), 2)"));
        Assert.Contains("FAIL a trailing newline", Agent.RunTests());

        File.WriteAllText(invoice, File.ReadAllText(invoice).Replace(
            "csv.Split('\\n')", "csv.Split('\\n', StringSplitOptions.RemoveEmptyEntries)"));
        Assert.Contains("All tests passed.", Agent.RunTests());
    }
}

public class CommitFormatTests
{
    [Theory]
    [InlineData("fix: stop treating cut-off replies as complete\n\nWhy it matters.")]
    [InlineData("fix(claude)!: stop treating cut-off replies as complete")]
    public void Accepts_conventional_messages(string message) =>
        Assert.Null(EvaluatorOptimizer.FormatProblem(message));

    [Theory]
    [InlineData("Fixed the bug")]
    [InlineData("```\nfix: wrapped in a fence\n```")]
    [InlineData("fix: this subject line is far too long to fit within the seventy two character limit")]
    [InlineData("fix: no blank line\nbody starts here")]
    public void Rejects_broken_messages(string message) =>
        Assert.NotNull(EvaluatorOptimizer.FormatProblem(message));
}
