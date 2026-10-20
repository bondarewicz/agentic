// Parallelization: security, speed and readability are independent checks (sectioning), and a yes/no vulnerability call is more reliable as a majority vote.
static class Parallelization
{
    const string Code = """
        public User? FindUser(string name)
        {
            var sql = "SELECT * FROM Users WHERE Name = '" + name + "'";
            using var cmd = new SqlCommand(sql, _connection);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
        """;

    record Vote(bool Vulnerable, string Reason);

    static readonly object VoteSchema = new
    {
        type = "object",
        properties = new { vulnerable = new { type = "boolean" }, reason = new { type = "string" } },
        required = new[] { "vulnerable", "reason" },
        additionalProperties = false,
    };

    public static async Task Run()
    {
        string[] aspects = ["security", "performance", "readability"];
        var reviews = await Task.WhenAll(aspects.Select(aspect =>
            Claude.Ask($"Review this C# code for {aspect} only. Two sentences max.\n\n{Code}")));

        var votes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Claude.AskJson<Vote>($"Does this code contain a security vulnerability?\n\n{Code}", VoteSchema)));
        var yes = votes.Count(v => v.Vulnerable);
        Console.WriteLine($"{aspects.Length} reviews and {votes.Length} votes in, {yes}/{votes.Length} say vulnerable.\n");

        var findings = string.Join("\n", aspects.Zip(reviews, (a, r) => $"[{a}] {r}"))
                     + "\n" + string.Join("\n", votes.Select(v => $"[vote vulnerable={v.Vulnerable}] {v.Reason}"));
        var verdict = await Claude.Ask(
            $"Code:\n{Code}\n\nReviewer findings:\n{findings}",
            system: "You are a senior engineer giving the final call on a code review. Reply in at most three short lines: the verdict (ship or block), the one issue that matters most, and the fix. No preamble, no repeating the reviewers.");
        Console.WriteLine(verdict);
    }
}
