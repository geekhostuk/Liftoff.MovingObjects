using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Photon.Pun;

namespace Liftoff.MovingObjects.Multiplayer;

// Finds the game's [PunRPC] methods so they can be Harmony-patched.
//
// Why by scan and not typeof: the declaring types are obfuscated (quote-junk names in the global
// namespace), so they can't be named in source. The *method names* survive, and not by luck — PUN
// dispatches RPCs by method-name string at runtime, so an obfuscator that renamed them would break
// the game's own netcode. That makes a name+attribute scan a sturdier binding than the readable-type
// hooks used elsewhere in the mod.
//
// The scan walks every type in Assembly-CSharp (~5k), so the result is computed once and cached —
// SpectatorSync asks for it during Awake.
internal static class PunRpcScanner
{
    private static List<MethodBase> _cache;

    private static IReadOnlyList<MethodBase> All()
    {
        return _cache ??= Scan().ToList();
    }

    internal static IEnumerable<MethodBase> ByName(string name)
    {
        return All().Where(m => m.Name == name);
    }

    private static IEnumerable<MethodBase> Scan()
    {
        // typeof(FlightManager) is a readable type in Assembly-CSharp — cheaper and more reliable
        // than searching every loaded assembly by name.
        Type[] types;
        try
        {
            types = typeof(FlightManager).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A partially-loadable assembly still yields the types we care about.
            types = ex.Types.Where(t => t != null).ToArray();
        }

        foreach (var type in types)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                          BindingFlags.Instance | BindingFlags.Static |
                                          BindingFlags.DeclaredOnly);
            }
            catch
            {
                continue;
            }

            foreach (var m in methods)
            {
                // Generic definitions and abstract bodies can't be patched.
                if (m.IsAbstract || m.ContainsGenericParameters)
                    continue;

                var isRpc = false;
                try
                {
                    isRpc = m.GetCustomAttributes(typeof(PunRPC), false).Length > 0;
                }
                catch
                {
                    // Attribute resolution can throw on obfuscated members; skip those.
                }

                if (isRpc)
                    yield return m;
            }
        }
    }
}
