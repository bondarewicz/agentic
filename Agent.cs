using Anthropic.Models.Messages;

// Agent: what's short and which supplier can deliver is only discovered along the way, so the model drives the loop; the stock check is the ground truth, and order approval and a step cap are the guardrails.
namespace Agents;

internal static class Agent
{
    private static readonly string Root = Path.GetFullPath("workspace");
    private static readonly string[] Files =
    [
        "bookings.csv",
        "recipes.csv",
        "stock.csv",
        "suppliers.csv",
        "orders.csv",
    ];
    private static readonly string[] Suppliers = ["Fresco", "Dairy Dan"];
    private static readonly HashSet<(string Supplier, string Ingredient)> SoldOut =
    [
        ("Fresco", "gorgonzola"),
    ];

    private static readonly List<Tool> Tools =
    [
        Claude.DefineTool(
            "read_file",
            "Read one of the kitchen's files: bookings.csv (pizzas expected tonight per type), recipes.csv (grams of each ingredient per pizza), stock.csv (grams in the fridge), suppliers.csv (what each supplier sells and its pack size in grams), orders.csv (orders placed today).",
            ("name", new { type = "string", @enum = Files })
        ),
        Claude.DefineTool(
            "check_kitchen",
            "Compare tonight's bookings with the stock and list every ingredient that is short, in grams. This is the source of truth for whether the kitchen is ready."
        ),
        Claude.DefineTool(
            "place_order",
            "Order whole packs of an ingredient for delivery before service. Fresco is the usual supplier. Pack sizes are in suppliers.csv. A human approves every order.",
            ("supplier", new { type = "string", @enum = Suppliers }),
            ("ingredient", new { type = "string" }),
            ("packs", new { type = "integer", minimum = 1 })
        ),
    ];

    public static async Task Run()
    {
        SeedWorkspace();
        Console.WriteLine($"Workspace: {Root}\n");

        List<MessageParam> messages =
        [
            new()
            {
                Role = Role.User,
                Content =
                    "Make sure the kitchen can cook every pizza expected tonight. Order only what is missing.",
            },
        ];
        var result = await Claude.ToolLoop(
            messages,
            Tools,
            Execute,
            system: "You are the kitchen assistant at Luigi's, a small pizzeria. Look before you act: read the files and run check_kitchen before ordering, and run it again after ordering. If a supplier cannot deliver, find another way. Stop when check_kitchen says everything is in stock, and summarise what you ordered.",
            maxSteps: 15
        );
        Console.WriteLine($"\nAgent: {result}");
    }

    private static string Execute(ToolUseBlock call) =>
        call.Name switch
        {
            "read_file" => ReadFile(call.Input["name"].GetString()!),
            "check_kitchen" => Shortages(),
            "place_order" => PlaceOrder(
                call.Input["supplier"].GetString()!,
                call.Input["ingredient"].GetString()!,
                call.Input["packs"].GetInt32()
            ),
            _ => throw new ArgumentException($"Unknown tool {call.Name}"),
        };

    private static string ReadFile(string name) =>
        Files.Contains(name)
            ? File.ReadAllText(Path.Combine(Root, name))
            : throw new ArgumentException(
                $"Unknown file '{name}'. Use one of: {string.Join(", ", Files)}."
            );

    internal static string PlaceOrder(string supplier, string ingredient, int packs)
    {
        ingredient = ingredient.Trim().ToLowerInvariant();
        if (!Suppliers.Contains(supplier))
            throw new ArgumentException(
                $"Unknown supplier '{supplier}'. Use one of: {string.Join(", ", Suppliers)}."
            );
        var pack = Csv("suppliers.csv").FirstOrDefault(r => r[0] == supplier && r[1] == ingredient);
        if (pack is null)
            throw new ArgumentException(
                $"{supplier} does not sell '{ingredient}'. See suppliers.csv."
            );
        if (packs <= 0)
            throw new ArgumentException("Order at least 1 pack.");
        if (SoldOut.Contains((supplier, ingredient)))
            return $"{supplier}: {ingredient} is sold out today.";

        var grams = packs * int.Parse(pack[2]);
        Console.WriteLine(
            $"\n--- Agent wants to order {packs} x {pack[2]} g of {ingredient} ({grams} g) from {supplier}. Approve? [y/N] "
        );
        if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
            return "The user rejected this order. Explain what you intended and stop.";

        File.AppendAllText(Path.Combine(Root, "orders.csv"), $"{supplier},{ingredient},{grams}\n");
        var stock = Csv("stock.csv").ToDictionary(r => r[0], r => int.Parse(r[1]));
        stock[ingredient] = stock.GetValueOrDefault(ingredient) + grams;
        File.WriteAllLines(
            Path.Combine(Root, "stock.csv"),
            ["ingredient,grams", .. stock.Select(s => $"{s.Key},{s.Value}")]
        );
        return $"Ordered {packs} x {pack[2]} g ({grams} g) of {ingredient} from {supplier}. It arrives before service and stock.csv is updated.";
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
        return shortages.Count == 0
            ? "Everything for tonight's bookings is in stock."
            : "Short:\n" + string.Join("\n", shortages);
    }

    private static IEnumerable<string[]> Csv(string name) =>
        File.ReadLines(Path.Combine(Root, name))
            .Skip(1)
            .Where(l => l.Trim() != "")
            .Select(l => l.Split(','));

    internal static void SeedWorkspace()
    {
        if (Directory.Exists(Root))
        {
            if (!File.Exists(Path.Combine(Root, "bookings.csv")))
                throw new InvalidOperationException(
                    $"{Root} exists and is not the agent's workspace. Move or delete it, or run from the project folder."
                );
            Directory.Delete(Root, recursive: true);
        }
        Directory.CreateDirectory(Root);
        File.WriteAllText(
            Path.Combine(Root, "bookings.csv"),
            "pizza,portions\nmargherita,16\nsalame piccante,12\nnduja piccante,8\nverdure,6\nquattro formaggi,8\n"
        );
        File.WriteAllText(
            Path.Combine(Root, "recipes.csv"),
            "pizza,ingredient,grams_per_portion\n"
                + "margherita,dough,250\nmargherita,tomato sauce,80\nmargherita,mozzarella,120\nmargherita,basil,5\n"
                + "salame piccante,dough,250\nsalame piccante,tomato sauce,80\nsalame piccante,mozzarella,100\nsalame piccante,spianata salami,60\n"
                + "nduja piccante,dough,250\nnduja piccante,tomato sauce,80\nnduja piccante,mozzarella,100\nnduja piccante,onion,30\nnduja piccante,nduja,50\nnduja piccante,green chilli peppers,20\nnduja piccante,lemon oil,5\nnduja piccante,parsley,3\n"
                + "verdure,dough,250\nverdure,tomato sauce,80\nverdure,mozzarella,100\nverdure,spinach,40\nverdure,grilled pepper,40\nverdure,grilled courgette,40\nverdure,gorgonzola,50\nverdure,balsamic glaze,10\n"
                + "quattro formaggi,dough,250\nquattro formaggi,tomato sauce,80\nquattro formaggi,mozzarella,60\nquattro formaggi,ricotta salata,40\nquattro formaggi,gorgonzola,50\nquattro formaggi,pecorino romano,20\nquattro formaggi,basil,5\n"
        );
        File.WriteAllText(
            Path.Combine(Root, "stock.csv"),
            "ingredient,grams\ndough,14000\ntomato sauce,5000\nmozzarella,3200\nbasil,200\nspianata salami,1000\nonion,500\nnduja,600\ngreen chilli peppers,300\nlemon oil,150\nparsley,50\nspinach,400\ngrilled pepper,400\ngrilled courgette,400\ngorgonzola,200\nbalsamic glaze,200\nricotta salata,500\npecorino romano,300\n"
        );
        File.WriteAllText(
            Path.Combine(Root, "suppliers.csv"),
            "supplier,ingredient,pack_grams\nFresco,mozzarella,2500\nFresco,gorgonzola,1500\nDairy Dan,mozzarella,1000\nDairy Dan,gorgonzola,1000\n"
        );
        File.WriteAllText(Path.Combine(Root, "orders.csv"), "supplier,ingredient,grams\n");
    }
}
