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
            var contract = new Contract(reader, pe);

            contract.Method("Player", "HaveRequirements", "System.Boolean",
                new[] { "Piece", "RequirementMode" }, MethodAttributes.Public);
            contract.Method("Player", "GetRunSpeedFactor", "System.Single", Array.Empty<string>(),
                MethodAttributes.Family | MethodAttributes.Virtual);
            contract.Method("SEMan", "ModifyRunStaminaDrain", "System.Void",
                new[] { "System.Single", "System.Single&", "UnityEngine.Vector3", "System.Boolean" }, MethodAttributes.Public);
            contract.Method("CraftingStation", "HaveBuildStationInRange", "CraftingStation",
                new[] { "System.String", "UnityEngine.Vector3" }, MethodAttributes.Public | MethodAttributes.Static);
            contract.HaveRequirementsStationCallSequence();
            contract.HaveRequirementsStationFailureBranch();
            contract.RequirementCallSite("Player", "UpdatePlacement", new[] { "System.Boolean", "System.Single" }, 0, true);
            contract.RequirementCallSite("BuildUiPieceButton", "UpdateRequirements", Array.Empty<string>(), 0, false);
            contract.RequirementCallSite("Hud", "UpdatePieceBuildStatus", new[] { "System.Collections.Generic.List`1<Piece>", "Player" }, 0, false);
            contract.RequirementCallSite("Hud", "UpdatePieceBuildStatusAll", new[] { "System.Collections.Generic.List`1<Piece>", "Player" }, 0, false);
            contract.RequirementCallSite("PieceTable", "UpdateAvailable", new[] { "System.Collections.Generic.HashSet`1<System.String>", "Player", "System.Boolean", "System.Boolean" }, 2, false);
            contract.NoDirectStationCall("Player", "TryPlacePiece", new[] { "Piece" });
            contract.Method("Character", "IsOnGround", "System.Boolean", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Character", "IsRunning", "System.Boolean", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Character", "GetLastGroundCollider", "UnityEngine.Collider", Array.Empty<string>(), MethodAttributes.Public);
            contract.Method("Heightmap", "GetPaintMask", "UnityEngine.Color", new[] { "UnityEngine.Vector3" }, MethodAttributes.Public);

            contract.Field("Player", "m_localPlayer", "Player", FieldAttributes.Public | FieldAttributes.Static);
            contract.Field("Piece", "m_name", "System.String", FieldAttributes.Public);
            contract.Field("Piece", "m_craftingStation", "CraftingStation", FieldAttributes.Public);
            contract.Field("CraftingStation", "m_name", "System.String", FieldAttributes.Public);
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
            contract.EnumValues("Player", "RequirementMode", new Dictionary<string, int>
            {
                ["CanBuild"] = 0,
                ["IsKnown"] = 1,
                ["CanAlmostBuild"] = 2
            });

            Console.WriteLine(_passed + "/28 compatibility contract checks passed");
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
            private readonly PEReader _pe;
            private readonly TypeNameProvider _provider;

            internal Contract(MetadataReader reader, PEReader pe)
            {
                _reader = reader;
                _pe = pe;
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

            internal void HaveRequirementsStationCallSequence()
            {
                var player = FindTopLevel("Player");
                var haveRequirementsHandle = player.GetMethods().Single(handle =>
                {
                    var method = _reader.GetMethodDefinition(handle);
                    var signature = method.DecodeSignature(_provider, null);
                    return _reader.GetString(method.Name) == "HaveRequirements" &&
                           signature.ReturnType == "System.Boolean" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "Piece", "RequirementMode" });
                });
                var station = FindTopLevel("CraftingStation");
                var stationMethodHandle = station.GetMethods().Single(handle =>
                {
                    var method = _reader.GetMethodDefinition(handle);
                    var signature = method.DecodeSignature(_provider, null);
                    return _reader.GetString(method.Name) == "HaveBuildStationInRange" &&
                           signature.ReturnType == "CraftingStation" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "System.String", "UnityEngine.Vector3" });
                });
                var implicitHandle = _reader.MemberReferences.Single(handle =>
                {
                    var member = _reader.GetMemberReference(handle);
                    if (_reader.GetString(member.Name) != "op_Implicit" || member.Parent.Kind != HandleKind.TypeReference)
                        return false;
                    var parent = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    if (_reader.GetString(parent.Namespace) != "UnityEngine" || _reader.GetString(parent.Name) != "Object")
                        return false;
                    var signature = member.DecodeMethodSignature(_provider, null);
                    return signature.ReturnType == "System.Boolean" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Object" });
                });

                var firstToken = MetadataTokens.GetToken(stationMethodHandle);
                var secondToken = MetadataTokens.GetToken(implicitHandle);
                var pattern = new byte[10];
                pattern[0] = 0x28;
                BitConverter.GetBytes(firstToken).CopyTo(pattern, 1);
                pattern[5] = 0x28;
                BitConverter.GetBytes(secondToken).CopyTo(pattern, 6);

                var definition = _reader.GetMethodDefinition(haveRequirementsHandle);
                var il = _pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()
                    ?? throw new InvalidOperationException("Player.HaveRequirements has no IL body");
                var matches = 0;
                for (var index = 0; index <= il.Length - pattern.Length; index++)
                {
                    if (il.AsSpan(index, pattern.Length).SequenceEqual(pattern)) matches++;
                }
                if (matches != 1)
                    throw new InvalidOperationException("Player.HaveRequirements station call sequence mismatch: " + matches);
                _passed++;
                Console.WriteLine("PASS IL Player.HaveRequirements station call sequence");
            }

            internal void HaveRequirementsStationFailureBranch()
            {
                var haveRequirementsHandle = FindMethodHandle("Player", "HaveRequirements", new[] { "Piece", "RequirementMode" });
                var stationHandle = FindMethodHandle("CraftingStation", "HaveBuildStationInRange", new[] { "System.String", "UnityEngine.Vector3" });
                var zoneInstanceHandle = FindMethodHandle("ZoneSystem", "get_instance", Array.Empty<string>());
                var globalKeyHandle = FindMethodHandle("ZoneSystem", "GetGlobalKey", new[] { "GlobalKeys" });
                var implicitHandle = _reader.MemberReferences.Single(handle =>
                {
                    var member = _reader.GetMemberReference(handle);
                    if (_reader.GetString(member.Name) != "op_Implicit" || member.Parent.Kind != HandleKind.TypeReference)
                        return false;
                    var parent = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    if (_reader.GetString(parent.Namespace) != "UnityEngine" || _reader.GetString(parent.Name) != "Object")
                        return false;
                    var signature = member.DecodeMethodSignature(_provider, null);
                    return signature.ReturnType == "System.Boolean" &&
                           signature.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Object" });
                });

                var il = MethodIl(haveRequirementsHandle);
                var stationCall = FindCallOffsets(il, MetadataTokens.GetToken(stationHandle)).Single();
                if (stationCall + 28 >= il.Length ||
                    il[stationCall] != 0x28 ||
                    ReadToken(il, stationCall + 1) != MetadataTokens.GetToken(stationHandle) ||
                    il[stationCall + 5] != 0x28 ||
                    ReadToken(il, stationCall + 6) != MetadataTokens.GetToken(implicitHandle) ||
                    il[stationCall + 10] != 0x2d ||
                    il[stationCall + 12] != 0x28 ||
                    ReadToken(il, stationCall + 13) != MetadataTokens.GetToken(zoneInstanceHandle) ||
                    il[stationCall + 17] != 0x1f || il[stationCall + 18] != 27 ||
                    il[stationCall + 19] != 0x6f ||
                    ReadToken(il, stationCall + 20) != MetadataTokens.GetToken(globalKeyHandle) ||
                    il[stationCall + 24] != 0x2d ||
                    il[stationCall + 26] != 0x16 || il[stationCall + 27] != 0x2a)
                {
                    throw new InvalidOperationException("Player.HaveRequirements station-failure branch shape mismatch");
                }

                var stationSuccessTarget = stationCall + 12 + unchecked((sbyte)il[stationCall + 11]);
                var freeBuildSuccessTarget = stationCall + 26 + unchecked((sbyte)il[stationCall + 25]);
                if (stationSuccessTarget != stationCall + 28 || freeBuildSuccessTarget != stationCall + 28)
                    throw new InvalidOperationException("Player.HaveRequirements station-failure branch target mismatch");

                _passed++;
                Console.WriteLine("PASS IL Player.HaveRequirements complete station-failure branch");
            }

            internal void RequirementCallSite(
                string typeName,
                string methodName,
                string[] parameters,
                int expectedMode,
                bool requireTryPlaceAfter)
            {
                var methodHandle = FindMethodHandle(typeName, methodName, parameters);
                var haveRequirementsHandle = FindMethodHandle("Player", "HaveRequirements", new[] { "Piece", "RequirementMode" });
                var il = MethodIl(methodHandle);
                var calls = FindCallOffsets(il, MetadataTokens.GetToken(haveRequirementsHandle));
                if (calls.Count != 1)
                    throw new InvalidOperationException(typeName + "." + methodName + " requirement call count mismatch: " + calls.Count);

                var expectedModeOpcode = expectedMode switch
                {
                    0 => (byte)0x16,
                    1 => (byte)0x17,
                    2 => (byte)0x18,
                    _ => throw new ArgumentOutOfRangeException(nameof(expectedMode))
                };
                if (calls[0] == 0 || il[calls[0] - 1] != expectedModeOpcode)
                    throw new InvalidOperationException(typeName + "." + methodName + " requirement mode mismatch");

                if (requireTryPlaceAfter)
                {
                    var tryPlaceHandle = FindMethodHandle("Player", "TryPlacePiece", new[] { "Piece" });
                    var tryPlaceCalls = FindCallOffsets(il, MetadataTokens.GetToken(tryPlaceHandle));
                    if (tryPlaceCalls.Count != 1 || tryPlaceCalls[0] <= calls[0])
                        throw new InvalidOperationException(typeName + "." + methodName + " placement call ordering mismatch");
                }

                _passed++;
                Console.WriteLine("PASS IL " + typeName + "." + methodName + " requirement path");
            }

            internal void NoDirectStationCall(string typeName, string methodName, string[] parameters)
            {
                var methodHandle = FindMethodHandle(typeName, methodName, parameters);
                var stationHandle = FindMethodHandle("CraftingStation", "HaveBuildStationInRange", new[] { "System.String", "UnityEngine.Vector3" });
                var calls = FindCallOffsets(MethodIl(methodHandle), MetadataTokens.GetToken(stationHandle));
                if (calls.Count != 0)
                    throw new InvalidOperationException(typeName + "." + methodName + " unexpectedly performs a direct station-range check");
                _passed++;
                Console.WriteLine("PASS IL " + typeName + "." + methodName + " has no direct station check");
            }

            private MethodDefinitionHandle FindMethodHandle(string typeName, string methodName, string[] parameters)
            {
                var type = FindTopLevel(typeName);
                return type.GetMethods().Single(handle =>
                {
                    var method = _reader.GetMethodDefinition(handle);
                    var signature = method.DecodeSignature(_provider, null);
                    return _reader.GetString(method.Name) == methodName && signature.ParameterTypes.SequenceEqual(parameters);
                });
            }

            private byte[] MethodIl(MethodDefinitionHandle handle)
            {
                var definition = _reader.GetMethodDefinition(handle);
                return _pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()
                    ?? throw new InvalidOperationException("Method has no IL body");
            }

            private static List<int> FindCallOffsets(byte[] il, int token)
            {
                var offsets = new List<int>();
                for (var index = 0; index <= il.Length - 5; index++)
                {
                    if ((il[index] == 0x28 || il[index] == 0x6f) && ReadToken(il, index + 1) == token)
                        offsets.Add(index);
                }
                return offsets;
            }

            private static int ReadToken(byte[] il, int offset) => BitConverter.ToInt32(il, offset);

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
