﻿namespace ADB_Explorer.Services;

public static class Storage
{
    public static object RetrieveValue(Enum key) => RetrieveValue(key.ToString());

    public static object RetrieveValue(string key)
    {
        return Application.Current.Properties[key];
    }

    public static T Retrieve<T>(string key)
    {
        return (T)Application.Current.Properties[key];
    }

    public static void StoreValue(Enum key, object value) => StoreValue(key.ToString(), value);

    public static void StoreValue(string key, object value)
    {
        Application.Current.Properties[key] = value;
    }

    public static object RetrieveEnum(Type type) => Application.Current.Properties[type.ToString()];
    public static object RetrieveEnum(string key) => Application.Current.Properties[key];

    public static T RetrieveEnum<T>(string key = "") => RetrieveEnum(string.IsNullOrEmpty(key) ? typeof(T).ToString() : key) switch
    {
        string strVal => (T)Enum.Parse(typeof(T), strVal),
        long longVal => (T)Enum.ToObject(typeof(T), longVal),
        _ => default
    };

    public static void StoreEnum(Enum value)
    {
        Application.Current.Properties[value.GetType().ToString()] = value;
    }

    public static bool? RetrieveBool(Enum value) => RetrieveBool(value.ToString());

    public static bool? RetrieveBool(string key)
    {
        return Application.Current?.Properties[key] switch
        {
            string value when !string.IsNullOrEmpty(value) => bool.Parse(value),
            bool val => val,
            _ => null
        };
    }
}
