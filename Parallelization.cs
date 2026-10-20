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
        Console.WriteLine("== Sectioning: three reviewers, three aspects, in parallel ==");
        string[] aspects = ["security", "performance", "readability"];
        var reviews = await Task.WhenAll(aspects.Select(aspect =>
            Claude.Ask($"Review this C# code for {aspect} only. Two sentences max.\n\n{Code}")));
        foreach (var (aspect, review) in aspects.Zip(reviews))
            Console.WriteLine($"\n[{aspect}] {review}");

        Console.WriteLine("\n== Voting: three independent opinions on one question ==");
        var votes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Claude.AskJson<Vote>($"Does this code contain a security vulnerability?\n\n{Code}", VoteSchema)));
        foreach (var vote in votes)
            Console.WriteLine($"  vulnerable={vote.Vulnerable}: {vote.Reason}");
        var yes = votes.Count(v => v.Vulnerable);
        Console.WriteLine($"\nVerdict: {(yes >= 2 ? "VULNERABLE" : "ok")} ({yes}/3 votes)");
    }
}
