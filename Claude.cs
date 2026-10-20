using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

static class Claude
{
    public const string Opus = "claude-opus-5-5";
    public const string Haiku = "claude-haiku-5-5";

    static readonly AnthropicClient Client = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // shortcut: USD per million tokens copied from the October 2026 price list, update when pricing changes.
    static readonly Dictionary<string, (decimal Input, decimal Output)> Prices = new() { [Opus] = (4m, 20m), [Haiku] = (0.10m, 0.50m) };
    static readonly Lock Gate = new();
    static int calls;
    static long inputTokens, outputTokens;
    static decimal cost;

    public static string UsageSummary() =>
        $"{calls} calls, {inputTokens:N0} input tokens, {outputTokens:N0} output tokens, about ${cost:F4}";

    static async Task<Message> Create(string model, MessageCreateParams p)
    {
        var response = await Client.Messages.Create(p);
        var (input, output) = (response.Usage.InputTokens, response.Usage.OutputTokens);
        lock (Gate)
        {
            calls++;
            inputTokens += input;
            outputTokens += output;
            cost += (input * Prices[model].Input + output * Prices[model].Output) / 1_000_000m;
        }
        return response;
    }

    public static async Task<string> Ask(string prompt, string? system = null, string model = Opus)
    {
        var response = await Create(model, Params(prompt, system, model));
        return Text(response);
    }

    public static async Task<T> AskJson<T>(string prompt, object schema)
    {
        var format = new JsonOutputFormat
        {
            Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(schema))!,
        };
        var response = await Create(Opus, Params(prompt, null, Opus, new OutputConfig { Format = format }));
        return JsonSerializer.Deserialize<T>(Text(response), Json)!;
    }

    public static async Task<string> ToolLoop(List<MessageParam> messages, List<Tool> tools, Func<ToolUseBlock, string> execute, string? system = null, int maxSteps = 10)
    {
        for (var step = 1; step <= maxSteps; step++)
        {
            var response = await Create(Opus, new MessageCreateParams
            {
                Model = Opus,
                MaxTokens = 16000,
                System = Sys(system),
                Messages = messages,
                Tools = tools.Select(t => (ToolUnion)t).ToList(),
            });

            messages.Add(new() { Role = Role.Assistant, Content = response.Content.Select(ToParam).ToList() });
            if (response.StopReason != "tool_use") return Text(response);

            List<ContentBlockParam> results = [.. response.Content.Select(b => b.Value).OfType<ToolUseBlock>().Select(use => RunTool(use, execute, step))];
            messages.Add(new() { Role = Role.User, Content = results });
        }
        throw new InvalidOperationException($"Did not finish within {maxSteps} steps.");
    }

    static ContentBlockParam ToParam(ContentBlock block) => block.Value switch
    {
        TextBlock t => new TextBlockParam { Text = t.Text },
        ThinkingBlock t => new ThinkingBlockParam { Thinking = t.Thinking, Signature = t.Signature },
        RedactedThinkingBlock r => new RedactedThinkingBlockParam { Data = r.Data },
        ToolUseBlock u => new ToolUseBlockParam { ID = u.ID, Name = u.Name, Input = u.Input },
        var other => throw new NotSupportedException($"Unexpected content block {other?.GetType().Name}"),
    };

    static ToolResultBlockParam RunTool(ToolUseBlock use, Func<ToolUseBlock, string> execute, int step)
    {
        var input = JsonSerializer.Serialize(use.Input);
        Console.WriteLine($"  [step {step}] {use.Name}({(input.Length > 120 ? input[..120] + "…" : input)})");
        try { return new() { ToolUseID = use.ID, Content = execute(use) }; }
        catch (Exception e) { return new() { ToolUseID = use.ID, Content = e.Message, IsError = true }; }
    }

    static MessageCreateParams Params(string prompt, string? system, string model, OutputConfig? outputConfig = null) => new()
    {
        Model = model,
        MaxTokens = 16000,
        System = Sys(system),
        OutputConfig = outputConfig,
        Messages = [new() { Role = Role.User, Content = prompt }],
    };

    static MessageCreateParamsSystem? Sys(string? system) => system is null ? null : (MessageCreateParamsSystem)system;

    static string Text(Message response)
    {
        if (response.StopReason == "refusal")
            throw new InvalidOperationException($"Claude refused: {response.StopDetails?.Explanation}");
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("Reply was cut off at the token limit.");
        return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }
}
