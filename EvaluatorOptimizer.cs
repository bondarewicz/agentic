using System.Text.RegularExpressions;

// Evaluator-optimizer: a commit message has clear rules, some checkable by code and some needing judgement, and it measurably improves with specific feedback.
static partial class EvaluatorOptimizer
{
    record Verdict(bool Pass, string Feedback);

    private const string Diff = """
        diff --git a/Claude.cs b/Claude.cs
        @@ static string Text(Message response)
             if (response.StopReason == "refusal")
                 throw new InvalidOperationException($"Claude refused: {response.StopDetails?.Explanation}");
        +    if (response.StopReason == "max_tokens")
        +        throw new InvalidOperationException("Reply was cut off at the token limit.");
             return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        """;

    private const string Criteria =
        "Criteria: 1) the type fits the change, 2) the body explains what changed and why, not how, 3) the subject is imperative and completes 'If applied, this commit will ...', 4) the subject aims for about 50 characters, 5) nothing in it just restates the diff.";

    public static async Task Run()
    {
        var message = "";
        string? feedback = null;

        for (var round = 1; round <= 3; round++)
        {
            message = feedback is null
                ? await Claude.Ask(
                    $"Write a git commit message for this diff. Output only the message.\n\n{Diff}"
                )
                : await Claude.Ask(
                    $"Revise this commit message using the reviewer's feedback. Output only the message.\n\nDiff:\n{Diff}\n\nPrevious message:\n{message}\n\nFeedback: {feedback}"
                );
            Console.WriteLine($"\nRound {round}:\n{message}");

            feedback = FormatProblem(message);
            if (feedback is null)
            {
                var verdict = await Claude.AskJson<Verdict>(
                    $"You are a demanding code reviewer. Evaluate this commit message for the diff. Pass only if every criterion holds.\n{Criteria}\n\nDiff:\n{Diff}\n\nMessage:\n{message}");
                if (verdict.Pass)
                {
                    Console.WriteLine("Reviewer: PASS");
                    return;
                }
                feedback = verdict.Feedback;
            }
            Console.WriteLine($"Reviewer: REVISE - {feedback}");
        }
        Console.WriteLine("\nStopped after 3 rounds without a pass.");
    }

    internal static string? FormatProblem(string message)
    {
        var lines = message.ReplaceLineEndings("\n").Trim().Split('\n');
        var subject = lines[0];

        if (!MyRegex().IsMatch(subject))
            return "The subject must follow Conventional Commits, 'type(scope): summary', for example 'fix: ...'. No code fences or quotes around the message.";
        if (subject.Length > 72)
            return $"The subject is {subject.Length} characters. Keep it at 72 or fewer.";
        if (subject.EndsWith('.'))
            return "Do not end the subject with a period.";
        if (lines.Length > 1 && lines[1].Trim() != "")
            return "Leave one blank line between the subject and the body.";
        if (lines.Skip(2).FirstOrDefault(l => l.Length > 72) is { } longLine)
            return $"Wrap the body at 72 characters. This line is {longLine.Length}: '{longLine}'";
        return null;
    }

    [GeneratedRegex(
        @"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([\w-]+\))?!?: \S"
    )]
    private static partial Regex MyRegex();
}
