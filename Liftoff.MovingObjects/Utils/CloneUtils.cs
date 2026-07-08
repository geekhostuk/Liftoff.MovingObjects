using System;
using System.Collections;
using System.Reflection;

namespace Liftoff.MovingObjects.Utils;

internal static class CloneUtils
{
    // Generic deep clone by reflection: structs/strings are copied by value, lists element-wise,
    // and reference types field-by-field. Covers every current and future MO_* field automatically.
    public static object DeepClone(object src)
    {
        if (src == null)
            return null;

        var type = src.GetType();
        if (type.IsValueType || type == typeof(string))
            return src;

        if (src is IList list)
        {
            var cloneList = (IList)Activator.CreateInstance(type);
            foreach (var item in list)
                cloneList.Add(DeepClone(item));
            return cloneList;
        }

        var dst = Activator.CreateInstance(type);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            field.SetValue(dst, DeepClone(field.GetValue(src)));
        return dst;
    }

    public static T DeepClone<T>(T src)
    {
        return (T)DeepClone((object)src);
    }
}
