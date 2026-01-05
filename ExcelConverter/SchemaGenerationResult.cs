using NPOI.SS.UserModel;

using System.Collections.Generic;

namespace ExcelConvertor
{
    internal class SchemaGenerationResult
    {
        public required ISheet Sheet { get; init; }

        public required SchemaTemplate CurrentSchema { get; init; }

        public required ActionType Action { get; init; }

        public required List<string> History { get; init; }

        public required HashSet<int> SkipColumns { get; init; }

        public required int StartColumIndex { get; init; }
    }
}
