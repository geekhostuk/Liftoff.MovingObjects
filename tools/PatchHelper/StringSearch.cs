using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class StringSearch
{
    public static int Run(string[] args)
    {
        var asmPath = args[1];
        var needle = args[2];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(asmPath)));
        var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { AssemblyResolver = resolver });

        foreach (var t in asm.MainModule.Types)
        foreach (var m in t.Methods)
        {
            if (!m.HasBody) continue;
            foreach (var instr in m.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Ldstr && instr.Operand is string s && s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine($"  Hit: {t.FullName}.{m.Name}    string=\"{s}\"");
                    break;
                }
            }
        }
        return 0;
    }
}
