using System;
using System.Linq;
using System.Reflection;

namespace Liftoff.MovingObjects.Utils;

internal static class ReflectionUtils
{
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
        var typ = obj.GetType();
        while (typ != null)
        {
            var field = obj.GetType()
                .GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(info => info.PropertyType == typeof(T));
            if (field != null)
                return (T)field.GetValue(obj);
            typ = typ.BaseType;
        }

        throw new Exception($"Field of type {typeof(T)} not found");
    }
}