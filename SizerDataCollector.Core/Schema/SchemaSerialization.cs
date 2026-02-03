using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SizerDataCollector.Core.Schema
{
	public static class SchemaSerialization
	{
		public static string ToJson(SchemaMap map)
		{
			var payload = new SchemaMapDto(map);
			return JsonConvert.SerializeObject(payload, Formatting.Indented, new JsonSerializerSettings
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver(),
				NullValueHandling = NullValueHandling.Ignore
			});
		}

		public static string ToMarkdown(SchemaMap map)
		{
			var builder = new System.Text.StringBuilder();
			foreach (var table in map.Tables)
			{
				builder.AppendLine($"## {table.FullName}");
				builder.AppendLine();
				builder.AppendLine("| Column | Type | Nullable | PK | FK | Default |");
				builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
				foreach (var column in table.Columns.OrderBy(c => c.ColumnName))
				{
					builder.AppendLine($"| {column.ColumnName} | {column.DataType} | {column.IsNullable} | {column.IsPrimaryKey} | {column.IsForeignKey} | {column.ColumnDefault ?? string.Empty} |");
				}
				builder.AppendLine();
				builder.AppendLine("References: " + string.Join(", ", table.References));
				builder.AppendLine("Referenced By: " + string.Join(", ", table.ReferencedBy));
				builder.AppendLine();
			}

			return builder.ToString();
		}

		private sealed class SchemaMapDto
		{
			public SchemaMapDto(SchemaMap map)
			{
				Success = map.Success;
				Warnings = map.Warnings;
				Errors = map.Errors;
				Tables = map.Tables.Select(table => new TableDto(table)).ToList();
			}

			public bool Success { get; }
			public IReadOnlyList<string> Warnings { get; }
			public IReadOnlyList<string> Errors { get; }
			public IReadOnlyList<TableDto> Tables { get; }
		}

		private sealed class TableDto
		{
			public TableDto(TableDefinition table)
			{
				Schema = table.Schema;
				Name = table.Name;
				FullName = table.FullName;
				Columns = table.Columns.OrderBy(c => c.ColumnName).Select(c => new ColumnDto(c)).ToList();
				PrimaryKey = table.PrimaryKeyColumns;
				References = table.References;
				ReferencedBy = table.ReferencedBy;
				ForeignKeys = table.ForeignKeys.Select(fk => new ForeignKeyDto(fk)).ToList();
			}

			public string Schema { get; }
			public string Name { get; }
			public string FullName { get; }
			public IReadOnlyList<ColumnDto> Columns { get; }
			public IReadOnlyList<string> PrimaryKey { get; }
			public IReadOnlyList<string> References { get; }
			public IReadOnlyList<string> ReferencedBy { get; }
			public IReadOnlyList<ForeignKeyDto> ForeignKeys { get; }
		}

		private sealed class ColumnDto
		{
			public ColumnDto(ColumnDefinition column)
			{
				ColumnName = column.ColumnName;
				DataType = column.DataType;
				IsNullable = column.IsNullable;
				ColumnDefault = column.ColumnDefault;
				IsPrimaryKey = column.IsPrimaryKey;
				IsForeignKey = column.IsForeignKey;
				ForeignSchema = column.ForeignSchema;
				ForeignTable = column.ForeignTable;
				ForeignColumn = column.ForeignColumn;
			}

			public string ColumnName { get; }
			public string DataType { get; }
			public bool IsNullable { get; }
			public string ColumnDefault { get; }
			public bool IsPrimaryKey { get; }
			public bool IsForeignKey { get; }
			public string ForeignSchema { get; }
			public string ForeignTable { get; }
			public string ForeignColumn { get; }
		}

		private sealed class ForeignKeyDto
		{
			public ForeignKeyDto(ForeignKeyDefinition fk)
			{
				Name = fk.Name;
				ReferencingTable = fk.ReferencingTable.FullName;
				ReferencedTable = fk.ReferencedTable.FullName;
				ReferencingColumns = fk.ReferencingColumns.ToList();
				ReferencedColumns = fk.ReferencedColumns.ToList();
			}

			public string Name { get; }
			public string ReferencingTable { get; }
			public string ReferencedTable { get; }
			public IReadOnlyList<string> ReferencingColumns { get; }
			public IReadOnlyList<string> ReferencedColumns { get; }
		}
	}
}
