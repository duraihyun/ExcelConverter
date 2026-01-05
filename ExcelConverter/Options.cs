using CommandLine;

namespace ExcelConvertor
{
    internal class Options
    {
        [Option('p', Required = true, HelpText = "Root path of the Excel files.")]
        public string RootDir { get; init; } = string.Empty;


        [Option('o', Required = true, HelpText = "Output path for the converted files.")]
        public string OutputDir { get; init; } = string.Empty;


        [Option('r', Required = true, HelpText = "Revision number.", Default = 1)]
        public int Revision { get; init;  }


        [Option('n', Required = false, HelpText = "Row number for field names.", Default = 1)]
        public int NameRowNumber { get; init; } = 1;


        [Option('t', Required = false, HelpText = "Row number for field types.", Default = 2)]
        public int TypeRowNumber { get; init; } = 2;

        [Option('h', Required = false, HelpText = "Number of header rows to skip.", Default = 3)]
        public int HeaderRowCount { get; init; } = 3;

        // 무시하고 필드 타입 덮어쓰기
        [Option('f', Required = false, HelpText = "Force field type overwrite.", Default = false)]
        public bool ForceFieldTypeOverwrite { get; init; } = false;

        // 툴 생성을 배포파이프라인을 사용하지 않고 로컬에서 개발용으로 만들었을 때..
        [Option('d', "development", Required = false, HelpText = "Run in development mode.", Default = false)]
        public bool IsDevelopment { get; init; } = false;


        // 암호화 키 (Base64)
        [Option('e', "encryption-key", Required = false, HelpText = "Base64 encoded encryption key for production.", Default = "")]
        public string EncryptionKey { get; init; } = "";
    }
}
