using System;
using System.Windows;

namespace ADB_Explorer.Services
{
    public static class Storage
    {
        public static string RetrieveValue(Enum key) => RetrieveValue(key.ToString());

        public static string RetrieveValue(string key)
        {
            return (string)Application.Current.Properties[key];
        }

        public static void StoreValue(Enum key, object value) => StoreValue(key.ToString(), value);

        public static void StoreValue(string key, object value)
        {
            Application.Current.Properties[key] = value;
        }

        public static T RetrieveEnum<T>()
        {
            return Application.Current.Properties[typeof(T).ToString()] is string value ? (T)Enum.Parse(typeof(T), value) : default;
        }

        public static void StoreEnum(Enum value)
        {
            Application.Current.Properties[value.GetType().ToString()] = value;
        }
    }
}
