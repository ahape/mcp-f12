using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

#region Main

// 1. Register MSBuild (Once per process)
if (!MSBuildLocator.IsRegistered)
{
    var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
    if (instances.Length == 0)
    {
        Console.Error.WriteLine("Error: No .NET SDK or Visual Studio instance found.");
        return 1;
    }
    MSBuildLocator.RegisterInstance(instances.OrderByDescending(x => x.Version).First());
}

// 2. Define Shortcuts
var solutionShortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Web"] = Environment.GetEnvironmentVariable("WEB_SLN_PATH")
              ?? @"C:\src\projects\BrightMetricsWeb\BrightMetricsWeb.sln"
};

// 3. State Management
MSBuildWorkspace? currentWorkspace = null;
Solution? currentSolution = null;
string? loadedSlnPath = null;

Console.Error.WriteLine("Ready. Enter: <SolutionPathOrKey> <SymbolName>");
Console.Error.WriteLine("Example: Web MyController");

// 4. Input Loop
while (true)
{
    Console.Error.Write("> "); // Prompt on Stderr so it doesn't pollute Stdout pipe
    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) break;
    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2)
    {
        Console.Error.WriteLine("Invalid input. Expected: <Solution> <Symbol>");
        continue;
    }

    string slnKey = parts[0];
    string symbolName = parts[1];

    // Resolve Path
    string targetSlnPath = solutionShortcuts.ContainsKey(slnKey)
        ? solutionShortcuts[slnKey]
        : slnKey;

    if (!File.Exists(targetSlnPath))
    {
        WriteJsonError($"Solution file not found: {targetSlnPath}");
        continue;
    }

    try
    {
        // 5. Load Solution (Only if changed)
        if (currentSolution == null || !string.Equals(loadedSlnPath, targetSlnPath, StringComparison.OrdinalIgnoreCase))
        {
            // Dispose previous workspace if exists
            currentWorkspace?.Dispose();

            Console.Error.WriteLine($"Loading solution: {targetSlnPath}...");

            // Create new
            currentWorkspace = MSBuildWorkspace.Create();
            currentWorkspace.LoadMetadataForReferencedProjects = true;

            // Capture failures silently to stderr
            currentWorkspace.WorkspaceFailed += (_, e) => Console.Error.WriteLine($"[MSBuild Warning] {e.Diagnostic.Message}");

            currentSolution = await currentWorkspace.OpenSolutionAsync(targetSlnPath);
            loadedSlnPath = targetSlnPath;
            Console.Error.WriteLine("Solution loaded.");
        }

        // 6. Find and Output
        await FindAndPrintJson(currentSolution!, symbolName);
    }
    catch (Exception ex)
    {
        WriteJsonError(ex.Message);
        // Reset state on critical failure
        currentWorkspace?.Dispose();
        currentWorkspace = null;
        currentSolution = null;
        loadedSlnPath = null;
    }
}

return 0;

#endregion

async Task FindAndPrintJson(Solution solution, string symbolName)
{
    var symbols = await SymbolFinder.FindSourceDeclarationsAsync(solution, symbolName, ignoreCase: false);

    var results = symbols
        .SelectMany(sym => sym.Locations)
        .Where(loc => loc.IsInSource)
        .Select(loc =>
        {
            var span = loc.GetLineSpan();
            var sym = symbols.First(s => s.Locations.Contains(loc));

            return new
            {
                symbol = sym.Name,
                container = sym.ContainingType?.Name ?? "global",
                kind = sym.Kind.ToString(),
                file = span.Path,
                // Roslyn is 0-based, Editors are 1-based
                line = span.StartLinePosition.Line + 1,
                character = span.StartLinePosition.Character + 1
            };
        })
        .ToList();

    // Output raw JSON to StdOut
    string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = false });
    Console.WriteLine(json);
    Console.Out.Flush();
}

void WriteJsonError(string message)
{
    var errObj = new { error = message };
    Console.WriteLine(JsonSerializer.Serialize(errObj));
    Console.Out.Flush();
}
