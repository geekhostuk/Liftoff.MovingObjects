using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

public static class Audit
{
    public static int Run(string[] args)
    {
        var asmPath = args[1];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(asmPath)));
        var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { AssemblyResolver = resolver });

        // (TypeName, MethodName)
        var targets = new (string Type, string Method)[]
        {
            ("TrackEditorGUI", "Start"),
            ("PopupShareContent", "ShareItem"),
            ("TrackEditorEditWindow", "AtLeastOneItemAvailable"),
            ("TrackDragCenterOnCamera", "OnDragHold"),
            ("TrackDragBehaviorSnap", "OnDragHold"),
            ("TrackDragBehaviorRibbon", "OnDragHold"),
            ("TrackDragCenterOnCamera", "OnDragRelease"),
            ("TrackDragBehaviorSnap", "OnDragRelease"),
            ("TrackDragBehaviorRibbon", "OnDragRelease"),
            ("FlightManager", "ResetDroneRoutine"),
            ("LevelInitSequence", "InitializeLevel"),
            ("LevelInitSequence", "SetupLevel"),
            ("LevelInitSequence", "InitSinglePlayerGame"),
            ("LevelInitSequence", "RoutineInitialize"),
        };

        foreach (var (typeName, methodName) in targets)
        {
            var t = asm.MainModule.Types.FirstOrDefault(x => x.Name == typeName);
            if (t == null)
            {
                Console.WriteLine($"  TYPE MISSING: {typeName}");
                continue;
            }
            var matches = t.Methods.Where(m => m.Name == methodName).ToList();
            if (matches.Count == 0)
            {
                Console.WriteLine($"  METHOD MISSING: {typeName}.{methodName}");
                continue;
            }
            foreach (var m in matches)
            {
                var ps = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name));
                Console.WriteLine($"  OK: {typeName}.{methodName}({ps})");
            }
        }

        return 0;
    }
}
