using System.Collections.Generic;

namespace ExcelConvertor
{
    internal class SchemaTemplate
    {
        public string Table { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public int Version { get; set; }

        public bool Deprecated { get; set; }

        public string PrimaryKey { get; set; } = string.Empty;

        public int NextFieldId { get; set; }

        public Dictionary<string, SchemaField> Fields { get; set; } = [];


        public SchemaTemplate Clone()
        {
            return new SchemaTemplate
            {
                Table = this.Table,
                Target = this.Target,
                Version = this.Version,
                Deprecated = this.Deprecated,
                PrimaryKey = this.PrimaryKey,
                NextFieldId = this.NextFieldId,
                Fields = new Dictionary<string, SchemaField>(this.Fields),
            };
        }
    }
}
