using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    RegisterMSBuild();

    if (args.Length < 2)
    {
        PrintUsage();
        return 1;
    }

    var solutionPath = Path.GetFullPath(args[0]);
    var mode = args[1];

    if (!File.Exists(solutionPath))
    {
        Console.WriteLine($"Error: solution '{solutionPath}' not found");
        return 1;
    }

    using var workspace = MSBuildWorkspace.Create();
    workspace.LoadMetadataForReferencedProjects = true;

    try
    {
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        switch (mode)
        {
            case "-pos" when args.Length >= 5:
                await ResolveByLocation(solution, args[2], args[3], args[4]);
                return 0;
            case "-name" when args.Length >= 3:
                await ResolveByName(solution, args[2]);
                return 0;
            default:
                PrintUsage();
                return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage Modes:");
    Console.WriteLine("  1. Exact: FindDefTool <SolutionPath> -pos <FilePath> <Line> <Column>");
    Console.WriteLine("  2. Fuzzy: FindDefTool <SolutionPath> -name <SymbolName>");
}

static void RegisterMSBuild()
{
    if (MSBuildLocator.IsRegistered)
    {
        return;
    }

    var instance = MSBuildLocator.QueryVisualStudioInstances().OrderByDescending(i => i.Version).FirstOrDefault();
    if (instance != null)
    {
        MSBuildLocator.RegisterInstance(instance);
        return;
    }

    MSBuildLocator.RegisterDefaults();
}

static async Task ResolveByLocation(Solution solution, string filePathArg, string lineArg, string columnArg)
{
    if (!int.TryParse(lineArg, out var line) || !int.TryParse(columnArg, out var column))
    {
        Console.WriteLine("Error: line and column must be integers");
        return;
    }

    var filePath = Path.GetFullPath(filePathArg);
    var document = solution.Projects.SelectMany(p => p.Documents)
        .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    if (document == null)
    {
        Console.WriteLine("Error: document not found in solution");
        return;
    }

    var text = await document.GetTextAsync();
    if (line < 0 || line >= text.Lines.Count)
    {
        Console.WriteLine("Error: line number out of range");
        return;
    }

    var targetLine = text.Lines[line];
    if (column < 0 || column > targetLine.End - targetLine.Start)
    {
        Console.WriteLine("Error: column number out of range");
        return;
    }

    var position = targetLine.Start + column;
    var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position);

    if (symbol == null)
    {
        Console.WriteLine("No symbol at provided coordinates");
        return;
    }

    var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
    if (sourceLocation == null)
    {
        Console.WriteLine("Definition is not in source");
        return;
    }

    var span = sourceLocation.GetLineSpan();
    Console.WriteLine("DEFINITION_FOUND:");
    Console.WriteLine($"Symbol: {symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
    Console.WriteLine($"File: {span.Path}");
    Console.WriteLine($"Line: {span.StartLinePosition.Line}");
}

static async Task ResolveByName(Solution solution, string symbolName)
{
    if (string.IsNullOrWhiteSpace(symbolName))
    {
        Console.WriteLine("Error: symbol name required");
        return;
    }

    var symbols = await SymbolFinder.FindSourceDeclarationsAsync(solution, symbolName, ignoreCase: false);
    var results = symbols.ToList();

    if (!results.Any())
    {
        Console.WriteLine("No symbols found with that name");
        return;
    }

    Console.WriteLine($"Found {results.Count} matching definitions:");
    foreach (var symbol in results)
    {
        var location = symbol.Locations.FirstOrDefault();
        if (location == null)
        {
            continue;
        }

        var span = location.GetLineSpan();
        var container = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "Global";
        Console.WriteLine("---");
        Console.WriteLine($"Context: {container}.{symbol.Name} ({symbol.DeclaredAccessibility})");
        Console.WriteLine($"File: {span.Path}");
        Console.WriteLine($"Line: {span.StartLinePosition.Line}");
    }
}
