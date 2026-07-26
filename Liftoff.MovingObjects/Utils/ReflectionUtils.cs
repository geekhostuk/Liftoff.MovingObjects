using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Liftoff.MovingObjects.Utils;

internal static class ReflectionUtils
{
    // GetPrivateFieldValueByType is called once per flag inside every FindAllFlags scan, and until now
    // re-ran GetProperties + LINQ on every call. Cache the resolved PropertyInfo per (object type, T)
    // so the per-flag read is a dictionary lookup + GetValue. A null entry means "resolved, but no such
    // property" — kept out of the cache so the exceptional not-found path (which throws) isn't cached.
    private static readonly Dictionary<(Type, Type), PropertyInfo> _propCache = new();

    public static T GetPrivateFieldValue<T>(object obj, string name)
    {
        var field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new NullReferenceException(name);
        return (T)field.GetValue(obj);
    }

    // First *field* of type T anywhere in the type's hierarchy, or default(T) if there is none.
    //
    // Deliberately different from GetPrivateFieldValueByType below on three counts, all of which
    // matter when reading the obfuscated argument structs the game passes to its [PunRPC] methods:
    // it reads fields rather than properties; it takes the first match instead of throwing when
    // several members share a type (those structs carry more than one obfuscated member of the same
    // type); and it returns default(T) rather than throwing when there's no match, because a
    // diagnostic probe must never take the game down on an unrecognised payload.
    public static T GetFieldValueByType<T>(object obj)
    {
        if (obj == null)
            return default;

        for (var typ = obj.GetType(); typ != null; typ = typ.BaseType)
        {
            var field = typ
                .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(info => typeof(T).IsAssignableFrom(info.FieldType));
            if (field != null)
                return (T)field.GetValue(obj);
        }

        return default;
    }

    public static T GetPrivateFieldValueByType<T>(object obj)
    {
        var key = (obj.GetType(), typeof(T));
        if (_propCache.TryGetValue(key, out var cached))
            return (T)cached.GetValue(obj);

        // Walk the hierarchy one level at a time (DeclaredOnly), so SingleOrDefault's "exactly one of
        // this type at this level" contract holds per level rather than being re-evaluated against the
        // full flattened set on every iteration. (The old code queried obj.GetType() inside the loop
        // instead of the walking `typ`, so the base-type walk was a no-op that re-checked the leaf.)
        for (var typ = obj.GetType(); typ != null; typ = typ.BaseType)
        {
            var property = typ
                .GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                               | BindingFlags.DeclaredOnly)
                .SingleOrDefault(info => info.PropertyType == typeof(T));
            if (property != null)
            {
                _propCache[key] = property;
                return (T)property.GetValue(obj);
            }
        }

        throw new Exception($"Field of type {typeof(T)} not found");
    }
}