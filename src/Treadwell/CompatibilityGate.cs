#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
        // Exact runtime supplied by the verified Valheim 1.0.14 / Steam build 25364309 reference bundle.
        private static readonly Guid SupportedValheimMvid = new Guid("a63433e8-968e-407a-918a-9f9fe7e7ba9a");
        private const string SupportedValheimSha256 = "e5af0669755ed3b098f71b4dd0753f8a997761b99bca1e8dac3d5ca4c706a0be";
        private const string SupportedGameVersion = "1.0.14";
        private const string SupportedUnityVersion = "6000.0.75f1";
        private const string SupportedBepInExVersion = "5.4.23.5";
        private const string SupportedHarmonyVersion = "2.9.0.0";

        internal static CompatibilityResult Evaluate(FeatureHost features)
        {
            var failures = new List<string>();
            RequireEqual(failures, "Valheim API", global::Version.CurrentVersion.ToString(), SupportedGameVersion);
            RequireEqual(failures, "Unity", Application.unityVersion, SupportedUnityVersion);
            RequireAssemblyVersion(failures, typeof(BaseUnityPlugin).Assembly, SupportedBepInExVersion, "BepInEx");
            RequireAssemblyVersion(failures, typeof(Harmony).Assembly, SupportedHarmonyVersion, "Harmony");

            var valheimAssembly = typeof(Player).Assembly;
            if (valheimAssembly.ManifestModule.ModuleVersionId != SupportedValheimMvid)
                failures.Add("assembly_valheim MVID is not the verified build");
            try
            {
                if (!string.Equals(Sha256(valheimAssembly.Location), SupportedValheimSha256, StringComparison.Ordinal))
                    failures.Add("assembly_valheim SHA-256 is not the verified build");
            }
            catch (Exception exception)
            {
                failures.Add("assembly_valheim SHA-256 could not be verified: " + exception.GetType().Name);
            }

            try { features.ValidateCompatibility(failures); }
            catch (Exception exception) { failures.Add("feature compatibility validation threw: " + exception.GetType().Name); }

            return failures.Count == 0
                ? new CompatibilityResult(true, "verified runtime surface")
                : new CompatibilityResult(false, string.Join("; ", failures));
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
            var method = declaringType.GetMethod(name, flags, null, parameterTypes, null);
            if (method == null || method.ReturnType != returnType || (additionalCheck != null && !additionalCheck(method)))
                failures.Add(declaringType.Name + "." + name + " signature is not the verified build");
        }

        internal static void RequireMethodNamedReturn(
            ICollection<string> failures,
            Type declaringType,
            string name,
            string returnTypeFullName,
            BindingFlags flags,
            Type[] parameterTypes)
        {
            var method = declaringType.GetMethod(name, flags, null, parameterTypes, null);
            if (method == null || !string.Equals(method.ReturnType.FullName, returnTypeFullName, StringComparison.Ordinal))
                failures.Add(declaringType.Name + "." + name + " signature is not the verified build");
        }

        internal static void RequireField(
            ICollection<string> failures,
            Type declaringType,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            var field = declaringType.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                failures.Add(declaringType.Name + "." + name + " field is not the verified build");
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
                failures.Add(label + " encoding is not the verified build");
        }

        private static void RequireEqual(ICollection<string> failures, string label, string observed, string expected)
        {
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
                failures.Add(label + " version " + observed + " != " + expected);
        }

        private static void RequireAssemblyVersion(ICollection<string> failures, Assembly assembly, string expected, string label)
            => RequireEqual(failures, label, assembly.GetName().Version?.ToString() ?? "unknown", expected);

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
            {
                var hash = algorithm.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }
    }
}
