# Building Effective Agents, in C#

Runnable versions of every pattern in Anthropic's
[Building Effective Agents](https://www.anthropic.com/engineering/building-effective-agents),
written against the official `Anthropic` NuGet SDK. One file per pattern, no framework.

## Run

```bash
cp .env.example .env        # then put your real key in .env (gitignored)
dotnet run -- chaining      # or any name from the table
```

Program.cs loads `.env` at startup; a key already exported in your shell takes precedence.

Open `agentic.sln` in Rider to see both projects. `dotnet test agentic.sln` runs the offline tests: the `.env` parser, the commit message format check, and the agent's stock check, ordering and workspace guard.

| Example | Article section | File | What to look at |
|---|---|---|---|
| `augmented-llm` | Building block: the augmented LLM | `AugmentedLlm.cs` | Two tools (retrieval + save_note), chat memory in the messages list, notes that survive across chats |
| `chaining` | Workflow: prompt chaining | `PromptChaining.cs` | Draft, then a plain-code gate, then translate. The gate is the point |
| `routing` | Workflow: routing | `Routing.cs` | Structured-output classifier picks a specialist prompt and a model size |
| `parallel` | Workflow: parallelization | `Parallelization.cs` | `Task.WhenAll` for sectioning, majority count for voting |
| `orchestrator` | Workflow: orchestrator-workers | `OrchestratorWorkers.cs` | The model decides the subtasks; code fans them out and the model merges |
| `evaluator` | Workflow: evaluator-optimizer | `EvaluatorOptimizer.cs` | Writes a commit message for a diff. Code checks the Conventional Commits format first, then Claude grades the judgement criteria, and the feedback loops back until it passes or hits the round limit |
| `agent` | Agents | `Agent.cs` | Luigi's kitchen assistant: checks tonight's bookings against stock, orders what's missing with your approval, and works around a sold-out supplier until the stock check passes. Review the CSV files in the gitignored `workspace` folder |

## The shared helper

`Claude.cs` is the only place the SDK is called. Three functions:

- `Ask` is one call, text in, text out.
- `AskJson<T>` is one call constrained to a JSON schema, deserialised into a record. This is how the gates, classifiers and evaluators return something code can branch on.
- `ToolLoop` is the agent loop: call Claude, run each tool it asked for, send the results back, repeat until it stops asking or hits `maxSteps`.

Every workflow is ordinary C# control flow around those three calls. That is the article's main point: workflows are code paths you own, an agent is the loop in `ToolLoop` with the model deciding what comes next.

## Things to try

- Make the gate in `chaining` fail (set the word limit to 20) and watch the chain stop.
- Add a fourth route to `routing` and see the classifier pick it up from the enum alone.
- Replace the SQL injection in `parallel` with clean code and see the vote flip.
- Reject the agent's first order and watch how it explains itself and stops.
- Add a booking or sell out a second ingredient in `Agent.cs` and see the agent need more rounds.
