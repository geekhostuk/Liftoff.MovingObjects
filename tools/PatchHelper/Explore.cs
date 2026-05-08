using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

public static class Explore
{
    public static int Run(string[] args)
    {
        var asmPath = args[1];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(asmPath)));
        var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { AssemblyResolver = resolver });

        var keywords = args.Skip(2).ToArray();
        if (keywords.Length == 0)
        {
            Console.Error.WriteLine("Usage: explore <asm.dll> <keyword>...");
            return 2;
        }

        foreach (var t in asm.MainModule.Types.Where(t =>
                     keywords.Any(k => t.FullName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            Console.WriteLine($"== {t.FullName} (base={t.BaseType?.FullName}) ==");
            foreach (var f in t.Fields)
                Console.WriteLine($"  field: {f.FieldType.FullName} {f.Name}");
            foreach (var e in t.Events)
                Console.WriteLine($"  event: {e.EventType.FullName} {e.Name}");
            foreach (var m in t.Methods)
            {
                var ps = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                Console.WriteLine($"  method: {m.ReturnType.FullName} {m.Name}({ps})");
            }
            Console.WriteLine();
        }

        return 0;
    }
}
