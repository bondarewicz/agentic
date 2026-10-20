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

`dotnet test tests` runs the offline tests for the agent's sandbox check and the `.env` parser.

| Example | Article section | File | What to look at |
|---|---|---|---|
| `augmented-llm` | Building block: the augmented LLM | `AugmentedLlm.cs` | Two tools (retrieval + save_note), chat memory in the messages list, notes that survive across chats |
| `chaining` | Workflow: prompt chaining | `PromptChaining.cs` | Draft, then a plain-code gate, then translate. The gate is the point |
| `routing` | Workflow: routing | `Routing.cs` | Structured-output classifier picks a specialist prompt and a model size |
| `parallel` | Workflow: parallelization | `Parallelization.cs` | `Task.WhenAll` for sectioning, majority count for voting |
| `orchestrator` | Workflow: orchestrator-workers | `OrchestratorWorkers.cs` | The model decides the subtasks; code fans them out and the model merges |
| `evaluator` | Workflow: evaluator-optimizer | `EvaluatorOptimizer.cs` | Generate, grade against written criteria, feed the feedback back, stop on pass or round limit |
| `agent` | Agents | `Agent.cs` | Tool loop over a sandboxed temp folder, human approval on every write, max step count |

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
- Give the agent a tool it should not have (`delete_file`) and watch the human checkpoint earn its keep.
- Raise `maxSteps` in `Agent.cs` and remove the "verify" sentence from the task to see how the stopping condition changes behaviour.
