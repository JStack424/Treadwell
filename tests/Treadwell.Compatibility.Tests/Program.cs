using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Treadwell.Compatibility.Tests
{
    internal static class Program
    {
        private const string ExpectedSha256 = "27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84";
        private static readonly Guid ExpectedMvid = new Guid("b8a6fd30-3061-43b3-99f2-11c2e315bc54");
        private static int _passed;

        private static int Main(string[] args)
        {
            if (args.Length != 1) throw new ArgumentException("Expected path to assembly_valheim.dll.");
            var path = Path.GetFullPath(args[0]);
            Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), "assembly SHA-256");

            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            Equal(ExpectedMvid, reader.GetGuid(reader.GetModuleDefinition().Mvid), "assembly MVID");
            var contract = new Contract(reader);

            contract.Method("Player", "GetRunSpeedFactor", "System.Single", Array.Empty<string>(),
                MethodAttributes.Family | MethodAttributes.Virtual);
            contract.Method("SEMan", "ModifyRunStaminaDrain", "System.Void",
                new[] { "System.Single", "System.Single&", "UnityEngine.Vector3", "System.Boolean" }, MethodAttributes.Public);
            contract.Method("Character", "IsOnGround", "System.Boolean", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Character", "IsRunning", "System.Boolean", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Character", "GetLastGroundCollider", "UnityEngine.Collider", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Heightmap", "GetPaintMask", "UnityEngine.Color", new[] { "UnityEngine.Vector3" }, MethodAttributes.Public);

            contract.Field("Player", "m_localPlayer", "Player", FieldAttributes.Public | FieldAttributes.Static);
            contract.Field("SEMan", "m_character", "Character", FieldAttributes.Private);
            contract.Field("Heightmap", "m_paintMaskDirt", "UnityEngine.Color", FieldAttributes.Public | FieldAttributes.Static);
            contract.Field("Heightmap", "m_paintMaskCultivated", "UnityEngine.Color", FieldAttributes.Public | FieldAttributes.Static);
            contract.Field("Heightmap", "m_paintMaskPaved", "UnityEngine.Color", FieldAttributes.Public | FieldAttributes.Static);

            contract.EnumValues("TerrainModifier", "PaintType", new Dictionary<string, int>
            {
                ["Dirt"] = 0,
                ["Cultivate"] = 1,
                ["Paved"] = 2
            });

            Console.WriteLine(_passed + "/14 compatibility contract checks passed");
            return 0;
        }

        private static void Equal<T>(T expected, T actual, string label) where T : notnull
        {
            if (!expected.Equals(actual)) throw new InvalidOperationException(label + " mismatch: " + actual);
            _passed++;
            Console.WriteLine("PASS " + label);
        }

        private sealed class Contract
        {
            private readonly MetadataReader _reader;
            private readonly TypeNameProvider _provider;

            internal Contract(MetadataReader reader)
            {
                _reader = reader;
                _provider = new TypeNameProvider();
            }

            internal void Method(string typeName, string methodName, string returnType, string[] parameters, MethodAttributes required)
            {
                var type = FindTopLevel(typeName);
                var matches = type.GetMethods()
                    .Select(handle => _reader.GetMethodDefinition(handle))
                    .Where(method => _reader.GetString(method.Name) == methodName)
                    .Where(method =>
                    {
                        var signature = method.DecodeSignature(_provider, null);
                        return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters);
                    }).ToArray();
                if (matches.Length != 1 || (matches[0].Attributes & required) != required)
                    throw new InvalidOperationException(typeName + "." + methodName + " signature/attributes mismatch");
                _passed++;
                Console.WriteLine("PASS method " + typeName + "." + methodName);
            }

            internal void Field(string typeName, string fieldName, string fieldType, FieldAttributes required)
            {
                var type = FindTopLevel(typeName);
                var matches = type.GetFields()
                    .Select(handle => _reader.GetFieldDefinition(handle))
                    .Where(field => _reader.GetString(field.Name) == fieldName && field.DecodeSignature(_provider, null) == fieldType)
                    .ToArray();
                if (matches.Length != 1 || (matches[0].Attributes & required) != required)
                    throw new InvalidOperationException(typeName + "." + fieldName + " signature/attributes mismatch");
                _passed++;
                Console.WriteLine("PASS field " + typeName + "." + fieldName);
            }

            internal void EnumValues(string outerName, string nestedName, IReadOnlyDictionary<string, int> expected)
            {
                var outer = _reader.GetTypeDefinition(FindTopLevelHandle(outerName));
                var nested = outer.GetNestedTypes()
                    .Select(handle => _reader.GetTypeDefinition(handle))
                    .Single(value => _reader.GetString(value.Name) == nestedName);
                foreach (var pair in expected)
                {
                    var field = nested.GetFields().Select(handle => _reader.GetFieldDefinition(handle))
                        .Single(value => _reader.GetString(value.Name) == pair.Key);
                    var constant = _reader.GetConstant(field.GetDefaultValue());
                    var value = BitConverter.ToInt32(_reader.GetBlobBytes(constant.Value), 0);
                    if (value != pair.Value) throw new InvalidOperationException(outerName + "." + nestedName + "." + pair.Key + " mismatch");
                }
                _passed++;
                Console.WriteLine("PASS enum " + outerName + "." + nestedName);
            }

            private TypeDefinition FindTopLevel(string name) => _reader.GetTypeDefinition(FindTopLevelHandle(name));

            private TypeDefinitionHandle FindTopLevelHandle(string name)
                => _reader.TypeDefinitions.Single(handle =>
                {
                    var type = _reader.GetTypeDefinition(handle);
                    return type.GetDeclaringType().IsNil && _reader.GetString(type.Name) == name;
                });
        }

        private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Void => "System.Void",
                _ => typeCode.ToString()
            };
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                var type = reader.GetTypeDefinition(handle);
                var ns = reader.GetString(type.Namespace);
                return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
            }
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var type = reader.GetTypeReference(handle);
                var ns = reader.GetString(type.Namespace);
                return string.IsNullOrEmpty(ns) ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
            }
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }
}
