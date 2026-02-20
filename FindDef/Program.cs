using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

#region Main

// 1. Register MSBuild
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

// 2. Parse Arguments (Separate flags from positional args)
var argList = args.ToList();
bool verbose = argList.Remove("--verbose");

if (argList.Count < 3 || argList[1] != "-name")
{
    Console.WriteLine("FindDef <SolutionPath> -name <SymbolName> [--verbose]");
    Console.WriteLine("Example: FindDef Web -name MyController --verbose");
    return 1;
}

string inputPath = argList[0];
string symbol = argList[2];

// 3. Resolve Solution Path (Dictionary Lookup)
var solutionShortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Web"] = Environment.GetEnvironmentVariable("WEB_SLN_PATH")
              ?? @"C:\src\projects\BrightMetricsWeb\BrightMetricsWeb.sln"
};

string slnPath = solutionShortcuts.ContainsKey(inputPath)
    ? solutionShortcuts[inputPath]
    : inputPath;

// 4. Setup Logging
if (verbose)
{
    var tracer = new VerboseListener();
    Trace.Listeners.Add(tracer);
    Trace.AutoFlush = true;
}

// 5. Open Workspace
using var workspace = MSBuildWorkspace.Create();
workspace.LoadMetadataForReferencedProjects = true;

var workspaceErrors = new List<string>();
workspace.WorkspaceFailed += (_, e) => workspaceErrors.Add(e.Diagnostic.Message);

try
{
    Trace.TraceInformation("Opening solution {0}", slnPath);

    // Validate file existence before letting Roslyn throw a generic error
    if (!System.IO.File.Exists(slnPath))
    {
        Console.WriteLine($"Error: Solution file not found at '{slnPath}'");
        return 1;
    }

    var solution = await workspace.OpenSolutionAsync(slnPath);
    Trace.TraceInformation("Finished loading solution");

    if (workspaceErrors.Count > 0)
    {
        Trace.TraceWarning($"[Workspace warnings: {workspaceErrors.Count}]");
        foreach (var err in workspaceErrors.Take(10))
            Trace.TraceWarning($"  {err}");
    }

    await ResolveByName(solution, symbol);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (verbose) Console.WriteLine(ex.StackTrace);
    return 1;
}

return 0;

#endregion

async Task ResolveByName(Solution solution, string symbolName)
{
    Trace.TraceInformation("Running SymbolFinder.FindSourceDeclarationsAsync '{0}'", symbolName);
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
        // Safety: Ensure there is a location and it is in source code
        var loc = sym.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc == null)
        {
            Trace.TraceWarning($"Skipping symbol '{sym.Name}': No source location found.");
            continue;
        }

        var span = loc.GetLineSpan();

        string container = sym.ContainingType != null ? sym.ContainingType.Name : "global::";
        string accessibility = sym.DeclaredAccessibility.ToString();

        Console.WriteLine("---");
        Console.WriteLine($"Context: {container}.{sym.Name} ({accessibility})");
        Console.WriteLine($"File:    {span.Path}");
        // Add 1 to line number for human-readable (editors are 1-based, Roslyn is 0-based)
        Console.WriteLine($"Line:    {span.StartLinePosition.Line + 1}");
    }
}

public class VerboseListener : ConsoleTraceListener
{
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (!string.IsNullOrEmpty(message))
            WriteLine($"[{eventType}] {message}");
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        if (format != null)
            WriteLine($"[{eventType}] {string.Format(format, args ?? Array.Empty<object>())}");
    }
}
