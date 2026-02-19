using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

// --- BOOTSTRAP MSBUILD ---
// Must be called before any MSBuild types are loaded.
if (!MSBuildLocator.IsRegistered)
{
    var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
    if (instances.Length == 0)
    {
        Console.WriteLine("Error: No .NET SDK or Visual Studio instance found. Please install the .NET SDK.");
        return 1;
    }
    MSBuildLocator.RegisterInstance(instances.OrderByDescending(x => x.Version).First());
}

// --- ARG PARSING ---
// In top-level programs, 'args' is the implicit string[] parameter from the runtime.

if (args.Length < 2)
{
    Console.WriteLine("Usage Modes:");
    Console.WriteLine("  1. By Location (Exact): FindDef <SolutionPath> -pos <FilePath> <Line> <Column>");
    Console.WriteLine("  2. By Name (Fuzzy):     FindDef <SolutionPath> -name <SymbolName>");
    return 1;
}

string slnPath = args[0];
string mode = args[1];

using var workspace = MSBuildWorkspace.Create();
workspace.LoadMetadataForReferencedProjects = true;

// Collect workspace load failures for diagnostics
var workspaceErrors = new System.Collections.Generic.List<string>();
workspace.WorkspaceFailed += (_, e) => workspaceErrors.Add(e.Diagnostic.Message);

try
{
    var solution = await workspace.OpenSolutionAsync(slnPath);

    if (workspaceErrors.Count > 0 && args.Contains("--verbose"))
    {
        Console.Error.WriteLine($"[Workspace warnings: {workspaceErrors.Count}]");
        foreach (var err in workspaceErrors.Take(10))
            Console.Error.WriteLine($"  {err}");
    }

    if (mode == "-pos")
    {
        if (args.Length < 5)
        {
            Console.WriteLine("Error: -pos mode requires <FilePath> <Line> <Column>");
            return 1;
        }
        await ResolveByLocation(solution, args[2], int.Parse(args[3]), int.Parse(args[4]));
    }
    else if (mode == "-name")
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Error: -name mode requires <SymbolName>");
            return 1;
        }
        await ResolveByName(solution, args[2]);
    }
    else
    {
        Console.WriteLine($"Error: Unknown mode '{mode}'. Use -pos or -name.");
        return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}

return 0;

// --- RESOLVER METHODS ---

async Task ResolveByLocation(Solution solution, string filePath, int line, int col)
{
    // 1. Find the document (normalize path separators for cross-platform matching)
    string normalizedInput = Path.GetFullPath(filePath);
    var doc = solution.Projects
        .SelectMany(p => p.Documents)
        .FirstOrDefault(d => string.Equals(
            d.FilePath != null ? Path.GetFullPath(d.FilePath) : null,
            normalizedInput,
            StringComparison.OrdinalIgnoreCase));

    if (doc == null)
    {
        Console.WriteLine("Error: Could not find document in solution.");
        return;
    }

    // 2. Get the specific position in text
    var text = await doc.GetTextAsync();
    if (line >= text.Lines.Count)
    {
        Console.WriteLine("Error: Line number out of bounds.");
        return;
    }

    var position = text.Lines[line].Start + col;

    // 3. Resolve symbol
    var symbol = await SymbolFinder.FindSymbolAtPositionAsync(doc, position);

    if (symbol == null)
    {
        Console.WriteLine("No symbol found at this specific location.");
        return;
    }

    // 4. Get definition source
    var defLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);

    if (defLocation != null)
    {
        var lineSpan = defLocation.GetLineSpan();
        Console.WriteLine("DEFINITION_FOUND:");
        Console.WriteLine($"Symbol: {symbol.ToDisplayString()}");
        Console.WriteLine($"File: {lineSpan.Path}");
        Console.WriteLine($"Line: {lineSpan.StartLinePosition.Line}");
        Console.WriteLine($"Code:\n{await GetLineText(lineSpan.Path, lineSpan.StartLinePosition.Line)}");
    }
    else
    {
        Console.WriteLine($"Symbol '{symbol.Name}' found, but definition is not in source (compiled metadata/NuGet).");
    }
}

async Task ResolveByName(Solution solution, string symbolName)
{
    // Search entire solution for declarations matching the name
    var symbols = await SymbolFinder.FindSourceDeclarationsAsync(solution, symbolName, ignoreCase: false);
    var results = symbols.ToList();

    if (!results.Any())
    {
        Console.WriteLine("No symbols found with that name.");
        return;
    }

    Console.WriteLine($"Found {results.Count} matching definitions:");

    foreach (var sym in results)
    {
        var loc = sym.Locations.First();
        var span = loc.GetLineSpan();

        // Print the containing type so the agent can disambiguate private scopes
        string container = sym.ContainingType != null ? sym.ContainingType.Name : "Global";
        string accessibility = sym.DeclaredAccessibility.ToString();

        Console.WriteLine("---");
        Console.WriteLine($"Context: {container}.{sym.Name} ({accessibility})");
        Console.WriteLine($"File: {span.Path}");
        Console.WriteLine($"Line: {span.StartLinePosition.Line}");
    }
}

async Task<string> GetLineText(string path, int line)
{
    if (!File.Exists(path)) return "";
    var lines = await File.ReadAllLinesAsync(path);
    return lines.Length > line ? lines[line].Trim() : "";
}
