#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.CodeAnalysis.Workspaces.MSBuild, 4.8.0"
#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"
#r "nuget: Microsoft.CodeAnalysis.CSharp.Workspaces, 4.8.0"
#r "nuget: Microsoft.Build.Locator, 1.6.10"

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

var forwardedArgs = Args.ToArray();
if (forwardedArgs.Length == 0)
{
    Console.WriteLine("Usage: dotnet script FindDef.csx <SolutionPath> [-pos ... | -name ...]");
    return;
}

var repoRoot = Directory.GetCurrentDirectory();
var toolProject = Path.Combine(repoRoot, "FindDefTool");
if (!Directory.Exists(toolProject))
{
    Console.WriteLine("Error: FindDefTool project not found next to FindDef.csx.");
    return;
}

var psi = new ProcessStartInfo("dotnet")
{
    Arguments = BuildArguments(toolProject, forwardedArgs),
    UseShellExecute = false
};

using var process = Process.Start(psi);
process.WaitForExit();
Environment.Exit(process.ExitCode);

static string BuildArguments(string projectPath, string[] args)
{
    var forwarded = string.Join(" ", args.Select(QuoteIfNeeded));
    return $"run --project \"{projectPath}\" -- {forwarded}";
}

static string QuoteIfNeeded(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return "\"\"";
    }

    return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
        ? $"\"{value.Replace("\"", "\\\"")}\""
        : value;
}
