static class Routing
{
    record Route(string Category, string Difficulty, string Reason);

    static readonly object RouteSchema = new
    {
        type = "object",
        properties = new
        {
            category = new { type = "string", @enum = new[] { "refund", "technical", "general" } },
            difficulty = new { type = "string", @enum = new[] { "easy", "hard" } },
            reason = new { type = "string" },
        },
        required = new[] { "category", "difficulty", "reason" },
        additionalProperties = false,
    };

    static readonly Dictionary<string, string> Specialists = new()
    {
        ["refund"] = "You are a billing specialist. Be precise about the refund policy: 30 days, original payment method, 5 business days to appear.",
        ["technical"] = "You are a senior support engineer. Ask for logs or versions only if necessary; otherwise give concrete steps.",
        ["general"] = "You are a friendly front-desk assistant. Opening hours are 9-17 Monday to Friday. Keep answers short.",
    };

    public static async Task Run()
    {
        string[] inbox =
        [
            "What are your opening hours?",
            "I was charged twice for order #4411, I want my money back.",
            "After upgrading to 3.2 the app crashes on startup with NullReferenceException in ConfigLoader. Worked fine on 3.1.",
        ];

        foreach (var message in inbox)
        {
            var route = await Claude.AskJson<Route>($"Classify this customer message.\n\n{message}", RouteSchema);
            var model = route.Difficulty == "easy" ? Claude.Haiku : Claude.Opus;
            Console.WriteLine($"\nMessage: {message}\n  -> {route.Category} / {route.Difficulty} via {model} ({route.Reason})");

            var reply = await Claude.Ask(message, Specialists[route.Category], model);
            Console.WriteLine($"  Reply: {reply}");
        }
    }
}
