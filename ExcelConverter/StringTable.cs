using NPOI.SS.UserModel;

using System;
using System.Collections.Generic;

namespace ExcelConvertor
{
    /// <summary>
    /// 생성된 코드에서 사용하는 스트링 테이블과 다름..
    /// </summary>
    internal class StringTable
    {
        private int _nextIndex = 0;

        private readonly Dictionary<string, int> _stringToId = [];

        public void AddString(string value)
        {
            if (!_stringToId.ContainsKey(value))
            {
                _stringToId[value] = _nextIndex++;
            }
        }

        public int GetStringId(string value)
        {
            if (_stringToId.TryGetValue(value, out int id))
            {
                return id;
            }
            else
            {
                throw new KeyNotFoundException($"String '{value}' not found in the string table.");
            }
        }

        public string[] ToArray()
        {
            var result = new string[_stringToId.Count];
            foreach (var kvp in _stringToId)
            {
                result[kvp.Value] = kvp.Key;
            }
            return result;
        }


        public static StringTable? MakeTable(List<SchemaGenerationResult> source, int headerRowCount)
        {
            var table = new StringTable();

            foreach (var schema in source)
            {
                if (schema.CurrentSchema.Deprecated)
                    continue;

                // 데이터 행 순회 (헤더 제외)
                for (int i = headerRowCount; i <= schema.Sheet.LastRowNum; ++i)
                {
                    var row = schema.Sheet.GetRow(i);
                    if (row == null)
                    {
                        Console.WriteLine($"Warning: Null row at index {i} in sheet {schema.Sheet.SheetName}.");
                        return null;
                    }

                    foreach (var field in schema.CurrentSchema.Fields)
                    {
                        if (field.Value.Deprecated)
                            continue;

                        if (false == string.Equals(field.Value.Type, "string", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var cell = row.GetCell(field.Value.ColumnIndex);
                        if (cell == null || cell.CellType == CellType.Blank)
                        {
                            // 빈 셀은 건너뛰기
                            continue;
                        }

                        table.AddString(cell.StringCellValue);
                    }
                }
            }

            return table;
        }
    }
}
