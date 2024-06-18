namespace RandomSkunk.Hosting.Cron;

internal static class TypeExtensions
{
    public static string GetFullName(this Type type)
    {
        if (type.FullName is not null)
            return type.FullName;

        if (type.Namespace is not null)
            return $"{type.Namespace}.{type.Name}";

        return type.Name;
    }
}
