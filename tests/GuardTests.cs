using Agents;

public class GuardTests
{
    [Fact]
    public void DotEnv_skips_comments_and_strips_quotes_and_export() =>
        Assert.Equal(
            [("A", "1"), ("B", "x=y"), ("C", "2"), ("D", "3")],
            DotEnv.Parse([
                "# ANTHROPIC_API_KEY=old",
                "A = \"1\"",
                "B=x=y",
                "junk",
                "C='2'",
                "export D=3",
            ])
        );
}

public class SeededWorkspaceTests
{
    [Fact]
    public void Seeded_kitchen_is_short_of_mozzarella_and_gorgonzola_only()
    {
        if (Directory.Exists(Agent.Root))
            Directory.Delete(Agent.Root, recursive: true);
        Agent.SeedWorkspace();
        Assert.Equal(
            "Short:\nmozzarella: 1800 g short\ngorgonzola: 500 g short",
            Agent.Shortages()
        );
    }

    [Fact]
    public void Approved_order_updates_stock_and_sold_out_item_is_refused()
    {
        if (Directory.Exists(Agent.Root))
            Directory.Delete(Agent.Root, recursive: true);
        Agent.SeedWorkspace();
        Console.SetIn(new StringReader("y\n"));

        Assert.Contains("sold out", Agent.PlaceOrder("Fresco", "gorgonzola", 1));
        Assert.Contains("Ordered", Agent.PlaceOrder("Fresco", " Mozzarella ", 1));
        Assert.Equal("Short:\ngorgonzola: 500 g short", Agent.Shortages());
        Assert.Throws<ArgumentException>(() => Agent.PlaceOrder("Fresco", "mozzarella cheese", 1));
    }

    [Fact]
    public void Refuses_to_delete_a_workspace_folder_it_did_not_create()
    {
        if (Directory.Exists(Agent.Root))
            Directory.Delete(Agent.Root, recursive: true);
        Directory.CreateDirectory(Agent.Root);
        File.WriteAllText(Path.Combine(Agent.Root, "keep.txt"), "someone's real file");
        try
        {
            Assert.Throws<InvalidOperationException>(Agent.SeedWorkspace);
            Assert.True(File.Exists(Path.Combine(Agent.Root, "keep.txt")));
        }
        finally
        {
            Directory.Delete(Agent.Root, recursive: true);
        }
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
    [InlineData(
        "fix: this subject line is far too long to fit within the seventy two character limit"
    )]
    [InlineData("fix: no blank line\nbody starts here")]
    [InlineData("fix: end the subject with a period.")]
    [InlineData(
        "fix: long body line\n\nThis body line keeps going well past the seventy two character wrapping limit."
    )]
    public void Rejects_broken_messages(string message) =>
        Assert.NotNull(EvaluatorOptimizer.FormatProblem(message));
}
