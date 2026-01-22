using Scriban;

using System.IO;

namespace ExcelConvertor.CodeGen
{
    internal class CodeGenerator
    {
        public void GenerateFromSchema(CodeGenViewModel viewModel, string templatePath, string outputPath)
        {
            // Scriban 템플릿 로드 및 렌더링
            var templateContent = File.ReadAllText(templatePath);
            var template = Template.Parse(templateContent);
            var generatedCode = template.Render(viewModel);

            // 결과 파일 저장
            File.WriteAllText(outputPath, generatedCode);
        }
    }
}