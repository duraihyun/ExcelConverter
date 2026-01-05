using System;


namespace ExcelConvertor.CodeGen
{
    internal class FieldInfo
    {
        public string Name { get; }
        public string CamelCaseName { get; }
        public int Id { get; }
        public string Type { get; }
        public string CleanType { get; }
        public bool IsEnum { get; }
        public bool IsString { get; }
        public string ReadStatement { get; }

        public FieldInfo(string name, SchemaField field)
        {
            Name = name;
            CamelCaseName = ToCamelCase(name);
            Id = field.Id;
            Type = field.Type;
            IsString = Type == "string";
            IsEnum = Type.StartsWith("e.");
            CleanType = IsEnum ? Type.Substring(2) : Type;

            if (IsString)
            {
                ReadStatement = $"{CamelCaseName}Index = reader.ReadInt32();";
            }
            else if (IsEnum)
            {
                ReadStatement = $"{CamelCaseName} = Enum.Parse<{CleanType}>(Encoding.UTF8.GetString(reader.ReadBytes(length)));";
            }
            else
            {
                ReadStatement = $"{CamelCaseName} = reader.{GetReaderMethod(Type)}();";
            }
        }

        private static string GetReaderMethod(string type)
        {
            return type switch
            {
                "bool" => "ReadBoolean",
                "byte" => "ReadByte",
                "char" => "ReadChar",
                "short" => "ReadInt16",
                "ushort" => "ReadUInt16",
                "int" => "ReadInt32",
                "uint" => "ReadUInt32",
                "long" => "ReadInt64",
                "ulong" => "ReadUInt64",
                "float" => "ReadSingle",
                "double" => "ReadDouble",
                "decimal" => "ReadDecimal",
                _ => throw new NotSupportedException($"Unsupported type for BinaryReader: {type}")
            };
        }

        private static string ToCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str) || char.IsLower(str, 0))
                return str;
            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }
    }
}
