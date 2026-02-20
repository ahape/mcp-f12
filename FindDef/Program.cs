using System;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;
# region Main
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
if (args.Length < 3 || args[1] != "-name") // Currently only supports `-name`
{
    // args[0] <SolutionPath>; args[1] <mode>; args[2] <SymbolName>
    Console.WriteLine("FindDef <SolutionPath> -name <SymbolName>");
    return 1;
}
string slnPath = args[0];
string mode = args[1];
string symbol = args[2];
using var workspace = MSBuildWorkspace.Create();
workspace.LoadMetadataForReferencedProjects = true;
// Collect workspace load failures for diagnostics
var workspaceErrors = new System.Collections.Generic.List<string>();
workspace.WorkspaceFailed += (_, e) => workspaceErrors.Add(e.Diagnostic.Message);

var verboseMode = args.Contains("--verbose");
var timing = System.Diagnostics.Stopwatch.StartNew();

if (verboseMode)
{
    Trace.Listeners.Add(new ConsoleTraceListener());
    Trace.AutoFlush = true;
}
try
{
    Trace.TraceInformation("Opening solution {0}", slnPath);
    var solution = await workspace.OpenSolutionAsync(slnPath);
    Trace.TraceInformation("Finished");
    if (workspaceErrors.Count > 0)
    {
        Trace.TraceWarning($"[Workspace warnings: {workspaceErrors.Count}]");
        foreach (var err in workspaceErrors.Take(10))
            Trace.TraceWarning($"  {err}");
    }
    await ResolveByName(solution, args[2]);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
return 0;
# endregion
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
        var loc = sym.Locations.First();
        var span = loc.GetLineSpan();
        // Print the containing type so the agent can disambiguate private scopes
        string container = sym.ContainingType != null ? sym.ContainingType.Name : "global::";
        string accessibility = sym.DeclaredAccessibility.ToString();
        Console.WriteLine("---");
        Console.WriteLine($"Context: {container}.{sym.Name} ({accessibility})");
        Console.WriteLine($"File: {span.Path}");
        Console.WriteLine($"Line: {span.StartLinePosition.Line}");
    }
}
