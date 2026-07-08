using System;
using System.IO;
using Mono.Cecil;

if (args.Length >= 1 && args[0] == "explore")
    return Explore.Run(args);
if (args.Length >= 1 && args[0] == "audit")
    return Audit.Run(args);
if (args.Length >= 1 && args[0] == "search")
    return StringSearch.Run(args);
if (args.Length >= 1 && args[0] == "il")
    return DumpIL.Run(args);

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PatchHelper <input-asm-csharp.dll> <output-asm-csharp.dll>");
    Console.Error.WriteLine("       PatchHelper explore <asm.dll> <keyword>...");
    return 1;
}

var input = args[0];
var output = args[1];

if (!File.Exists(input))
{
    Console.Error.WriteLine($"Input not found: {input}");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));

var rp = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false };
using (var assembly = AssemblyDefinition.ReadAssembly(input, rp))
{
    Liftoff.MovingObjects.Patcher.Patcher.Patch(assembly);
    assembly.Write(output);
}

Console.WriteLine($"Patched -> {output}");
return 0;
