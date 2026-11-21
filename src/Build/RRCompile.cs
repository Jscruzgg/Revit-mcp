// RRCompile: compiles the add-in or the tools for Revit 2025+ (.NET 8/10) with Roslyn (Microsoft.CodeAnalysis from lib\).
// It runs on the .NET Framework that every Windows has, so neither Visual Studio nor the .NET SDK is needed.
// Built and called by src\Build\compile.ps1 (used by build.ps1 and Installer\Install.ps1). C# 5 syntax (Framework csc).
// Usage: RRCompile.exe <lib dir> <out dll> <refs list file> <sources list file> <defines ;-separated or -> <pdb: 1|0>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

static class RRCompile
{
    static string Lib;

    static int Main(string[] a)
    {
        if (a.Length != 6) { Console.Error.WriteLine("usage: RRCompile <lib> <out.dll> <refs.txt> <sources.txt> <defines|-> <pdb 1|0>"); return 2; }
        Lib = a[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            // lib\ of the installer has everything; pyRevit's netfx folder keeps System.Buffers under engines\IPY342
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var d in new[] { Lib, Path.Combine(Lib, "engines", "IPY342") })
            {
                string p = Path.Combine(d, name);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        try { return Run(a); }
        catch (Exception ex) { Console.Error.WriteLine("RRCompile: " + ex); return 3; }
    }

    // separate method: Roslyn types are resolved only after the AssemblyResolve handler is in place
    static int Run(string[] a)
    {
        string outDll = a[1];
        var refs = File.ReadAllLines(a[2]).Select(l => l.Trim()).Where(l => l != "")
                       .Select(p => (Microsoft.CodeAnalysis.MetadataReference)Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)).ToList();
        string[] defines = a[4] == "-" ? new string[0] : a[4].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
                                                                          Microsoft.CodeAnalysis.DocumentationMode.None,
                                                                          Microsoft.CodeAnalysis.SourceCodeKind.Regular, defines);
        var trees = File.ReadAllLines(a[3]).Select(l => l.Trim()).Where(l => l != "")
                        .Select(f => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                            Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(f), System.Text.Encoding.UTF8), parse, f)).ToList();
        var opts = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                                                                               optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release);
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outDll), trees, refs, opts);
        bool pdb = a[5] == "1";
        Microsoft.CodeAnalysis.Emit.EmitResult r;
        using (var pe = new MemoryStream())
        using (var pd = new MemoryStream())
        {
            var eo = new Microsoft.CodeAnalysis.Emit.EmitOptions(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb,
                                                                pdbFilePath: Path.ChangeExtension(outDll, ".pdb"));
            r = pdb ? comp.Emit(pe, pd, options: eo) : comp.Emit(pe);
            if (r.Success)
            {
                File.WriteAllBytes(outDll, pe.ToArray());
                if (pdb) File.WriteAllBytes(Path.ChangeExtension(outDll, ".pdb"), pd.ToArray());
            }
        }
        var errors = r.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        foreach (var d in errors.Take(50)) Console.WriteLine(d.ToString());
        if (errors.Count > 50) Console.WriteLine("... " + (errors.Count - 50) + " more errors");
        return r.Success ? 0 : 1;
    }
}
