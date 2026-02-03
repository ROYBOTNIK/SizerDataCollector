using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SizerDataCollector.Core.Schema
{
	public static class SchemaParser
	{
		private static readonly Regex CreateTableRegex = new Regex(@"\bCREATE\s+TABLE\s+(?<name>[^\s(]+)\s*\((?<body>.*?)\)\s*;?", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
		private static readonly Regex AlterTableRegex = new Regex(@"\bALTER\s+TABLE\s+(?<name>[^\s]+)\s+ADD\s+CONSTRAINT\s+(?<constraint>[^\s]+)\s+(?<definition>.+?)\s*;?", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
		private static readonly Regex PrimaryKeyRegex = new Regex(@"\bPRIMARY\s+KEY\s*\((?<columns>[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ForeignKeyRegex = new Regex(@"\bFOREIGN\s+KEY\s*\((?<columns>[^)]+)\)\s+REFERENCES\s+(?<target>[^\s(]+)\s*\((?<targetColumns>[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ColumnInlinePrimaryKeyRegex = new Regex(@"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ColumnInlineReferencesRegex = new Regex(@"\bREFERENCES\s+(?<target>[^\s(]+)\s*\((?<targetColumns>[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ColumnNotNullRegex = new Regex(@"\bNOT\s+NULL\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ColumnDefaultRegex = new Regex(@"\bDEFAULT\s+(?<value>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		public static SchemaMap ParseSchemaDefinitionFile(string path, SchemaParseOptions options = null)
		{
			var map = new SchemaMap(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrWhiteSpace(path))
			{
				map.Errors.Add("Schema file path was not provided.");
				map.Success = false;
				return map;
			}

			if (!File.Exists(path))
			{
				map.Errors.Add($"Schema file was not found: {path}");
				map.Success = false;
				return map;
			}

			options ??= new SchemaParseOptions();
			string content;
			try
			{
				content = File.ReadAllText(path);
			}
			catch (Exception ex)
			{
				map.Errors.Add($"Failed to read schema file: {ex.Message}");
				map.Success = false;
				return map;
			}

			content = RemoveComments(content);
			ParseCreateTableStatements(content, map, options);
			ParseAlterTableConstraints(content, map, options);
			FinalizeRelationships(map);

			if (!map.GetTables().Any())
			{
				map.Errors.Add("No tables were found in schema file.");
				map.Success = false;
				return map;
			}

			map.Success = true;
			return map;
		}

		public static string ParseToJson(string path, SchemaParseOptions options = null)
		{
			var map = ParseSchemaDefinitionFile(path, options);
			return SchemaSerialization.ToJson(map);
		}

		private static void ParseCreateTableStatements(string content, SchemaMap map, SchemaParseOptions options)
		{
			foreach (Match match in CreateTableRegex.Matches(content))
			{
				var nameToken = match.Groups["name"].Value;
				if (!TryParseTableIdentifier(nameToken, options, out var tableId))
				{
					map.Warnings.Add($"Could not parse table name: {nameToken}");
					continue;
				}

				var table = map.GetOrAddTable(tableId.Schema, tableId.Name);
				var body = match.Groups["body"].Value;
				var entries = SplitTableBody(body);
				foreach (var entry in entries)
				{
					if (string.IsNullOrWhiteSpace(entry))
					{
						continue;
					}

					if (TryParseTableLevelConstraint(entry, tableId, map, options))
					{
						continue;
					}

					if (!TryParseColumn(entry, table, tableId, map, options, out var column))
					{
						map.Warnings.Add($"Could not parse column definition: {entry}");
						continue;
					}

					table.Columns.Add(column);
				}
			}
		}

		private static void ParseAlterTableConstraints(string content, SchemaMap map, SchemaParseOptions options)
		{
			foreach (Match match in AlterTableRegex.Matches(content))
			{
				var nameToken = match.Groups["name"].Value;
				var constraintName = match.Groups["constraint"].Value;
				var definition = match.Groups["definition"].Value;

				if (!TryParseTableIdentifier(nameToken, options, out var tableId))
				{
					map.Warnings.Add($"Could not parse ALTER TABLE target: {nameToken}");
					continue;
				}

				if (TryParsePrimaryKey(definition, tableId, map, constraintName))
				{
					continue;
				}

				if (TryParseForeignKey(definition, tableId, map, constraintName, options))
				{
					continue;
				}

				map.Warnings.Add($"Unsupported ALTER TABLE constraint: {definition}");
			}
		}

		private static bool TryParseTableLevelConstraint(string entry, TableIdentifier tableId, SchemaMap map, SchemaParseOptions options)
		{
			var trimmed = entry.Trim();
			if (TryParsePrimaryKey(trimmed, tableId, map, null))
			{
				return true;
			}

			if (TryParseForeignKey(trimmed, tableId, map, null, options))
			{
				return true;
			}

			return false;
		}

		private static bool TryParsePrimaryKey(string entry, TableIdentifier tableId, SchemaMap map, string constraintName)
		{
			var match = PrimaryKeyRegex.Match(entry);
			if (!match.Success)
			{
				return false;
			}

			var columns = SplitIdentifiers(match.Groups["columns"].Value);
			var table = map.GetOrAddTable(tableId.Schema, tableId.Name);
			table.AddPrimaryKey(columns);
			ApplyPrimaryKeyFlags(table, columns);
			return true;
		}

		private static bool TryParseForeignKey(string entry, TableIdentifier tableId, SchemaMap map, string constraintName, SchemaParseOptions options)
		{
			var match = ForeignKeyRegex.Match(entry);
			if (!match.Success)
			{
				return false;
			}

			var referencingColumns = SplitIdentifiers(match.Groups["columns"].Value);
			var targetToken = match.Groups["target"].Value;
			if (!TryParseTableIdentifier(targetToken, options, out var referencedTable))
			{
				map.Warnings.Add($"Could not parse foreign key reference: {targetToken}");
				return true;
			}

			var referencedColumns = SplitIdentifiers(match.Groups["targetColumns"].Value);
			var fk = new ForeignKeyDefinition(tableId, referencedTable, referencingColumns, referencedColumns)
			{
				Name = constraintName
			};

			RegisterForeignKey(map, fk);
			return true;
		}

		private static bool TryParseColumn(string entry, TableDefinition table, TableIdentifier tableId, SchemaMap map, SchemaParseOptions options, out ColumnDefinition column)
		{
			column = null;
			var tokens = SplitColumnTokens(entry);
			if (tokens.Count < 2)
			{
				return false;
			}

			var name = tokens[0];
			var index = 1;
			var typeTokens = new List<string>();
			for (; index < tokens.Count; index++)
			{
				if (IsConstraintToken(tokens[index]))
				{
					break;
				}
				typeTokens.Add(tokens[index]);
			}

			if (typeTokens.Count == 0)
			{
				return false;
			}

			var rawType = string.Join(" ", typeTokens);
			var remainder = string.Join(" ", tokens.Skip(index));
			column = new ColumnDefinition
			{
				ColumnName = name,
				DataType = rawType.Trim(),
				IsNullable = !ColumnNotNullRegex.IsMatch(remainder)
			};

			var defaultMatch = ColumnDefaultRegex.Match(remainder);
			if (defaultMatch.Success)
			{
				var defaultValue = defaultMatch.Groups["value"].Value.Trim();
				column.ColumnDefault = TrimTrailingConstraint(defaultValue);
			}

			if (ColumnInlinePrimaryKeyRegex.IsMatch(remainder))
			{
				table.AddPrimaryKey(new[] { name });
				column.IsPrimaryKey = true;
			}

			var referenceMatch = ColumnInlineReferencesRegex.Match(remainder);
			if (referenceMatch.Success)
			{
				var targetToken = referenceMatch.Groups["target"].Value;
				if (TryParseTableIdentifier(targetToken, options, out var referencedTable))
				{
					var referencedColumns = SplitIdentifiers(referenceMatch.Groups["targetColumns"].Value);
					var fk = new ForeignKeyDefinition(tableId, referencedTable, new[] { name }, referencedColumns)
					{
						Name = null
					};
					RegisterForeignKey(map, fk);
				}
				else
				{
					map.Warnings.Add($"Could not parse inline foreign key reference: {targetToken}");
				}
			}

			return true;
		}

		private static void RegisterForeignKey(SchemaMap map, ForeignKeyDefinition foreignKey)
		{
			var referencingTable = map.GetOrAddTable(foreignKey.ReferencingTable.Schema, foreignKey.ReferencingTable.Name);
			referencingTable.AddForeignKey(foreignKey);
			ApplyForeignKeyFlags(map, foreignKey);
		}

		private static void ApplyPrimaryKeyFlags(TableDefinition table, IEnumerable<string> columns)
		{
			foreach (var columnName in columns)
			{
				var column = table.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
				if (column != null)
				{
					column.IsPrimaryKey = true;
				}
			}
		}

		private static void ApplyForeignKeyFlags(SchemaMap map, ForeignKeyDefinition foreignKey)
		{
			var referencingTable = map.GetOrAddTable(foreignKey.ReferencingTable.Schema, foreignKey.ReferencingTable.Name);
			var count = Math.Min(foreignKey.ReferencingColumns.Count, foreignKey.ReferencedColumns.Count);
			for (var i = 0; i < count; i++)
			{
				var localColumn = foreignKey.ReferencingColumns[i];
				var remoteColumn = foreignKey.ReferencedColumns[i];
				var column = referencingTable.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, localColumn, StringComparison.OrdinalIgnoreCase));
				if (column == null)
				{
					map.Warnings.Add($"Foreign key references missing column: {referencingTable.FullName}.{localColumn}");
					continue;
				}

				column.IsForeignKey = true;
				column.ForeignSchema = foreignKey.ReferencedTable.Schema;
				column.ForeignTable = foreignKey.ReferencedTable.Name;
				column.ForeignColumn = remoteColumn;
			}
		}

		private static void FinalizeRelationships(SchemaMap map)
		{
			foreach (var table in map.GetTables())
			{
				foreach (var fk in table.ForeignKeys)
				{
					var referencing = map.GetOrAddTable(fk.ReferencingTable.Schema, fk.ReferencingTable.Name);
					referencing.AddReference(fk.ReferencedTable.FullName);
					if (map.TryGetTable(fk.ReferencedTable.Schema, fk.ReferencedTable.Name, out var referenced))
					{
						referenced.AddReferencedBy(referencing.FullName);
						foreach (var referencedColumn in fk.ReferencedColumns)
						{
							if (referenced.Columns.All(column => !string.Equals(column.ColumnName, referencedColumn, StringComparison.OrdinalIgnoreCase)))
							{
								map.Warnings.Add($"Foreign key references missing column: {referenced.FullName}.{referencedColumn}");
							}
						}
					}
					else
					{
						map.Warnings.Add($"Foreign key references missing table: {fk.ReferencedTable.FullName}");
					}
				}
			}
		}

		private static bool TryParseTableIdentifier(string token, SchemaParseOptions options, out TableIdentifier table)
		{
			table = null;
			if (string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			var trimmed = TrimIdentifier(token);
			var parts = trimmed.Split(new[] { '.' }, 2);
			if (parts.Length == 2)
			{
				table = new TableIdentifier(TrimIdentifier(parts[0]), TrimIdentifier(parts[1]));
				return true;
			}

			var schema = options?.DefaultSchema ?? "public";
			table = new TableIdentifier(schema, trimmed);
			return true;
		}

		private static string TrimIdentifier(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
			{
				return string.Empty;
			}

			var trimmed = token.Trim();
			if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
				|| (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
			{
				return trimmed.Substring(1, trimmed.Length - 2);
			}

			return trimmed;
		}

		private static List<string> SplitIdentifiers(string input)
		{
			return input.Split(',')
				.Select(value => TrimIdentifier(value))
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.ToList();
		}

		private static string RemoveComments(string input)
		{
			var withoutBlockComments = Regex.Replace(input, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
			var lines = withoutBlockComments.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
			var builder = new StringBuilder();
			foreach (var line in lines)
			{
				var index = line.IndexOf("--", StringComparison.Ordinal);
				if (index >= 0)
				{
					builder.AppendLine(line.Substring(0, index));
				}
				else
				{
					builder.AppendLine(line);
				}
			}

			return builder.ToString();
		}

		private static List<string> SplitTableBody(string body)
		{
			var entries = new List<string>();
			var depth = 0;
			var inString = false;
			var current = new StringBuilder();
			for (var i = 0; i < body.Length; i++)
			{
				var ch = body[i];
				if (ch == '\'' && (i == 0 || body[i - 1] != '\\'))
				{
					inString = !inString;
				}

				if (!inString)
				{
					if (ch == '(')
					{
						depth++;
					}
					else if (ch == ')')
					{
						depth = Math.Max(0, depth - 1);
					}
					else if (ch == ',' && depth == 0)
					{
						entries.Add(current.ToString().Trim());
						current.Clear();
						continue;
					}
				}

				current.Append(ch);
			}

			if (current.Length > 0)
			{
				entries.Add(current.ToString().Trim());
			}

			return entries;
		}

		private static List<string> SplitColumnTokens(string entry)
		{
			var tokens = new List<string>();
			var current = new StringBuilder();
			var depth = 0;
			var inString = false;
			foreach (var ch in entry)
			{
				if (ch == '\'' && (current.Length == 0 || current[current.Length - 1] != '\\'))
				{
					inString = !inString;
				}

				if (!inString)
				{
					if (ch == '(')
					{
						depth++;
					}
					else if (ch == ')')
					{
						depth = Math.Max(0, depth - 1);
					}
					else if (char.IsWhiteSpace(ch) && depth == 0)
					{
						if (current.Length > 0)
						{
							tokens.Add(current.ToString());
							current.Clear();
						}
						continue;
					}
				}

				current.Append(ch);
			}

			if (current.Length > 0)
			{
				tokens.Add(current.ToString());
			}

			return tokens;
		}

		private static bool IsConstraintToken(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			switch (token.ToUpperInvariant())
			{
				case "NOT":
				case "NULL":
				case "DEFAULT":
				case "PRIMARY":
				case "KEY":
				case "REFERENCES":
				case "CONSTRAINT":
				case "CHECK":
				case "UNIQUE":
					return true;
				default:
					return false;
			}
		}

		private static string TrimTrailingConstraint(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return input;
			}

			var tokens = SplitColumnTokens(input);
			if (tokens.Count == 0)
			{
				return input.Trim();
			}

			var constraintIndex = tokens.FindIndex(IsConstraintToken);
			if (constraintIndex <= 0)
			{
				return input.Trim();
			}

			return string.Join(" ", tokens.Take(constraintIndex)).Trim();
		}
	}
}
