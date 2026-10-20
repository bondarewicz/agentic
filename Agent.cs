using System.Text.Json;
using Anthropic.Models.Messages;

// Agent: how many files to read and when it is done can't be hardcoded, so the model drives the loop, with a human checkpoint and step cap as guardrails.
static class Agent
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "agents-workspace");

    static readonly List<Tool> Tools =
    [
        new()
        {
            Name = "list_files",
            Description = "List all files in the workspace, as paths relative to the workspace root.",
            InputSchema = new() { Properties = new Dictionary<string, JsonElement>(), Required = [] },
        },
        new()
        {
            Name = "read_file",
            Description = "Read a file. Path is relative to the workspace root, e.g. 'src/Program.cs'.",
            InputSchema = Schema("path"),
        },
        new()
        {
            Name = "write_file",
            Description = "Create or overwrite a file. Path is relative to the workspace root. A human approves every write.",
            InputSchema = Schema("path", "content"),
        },
    ];

    public static async Task Run()
    {
        SeedWorkspace();
        Console.WriteLine($"Workspace: {Root}\n");

        List<MessageParam> messages =
        [
            new() { Role = Role.User, Content = "Create SUMMARY.md: one paragraph on what this project is, then a checklist of every TODO you find in the code. Read the file back after writing it to verify." },
        ];
        var result = await Claude.ToolLoop(messages, Tools, Execute,
            system: "You are a coding agent in a sandboxed workspace. Look before you act: list and read files before writing. When the task is complete, reply with a short summary of what you did.",
            maxSteps: 15);
        Console.WriteLine($"\nAgent: {result}");
    }

    static string Execute(ToolUseBlock call) => call.Name switch
    {
        "list_files" => string.Join("\n", Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Root, f))),
        "read_file" => File.ReadAllText(Resolve(call.Input["path"].GetString()!)),
        "write_file" => Write(call.Input["path"].GetString()!, call.Input["content"].GetString()!),
        _ => throw new ArgumentException($"Unknown tool {call.Name}"),
    };

    static string Write(string path, string content)
    {
        var full = Resolve(path);
        Console.WriteLine($"\n--- Agent wants to write {path} ---\n{content}\n--- Approve? [y/N] ");
        if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
            return "The user rejected this write. Explain what you intended and stop.";
        File.WriteAllText(full, content);
        return $"Wrote {content.Length} characters to {path}.";
    }

    internal static string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (full != Root && !full.StartsWith(Root + Path.DirectorySeparatorChar))
            throw new ArgumentException($"'{relative}' is outside the workspace. Use a path relative to the workspace root.");
        return full;
    }

    static InputSchema Schema(params string[] stringProps) => new()
    {
        Properties = stringProps.ToDictionary(p => p, _ => JsonSerializer.SerializeToElement(new { type = "string" })),
        Required = [.. stringProps],
    };

    static void SeedWorkspace()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        Directory.CreateDirectory(Path.Combine(Root, "src"));
        File.WriteAllText(Path.Combine(Root, "README.md"), "# Invoicer\nTiny CLI that turns a CSV of hours into a PDF invoice.\n");
        File.WriteAllText(Path.Combine(Root, "src", "Program.cs"), "// TODO: read the CSV path from args instead of hardcoding\nvar rows = Csv.Load(\"hours.csv\");\n// TODO: handle an empty CSV\nPdf.Render(rows);\n");
        File.WriteAllText(Path.Combine(Root, "src", "Pdf.cs"), "static class Pdf\n{\n    // TODO: add VAT line\n    public static void Render(IEnumerable<Row> rows) { }\n}\n");
    }
}
