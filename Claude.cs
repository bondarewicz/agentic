using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Anthropic;
using Anthropic.Models.Messages;

namespace Agents;

internal static class Claude
{
    public const string Opus = "claude-opus-5-5";
    public const string Haiku = "claude-haiku-5-5";

    private static readonly AnthropicClient Client = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    // shortcut: USD per million tokens copied from the October 2026 price list, update when pricing changes.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Prices = new()
    {
        [Opus] = (4m, 20m),
        [Haiku] = (0.10m, 0.50m),
    };

    private static readonly Lock Gate = new();
    private static int calls;

    private static long inputTokens,
        outputTokens;

    private static decimal cost;
    private static bool unpriced;

    public static string UsageSummary() =>
        $"{calls} calls, {inputTokens:N0} input tokens, {outputTokens:N0} output tokens, about ${cost:F4}{(unpriced ? " (excludes unpriced models)" : "")}";

    private static async Task<Message> Create(string model, MessageCreateParams p)
    {
        var response = await Client.Messages.Create(p);
        var (input, output) = (response.Usage.InputTokens, response.Usage.OutputTokens);
        lock (Gate)
        {
            calls++;
            inputTokens += input;
            outputTokens += output;
            if (Prices.TryGetValue(model, out var price))
                cost += (input * price.Input + output * price.Output) / 1_000_000m;
            else
                unpriced = true;
        }
        return response;
    }

    public static async Task<string> Ask(string prompt, string? system = null, string model = Opus)
    {
        var response = await Create(model, Params(prompt, system, model));
        return Text(response);
    }

    public static async Task<T> AskJson<T>(string prompt)
    {
        var format = new JsonOutputFormat
        {
            Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                Json.GetJsonSchemaAsNode(
                        typeof(T),
                        new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }
                    )
                    .ToJsonString()
            )!,
        };
        var response = await Create(
            Opus,
            Params(prompt, null, Opus, new OutputConfig { Format = format })
        );
        return JsonSerializer.Deserialize<T>(Text(response), Json)!;
    }

    public static Tool DefineTool(
        string name,
        string description,
        params (string Name, object Schema)[] parameters
    ) =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = new()
            {
                Properties = parameters.ToDictionary(
                    p => p.Name,
                    p => JsonSerializer.SerializeToElement(p.Schema)
                ),
                Required = [.. parameters.Select(p => p.Name)],
            },
        };

    public static async Task<string> ToolLoop(
        List<MessageParam> messages,
        List<Tool> tools,
        Func<ToolUseBlock, string> execute,
        string? system = null,
        int maxSteps = 10
    )
    {
        for (var step = 1; step <= maxSteps; step++)
        {
            var response = await Create(
                Opus,
                new MessageCreateParams
                {
                    Model = Opus,
                    MaxTokens = 16000,
                    System = Sys(system),
                    Messages = messages,
                    Tools = tools.Select(t => (ToolUnion)t).ToList(),
                }
            );

            messages.Add(
                new() { Role = Role.Assistant, Content = response.Content.Select(ToParam).ToList() }
            );
            if (response.StopReason != StopReason.ToolUse)
                return Text(response);

            List<ContentBlockParam> results =
            [
                .. response
                    .Content.Select(b => b.Value)
                    .OfType<ToolUseBlock>()
                    .Select(use => RunTool(use, execute, step)),
            ];
            messages.Add(new() { Role = Role.User, Content = results });
        }
        throw new InvalidOperationException($"Did not finish within {maxSteps} steps.");
    }

    private static ContentBlockParam ToParam(ContentBlock block) =>
        block.Value switch
        {
            TextBlock t => new TextBlockParam { Text = t.Text },
            ThinkingBlock t => new ThinkingBlockParam
            {
                Thinking = t.Thinking,
                Signature = t.Signature,
            },
            RedactedThinkingBlock r => new RedactedThinkingBlockParam { Data = r.Data },
            ToolUseBlock u => new ToolUseBlockParam
            {
                ID = u.ID,
                Name = u.Name,
                Input = u.Input,
            },
            var other => throw new NotSupportedException(
                $"Unexpected content block {other?.GetType().Name}"
            ),
        };

    private static ToolResultBlockParam RunTool(
        ToolUseBlock use,
        Func<ToolUseBlock, string> execute,
        int step
    )
    {
        Console.WriteLine(
            $"\n  [step {step}] Claude tool {use.Name}: {Shorten(string.Join(", ", use.Input.Select(kv => $"{kv.Key}={kv.Value}")))}"
        );
        try
        {
            var result = execute(use);
            Console.WriteLine($"           -> {Shorten(result)}");
            return new() { ToolUseID = use.ID, Content = result };
        }
        catch (Exception e)
        {
            Console.WriteLine($"           -> error: {Shorten(e.Message)}");
            return new()
            {
                ToolUseID = use.ID,
                Content = e.Message,
                IsError = true,
            };
        }
    }

    private static string Shorten(string text)
    {
        text = text.Trim().ReplaceLineEndings(" | ");
        return text.Length > 120 ? text[..120] + "…" : text;
    }

    private static MessageCreateParams Params(
        string prompt,
        string? system,
        string model,
        OutputConfig? outputConfig = null
    ) =>
        new()
        {
            Model = model,
            MaxTokens = 16000,
            System = Sys(system),
            OutputConfig = outputConfig,
            Messages = [new() { Role = Role.User, Content = prompt }],
        };

    private static MessageCreateParamsSystem? Sys(string? system) =>
        system is null ? null : (MessageCreateParamsSystem)system;

    private static string Text(Message response)
    {
        if (response.StopReason == StopReason.Refusal)
            throw new InvalidOperationException(
                $"Claude refused: {response.StopDetails?.Explanation}"
            );

        return response.StopReason == StopReason.MaxTokens
            ? throw new InvalidOperationException("Reply was cut off at the token limit.")
            : string.Concat(
                response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)
            );
    }
}
