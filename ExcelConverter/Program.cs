using CommandLine;

using ExcelConvertor.CodeGen;

using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ExcelConvertor
{
    enum ErrorCode
    {
        Success = 0,
        InvalidArguments = 1,
        Exception = 2,
        SchemaError = 3,
        StringTableGenerationFailed = 4,
        BinaryGenerationFailed = 5,
    }


    public class Program
    {
        private const string FILE_EXTENSION = ".xlsx";
        private const string SEARCH_PATTERN = "*" + FILE_EXTENSION;

        private static readonly JsonSerializerOptions s_serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };


        public static int Main(string[] args)
        {
            Console.WriteLine("ExcelConvertor is running...");

            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    (Options opts) =>
                    {
                        try
                        {
                            return (int)RunOptions(opts);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Exception occurred: {e.Message}");
                            Console.WriteLine(e.ToString());
                            return (int)ErrorCode.Exception;
                        }
                    },
                    errs =>
                    {
                        HandleParseError(errs);
                        return (int)ErrorCode.InvalidArguments;
                    });
        }



        static ErrorCode RunOptions(Options opts)
        {
            var excelFiles = Directory.GetFiles(opts.RootDir, SEARCH_PATTERN);

            // 이번 리비전 엑셀 기반으로 생성/갱신 된 스키마 결과 리스트
            var schemas = new List<SchemaGenerationResult>();
            var enums = new List<EnumDefinition>();

            foreach (var file in excelFiles)
            {
                // 파일 이름이 ~로 시작하면 패스
                if (Path.GetFileName(file).StartsWith('~'))
                    continue;

                var temp = GenerateSchemasFromExcelFile(file, opts);
                schemas.AddRange(temp);

                var tempEnum = GenerateEnumFromExcelFile(file);
                if (null != tempEnum)
                {
                    enums.AddRange(tempEnum);
                }

                var tempConst = GenerateConstantFromExcelFile(file, opts);
                if (null != tempConst)
                {
                    enums.Add(tempConst);
                }
            }

            // 구조 변경에 대한 보고 (단순 레코드 추가는 제외)
            // 에러가 존재하면 에러만 보고하고 종료
            var errors = schemas.FindAll(p => p.Action == ActionType.Error);
            if (0 < errors.Count)
            {
                Console.WriteLine("Errors detected during schema generation:");

                foreach (var error in errors)
                {
                    Console.WriteLine($"Error in schema {error.CurrentSchema.Table}:");

                    foreach (var msg in error.History)
                    {
                        Console.WriteLine($"  {msg}");
                    }

                    // 시트간 개행
                    Console.WriteLine();
                }

                return ErrorCode.SchemaError;
            }

            #region 스키마 갱신
            if (opts.IsReadOnly == false)
            {
                // 엑셀에서 삭제된 테이블은 스키마 파일에 Deprecated 플래그를 설정해야한다.
                ReconcileDeletedSchemas(schemas, opts.SchemaDir);

                // 변경 사항이 있는 스키마에 대해서만 JSON 생성
                var updateSchemas = schemas.Where(p => p.Action == ActionType.Create || p.Action == ActionType.Update).ToList();
                SchemaGenerator.GenerateSchemaJson(updateSchemas, opts.SchemaDir, s_serializerOptions);
            }
            #endregion

            #region 코드 생성
            if (opts.IsReadOnly == false)
            {
                var appRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

                var tableCodeGen = new CodeGenerator();
                var viewModel = new CodeGenViewModel(schemas.Select(p => p.CurrentSchema).ToList(), enums);
                tableCodeGen.GenerateFromSchema(viewModel, Path.Combine(appRoot, "Scriban", "Data.cs.scriban"), Path.Combine(opts.OutputDir, "DataTable.cs"));
                tableCodeGen.GenerateFromSchema(viewModel, Path.Combine(appRoot, "Scriban", "DataTableManager.cs.scriban"), Path.Combine(opts.OutputDir, "DataTableManager.cs"));
                tableCodeGen.GenerateFromSchema(viewModel, Path.Combine(appRoot, "Scriban", "DataTableManager.Server.cs.scriban"), Path.Combine(opts.OutputDir, "DataTableManager.Server.cs"));
                tableCodeGen.GenerateFromSchema(viewModel, Path.Combine(appRoot, "Scriban", "DataTableManager.Client.cs.scriban"), Path.Combine(opts.OutputDir, "DataTableManager.Client.cs"));
                tableCodeGen.GenerateFromSchema(viewModel, Path.Combine(appRoot, "Scriban", "Data.Enum.cs.scriban"), Path.Combine(opts.OutputDir, "Data.Enum.cs"));
            }
            #endregion

            var manifest = new ManifestTemplate
            {
                Revision = opts.Revision,
                GeneratedAt = DateTime.UtcNow,
            };
            #region 바이너리 생성
            {
                // 바이너리를 만들기 전에 스트링 테이블을 메모리에 생성해야한다.
                StringTable? stringTable = StringTable.MakeTable(schemas, opts.HeaderRowCount);
                if (stringTable == null)
                {
                    Console.WriteLine("String table generation failed due to previous errors.");
                    return ErrorCode.StringTableGenerationFailed;
                }

                IEncryptionKeyProvider keyProvider = opts.IsDevelopment ? new DevelopmentKeyProvider() : new ProductionKeyProvider(opts.EncryptionKey);
                var binaryGenerator = new BinarySerializer(keyProvider);

                var stringTableName = "StringTable.bytes";
                // 문자열 테이블 바이너리 저장
                if (false == binaryGenerator.SaveStringTable(stringTable, Path.Combine(opts.OutputDir, stringTableName), out string stringTableHash))
                {
                    Console.WriteLine("Failed to save string table binary.");
                    return ErrorCode.BinaryGenerationFailed;
                }

                manifest.Hash[stringTableName] = stringTableHash;

                // 데이터 테이블 바이너리 저장 (문자열은 테이블 인덱스로 컨버트해서 저장)
                foreach (var schema in schemas)
                {
                    var fileName = $"{schema.CurrentSchema.Table}.bytes";
                    var outputPath = Path.Combine(opts.OutputDir, fileName);
                    if (false == binaryGenerator.GenerateBinary(schema, stringTable, outputPath, opts.HeaderRowCount, out string fileHash))
                    {
                        Console.WriteLine($"Failed to generate binary for table {schema.CurrentSchema.Table}.");
                        return ErrorCode.BinaryGenerationFailed;
                    }

                    manifest.Hash[fileName] = fileHash;
                }
            }
            #endregion

            #region 매니페스트 생성
            {
                // 매니페스트 생성 및 갱신
                var manifestPath = Path.Combine(opts.OutputDir, "Manifest.json");
                var manifestJson = JsonSerializer.Serialize(manifest, s_serializerOptions);

                if (Path.Exists(manifestPath))
                {
                    File.WriteAllText(manifestPath, manifestJson);
                }
                else
                {
                    using var sw = new StreamWriter(manifestPath);
                    sw.Write(manifestJson);
                    sw.Flush();
                }
            }
            #endregion

            return ErrorCode.Success;
        }



        static void HandleParseError(IEnumerable<CommandLine.Error> errs)
        {
        }


        private static List<SchemaGenerationResult> GenerateSchemasFromExcelFile(string filePath, Options opts)
        {
            using var fs = File.OpenRead(filePath);
            var workbook = new XSSFWorkbook(fs);

            var results = new List<SchemaGenerationResult>();

            int sheetCount = workbook.NumberOfSheets;

            for (int i = 0; i < sheetCount; ++i)
            {
                if (workbook.IsSheetHidden(i) || workbook.IsSheetVeryHidden(i))
                    continue;

                var sheet = workbook.GetSheetAt(i);

                // 빈 시트거나 Desc_로 시작하는 시트는 패스
                if (sheet.PhysicalNumberOfRows < 2 || 
                    sheet.SheetName.StartsWith("Desc_", StringComparison.OrdinalIgnoreCase) ||
                    sheet.SheetName.StartsWith("Enum", StringComparison.OrdinalIgnoreCase))
                    continue;

                Console.WriteLine($"Sheet {i}: {sheet.SheetName}");

                var schemaGenerator = new SchemaGenerator();
                if (!schemaGenerator.Init(sheet, opts))
                    continue;

                var schemaPath = Path.Combine(opts.SchemaDir, $"{Path.GetFileName(sheet.SheetName)}.Schema.json");

                // 스키마 존재 (계약이 존재하는 경우)
                if (File.Exists(schemaPath) == false)
                {
                    // 스키마 생성
                    results.Add(schemaGenerator.Create(schemaPath, s_serializerOptions));
                }
                else
                {
                    // 갱신인 경우는 diff 필요
                    results.Add(schemaGenerator.Update(schemaPath, opts));
                }
            }

            return results;
        }


        private static List<EnumDefinition>? GenerateEnumFromExcelFile(string filePath)
        {
            // 열거형 테이블은 첫 셀에 "Enum_" 타입으로 열거형을 정의한다.

            const string ENUM_PREFIX = "Enum_";
            const string FLAGS_PREFIX = "Flags_";


            using var fs = File.OpenRead(filePath);
            var workbook = new XSSFWorkbook(fs);

            int sheetCount = workbook.NumberOfSheets;

            for (int i = 0; i < sheetCount; ++i)
            {
                if (workbook.IsSheetHidden(i) || workbook.IsSheetVeryHidden(i))
                    continue;

                var sheet = workbook.GetSheetAt(i);
                if (false == string.Equals(sheet.SheetName, "enum", StringComparison.OrdinalIgnoreCase))
                    continue;

                var enumInfo = new EnumInfo();

                var firstCellNum = sheet.GetRow(sheet.FirstRowNum).FirstCellNum;

                int rowBlockIndex = -1;

                // 블럭 단위로 파싱
                for (int rNum = sheet.FirstRowNum; rNum <= sheet.LastRowNum; ++rNum)
                {
                    var row = sheet.GetRow(rNum);
                    if (row == null)
                        continue;

                    var firstCell = row.GetCell(row.FirstCellNum);
                    if (firstCell.StringCellValue.StartsWith(ENUM_PREFIX) || firstCell.StringCellValue.StartsWith(FLAGS_PREFIX))
                    {
                        enumInfo.Infos[++rowBlockIndex] = new List<EnumDefinition>();
                    }

                    // 4칸 단위로 열거형 파싱 (멤버, 값, 설명, 공백)
                    for (int col = row.FirstCellNum; col < row.LastCellNum; col += 4)
                    {
                        var typeCell = row.GetCell(col);
                        var valueCell = row.GetCell(col + 1);
                        var commentCell = row.GetCell(col + 2);

                        if (typeCell == null || string.IsNullOrEmpty(typeCell.StringCellValue))
                        {
                            // 해당 열거형 정의는 끝
                            continue;
                        }

                        if (typeCell.StringCellValue.StartsWith(ENUM_PREFIX) || typeCell.StringCellValue.StartsWith(FLAGS_PREFIX))
                        {
                            var isFlags = typeCell.StringCellValue.StartsWith(FLAGS_PREFIX);
                            var temp = new EnumDefinition
                            {
                                IsFlag = isFlags,
                                Name = isFlags ? typeCell.StringCellValue.Replace(FLAGS_PREFIX, string.Empty) : typeCell.StringCellValue.Replace(ENUM_PREFIX, string.Empty),
                                StartRow = rNum,
                                StartColumn = col,
                            };

                            enumInfo.Infos[rowBlockIndex].Add(temp);
                        }
                        else
                        {
                            EnumDefinition temp = enumInfo.Infos[rowBlockIndex].Find(p => p.ContainsColumn(col))!;

                            var value = valueCell.ToString();
                            var comment = commentCell != null ? commentCell.StringCellValue : string.Empty;

                            temp.Members.Add(new EnumMember(typeCell.StringCellValue, value!, comment));
                        }
                    }
                }

                return enumInfo.GetAll();
            }

            return null;
        }

        // 엑셀에서 상수 테이블 생성
        private static EnumDefinition? GenerateConstantFromExcelFile(string filePath, Options opts)
        {
            using var fs = File.OpenRead(filePath);
            var workbook = new XSSFWorkbook(fs);

            int sheetCount = workbook.NumberOfSheets;

            for (int sheetIndex = 0; sheetIndex < sheetCount; ++sheetIndex)
            {
                if (workbook.IsSheetHidden(sheetIndex) || workbook.IsSheetVeryHidden(sheetIndex))
                    continue;

                var sheet = workbook.GetSheetAt(sheetIndex);
                if (false == string.Equals(sheet.SheetName, "constant", StringComparison.OrdinalIgnoreCase))
                    continue;

                var constantDef = new EnumDefinition
                {
                    Name = "Constant",
                    IsFlag = false,
                    StartRow = 0,
                    StartColumn = 0,
                };

                if (sheet.PhysicalNumberOfRows <= opts.HeaderRowCount)
                {
                    return constantDef;
                }

                var fieldNameRow = sheet.GetRow(opts.NameRowNumber);

                // SerialNo로 시작하는 컬럼 인덱스찾기
                ICell? serialCell = fieldNameRow.Cells.FirstOrDefault(p => p.StringCellValue == "SerialNo");
                if (serialCell == null)
                    return null;

                for (int rowIndex = opts.HeaderRowCount; rowIndex <= sheet.LastRowNum; ++rowIndex)
                {
                    IRow row = sheet.GetRow(rowIndex);
                    if (row == null)
                        continue;

                    string name = row.GetCell(serialCell.ColumnIndex + 1).StringCellValue;
                    string value = row.GetCell(serialCell.ColumnIndex)?.ToString() ?? string.Empty;
                    string comment = row.GetCell(serialCell.ColumnIndex + 3)?.StringCellValue ?? string.Empty;

                    constantDef.Members.Add(new EnumMember(name, value, comment));
                }

                return constantDef;
            }

            return null;
        }


        /// <summary>
        /// 삭제된 테이블 스키마 비활성화 플래그 설정
        /// </summary>
        /// <param name="currentRevisionSchemas"></param>
        /// <param name="originSchemaDir">스키마 디렉토리 경로</param>
        private static void ReconcileDeletedSchemas(List<SchemaGenerationResult> currentRevisionSchemas, string originSchemaDir)
        {
            var existingSchemaFiles = Directory.GetFiles(originSchemaDir, "*.Schema.json")
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var schema in currentRevisionSchemas)
            {
                existingSchemaFiles.Remove($"{schema.CurrentSchema.Table}.Schema.json");
            }

            // 엑셀에서 삭제된 테이블은 스키마 파일에 Deprecated 플래그를 설정해야한다.
            foreach (var deprecatedSchemaFile in existingSchemaFiles)
            {
                var schemaPath = Path.Combine(originSchemaDir, deprecatedSchemaFile!);
                var schemaJson = File.ReadAllText(schemaPath);
                var schema = JsonSerializer.Deserialize<SchemaTemplate>(schemaJson, s_serializerOptions);
                if (schema == null)
                    continue;

                if (schema.Deprecated)
                    continue;

                schema.Deprecated = true;
                
                currentRevisionSchemas.Add(new SchemaGenerationResult
                {
                    Action = ActionType.Update,
                    CurrentSchema = schema,
                    History = ["Table marked as deprecated because the corresponding Excel sheet was not found."],
                    Sheet = null!,
                    SkipColumns = [],
                    StartColumIndex = -1,
                });
            }
        }
    }



    internal class ManifestTemplate
    {
        public required int Revision { get; init; }

        public required DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

        public Dictionary<string, string> Hash { get; init; } = [];
    }


    /// <summary>
    /// 개발자가 로컬에서 파일 생성을 실행한 경우 적용 (로컬 암호키를 사용하기 위함)
    /// </summary>
    internal class DevelopmentManifestTemplate : ManifestTemplate
    {
        public bool IsDevelopment { get; set; }
    }




    // 열거형 파싱을 위한 객체
    internal class EnumInfo
    {
        public Dictionary<int, List<EnumDefinition>> Infos { get; } = [];

        public List<EnumDefinition> GetAll()
        {
            var all = new List<EnumDefinition>();
            foreach (var kvp in Infos)
            {
                all.AddRange(kvp.Value);
            }
            return all;
        }
    }
}