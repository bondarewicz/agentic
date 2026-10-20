using Anthropic.Models.Messages;

// Agent: what's short and which supplier can deliver is only discovered along the way, so the model drives the loop; the stock check is the ground truth, and order approval and a step cap are the guardrails.
static class Agent
{
    internal static readonly string Root = Path.GetFullPath("workspace");
    static readonly string[] Files = ["bookings.csv", "recipes.csv", "stock.csv", "orders.csv"];
    static readonly string[] Suppliers = ["Fresco", "Dairy Dan"];
    static readonly HashSet<(string Supplier, string Ingredient)> SoldOut = [("Fresco", "mascarpone")];

    static readonly List<Tool> Tools =
    [
        Claude.DefineTool("read_file", "Read one of the kitchen's files: bookings.csv (tonight's pre-ordered portions per dish), recipes.csv (grams of each ingredient per portion), stock.csv (grams in the fridge), orders.csv (orders placed today).",
            ("name", new { type = "string", @enum = Files })),
        Claude.DefineTool("check_kitchen", "Compare tonight's bookings with the stock and list every ingredient that is short, in grams. This is the source of truth for whether the kitchen is ready."),
        Claude.DefineTool("place_order", "Order an ingredient for delivery before service. Fresco is the usual supplier. Use ingredient names exactly as in recipes.csv. A human approves every order.",
            ("supplier", new { type = "string", @enum = Suppliers }),
            ("ingredient", new { type = "string" }),
            ("grams", new { type = "integer", minimum = 1 })),
    ];

    public static async Task Run()
    {
        SeedWorkspace();
        Console.WriteLine($"Workspace: {Root}\n");

        List<MessageParam> messages =
        [
            new() { Role = Role.User, Content = "Make sure the kitchen can cook every pre-ordered dish tonight. Order only what is missing." },
        ];
        var result = await Claude.ToolLoop(messages, Tools, Execute,
            system: "You are the kitchen assistant at Luigi's, a small pizzeria. Look before you act: read the files and run check_kitchen before ordering, and run it again after ordering. If a supplier cannot deliver, find another way. Stop when check_kitchen says everything is in stock, and summarise what you ordered.",
            maxSteps: 15);
        Console.WriteLine($"\nAgent: {result}");
    }

    static string Execute(ToolUseBlock call) => call.Name switch
    {
        "read_file" => ReadFile(call.Input["name"].GetString()!),
        "check_kitchen" => Shortages(),
        "place_order" => PlaceOrder(call.Input["supplier"].GetString()!, call.Input["ingredient"].GetString()!, call.Input["grams"].GetInt32()),
        _ => throw new ArgumentException($"Unknown tool {call.Name}"),
    };

    static string ReadFile(string name) =>
        Files.Contains(name) ? File.ReadAllText(Path.Combine(Root, name)) : throw new ArgumentException($"Unknown file '{name}'. Use one of: {string.Join(", ", Files)}.");

    internal static string PlaceOrder(string supplier, string ingredient, int grams)
    {
        ingredient = ingredient.Trim().ToLowerInvariant();
        if (!Suppliers.Contains(supplier))
            throw new ArgumentException($"Unknown supplier '{supplier}'. Use one of: {string.Join(", ", Suppliers)}.");
        if (!Csv("recipes.csv").Any(r => r[1] == ingredient))
            throw new ArgumentException($"Unknown ingredient '{ingredient}'. Use a name exactly as in recipes.csv.");
        if (grams <= 0)
            throw new ArgumentException("Order at least 1 gram.");
        if (SoldOut.Contains((supplier, ingredient)))
            return $"{supplier}: {ingredient} is sold out today.";

        Console.WriteLine($"\n--- Agent wants to order {grams} g of {ingredient} from {supplier}. Approve? [y/N] ");
        if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
            return "The user rejected this order. Explain what you intended and stop.";

        File.AppendAllText(Path.Combine(Root, "orders.csv"), $"{supplier},{ingredient},{grams}\n");
        var stock = Csv("stock.csv").ToDictionary(r => r[0], r => int.Parse(r[1]));
        stock[ingredient] = stock.GetValueOrDefault(ingredient) + grams;
        File.WriteAllLines(Path.Combine(Root, "stock.csv"), ["ingredient,grams", .. stock.Select(s => $"{s.Key},{s.Value}")]);
        return $"Ordered {grams} g of {ingredient} from {supplier}. It arrives before service and stock.csv is updated.";
    }

    internal static string Shortages()
    {
        var stock = Csv("stock.csv").ToDictionary(r => r[0], r => int.Parse(r[1]));
        var portions = Csv("bookings.csv").ToDictionary(r => r[0], r => int.Parse(r[1]));
        var shortages = Csv("recipes.csv")
            .GroupBy(r => r[1], r => int.Parse(r[2]) * portions.GetValueOrDefault(r[0]))
            .Select(g => (Ingredient: g.Key, Missing: g.Sum() - stock.GetValueOrDefault(g.Key)))
            .Where(x => x.Missing > 0)
            .Select(x => $"{x.Ingredient}: {x.Missing} g short")
            .ToList();
        return shortages.Count == 0 ? "Everything for tonight's bookings is in stock." : "Short:\n" + string.Join("\n", shortages);
    }

    static IEnumerable<string[]> Csv(string name) =>
        File.ReadLines(Path.Combine(Root, name)).Skip(1).Where(l => l.Trim() != "").Select(l => l.Split(','));

    internal static void SeedWorkspace()
    {
        if (Directory.Exists(Root))
        {
            if (!File.Exists(Path.Combine(Root, "bookings.csv")))
                throw new InvalidOperationException($"{Root} exists and is not the agent's workspace. Move or delete it, or run from the project folder.");
            Directory.Delete(Root, recursive: true);
        }
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "bookings.csv"), "dish,portions\nmargherita,8\npesto pasta,6\ntiramisu,5\n");
        File.WriteAllText(Path.Combine(Root, "recipes.csv"),
            "dish,ingredient,grams_per_portion\nmargherita,flour,200\nmargherita,mozzarella,120\nmargherita,tomato sauce,80\n" +
            "pesto pasta,pasta,120\npesto pasta,pesto,60\ntiramisu,mascarpone,100\ntiramisu,ladyfingers,60\n");
        File.WriteAllText(Path.Combine(Root, "stock.csv"),
            "ingredient,grams\nflour,5000\nmozzarella,400\ntomato sauce,1000\npasta,2000\npesto,500\nmascarpone,0\nladyfingers,1000\n");
        File.WriteAllText(Path.Combine(Root, "orders.csv"), "supplier,ingredient,grams\n");
    }
}
