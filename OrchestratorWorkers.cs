// Orchestrator-workers: the brief's sections can't be known in advance, so the model plans them instead of the code.

namespace Agents;

internal static class OrchestratorWorkers
{
    private record Plan(Subtask[] Subtasks);

    private record Subtask(string Title, string Instructions);

    public static async Task Run()
    {
        const string task =
            "Write a one-page brief for a 4-person team on moving a .NET monolith to a modular monolith first, microservices later.";
        Console.WriteLine($"Task:\n{task}");

        var plan = await Claude.AskJson<Plan>(
            $"Break this task into 2-4 independent sections a writer could draft separately. Give each precise instructions.\n\nTask: {task}"
        );
        if (plan.Subtasks.Length == 0)
            throw new InvalidOperationException("Orchestrator produced no subtasks.");
        Console.WriteLine("Plan:");
        foreach (var s in plan.Subtasks)
            Console.WriteLine($"  - {s.Title}: {s.Instructions}");

        var sections = await Task.WhenAll(
            plan.Subtasks.Select(async s =>
                $"## {s.Title}\n"
                + await Claude.Ask(
                    $"Write this section of a brief, 120 words max.\nTitle: {s.Title}\nInstructions: {s.Instructions}\nOverall task: {task}"
                )
            )
        );

        var brief = await Claude.Ask(
            $"Merge these sections into one coherent brief. Remove repetition, keep it under 350 words.\n\n{string.Join("\n\n", sections)}"
        );
        Console.WriteLine($"\nFinal brief:\n{brief}");
    }
}
