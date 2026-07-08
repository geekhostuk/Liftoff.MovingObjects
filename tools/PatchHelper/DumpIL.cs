using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class DumpIL
{
    public static int Run(string[] args)
    {
        var asmPath = args[1];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(asmPath)));
        var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { AssemblyResolver = resolver });

        // il <asm> <TypeName> [MethodNameSubstr]
        // il <asm> --scan <instrSubstr>   : dump every method whose IL contains instrSubstr
        var typeName = args[2];
        var methodFilter = args.Length > 3 ? args[3] : null;

        var allTypes = new System.Collections.Generic.List<TypeDefinition>();
        void Collect(TypeDefinition t) { allTypes.Add(t); foreach (var n in t.NestedTypes) Collect(n); }
        foreach (var t in asm.MainModule.Types) Collect(t);

        if (typeName == "--scan")
        {
            var needle = args[3];
            foreach (var t in allTypes)
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                if (!m.Body.Instructions.Any(i => i.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;
                var ps = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                var full = args.Length > 4 && args[4] == "--full";
                Console.WriteLine($"== {t.Name}.{m.Name}({ps}) : {m.ReturnType.Name} ==");
                foreach (var i in m.Body.Instructions)
                    if (full || i.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        Console.WriteLine($"    {i}");
            }
            return 0;
        }

        foreach (var t in allTypes.Where(x =>
                     x.Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            foreach (var m in t.Methods.Where(m => m.HasBody &&
                         (methodFilter == null || m.Name.IndexOf(methodFilter, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                var ps = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Console.WriteLine($"== {t.Name}.{m.Name}({ps}) : {m.ReturnType.Name} ==");
                foreach (var i in m.Body.Instructions)
                    Console.WriteLine($"  {i}");
                Console.WriteLine();
            }
        }
        return 0;
    }
}
