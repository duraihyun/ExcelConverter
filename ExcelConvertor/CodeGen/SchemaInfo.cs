using System.Collections.Generic;


namespace ExcelConvertor.CodeGen
{
    /// <summary>
    /// 단일 스키마에 대한 템플릿용 데이터 모델
    /// </summary>
    internal class SchemaInfo
    {
        public SchemaTemplate Schema { get; private set; }

        // 템플릿에서 사용하기 편하도록 미리 계산된 값들
        public string StructName => $"{Schema.Table}Data";
        public string TableClassName => $"{Schema.Table}DataTable";
        public string LoaderClassName => $"{Schema.Table}DataTableLoader";
        public string PrimaryKeyName => Schema.PrimaryKey;
        public string PrimaryKeyType { get; private set; }
        public string PrimaryKeyParameterName => ToCamelCase(Schema.PrimaryKey);

        // string이 아닌 필드들 (e.g., int, float, SoundType)
        public IEnumerable<KeyValuePair<string, SchemaField>> ValueFields { get; private set; }

        // string 타입인 필드들
        public IEnumerable<KeyValuePair<string, SchemaField>> StringFields { get; private set; }


        public IEnumerable<SortableField> SortableFields { get; private set; }


        // 크기에 따라 정렬된 필드 정보 (타입, 이름만 필요)


        // 로더 생성을 위한 모든 필드 정보
        public IEnumerable<FieldInfo> AllFields { get; private set; }


        public SchemaInfo(SchemaTemplate schema)
        {
            Schema = schema;

            // PrimaryKey의 C# 타입 찾기
            PrimaryKeyType = schema.Fields.TryGetValue(schema.PrimaryKey, out var pkField)
                ? pkField.Type
                : "int"; // 기본값 또는 오류 처리

            // 필드를 값 타입과 문자열 타입 분리
            var valueFields = new List<KeyValuePair<string, SchemaField>>();
            var stringFields = new List<KeyValuePair<string, SchemaField>>();
            var allFields = new List<FieldInfo>();
            var sortableFields = new List<SortableField>();

            foreach (var field in schema.Fields)
            {
                if (field.Value.Deprecated)
                    continue;

                var fieldInfo = new FieldInfo(field.Key, field.Value);
                allFields.Add(fieldInfo);

                if (field.Value.Type == "string")
                {
                    stringFields.Add(field);
                    sortableFields.Add(new SortableField(4, "int", $"_{field.Key.ToLower()}Index"));
                }
                else
                {
                    // 열거형 필드의 타입 이름에서 접두사 "e." 제거
                    if (field.Value.Type.StartsWith("e."))
                    {
                        var cleanTypeField = new SchemaField
                        {
                            Id = field.Value.Id,
                            Type = field.Value.Type.Substring(2),
                            Deprecated = field.Value.Deprecated,
                        };
                        valueFields.Add(new KeyValuePair<string, SchemaField>(field.Key, cleanTypeField));
                        sortableFields.Add(new SortableField(4, cleanTypeField.Type, field.Key));
                    }
                    else
                    {
                        valueFields.Add(field);

                        // Sizeof(field.Value.Type), field.Value.Type, field.Key
                        sortableFields.Add(new SortableField(Sizeof(field.Value.Type), field.Value.Type, field.Key));
                    }
                }
            }

            // 크기가 큰 필드부터 작은 필드로 정렬, 같은 크기인 경우 _로 시작하지 않는 필드가 먼저 오도록 정렬
            sortableFields.Sort((a, b) =>
            {
                var sizeComparison = b.Size.CompareTo(a.Size);
                if (sizeComparison != 0)
                    return sizeComparison;
                
                var aStartsWithUnderscore = a.Name.StartsWith('_');
                var bStartsWithUnderscore = b.Name.StartsWith('_');
                
                if (aStartsWithUnderscore == bStartsWithUnderscore)
                    return 0;
                
                return aStartsWithUnderscore ? 1 : -1;
            });

            ValueFields = valueFields;
            StringFields = stringFields;
            AllFields = allFields;
            SortableFields = sortableFields;
        }

        private static string ToCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str) || char.IsLower(str, 0))
                return str;
            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }

        private static int Sizeof(string type)
        {
            return type.ToLower() switch
            {
                "bool" => 1,
                "byte" => 1,
                "short" => 2,
                "ushort" => 2,
                "int" => 4,
                "uint" => 4,
                "float" => 4,
                "long" => 8,
                "ulong" => 8,
                "double" => 8,
                _ => 4, // 기본값 (참조형 또는 알 수 없는 타입)
            };
        }
    }

    public readonly record struct SortableField(int Size, string Type, string Name);

}
