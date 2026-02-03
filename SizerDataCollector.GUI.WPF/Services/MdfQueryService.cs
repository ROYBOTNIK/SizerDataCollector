using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using SizerDataCollector.Core.Schema;

namespace SizerDataCollector.GUI.WPF.Services
{
	public sealed class MdfQueryService
	{
		public IReadOnlyList<string> LoadTables(MdfConnectionOptions options)
		{
			using (var connection = new SqlConnection(BuildConnectionString(options)))
			{
				connection.Open();
				using (var command = new SqlCommand(@"
SELECT TABLE_SCHEMA + '.' + TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;", connection))
				{
					using (var reader = command.ExecuteReader())
					{
						var tables = new List<string>();
						while (reader.Read())
						{
							tables.Add(reader.GetString(0));
						}

						return tables;
					}
				}
			}
		}

		public DataTable LoadPreview(MdfConnectionOptions options, string query)
		{
			using (var connection = new SqlConnection(BuildConnectionString(options)))
			{
				using (var adapter = new SqlDataAdapter(query, connection))
				{
					var table = new DataTable();
					adapter.Fill(table);
					return table;
				}
			}
		}

		public DataTable LoadSchemaDetails(MdfConnectionOptions options)
		{
			var map = LoadSchemaMap(options);
			var table = new DataTable("schema_details");
			table.Columns.Add("table_schema", typeof(string));
			table.Columns.Add("table_name", typeof(string));
			table.Columns.Add("column_name", typeof(string));
			table.Columns.Add("data_type", typeof(string));
			table.Columns.Add("is_nullable", typeof(string));
			table.Columns.Add("column_default", typeof(string));
			table.Columns.Add("is_primary_key", typeof(string));
			table.Columns.Add("is_foreign_key", typeof(string));
			table.Columns.Add("foreign_schema", typeof(string));
			table.Columns.Add("foreign_table", typeof(string));
			table.Columns.Add("foreign_column", typeof(string));

			foreach (var tableDefinition in map.Tables
				.OrderBy(item => item.Schema, StringComparer.OrdinalIgnoreCase)
				.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
			{
				foreach (var column in tableDefinition.Columns)
				{
					table.Rows.Add(
						tableDefinition.Schema,
						tableDefinition.Name,
						column.ColumnName,
						column.DataType,
						column.IsNullable ? "YES" : "NO",
						column.ColumnDefault,
						column.IsPrimaryKey ? "YES" : "NO",
						column.IsForeignKey ? "YES" : "NO",
						column.ForeignSchema,
						column.ForeignTable,
						column.ForeignColumn);
				}
			}

			return table;
		}

        // Pseudocode / Plan:
        // 1. Replace direct assignments to map.Success (which fails because the setter is non-public) with a helper that sets the property via reflection.
        // 2. Implement a private static helper SetSchemaMapSuccess(SchemaMap map, bool value):
        //    - Validate map not null.
        //    - Look up the "Success" PropertyInfo on SchemaMap (public instance property).
        //    - Retrieve the setter MethodInfo including non-public setters (GetSetMethod(true)).
        //    - If setter exists, invoke it with the provided boolean value.
        //    - If property or setter not found, do nothing (fail-safe).
        // 3. Update LoadSchemaMap to call SetSchemaMapSuccess(map, false) when no tables and SetSchemaMapSuccess(map, true) on success.
        // 4. Use fully-qualified reflection types to avoid adding new using directives (keeps patch minimal).
        //
        // The following replaces the LoadSchemaMap method and adds the helper method inside the MdfQueryService class.

        public SchemaMap LoadSchemaMap(MdfConnectionOptions options)
        {
            var map = new SchemaMap(StringComparer.OrdinalIgnoreCase);
            using (var connection = new SqlConnection(BuildConnectionString(options)))
            {
                connection.Open();
                LoadTablesAndColumns(connection, map);
                LoadPrimaryKeys(connection, map);
                LoadForeignKeys(connection, map);
            }

            if (!map.Tables.Any())
            {
                map.Errors.Add("No tables were found in MDF database.");
                SetSchemaMapSuccess(map, false);
                return map;
            }

            SetSchemaMapSuccess(map, true);
            return map;
        }

        private static void SetSchemaMapSuccess(SchemaMap map, bool success)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            // Get the public Success property and its setter (including non-public setter).
            var prop = typeof(SchemaMap).GetProperty("Success", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop == null) return;

            var setMethod = prop.GetSetMethod(true); // true = allow non-public
            if (setMethod == null) return;

            // Invoke the setter with the provided value.
            setMethod.Invoke(map, new object[] { success });
        }

		public void WriteText(string path, string content)
		{
			File.WriteAllText(path, content, Encoding.UTF8);
		}

		public void WritePdf(string path, DataTable table)
		{
			var lines = new List<string>
			{
				$"Database preview export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
				string.Join(" | ", table.Columns.Cast<DataColumn>().Select(column => column.ColumnName))
			};

			foreach (DataRow row in table.Rows)
			{
				lines.Add(string.Join(" | ", row.ItemArray.Select(value => Convert.ToString(value))));
			}

			SimplePdfWriter.Write(path, lines);
		}

		private string BuildConnectionString(MdfConnectionOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			var builder = new SqlConnectionStringBuilder
			{
				DataSource = options.ServerName,
				IntegratedSecurity = !options.UseSqlAuthentication,
				TrustServerCertificate = true
			};

			if (options.UseSqlAuthentication)
			{
				if (!string.IsNullOrWhiteSpace(options.UserName))
				{
					builder.UserID = options.UserName;
				}

				if (!string.IsNullOrWhiteSpace(options.Password))
				{
					builder.Password = options.Password;
				}
			}

			if (options.Mode == MdfSourceMode.LocalFile)
			{
				builder.AttachDBFilename = options.MdfPath;
				builder.InitialCatalog = options.DatabaseName;
			}
			else
			{
				builder.InitialCatalog = options.DatabaseName;
			}

			if (options.ReadOnly)
			{
				builder.ApplicationIntent = ApplicationIntent.ReadOnly;
			}

			return builder.ConnectionString;
		}

		private static void LoadTablesAndColumns(SqlConnection connection, SchemaMap map)
		{
			const string sql = @"
SELECT
	s.name AS schema_name,
	t.name AS table_name,
	c.name AS column_name,
	ty.name AS data_type,
	c.max_length,
	c.precision,
	c.scale,
	c.is_nullable,
	dc.definition AS column_default
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
INNER JOIN sys.columns c ON c.object_id = t.object_id
INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = t.object_id AND dc.parent_column_id = c.column_id
ORDER BY s.name, t.name, c.column_id;";

			using (var command = new SqlCommand(sql, connection))
			{
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var schema = reader.GetString(0);
						var tableName = reader.GetString(1);
						var columnName = reader.GetString(2);
						var dataType = reader.GetString(3);
						var maxLength = reader.GetInt16(4);
						var precision = reader.GetByte(5);
						var scale = reader.GetByte(6);
						var isNullable = reader.GetBoolean(7);
						var columnDefault = reader.IsDBNull(8) ? null : reader.GetString(8);

						var table = map.GetOrAddTable(schema, tableName);
						table.Columns.Add(new ColumnDefinition
						{
							ColumnName = columnName,
							DataType = BuildSqlServerType(dataType, maxLength, precision, scale),
							IsNullable = isNullable,
							ColumnDefault = columnDefault
						});
					}
				}
			}
		}

		private static void LoadPrimaryKeys(SqlConnection connection, SchemaMap map)
		{
			const string sql = @"
SELECT
	s.name AS schema_name,
	t.name AS table_name,
	c.name AS column_name
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
INNER JOIN sys.tables t ON t.object_id = i.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE i.is_primary_key = 1
ORDER BY s.name, t.name, ic.key_ordinal;";

			using (var command = new SqlCommand(sql, connection))
			{
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var schema = reader.GetString(0);
						var tableName = reader.GetString(1);
						var columnName = reader.GetString(2);

						var table = map.GetOrAddTable(schema, tableName);
						table.AddPrimaryKey(new[] { columnName });
						foreach (var column in table.Columns.Where(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
						{
							column.IsPrimaryKey = true;
						}
					}
				}
			}
		}

		private static void LoadForeignKeys(SqlConnection connection, SchemaMap map)
		{
			const string sql = @"
SELECT
	fk.name AS constraint_name,
	fs.name AS referencing_schema,
	ft.name AS referencing_table,
	fc.name AS referencing_column,
	rs.name AS referenced_schema,
	rt.name AS referenced_table,
	rc.name AS referenced_column,
	fkc.constraint_column_id
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
INNER JOIN sys.tables ft ON ft.object_id = fkc.parent_object_id
INNER JOIN sys.schemas fs ON fs.schema_id = ft.schema_id
INNER JOIN sys.columns fc ON fc.object_id = ft.object_id AND fc.column_id = fkc.parent_column_id
INNER JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
INNER JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.name, fkc.constraint_column_id;";

			var foreignKeys = new Dictionary<string, ForeignKeyDefinition>(StringComparer.OrdinalIgnoreCase);
			using (var command = new SqlCommand(sql, connection))
			{
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var constraintName = reader.GetString(0);
						var referencingSchema = reader.GetString(1);
						var referencingTable = reader.GetString(2);
						var referencingColumn = reader.GetString(3);
						var referencedSchema = reader.GetString(4);
						var referencedTable = reader.GetString(5);
						var referencedColumn = reader.GetString(6);

						if (!foreignKeys.TryGetValue(constraintName, out var fk))
						{
							fk = new ForeignKeyDefinition(
								new TableIdentifier(referencingSchema, referencingTable),
								new TableIdentifier(referencedSchema, referencedTable),
								new List<string>(),
								new List<string>())
							{
								Name = constraintName
							};
							foreignKeys[constraintName] = fk;
						}

						((List<string>)fk.ReferencingColumns).Add(referencingColumn);
						((List<string>)fk.ReferencedColumns).Add(referencedColumn);
					}
				}
			}

			foreach (var fk in foreignKeys.Values)
			{
				var table = map.GetOrAddTable(fk.ReferencingTable.Schema, fk.ReferencingTable.Name);
				table.AddForeignKey(fk);
				var count = Math.Min(fk.ReferencingColumns.Count, fk.ReferencedColumns.Count);
				for (var i = 0; i < count; i++)
				{
					var localColumn = fk.ReferencingColumns[i];
					var remoteColumn = fk.ReferencedColumns[i];
					var column = table.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, localColumn, StringComparison.OrdinalIgnoreCase));
					if (column == null)
					{
						map.Warnings.Add($"Foreign key references missing column: {table.FullName}.{localColumn}");
						continue;
					}

					column.IsForeignKey = true;
					column.ForeignSchema = fk.ReferencedTable.Schema;
					column.ForeignTable = fk.ReferencedTable.Name;
					column.ForeignColumn = remoteColumn;
				}

				table.AddReference(fk.ReferencedTable.FullName);
				if (map.TryGetTable(fk.ReferencedTable.Schema, fk.ReferencedTable.Name, out var referenced))
				{
					referenced.AddReferencedBy(table.FullName);
				}
				else
				{
					map.Warnings.Add($"Foreign key references missing table: {fk.ReferencedTable.FullName}");
				}
			}
		}

		private static string BuildSqlServerType(string dataType, short maxLength, byte precision, byte scale)
		{
			if (string.IsNullOrWhiteSpace(dataType))
			{
				return dataType;
			}

			switch (dataType.ToLowerInvariant())
			{
				case "nvarchar":
				case "nchar":
					return $"{dataType}({(maxLength < 0 ? "max" : (maxLength / 2).ToString())})";
				case "varchar":
				case "char":
				case "varbinary":
				case "binary":
					return $"{dataType}({(maxLength < 0 ? "max" : maxLength.ToString())})";
				case "decimal":
				case "numeric":
					return $"{dataType}({precision},{scale})";
				default:
					return dataType;
			}
		}
	}

	public sealed class MdfConnectionOptions
	{
		public MdfSourceMode Mode { get; set; }

		public string ServerName { get; set; }

		public string DatabaseName { get; set; }

		public string MdfPath { get; set; }

		public bool UseSqlAuthentication { get; set; }

		public string UserName { get; set; }

		public string Password { get; set; }

		public bool ReadOnly { get; set; }
	}

	public enum MdfSourceMode
	{
		AttachedInstance,
		LocalFile
	}

	public sealed class MdfSourceOption
	{
		public MdfSourceOption(MdfSourceMode mode, string displayName)
		{
			Mode = mode;
			DisplayName = displayName;
		}

		public MdfSourceMode Mode { get; }

		public string DisplayName { get; }
	}
}
