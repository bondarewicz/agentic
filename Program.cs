using Agents;

var envFile = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envFile))
    foreach (var (key, value) in DotEnv.Parse(File.ReadLines(envFile)))
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);

var examples = new (string Name, string What, Func<Task> Run)[]
{
    ("augmented-llm", "Building block: an LLM with retrieval, tools and memory", AugmentedLlm.Run),
    ("chaining", "Workflow: prompt chaining with a programmatic gate", PromptChaining.Run),
    ("routing", "Workflow: classify, then dispatch to a specialist prompt and model", Routing.Run),
    ("parallel", "Workflow: parallelization, sectioning and voting", Parallelization.Run),
    (
        "orchestrator",
        "Workflow: orchestrator plans, workers execute, orchestrator merges",
        OrchestratorWorkers.Run
    ),
    (
        "evaluator",
        "Workflow: generate, evaluate, refine until it passes",
        Agents.EvaluatorOptimizer.Run
    ),
    ("agent", "Agent: kitchen assistant that orders missing stock with your approval", Agent.Run),
};

var pick = examples.FirstOrDefault(e =>
    e.Name.Equals(args.ElementAtOrDefault(0), StringComparison.OrdinalIgnoreCase)
);
if (pick.Run is null)
{
    Console.WriteLine("Usage: dotnet run -- <example>\n");
    foreach (var e in examples)
        Console.WriteLine($"  {e.Name, -14} {e.What}");
    return 1;
}

Console.WriteLine($"### {pick.What}\n");
try
{
    await pick.Run();
}
finally
{
    Console.WriteLine($"\n{Claude.UsageSummary()}");
}
return 0;

namespace Agents
{
    internal static class DotEnv
    {
        public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
        {
            return from line in lines
                select line.Split('=', 2) into kv
                where kv.Length == 2 && !kv[0].TrimStart().StartsWith('#')
                let key = kv[0].Trim()
                select (
                    key.StartsWith("export ") ? key[7..].TrimStart() : key,
                    kv[1].Trim().Trim('"', '\'')
                );
        }
    }
}
