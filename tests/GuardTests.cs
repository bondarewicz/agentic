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
