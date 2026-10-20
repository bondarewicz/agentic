using System.Text.Json;
using Anthropic.Models.Messages;

static class AugmentedLlm
{
    static readonly string[] Docs =
    [
        "Runbook: to rotate the API key, open Console > Settings > Keys, create a new key, deploy it, then revoke the old one.",
        "Runbook: production deploys happen only Tuesday to Thursday, 10:00 to 16:00 CET.",
        "Policy: every service must expose /healthz and respond within 200 ms.",
    ];
    static readonly List<string> Notes = [];

    static readonly List<Tool> Tools =
    [
        new()
        {
            Name = "search_docs",
            Description = "Search the internal runbooks and policies. Returns every line containing the query word.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonSerializer.SerializeToElement(new { type = "string", description = "A single keyword, e.g. 'deploy'" }),
                },
                Required = ["query"],
            },
        },
        new()
        {
            Name = "save_note",
            Description = "Remember a fact about the user for future conversations.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["note"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                },
                Required = ["note"],
            },
        },
    ];

    public static async Task Run()
    {
        Console.WriteLine("== Chat 1 ==");
        List<MessageParam> chat = [];
        await Turn(chat, "I'm on the platform team. How do I rotate the API key, and when can I deploy the new one?");
        await Turn(chat, "Shorten that to one sentence.");

        Console.WriteLine("\n== Chat 2: fresh conversation, only the saved notes survive ==");
        await Turn([], "Which team am I on?");
    }

    static async Task Turn(List<MessageParam> chat, string userMessage)
    {
        Console.WriteLine($"\nUser: {userMessage}");
        chat.Add(new() { Role = Role.User, Content = userMessage });
        var system = "You are an internal assistant. Use search_docs to answer from the runbooks, and save_note when you learn something about the user worth remembering."
                   + (Notes.Count > 0 ? "\n\nNotes from earlier conversations:\n- " + string.Join("\n- ", Notes) : "");
        var answer = await Claude.ToolLoop(chat, Tools, Execute, system);
        Console.WriteLine($"Claude: {answer}");
    }

    static string Execute(ToolUseBlock call) => call.Name switch
    {
        "search_docs" => Search(call.Input["query"].GetString()!),
        "save_note" => Save(call.Input["note"].GetString()!),
        _ => throw new ArgumentException($"Unknown tool {call.Name}"),
    };

    static string Search(string query)
    {
        var hits = Docs.Where(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return hits.Count > 0 ? string.Join("\n", hits) : "No matching documents.";
    }

    static string Save(string note)
    {
        Notes.Add(note);
        return "Saved.";
    }
}
