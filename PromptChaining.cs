// Prompt chaining: write-then-translate splits into fixed steps, and a cheap code gate stops a bad draft before the second call.
static class PromptChaining
{
    public static async Task Run()
    {
        const string product = "Ponytail, a CLI tool that keeps your codebase small by flagging code nobody asked for";

        var copy = await Claude.Ask($"Write marketing copy for: {product}. At most 60 words, mention the product name. Output only the copy.");
        Console.WriteLine($"Step 1 - draft:\n{copy}\n");

        var words = copy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var mentionsProduct = copy.Contains("Ponytail", StringComparison.OrdinalIgnoreCase);
        if (words > 60 || !mentionsProduct)
        {
            Console.WriteLine($"Gate failed ({words} words, mentions product: {mentionsProduct}). Stopping the chain.");
            return;
        }
        Console.WriteLine($"Gate passed ({words} words).\n");

        var translated = await Claude.Ask($"Translate this marketing copy into Polish. Keep the tone. Output only the translation.\n\n{copy}");
        Console.WriteLine($"Step 2 - Polish:\n{translated}");
    }
}
