using System;
using System.Collections.Generic;
using System.Linq;

namespace SizerDataCollector.Core.Schema
{
	public sealed class SchemaParseOptions
	{
		public string DefaultSchema { get; set; } = "public";
	}

	public sealed class SchemaMap
	{
		private readonly Dictionary<string, TableDefinition> _tables;
		private readonly StringComparer _comparer;

		public SchemaMap(StringComparer comparer)
		{
			_comparer = comparer ?? StringComparer.OrdinalIgnoreCase;
			_tables = new Dictionary<string, TableDefinition>(_comparer);
			Warnings = new List<string>();
			Errors = new List<string>();
		}

		public bool Success { get; internal set; }
		public List<string> Warnings { get; }
		public List<string> Errors { get; }
		public IReadOnlyList<TableDefinition> Tables => _tables.Values.OrderBy(table => table.Schema, _comparer).ThenBy(table => table.Name, _comparer).ToList();

		public TableDefinition GetOrAddTable(string schema, string name)
		{
			var key = ToKey(schema, name);
			if (!_tables.TryGetValue(key, out var table))
			{
				table = new TableDefinition(schema, name, _comparer);
				_tables[key] = table;
			}

			return table;
		}

		public bool TryGetTable(string schema, string name, out TableDefinition table)
			=> _tables.TryGetValue(ToKey(schema, name), out table);

		public IEnumerable<TableDefinition> GetTables() => _tables.Values;

		public static string ToKey(string schema, string name)
			=> string.Concat(schema ?? string.Empty, ".", name ?? string.Empty);

		public IReadOnlyList<JoinEdge> GetDirectJoins(string tableA, string tableB)
		{
			if (string.IsNullOrWhiteSpace(tableA) || string.IsNullOrWhiteSpace(tableB))
			{
				return Array.Empty<JoinEdge>();
			}

			var normalizedA = tableA.Trim();
			var normalizedB = tableB.Trim();
			var edges = new List<JoinEdge>();
			foreach (var table in _tables.Values)
			{
				foreach (var fk in table.ForeignKeys)
				{
					var from = fk.ReferencingTable;
					var to = fk.ReferencedTable;
					if ((_comparer.Equals(from.FullName, normalizedA) && _comparer.Equals(to.FullName, normalizedB))
						|| (_comparer.Equals(from.FullName, normalizedB) && _comparer.Equals(to.FullName, normalizedA)))
					{
						edges.AddRange(fk.ToJoinEdges());
					}
				}
			}

			return edges;
		}

		public IReadOnlyList<JoinEdge> FindJoinPath(string fromTable, string toTable)
		{
			if (string.IsNullOrWhiteSpace(fromTable) || string.IsNullOrWhiteSpace(toTable))
			{
				return Array.Empty<JoinEdge>();
			}

			var start = fromTable.Trim();
			var target = toTable.Trim();
			var edges = GetAllJoinEdges();
			var adjacency = BuildAdjacency(edges);
			var queue = new Queue<string>();
			var visited = new HashSet<string>(_comparer);
			var parent = new Dictionary<string, (string Table, JoinEdge Edge)>(_comparer);

			queue.Enqueue(start);
			visited.Add(start);

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				if (_comparer.Equals(current, target))
				{
					break;
				}

				if (!adjacency.TryGetValue(current, out var neighbors))
				{
					continue;
				}

				foreach (var neighbor in neighbors)
				{
					if (visited.Contains(neighbor.Table))
					{
						continue;
					}

					visited.Add(neighbor.Table);
					parent[neighbor.Table] = (current, neighbor.Edge);
					queue.Enqueue(neighbor.Table);
				}
			}

			if (!visited.Contains(target))
			{
				return Array.Empty<JoinEdge>();
			}

			var path = new List<JoinEdge>();
			var node = target;
			while (!_comparer.Equals(node, start))
			{
				if (!parent.TryGetValue(node, out var step))
				{
					break;
				}

				path.Add(step.Edge);
				node = step.Table;
			}

			path.Reverse();
			return path;
		}

		private List<JoinEdge> GetAllJoinEdges()
		{
			var edges = new List<JoinEdge>();
			foreach (var table in _tables.Values)
			{
				foreach (var fk in table.ForeignKeys)
				{
					edges.AddRange(fk.ToJoinEdges());
				}
			}

			return edges;
		}

		private Dictionary<string, List<(string Table, JoinEdge Edge)>> BuildAdjacency(IEnumerable<JoinEdge> edges)
		{
			var adjacency = new Dictionary<string, List<(string Table, JoinEdge Edge)>>(_comparer);
			foreach (var edge in edges)
			{
				AddNeighbor(edge.FromTable, edge.ToTable, edge);
				AddNeighbor(edge.ToTable, edge.FromTable, edge);
			}

			return adjacency;

			void AddNeighbor(string from, string to, JoinEdge edge)
			{
				if (!adjacency.TryGetValue(from, out var neighbors))
				{
					neighbors = new List<(string Table, JoinEdge Edge)>();
					adjacency[from] = neighbors;
				}

				neighbors.Add((to, edge));
			}
		}
	}

	public sealed class TableDefinition
	{
		private readonly StringComparer _comparer;
		private readonly HashSet<string> _primaryKeyColumns;
		private readonly List<ForeignKeyDefinition> _foreignKeys;
		private readonly HashSet<string> _references;
		private readonly HashSet<string> _referencedBy;

		public TableDefinition(string schema, string name, StringComparer comparer)
		{
			Schema = schema;
			Name = name;
			_comparer = comparer ?? StringComparer.OrdinalIgnoreCase;
			Columns = new List<ColumnDefinition>();
			_primaryKeyColumns = new HashSet<string>(_comparer);
			_foreignKeys = new List<ForeignKeyDefinition>();
			_references = new HashSet<string>(_comparer);
			_referencedBy = new HashSet<string>(_comparer);
		}

		public string Schema { get; }
		public string Name { get; }
		public string FullName => string.Concat(Schema, ".", Name);
		public List<ColumnDefinition> Columns { get; }
		public IReadOnlyList<ForeignKeyDefinition> ForeignKeys => _foreignKeys;
		public IReadOnlyList<string> PrimaryKeyColumns => _primaryKeyColumns.OrderBy(column => column, _comparer).ToList();
		public IReadOnlyList<string> References => _references.OrderBy(item => item, _comparer).ToList();
		public IReadOnlyList<string> ReferencedBy => _referencedBy.OrderBy(item => item, _comparer).ToList();

		public void AddPrimaryKey(IEnumerable<string> columns)
		{
			foreach (var column in columns)
			{
				_primaryKeyColumns.Add(column);
			}
		}

		public void AddForeignKey(ForeignKeyDefinition foreignKey)
		{
			if (foreignKey != null)
			{
				_foreignKeys.Add(foreignKey);
			}
		}

		public void AddReference(string tableName) => _references.Add(tableName);
		public void AddReferencedBy(string tableName) => _referencedBy.Add(tableName);
	}

	public sealed class ColumnDefinition
	{
		public string ColumnName { get; set; }
		public string DataType { get; set; }
		public bool IsNullable { get; set; }
		public string ColumnDefault { get; set; }
		public bool IsPrimaryKey { get; set; }
		public bool IsForeignKey { get; set; }
		public string ForeignSchema { get; set; }
		public string ForeignTable { get; set; }
		public string ForeignColumn { get; set; }
	}

	public sealed class ForeignKeyDefinition
	{
		public ForeignKeyDefinition(TableIdentifier referencingTable, TableIdentifier referencedTable, IReadOnlyList<string> referencingColumns, IReadOnlyList<string> referencedColumns)
		{
			ReferencingTable = referencingTable;
			ReferencedTable = referencedTable;
			ReferencingColumns = referencingColumns ?? Array.Empty<string>();
			ReferencedColumns = referencedColumns ?? Array.Empty<string>();
		}

		public string Name { get; set; }
		public TableIdentifier ReferencingTable { get; }
		public TableIdentifier ReferencedTable { get; }
		public IReadOnlyList<string> ReferencingColumns { get; }
		public IReadOnlyList<string> ReferencedColumns { get; }

		public IReadOnlyList<JoinEdge> ToJoinEdges()
		{
			var edges = new List<JoinEdge>();
			var count = Math.Min(ReferencingColumns.Count, ReferencedColumns.Count);
			for (var i = 0; i < count; i++)
			{
				edges.Add(new JoinEdge
				{
					FromTable = ReferencingTable.FullName,
					ToTable = ReferencedTable.FullName,
					FromColumn = ReferencingColumns[i],
					ToColumn = ReferencedColumns[i],
					ConstraintName = Name
				});
			}

			return edges;
		}
	}

	public sealed class JoinEdge
	{
		public string FromTable { get; set; }
		public string ToTable { get; set; }
		public string FromColumn { get; set; }
		public string ToColumn { get; set; }
		public string ConstraintName { get; set; }
	}

	public sealed class TableIdentifier
	{
		public TableIdentifier(string schema, string name)
		{
			Schema = schema;
			Name = name;
		}

		public string Schema { get; }
		public string Name { get; }
		public string FullName => string.Concat(Schema, ".", Name);
	}
}
