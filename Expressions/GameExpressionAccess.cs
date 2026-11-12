using System;
using System.Collections.Generic;
using System.Reflection;

namespace AvatarReplacer
{
    /// <summary>
    /// Reads game expression values across versions that expose them as
    /// fields (stable) or properties backed by ExpressionRuntime (beta).
    /// </summary>
    internal static class GameExpressionAccess
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> Members =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();
        private static readonly HashSet<string> MissingMembersLogged = new HashSet<string>();

        public static float Weight(Expression expression) => ReadFloat(expression, "weight");
        public static float WeightMultiplier(Expression expression) => ReadFloat(expression, "weightMultiplier");

        private static float ReadFloat(Expression expression, string name)
        {
            if (expression == null) return 0f;

            Type type = expression.GetType();
            if (!Members.TryGetValue(type, out var byName))
            {
                byName = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
                Members[type] = byName;
            }

            if (!byName.TryGetValue(name, out var member))
            {
                member = (MemberInfo)type.GetProperty(name, InstanceMembers) ?? type.GetField(name, InstanceMembers);
                byName[name] = member;
            }

            try
            {
                if (member is PropertyInfo property && property.PropertyType == typeof(float) && property.CanRead)
                    return (float)property.GetValue(expression, null);
                if (member is FieldInfo field && field.FieldType == typeof(float))
                    return (float)field.GetValue(expression);
            }
            catch (Exception ex)
            {
                LogMissingOnce(type, name, $"could not be read: {ex.Message}");
                return 0f;
            }

            LogMissingOnce(type, name, "was not exposed as a float field or readable float property");
            return 0f;
        }

        private static void LogMissingOnce(Type type, string name, string reason)
        {
            string key = type.FullName + "." + name;
            if (MissingMembersLogged.Add(key))
                ModLog.Warning($"Game expression member {key} {reason}; expression sync will treat it as zero.");
        }
    }
}
