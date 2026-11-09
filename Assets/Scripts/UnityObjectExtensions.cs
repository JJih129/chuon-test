using UnityEngine;

/// <summary>
/// Unity overrides == for destroyed/missing objects ("fake null"), but C# ?? and ?. do not.
/// Use OrNull() before ?? so the fallback actually runs.
/// </summary>
public static class UnityObjectExtensions
{
    public static T OrNull<T>(this T obj) where T : Object
    {
        return obj ? obj : null;
    }
}
