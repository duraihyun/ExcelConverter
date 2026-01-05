using System.Collections.Generic;
using System.Linq;

namespace ExcelConvertor.CodeGen
{
    /// <summary>
    /// 여러 스키마를 포함하는 최상위 템플릿 데이터 모델
    /// </summary>
    internal class CodeGenViewModel
    {
        // 템플릿에서 사용하기 편하도록 미리 계산된 값들
        public string Namespace { get; set; } = "DataTable";

        public string ManagerName => "DataTableManager";

        public string StringTableName => "StringTable";

        // 데이터를 생성할 전체 스키마
        public List<SchemaInfo> Schemas { get; init; }

        // 서버용 테이블 목록
        public IEnumerable<PropertyInfo> Servers { get; init; }
        
        public IEnumerable<PropertyInfo> Clients { get; init; }


        public List<EnumDefinition> Enums { get; init; }


        public CodeGenViewModel(IEnumerable<SchemaTemplate> schemas, List<EnumDefinition> enums)
        {
            Schemas = schemas.Select(s => new SchemaInfo(s)).ToList();
            Servers = Schemas
                .Where(p => p.Schema.Target != "Client")
                .Select(p => new PropertyInfo(p.Schema.Table, p.TableClassName, p.LoaderClassName))
                .ToList();
            Clients = Schemas
                .Where(p => p.Schema.Target != "Server")
                .Select(p => new PropertyInfo(p.Schema.Table, p.TableClassName, p.LoaderClassName))
                .ToList();
            Enums = enums;
        }
    }


    internal readonly record struct PropertyInfo(string Name, string Type, string Loader);


    /// <summary>
    /// 열거형 코드 자동 생성에 필요한 정보
    /// </summary>
    internal class EnumDefinition
    {
        public required bool IsFlag { get; init; }

        public required string Name { get; init; }

        public List<EnumMember> Members { get; } = [];
        
        public required int StartRow { get; init; }

        public required int StartColumn { get; init; }

        // 컬럼이 해당 블럭에 속하는지 여부
        public bool ContainsColumn(int column)
        {
            // 멤버, 값, 코멘트 3열 블럭
            return StartColumn <= column && column < StartColumn + 2;
        }
    }


    internal readonly record struct EnumMember(string Name, string Value, string? Comment);

}
