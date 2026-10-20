static class EvaluatorOptimizer
{
    record Verdict(bool Pass, string Feedback);

    static readonly object VerdictSchema = new
    {
        type = "object",
        properties = new { pass = new { type = "boolean" }, feedback = new { type = "string" } },
        required = new[] { "pass", "feedback" },
        additionalProperties = false,
    };

    const string Source = "Hope is the thing with feathers that perches in the soul, and sings the tune without the words, and never stops at all.";
    const string Criteria = "Criteria: 1) meaning preserved, 2) natural, idiomatic Polish, not word-for-word, 3) the image of the bird and its song survives, 4) the rhythm feels like verse.";

    public static async Task Run()
    {
        var attempt = "";
        Verdict? verdict = null;

        for (var round = 1; round <= 3; round++)
        {
            attempt = verdict is null
                ? await Claude.Ask($"Translate into Polish. Output only the translation.\n\n{Source}")
                : await Claude.Ask($"Revise your Polish translation using the editor's feedback. Output only the new translation.\n\nOriginal: {Source}\nPrevious translation: {attempt}\nFeedback: {verdict.Feedback}");
            Console.WriteLine($"\nRound {round} - attempt:\n{attempt}");

            verdict = await Claude.AskJson<Verdict>(
                $"You are a demanding literary editor. Evaluate this Polish translation against the criteria. Pass only if all four hold.\n{Criteria}\n\nOriginal: {Source}\nTranslation: {attempt}",
                VerdictSchema);
            Console.WriteLine($"Evaluator: {(verdict.Pass ? "PASS" : "REVISE")} - {verdict.Feedback}");
            if (verdict.Pass) return;
        }
        Console.WriteLine("\nStopped after 3 rounds without a pass.");
    }
}
