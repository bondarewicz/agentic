using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

static class Claude
{
    public const string Opus = "claude-opus-5-5";
    public const string Haiku = "claude-haiku-5-5";

    static readonly AnthropicClient Client = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<string> Ask(string prompt, string? system = null, string model = Opus)
    {
        var response = await Client.Messages.Create(Params(prompt, system, model));
        return Text(response);
    }

    public static async Task<T> AskJson<T>(string prompt, object schema)
    {
        var format = new JsonOutputFormat
        {
            Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(schema))!,
        };
        var response = await Client.Messages.Create(Params(prompt, null, Opus, new OutputConfig { Format = format }));
        return JsonSerializer.Deserialize<T>(Text(response), Json)!;
    }

    public static async Task<string> ToolLoop(List<MessageParam> messages, List<Tool> tools, Func<ToolUseBlock, string> execute, string? system = null, int maxSteps = 10)
    {
        for (var step = 1; step <= maxSteps; step++)
        {
            var response = await Client.Messages.Create(new MessageCreateParams
            {
                Model = Opus,
                MaxTokens = 16000,
                System = Sys(system),
                Messages = messages,
                Tools = tools.Select(t => (ToolUnion)t).ToList(),
            });

            List<ContentBlockParam> assistant = [];
            List<ContentBlockParam> results = [];
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                    assistant.Add(new TextBlockParam { Text = text.Text });
                else if (block.TryPickThinking(out var thinking))
                    assistant.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out var redacted))
                    assistant.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out var use))
                {
                    assistant.Add(new ToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                    var input = JsonSerializer.Serialize(use.Input);
                    Console.WriteLine($"  [step {step}] {use.Name}({(input.Length > 120 ? input[..120] + "…" : input)})");
                    try { results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = execute(use) }); }
                    catch (Exception e) { results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = e.Message, IsError = true }); }
                }
            }
            messages.Add(new() { Role = Role.Assistant, Content = assistant });
            if (response.StopReason != "tool_use") return Text(response);
            messages.Add(new() { Role = Role.User, Content = results });
        }
        throw new InvalidOperationException($"Did not finish within {maxSteps} steps.");
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
