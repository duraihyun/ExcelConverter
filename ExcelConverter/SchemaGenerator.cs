using NPOI.SS.UserModel;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ExcelConvertor
{
    /// <summary>
    /// 스키마 json 생성에 필요한 정보를 생성한다.
    /// </summary>
    internal class SchemaGenerator
    {
        // 오류 값
        private const int ERROR_VALUE = -1;
        private const string KEY_CELL_NAME = "SerialNo";
        private const string SKIP_CELL_NAME = "Note";

        private ISheet? Sheet { get; set; }


        private IRow? FieldNameRow { get; set; }

        private IRow? FieldTypeRow { get; set; }

        private HashSet<int> SkipColumns { get; } = [];

        private int StartColumnIndex { get; set; } = ERROR_VALUE;

        private SchemaTemplate? CurrentSchema { get; set; }
        
        private List<string> History { get; set; } = [];


        public bool Init(ISheet sheet, Options opts)
        {
            Sheet = sheet;

            FieldNameRow = Sheet.GetRow(opts.NameRowNumber);
            FieldTypeRow = Sheet.GetRow(opts.TypeRowNumber);

            // SerialNo로 시작하는 컬럼 인덱스찾기
            ICell? serialCell = FieldNameRow.Cells.FirstOrDefault(p => p.StringCellValue == KEY_CELL_NAME);
            if (serialCell == null)
                return false;

            StartColumnIndex = serialCell.ColumnIndex;

            // 스킵 컬럼 인덱스
            foreach (var columnIndex in FieldNameRow.Cells
                                        .Where(p => p.StringCellValue == SKIP_CELL_NAME)
                                        .Select(p => p.ColumnIndex))
            {
                SkipColumns.Add(columnIndex);
            }

            // 해당 시트의 스키마 템플릿 생성
            CurrentSchema = new SchemaTemplate
            {
                Table = Sheet.SheetName,
                Target = FieldTypeRow.GetCell(0).StringCellValue,
                PrimaryKey = KEY_CELL_NAME,
                NextFieldId = 1,
                Version = 1,
            };

            for (int col = StartColumnIndex; col < FieldNameRow.LastCellNum; ++col)
            {
                if (SkipColumns.Contains(col))
                    continue;

                CurrentSchema.Fields[FieldNameRow.GetCell(col).StringCellValue] = new SchemaField(
                    CurrentSchema.NextFieldId++,
                    FieldTypeRow.GetCell(col).StringCellValue, 
                    false,
                    col);
            }

            return true;
        }



        public SchemaGenerationResult Create(string outputPath, JsonSerializerOptions opts)
        {
            return new SchemaGenerationResult
            {
                Sheet = Sheet!,
                CurrentSchema = CurrentSchema!,
                Action = ActionType.Create,
                History = [],
                SkipColumns = SkipColumns,
                StartColumIndex = StartColumnIndex,
            };
        }


        public SchemaGenerationResult Update(string outputPath, Options opts)
        {
            // 기존 스키마 파일 로드
            string jsonString = File.ReadAllText(outputPath);

            var originSchema = JsonSerializer.Deserialize<SchemaTemplate>(jsonString);

            // 원본 스키마와 현재 스키마 비교 및 조정
            return ReconcileSchemas(Sheet!, originSchema!, CurrentSchema!, SkipColumns, StartColumnIndex, opts.IsReadOnly, opts.ForceFieldTypeOverwrite);
        }


        public static void GenerateSchemaJson(List<SchemaGenerationResult> source, string outputDir, JsonSerializerOptions jsonOptions)
        {
            foreach (var result in source)
            {
                if (ActionHandlers.TryGetValue(result.Action, out var handler))
                {
                    string outputPath = Path.Combine(outputDir, $"{result.CurrentSchema.Table}.schema.json");
                    handler(result, outputPath, jsonOptions);
                }
            }
        }


        private static readonly Dictionary<ActionType, Action<SchemaGenerationResult, string, JsonSerializerOptions>> ActionHandlers = new()
        {
            [ActionType.Create] = (result, path, opts) =>
            {
                using var sw = new StreamWriter(path);
                string jsonString = JsonSerializer.Serialize(result.CurrentSchema, opts);
                sw.Write(jsonString);
                sw.Flush();

                Console.WriteLine($"Schema for '{result.CurrentSchema.Table}' created at '{path}'.");
            },
            [ActionType.Update] = (result, path, opts) =>
            {
                string jsonString = JsonSerializer.Serialize(result.CurrentSchema, opts);
                File.WriteAllText(path, jsonString);

                Console.WriteLine($"Schema for '{result.CurrentSchema.Table}' updated at '{path}':");
                result.History.ForEach(log => Console.WriteLine($"  {log}"));
            },
            [ActionType.Error] = (result, _, _) =>
            {
                Console.WriteLine($"[Error] Schema generation for '{result.CurrentSchema.Table}' failed:");
                result.History.ForEach(log => Console.WriteLine($"  {log}"));
            },
            [ActionType.None] = (result, _, _) =>
            {
                Console.WriteLine($"Schema for '{result.CurrentSchema.Table}' is already up to date.");
            }
        };


        /// <summary>
        /// 기존 스키마와 새 스키마를 비교하여 조정한다.
        /// </summary>
        /// <param name="existingSchema">기존 스키마</param>
        /// <param name="newSchema">새 스키마</param>
        /// <param name="forceFieldTypeOverwrite">필드 타입 변경을 강제로 적용한다. 주의: 배포 이후에는 절대 덮어쓰면 안된다.</param>
        /// <returns></returns>
        private static SchemaGenerationResult ReconcileSchemas(ISheet sheet, SchemaTemplate existingSchema, SchemaTemplate newSchema, HashSet<int> skips, int startColumIndex, bool isReadOnly, bool forceFieldTypeOverwrite = false)
        {
            bool hasError = false;
            bool hasUpdate = false;
            var updatedSchema = existingSchema.Clone();
            var log = new List<string>();

            // 읽기 전용인 경우에도 엑셀을 파싱할 수 있도록 컬럼 인덱스를 동기화해야한다. (json 저장만 안함)

            if (updatedSchema.Target != newSchema.Target)
            {
                updatedSchema.Target = newSchema.Target; // 배포 타겟은 변경 가능
                log.Add($"[Info] Target changed: {updatedSchema.Target} -> {newSchema.Target}");

                // TODO: 배포 타겟 변경이 버전 갱신 대상인가?? 고민해보자
                hasUpdate = true;
            }

            foreach (var (fieldName, newField) in newSchema.Fields)
            {
                if (updatedSchema.Fields.TryGetValue(fieldName, out var existingField))
                {
                    // 컬럼 인덱스는 최신 엑셀 기준으로 갱신 (메모리에서만 사용)
                    updatedSchema.Fields[fieldName] = existingField with { ColumnIndex = newField.ColumnIndex };

                    // 필드 타입 변경 확인
                    if (existingField.Type != newField.Type)
                    {
                        if (forceFieldTypeOverwrite)
                        {
                            updatedSchema.Fields[fieldName] = existingField with { Type = newField.Type };
                            log.Add($"[Warn] Field type updated for '{fieldName}': {existingField.Type} -> {newField.Type}");
                            hasUpdate = true;
                        }
                        else
                        {
                            log.Add($"[Error] Field type mismatch for '{fieldName}': existing is '{existingField.Type}', new is '{newField.Type}'. Type change not allowed. Create new field");
                            hasError = true;
                        }
                    }

                    // Deprecated 필드 재활성화
                    if (existingField.Deprecated)
                    {
                        updatedSchema.Fields[fieldName] = existingField with { Deprecated = false };
                        log.Add($"[Info] Field re-enabled: {fieldName}");
                        hasUpdate = true;
                    }
                }
                else
                {
                    // 새 필드 추가 (컬럼 인덱스는 바이너리 생성에 사용)
                    updatedSchema.Fields[fieldName] = new SchemaField(updatedSchema.NextFieldId++, newField.Type, false, newField.ColumnIndex);
                    log.Add($"[Info] New field added: {fieldName} (Type: {newField.Type})");
                    hasUpdate = true;
                }
            }

            if (hasError)
            {
                return new SchemaGenerationResult
                {
                    Sheet = sheet,
                    CurrentSchema = updatedSchema,
                    Action = ActionType.Error,
                    History = log,
                    SkipColumns = [],
                    StartColumIndex = ERROR_VALUE,
                };
            }

            // newSchema에 없는 필드는 deprecated 처리
            foreach (var (fieldName, existingField) in updatedSchema.Fields.ToList())
            {
                if (!existingField.Deprecated && !newSchema.Fields.ContainsKey(fieldName))
                {
                    updatedSchema.Fields[fieldName] = existingField with { Deprecated = true };
                    log.Add($"[Info] Field deprecated: {fieldName}");
                    hasUpdate = true;
                }
            }

            if (hasUpdate && !isReadOnly)
            {
                updatedSchema.Version += 1;
                log.Add($"[Info] Schema version updated to {updatedSchema.Version}");
            }

            return new SchemaGenerationResult
            {
                Sheet = sheet,
                CurrentSchema = updatedSchema,
                Action = hasUpdate && !isReadOnly ? ActionType.Update : ActionType.None,
                History = log,
                SkipColumns = skips,
                StartColumIndex = startColumIndex,
            };
        }
    }
}
