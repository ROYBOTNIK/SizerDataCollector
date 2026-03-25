using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SizerDataCollector.Core.Schema;

namespace SizerDataCollector.Tests
{
	[TestClass]
	public class SchemaParserTests
	{
		private const string SampleDdl = @"
CREATE TABLE public.parent (
	id int PRIMARY KEY NOT NULL,
	code text DEFAULT 'x'
);

CREATE TABLE child (
	id int,
	parent_id int,
	CONSTRAINT child_pk PRIMARY KEY (id)
);

CREATE TABLE public.grandchild (
	id int PRIMARY KEY,
	child_id int REFERENCES public.child(id)
);

CREATE TABLE other_schema.lookup (
	key1 int,
	key2 int,
	CONSTRAINT lookup_pk PRIMARY KEY (key1, key2)
);

CREATE TABLE public.composite_ref (
	key1 int,
	key2 int,
	missing_id int
);

ALTER TABLE public.child ADD CONSTRAINT fk_child_parent FOREIGN KEY (parent_id) REFERENCES public.parent(id);
ALTER TABLE public.composite_ref ADD CONSTRAINT fk_lookup FOREIGN KEY (key1, key2) REFERENCES other_schema.lookup(key1, key2);
ALTER TABLE public.composite_ref ADD CONSTRAINT fk_missing FOREIGN KEY (missing_id) REFERENCES public.missing_table(id);
";

		[TestMethod]
		public void ParseSchema_Builds_Table_Metadata_And_Relationships()
		{
			var path = WriteTempSchema(SampleDdl);
			try
			{
				var map = SchemaParser.ParseSchemaDefinitionFile(path);

				Assert.IsTrue(map.Success);
				Assert.AreEqual(5, map.Tables.Count);
				Assert.IsTrue(map.Warnings.Any(warning => warning.Contains("missing table", StringComparison.OrdinalIgnoreCase)));

				var parent = map.Tables.Single(table => table.FullName == "public.parent");
				var parentId = parent.Columns.Single(column => column.ColumnName == "id");
				Assert.IsTrue(parentId.IsPrimaryKey);
				Assert.IsFalse(parentId.IsNullable);
				Assert.AreEqual("text", parent.Columns.Single(column => column.ColumnName == "code").DataType);

				var child = map.Tables.Single(table => table.FullName == "public.child");
				var childId = child.Columns.Single(column => column.ColumnName == "id");
				var parentIdFk = child.Columns.Single(column => column.ColumnName == "parent_id");
				Assert.IsTrue(childId.IsPrimaryKey);
				Assert.IsTrue(parentIdFk.IsForeignKey);
				Assert.AreEqual("public", parentIdFk.ForeignSchema);
				Assert.AreEqual("parent", parentIdFk.ForeignTable);
				Assert.AreEqual("id", parentIdFk.ForeignColumn);

				var grandchild = map.Tables.Single(table => table.FullName == "public.grandchild");
				Assert.IsTrue(grandchild.Columns.Single(column => column.ColumnName == "child_id").IsForeignKey);

				var lookup = map.Tables.Single(table => table.FullName == "other_schema.lookup");
				Assert.AreEqual(2, lookup.PrimaryKeyColumns.Count);

				var compositeRef = map.Tables.Single(table => table.FullName == "public.composite_ref");
				var key1 = compositeRef.Columns.Single(column => column.ColumnName == "key1");
				var key2 = compositeRef.Columns.Single(column => column.ColumnName == "key2");
				Assert.IsTrue(key1.IsForeignKey);
				Assert.IsTrue(key2.IsForeignKey);
				Assert.AreEqual("key1", key1.ForeignColumn);
				Assert.AreEqual("key2", key2.ForeignColumn);

				Assert.IsTrue(parent.ReferencedBy.Contains("public.child"));
				Assert.IsTrue(child.ReferencedBy.Contains("public.grandchild"));

				var joinPath = map.FindJoinPath("public.grandchild", "public.parent");
				Assert.AreEqual(2, joinPath.Count);
			}
			finally
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		private static string WriteTempSchema(string ddl)
		{
			var path = Path.Combine(Path.GetTempPath(), $"schema_{Guid.NewGuid():N}.sql");
			File.WriteAllText(path, ddl);
			return path;
		}
	}
}
