using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;

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
