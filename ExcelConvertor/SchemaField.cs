using System.Text.Json.Serialization;

namespace ExcelConvertor
{
    internal readonly record struct SchemaField(int Id, string Type, bool Deprecated, [property:JsonIgnore] int ColumnIndex);
}
