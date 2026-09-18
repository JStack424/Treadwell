#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Treadwell
{
    internal sealed class CompatibilityResult
    {
        internal CompatibilityResult(bool isCompatible, string reason)
        {
            IsCompatible = isCompatible;
            Reason = reason;
        }
        internal bool IsCompatible { get; }
        internal string Reason { get; }
    }

    internal static class CompatibilityGate
    {
        internal static CompatibilityResult Evaluate(FeatureHost features)
        {
            var failures = new List<string>();
            try { features.ValidateCompatibility(failures); }
            catch (Exception exception) { failures.Add("feature compatibility validation threw: " + exception.GetType().Name); }

            return failures.Count == 0
                ? new CompatibilityResult(true, "runtime contract verified (" + RuntimeDiagnostics() + ")")
                : new CompatibilityResult(false, string.Join("; ", failures) + " (" + RuntimeDiagnostics() + ")");
        }

        internal static void RequireMethod(
            ICollection<string> failures,
            Type declaringType,
            string name,
            Type returnType,
            BindingFlags flags,
            Type[] parameterTypes,
            Func<MethodInfo, bool> additionalCheck = null)
        {
            MethodInfo[] matches;
            try
            {
                matches = declaringType.GetMethods(flags)
                    .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                    .Where(method => method.ReturnType == returnType)
                    .Where(method => ParametersMatch(method.GetParameters(), parameterTypes))
                    .Where(method => additionalCheck == null || additionalCheck(method))
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(declaringType.Name + "." + name + " contract inspection threw: " + exception.GetType().Name);
                return;
            }

            if (matches.Length != 1)
                failures.Add(declaringType.Name + "." + name + " requires one exact runtime signature; found " + matches.Length);
        }

        internal static void RequireMethodNamedReturn(
            ICollection<string> failures,
            Type declaringType,
            string name,
            string returnTypeFullName,
            BindingFlags flags,
            Type[] parameterTypes)
        {
            MethodInfo[] matches;
            try
            {
                matches = declaringType.GetMethods(flags)
                    .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                    .Where(method => string.Equals(method.ReturnType.FullName, returnTypeFullName, StringComparison.Ordinal))
                    .Where(method => ParametersMatch(method.GetParameters(), parameterTypes))
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(declaringType.Name + "." + name + " contract inspection threw: " + exception.GetType().Name);
                return;
            }

            if (matches.Length != 1)
                failures.Add(declaringType.Name + "." + name + " requires one exact runtime signature; found " + matches.Length);
        }

        internal static void RequirePatchMethod(
            ICollection<string> failures,
            Type declaringType,
            string name,
            Type[] parameterTypes)
        {
            RequireMethod(failures, declaringType, name, typeof(void),
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                parameterTypes, method => method.IsStatic);
        }

        internal static void RequireField(
            ICollection<string> failures,
            Type declaringType,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            FieldInfo[] matches;
            try
            {
                matches = declaringType.GetFields(flags | BindingFlags.DeclaredOnly)
                    .Where(field => string.Equals(field.Name, name, StringComparison.Ordinal) && field.FieldType == fieldType)
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(declaringType.Name + "." + name + " field inspection threw: " + exception.GetType().Name);
                return;
            }

            if (matches.Length != 1)
                failures.Add(declaringType.Name + "." + name + " requires one exact runtime field; found " + matches.Length);
        }

        internal static void RequireProperty(
            ICollection<string> failures,
            Type declaringType,
            string name,
            Type propertyType,
            BindingFlags flags,
            bool requireGetter,
            bool requireSetter)
        {
            PropertyInfo[] matches;
            try
            {
                matches = declaringType.GetProperties(flags)
                    .Where(property => string.Equals(property.Name, name, StringComparison.Ordinal))
                    .Where(property => property.PropertyType == propertyType && property.GetIndexParameters().Length == 0)
                    .Where(property => !requireGetter || property.GetGetMethod(true) != null)
                    .Where(property => !requireSetter || property.GetSetMethod(true) != null)
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(declaringType.Name + "." + name + " property inspection threw: " + exception.GetType().Name);
                return;
            }

            if (matches.Length != 1)
                failures.Add(declaringType.Name + "." + name + " requires one exact runtime property; found " + matches.Length);
        }

        internal static void RequireGenericMethod(
            ICollection<string> failures,
            Type declaringType,
            string name,
            BindingFlags flags,
            Type[] parameterTypes,
            bool returnsArray)
        {
            MethodInfo[] matches;
            try
            {
                matches = declaringType.GetMethods(flags)
                    .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                    .Where(method => method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1)
                    .Where(method => ParametersMatch(method.GetParameters(), parameterTypes))
                    .Where(method => GenericReturnMatches(method.ReturnType, returnsArray))
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(declaringType.Name + "." + name + " generic contract inspection threw: " + exception.GetType().Name);
                return;
            }

            if (matches.Length != 1)
                failures.Add(declaringType.Name + "." + name + " requires one exact generic runtime signature; found " + matches.Length);
        }

        internal static void RequireEnumValue(
            ICollection<string> failures,
            Type enumType,
            string name,
            int expectedValue)
        {
            try
            {
                if (!enumType.IsEnum || !Enum.IsDefined(enumType, name) || Convert.ToInt32(Enum.Parse(enumType, name)) != expectedValue)
                    failures.Add(enumType.Name + "." + name + " enum value is incompatible");
            }
            catch (Exception exception)
            {
                failures.Add(enumType.Name + "." + name + " enum inspection threw: " + exception.GetType().Name);
            }
        }

        internal static void RequireColor(
            ICollection<string> failures,
            string label,
            Color observed,
            float red,
            float green,
            float blue,
            float alpha)
        {
            const float epsilon = 0.0001f;
            if (Math.Abs(observed.r - red) > epsilon || Math.Abs(observed.g - green) > epsilon ||
                Math.Abs(observed.b - blue) > epsilon || Math.Abs(observed.a - alpha) > epsilon)
                failures.Add(label + " encoding is incompatible");
        }

        private static bool ParametersMatch(ParameterInfo[] observed, Type[] expected)
        {
            if (observed.Length != expected.Length) return false;
            for (var index = 0; index < observed.Length; index++)
            {
                if (observed[index].ParameterType != expected[index]) return false;
            }
            return true;
        }

        private static bool GenericReturnMatches(Type returnType, bool returnsArray)
        {
            if (!returnsArray)
                return returnType.IsGenericParameter && returnType.GenericParameterPosition == 0;
            var elementType = returnType.GetElementType();
            return returnType.IsArray && elementType != null && elementType.IsGenericParameter &&
                   elementType.GenericParameterPosition == 0;
        }

        private static string RuntimeDiagnostics()
        {
            try
            {
                var gameVersion = global::Version.CurrentVersion != null ? global::Version.CurrentVersion.ToString() : "unknown";
                var unityVersion = Application.unityVersion ?? "unknown";
                var bepinexVersion = typeof(BaseUnityPlugin).Assembly.GetName().Version?.ToString() ?? "unknown";
                var harmonyVersion = typeof(Harmony).Assembly.GetName().Version?.ToString() ?? "unknown";
                var valheimMvid = typeof(Player).Assembly.ManifestModule.ModuleVersionId;
                return "game " + gameVersion + ", Unity " + unityVersion + ", BepInEx " + bepinexVersion +
                       ", Harmony " + harmonyVersion + ", assembly MVID " + valheimMvid;
            }
            catch (Exception exception)
            {
                return "runtime diagnostics unavailable: " + exception.GetType().Name;
            }
        }
    }
}
