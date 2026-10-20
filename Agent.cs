using System.Diagnostics;
using System.Text.Json;
using Anthropic.Models.Messages;

// Agent: how many fix-and-test rounds it takes can't be known up front, so the model drives the loop; test output is the ground truth, and a human checkpoint and step cap are the guardrails.
static class Agent
{
    internal static readonly string Root = Path.GetFullPath("workspace");

    static readonly List<Tool> Tools =
    [
        new()
        {
            Name = "list_files",
            Description = "List the source files in the workspace, as paths relative to the workspace root.",
            InputSchema = new() { Properties = new Dictionary<string, JsonElement>(), Required = [] },
        },
        new()
        {
            Name = "read_file",
            Description = "Read a file. Path is relative to the workspace root, e.g. 'src/Invoice.cs'.",
            InputSchema = Schema("path"),
        },
        new()
        {
            Name = "write_file",
            Description = "Replace a file's whole content. Path is relative to the workspace root. Tests.cs is read-only. A human approves every write.",
            InputSchema = Schema("path", "content"),
        },
        new()
        {
            Name = "run_tests",
            Description = "Build the workspace and run its tests. Stops at the first failing test and prints it, or prints build errors.",
            InputSchema = new() { Properties = new Dictionary<string, JsonElement>(), Required = [] },
        },
    ];

    public static async Task Run()
    {
        SeedWorkspace();
        Console.WriteLine($"Workspace: {Root}\n");

        List<MessageParam> messages =
        [
            new() { Role = Role.User, Content = "The tests in this project fail. Fix the code so every test passes. Do not change the tests." },
        ];
        var result = await Claude.ToolLoop(messages, Tools, Execute,
            system: "You are a coding agent in a sandboxed workspace. Run the tests first, read the code the failure points to, make the smallest fix, and run the tests again after every change. Stop when all tests pass and summarise what you fixed.",
            maxSteps: 20);
        Console.WriteLine($"\nAgent: {result}");
    }

    static string Execute(ToolUseBlock call) => call.Name switch
    {
        "list_files" => string.Join("\n", Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f))
            .Where(f => f.Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj"))),
        "read_file" => File.ReadAllText(Resolve(call.Input["path"].GetString()!)),
        "write_file" => Write(call.Input["path"].GetString()!, call.Input["content"].GetString()!),
        "run_tests" => RunTests(),
        _ => throw new ArgumentException($"Unknown tool {call.Name}"),
    };

    static string Write(string path, string content)
    {
        var full = Resolve(path);
        if (Path.GetFileName(full) == "Tests.cs")
            throw new ArgumentException("Tests.cs is read-only. Fix the code under src instead.");
        Console.WriteLine($"\n--- Agent wants to write {path} ---\n{content}\n--- Approve? [y/N] ");
        if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
            return "The user rejected this write. Explain what you intended and stop.";
        File.WriteAllText(full, content);
        return $"Wrote {content.Length} characters to {path}.";
    }

    internal static string RunTests()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "run -v q")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            return "Tests timed out after 2 minutes.";
        }
        var result = $"exit code {process.ExitCode}\n{output.Result}{errors.Result}";
        Console.WriteLine(result);
        return result;
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

    internal static void SeedWorkspace()
    {
        if (Directory.Exists(Root))
        {
            if (!File.Exists(Path.Combine(Root, "Invoicer.csproj")))
                throw new InvalidOperationException($"{Root} exists and is not the agent's workspace. Move or delete it, or run from the project folder.");
            Directory.Delete(Root, recursive: true);
        }
        Directory.CreateDirectory(Path.Combine(Root, "src"));
        File.WriteAllText(Path.Combine(Root, "Invoicer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Root, "src", "Invoice.cs"), """
            using System.Globalization;

            public record Line(string Description, decimal Hours, decimal Rate);

            public static class Invoice
            {
                public const decimal VatRate = 0.23m;

                public static List<Line> Parse(string csv) =>
                    csv.Split('\n')
                       .Skip(1)
                       .Select(row => row.Split(','))
                       .Select(c => new Line(c[0], decimal.Parse(c[1], CultureInfo.InvariantCulture), decimal.Parse(c[2], CultureInfo.InvariantCulture)))
                       .ToList();

                public static decimal Total(IEnumerable<Line> lines) =>
                    lines.Sum(l => Math.Round(l.Hours * l.Rate * (1 + VatRate), 2));
            }
            """);
        File.WriteAllText(Path.Combine(Root, "Tests.cs"), """
            Equal("VAT total is rounded once, not per line", 193.68m, () => Invoice.Total(
                [new("Design", 1.5m, 33.33m), new("Build", 2.25m, 41.10m), new("Review", 0.75m, 19.99m)]));
            Equal("a trailing newline in the CSV is not a line", 1, () => Invoice.Parse("description,hours,rate\nDesign,2,100\n").Count);
            Console.WriteLine("All tests passed.");

            static void Equal<T>(string name, T expected, Func<T> actual)
            {
                try
                {
                    var value = actual();
                    if (Equals(value, expected)) { Console.WriteLine($"PASS {name}"); return; }
                    Console.WriteLine($"FAIL {name}: expected {expected}, got {value}");
                }
                catch (Exception e) { Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
                Environment.Exit(1);
            }
            """);
    }
}
