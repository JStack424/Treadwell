using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Treadwell.Core
{
    /// <summary>
    /// Shared, executable contract matching used by the runtime gate and its tests.
    /// A contract is accepted only when exactly one member has the requested shape.
    /// </summary>
    public static class ExactRuntimeContract
    {
        public static MethodInfo[] FindMethods(
            Type type,
            string name,
            BindingFlags flags,
            Type returnType,
            IReadOnlyList<Type> parameterTypes,
            Func<MethodInfo, bool>? additionalCheck = null)
        {
            return type.GetMethods(flags)
                .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                .Where(method => method.ReturnType == returnType)
                .Where(method => ParametersEqual(method.GetParameters(), parameterTypes))
                .Where(method => additionalCheck == null || additionalCheck(method))
                .ToArray();
        }

        public static ConstructorInfo[] FindConstructors(
            Type type,
            BindingFlags flags,
            IReadOnlyList<Type> parameterTypes)
        {
            return type.GetConstructors(flags)
                .Where(constructor => ParametersEqual(constructor.GetParameters(), parameterTypes))
                .ToArray();
        }

        public static FieldInfo[] FindFields(Type type, string name, BindingFlags flags, Type fieldType)
        {
            return type.GetFields(flags)
                .Where(field => string.Equals(field.Name, name, StringComparison.Ordinal))
                .Where(field => field.FieldType == fieldType)
                .ToArray();
        }

        public static PropertyInfo[] FindProperties(Type type, string name, BindingFlags flags, Type propertyType)
        {
            return type.GetProperties(flags)
                .Where(property => string.Equals(property.Name, name, StringComparison.Ordinal))
                .Where(property => property.PropertyType == propertyType)
                .ToArray();
        }

        private static bool ParametersEqual(ParameterInfo[] observed, IReadOnlyList<Type> expected)
        {
            if (observed.Length != expected.Count) return false;
            for (var index = 0; index < observed.Length; index++)
            {
                if (observed[index].ParameterType != expected[index]) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Runs every rollback step when installation fails, even when one cleanup step
    /// also fails, and preserves all failures for a fail-closed caller.
    /// </summary>
    public static class TransactionalInstall
    {
        public static void Run(Action install, params Action[] rollbackSteps)
        {
            if (install == null) throw new ArgumentNullException(nameof(install));
            if (rollbackSteps == null) throw new ArgumentNullException(nameof(rollbackSteps));

            try
            {
                install();
            }
            catch (Exception installException)
            {
                var failures = new List<Exception> { installException };
                foreach (var rollback in rollbackSteps)
                {
                    if (rollback == null) continue;
                    try { rollback(); }
                    catch (Exception cleanupException) { failures.Add(cleanupException); }
                }

                if (failures.Count > 1)
                    throw new AggregateException("Installation failed and rollback was incomplete.", failures);
                throw;
            }
        }
    }
}
