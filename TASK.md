# Tasks
- [ ] Create a portable project that will aim to be wrapped by an MCP command (e.g., go-to-definition)
- [ ] Make the project perform the function referenced in the section `## Goal`
- [ ] To test this, you can clone this repo: `https://github.com/dotnet/dev-proxy.git` (Note: `.sln` file located at `DevProxy.sln` in the root of the cloned repo) and try locating the definition of `LoadData` (which should point to `DevProxy.Plugins/Mocking/MockRequestLoader.cs:L24` ... `protected override void LoadData(string fileContents)`)

* You are not allowed to install any external dependencies (Nuget) that aren't referenced outside of this document
* You are free to use `dotnet` CLI command

## Goal

To solve the scoping issue (e.g., distinguishing between private void Helper() in ClassA vs ClassB), you can’t rely on simple string search. You need the Roslyn Semantic Model.
The ideal pattern for an AI agent is:
“I am looking at File X, Line Y, Column Z. Where is the definition of the symbol at this position?”This is exactly what happens when you press F12 in VS Code.
Below is a FindDef.csx script that supports two modes:

Exact Resolution (recommended): Uses call-site coordinates to resolve the exact overload or private method.
Fuzzy Search: Searches by name and prints the containing type so the agent can differentiate scopes.

## Prerequisites
You need dotnet-script and the .NET SDK installed.
dotnet tool install -g dotnet-scriptThe Script (FindDef.csx)
Save this file in your project root:

```cs
#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.CodeAnalysis.Workspaces.MSBuild, 4.8.0"
#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"
#r "nuget: Microsoft.Build.Locator, 1.6.10"

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

// --- BOOTSTRAP MSBUILD ---
// Necessary to find the SDK to parse .csproj/.sln files
if (!MSBuildLocator.IsRegistered)
{
    var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
    MSBuildLocator.RegisterInstance(instances.OrderByDescending(x => x.Version).First());
}

// --- MAIN LOGIC ---
var args = Args.ToArray();
if (args.Length < 2)
{
    Console.WriteLine("Usage Modes:");
    Console.WriteLine("  1. By Location (Exact): FindDef.csx <SolutionPath> -pos <FilePath> <Line> <Column>");
    Console.WriteLine("  2. By Name (Fuzzy):     FindDef.csx <SolutionPath> -name <SymbolName>");
    return;
}

string slnPath = args[0];
string mode = args[1];

using var workspace = MSBuildWorkspace.Create();
// Suppress heavy logging to keep output clean for the AI
workspace.LoadMetadataForReferencedProjects = true;

try
{
    var solution = await workspace.OpenSolutionAsync(slnPath);

    if (mode == "-pos")
    {
        // ARGS: slnPath -pos filePath line col
        await ResolveByLocation(solution, args[2], int.Parse(args[3]), int.Parse(args[4]));
    }
    else if (mode == "-name")
    {
        // ARGS: slnPath -name symbolName
        await ResolveByName(solution, args[2]);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

// --- RESOLVER METHODS ---

async Task ResolveByLocation(Solution solution, string filePath, int line, int col)
{
    // 1. Find the document
    var doc = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.FilePath == filePath);
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
    if (!System.IO.File.Exists(path)) return "";
    var lines = await System.IO.File.ReadAllLinesAsync(path);
    return lines.Length > line ? lines[line].Trim() : "";
}
```

## How to Instruct Your AI Agent
Add something like this to your system prompt or agent instructions:

"I have provided a script FindDef.csx to help you navigate code.
- Scenario 1: You see a method call and want to know what it does. Do not guess based on the name. Use the exact location of the call to find the definition. Command:
  ```ps1
  dotnet script FindDef.csx ./MySolution.sln -pos "/abs/path/to/CurrentFile.cs" 45 12
  ```
  (where 45 is the line number and 12 is the column of the method name).
- Scenario 2: You know a class or method name but not where it is. Command:
  ```ps1
  dotnet script FindDef.csx ./MySolution.sln -name "MyPrivateMethod"
  ```
The output will show a Context (containing type). Use that context to decide which private method is relevant to your current task."

Why this solves the scoping problem

1. -pos mode (the scope solver): Given a specific call to Update(), Roslyn applies full language semantics and returns the actual target (e.g., BaseClass.Update() vs ExternalHelper.Update()), with the correct file and line.
1. -name mode (the filter): For a symbol like DoWork, the script prints:
  - Context: WorkerA.DoWork (Private) -> File A
  - Context: WorkerB.DoWork (Private) -> File B

The AI agent, knowing it’s currently editing WorkerB.cs, can pick the correct definition.
