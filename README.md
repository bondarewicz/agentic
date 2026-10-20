# Building Effective Agents, in C#

Runnable versions of every pattern in Anthropic's
[Building Effective Agents](https://www.anthropic.com/engineering/building-effective-agents),
written against the official `Anthropic` NuGet SDK. One file per pattern, no framework.

## Run

```bash
cp .env.example .env        # then put your real key in .env (gitignored)
dotnet run -- chaining      # or any name from the table
dotnet run --no-launch-profile   # list the examples; plain `dotnet run` starts the first launch profile
```

Program.cs loads `.env` at startup; a key already exported in your shell takes precedence. Each run ends with a line of calls, tokens and estimated cost. In Rider, every example is a launch profile in the run dropdown.

Open `agentic.sln` in Rider to see both projects. Because the solution and the project share a folder, build with `dotnet build agentic.sln`. `dotnet test agentic.sln` runs the offline tests: the `.env` parser, the commit message format check, and the agent's stock check, ordering and workspace guard. Code is formatted with CSharpier, pinned in the tool manifest: run `dotnet tool restore`, then `dotnet csharpier format .`.

| Example | Article section | File | What to look at |
|---|---|---|---|
| `augmented-llm` | Building block: the augmented LLM | `AugmentedLlm.cs` | Two tools, `search_docs` for retrieval and `save_note` for memory. Chat memory is the messages list; saved notes are carried into a fresh chat within the same run |
| `chaining` | Workflow: prompt chaining | `PromptChaining.cs` | Draft, then a plain-code gate, then translate. The gate is the point |
| `routing` | Workflow: routing | `Routing.cs` | A pizzeria inbox: a structured-output classifier sends each message to the host, chef or manager prompt, and easy ones to Haiku, hard ones to Opus |
| `parallel` | Workflow: parallelization | `Parallelization.cs` | `Task.WhenAll` for sectioning, a majority count for voting, then a senior reviewer call turns it all into a short ship-or-block verdict |
| `orchestrator` | Workflow: orchestrator-workers | `OrchestratorWorkers.cs` | The model decides the subtasks; code fans them out and the model merges |
| `evaluator` | Workflow: evaluator-optimizer | `EvaluatorOptimizer.cs` | Writes a commit message for a diff. Code checks the Conventional Commits format first, then Claude grades the judgement criteria, and the feedback loops back until it passes or hits the round limit |
| `agent` | Agents | `Agent.cs` | Luigi's kitchen assistant: checks tonight's bookings against stock, orders what's missing in whole supplier packs with your approval, and works around a sold-out supplier until the stock check passes. Review the CSV files in the gitignored `workspace` folder |

## The shared helper

`Claude.cs` is the only place the SDK is called. Four functions:

- `Ask` is one call, text in, text out.
- `AskJson<T>` is one call constrained to a JSON schema that .NET generates from the record `T`, deserialised back into it. This is how the gates, classifiers and evaluators return something code can branch on.
- `DefineTool` builds a tool definition from a name, a description and its parameters.
- `ToolLoop` is the agent loop: call Claude, run each tool it asked for, send the results back, repeat until it stops asking or hits `maxSteps`. Each tool call and its result are printed as it goes.

Every call is counted for the cost line printed at the end of a run. Every workflow is ordinary C# control flow around these calls. That is the article's main point: workflows are code paths you own, an agent is the loop in `ToolLoop` with the model deciding what comes next.

## Things to try

- Make the gate in `chaining` fail (set the word limit to 20) and watch the chain stop.
- Add a fourth value to the `Category` enum in `routing`, with a specialist prompt, and see the classifier pick it up from the generated schema alone.
- Point `CodeToReview` at `SafeCode` in `parallel` and see the vote and the verdict flip.
- Reject the agent's first order and watch how it explains itself and stops.
- Add a booking or sell out a second ingredient in `Agent.cs` and see the agent need more rounds.
