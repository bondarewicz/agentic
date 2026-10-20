// Routing: bookings, menu questions and complaints each need different knowledge, and an easy question doesn't need the strongest model.
static class Routing
{
    record Route(string Category, string Difficulty, string Reason);

    static readonly object RouteSchema = new
    {
        type = "object",
        properties = new
        {
            category = new { type = "string", @enum = new[] { "booking", "menu", "complaint" } },
            difficulty = new { type = "string", @enum = new[] { "easy", "hard" } },
            reason = new { type = "string" },
        },
        required = new[] { "category", "difficulty", "reason" },
        additionalProperties = false,
    };

    static readonly Dictionary<string, string> Specialists = new()
    {
        ["booking"] = "You are the host at Luigi's, a small pizzeria. Open Tuesday to Sunday, 12:00 to 22:00, closed Mondays. Tables seat 2, 4 or 6. Step-free entrance and an accessible toilet. Keep answers short and friendly.",
        ["menu"] = "You are the chef at Luigi's. Menu and allergens: Margherita (gluten, milk), pesto pasta (gluten, milk, pine nuts, pesto made in-house), tiramisu (gluten, eggs, milk). The kitchen handles nuts, so traces are possible in any dish. Never guess about allergens.",
        ["complaint"] = "You are the manager at Luigi's. Apologise once, sincerely. For a late or cold delivery, offer a full refund for the dish or a free replacement, and let the customer choose. Keep it short.",
    };

    public static async Task Run()
    {
        string[] inbox =
        [
            "Are you open on Sunday?",
            "Table for six on Friday at eight, please. One of us uses a wheelchair.",
            "Is the pesto pasta safe for someone with a nut allergy?",
            "My pizza arrived cold and an hour late.",
        ];

        foreach (var message in inbox)
        {
            var route = await Claude.AskJson<Route>($"Classify this message to a pizzeria. Hard means a wrong answer could cause real harm or needs careful judgement.\n\n{message}", RouteSchema);
            var model = route.Difficulty == "easy" ? Claude.Haiku : Claude.Opus;
            Console.WriteLine($"\nMessage: {message}\n  -> {route.Category} / {route.Difficulty} via {model} ({route.Reason})");

            var reply = await Claude.Ask(message, Specialists[route.Category], model);
            Console.WriteLine($"  Reply: {reply}");
        }
    }
}
