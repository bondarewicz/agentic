var envFile = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envFile))
    foreach (var (key, value) in DotEnv.Parse(File.ReadLines(envFile)))
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);

var examples = new (string Name, string What, Func<Task> Run)[]
{
    ("augmented-llm", "Building block: one LLM call with retrieval, tools and memory", AugmentedLlm.Run),
    ("chaining",      "Workflow: prompt chaining with a programmatic gate", PromptChaining.Run),
    ("routing",       "Workflow: classify, then dispatch to a specialist prompt and model", Routing.Run),
    ("parallel",      "Workflow: parallelization, sectioning and voting", Parallelization.Run),
    ("orchestrator",  "Workflow: orchestrator plans, workers execute, orchestrator merges", OrchestratorWorkers.Run),
    ("evaluator",     "Workflow: generate, evaluate, refine until it passes", EvaluatorOptimizer.Run),
    ("agent",         "Agent: tool loop over a sandboxed workspace with a human checkpoint", Agent.Run),
};

var pick = examples.FirstOrDefault(e => e.Name.Equals(args.ElementAtOrDefault(0), StringComparison.OrdinalIgnoreCase));
if (pick.Run is null)
{
    Console.WriteLine("Usage: dotnet run -- <example>\n");
    foreach (var e in examples) Console.WriteLine($"  {e.Name,-14} {e.What}");
    return 1;
}

Console.WriteLine($"### {pick.What}\n");
await pick.Run();
return 0;

static class DotEnv
{
    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var kv = line.Split('=', 2);
            if (kv.Length == 2 && !kv[0].TrimStart().StartsWith('#'))
                yield return (kv[0].Trim(), kv[1].Trim().Trim('"'));
        }
    }
}
