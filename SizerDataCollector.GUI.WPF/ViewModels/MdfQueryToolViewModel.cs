using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using SizerDataCollector.GUI.WPF.Commands;
using SizerDataCollector.GUI.WPF.Services;

namespace SizerDataCollector.GUI.WPF.ViewModels
{
	public sealed class MdfQueryToolViewModel : INotifyPropertyChanged
	{
		private readonly MdfQueryService _queryService;
		private DataView _previewRows;
		private string _serverName;
		private string _databaseName;
		private string _mdfPath;
		private string _queryText;
		private string _statusMessage;
		private bool _isBusy;
		private MdfSourceOption _selectedSourceMode;
		private string _selectedTable;
		private bool _useSqlAuthentication;
		private string _userName;
		private string _password;
		private bool _readOnly;
		private string _previewSummary;
		private string _schemaDetailsText = string.Empty;

		public MdfQueryToolViewModel()
			: this(new MdfQueryService())
		{
		}

		public MdfQueryToolViewModel(MdfQueryService queryService)
		{
			_queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

			Title = "MDF Query Tool";
			Description = "Query MDF files from local or network SQL Server instances.";

			SourceModes = new ObservableCollection<MdfSourceOption>
			{
				new MdfSourceOption(MdfSourceMode.AttachedInstance, "Attached SQL Server instance"),
				new MdfSourceOption(MdfSourceMode.LocalFile, "Local MDF file")
			};

			SelectedSourceMode = SourceModes[0];
			ServerName = "localhost";
			DatabaseName = "objectstore";
			MdfPath = @"C:\Data\objectstore.mdf";
			ReadOnly = true;

			Tables = new ObservableCollection<string>();
			QueryText = "SELECT TOP (100) *\nFROM dbo.LotHistory;";

			BrowseMdfCommand = new RelayCommand(_ => BrowseForMdf(), _ => SelectedSourceMode?.Mode == MdfSourceMode.LocalFile);
			LoadSchemaCommand = new RelayCommand(_ => LoadSchema(), _ => CanLoadSchema());
			LoadSchemaDetailsCommand = new RelayCommand(_ => LoadSchemaDetails(), _ => CanLoadSchema());
			GenerateQueryCommand = new RelayCommand(_ => GenerateQuery(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SelectedTable));
			PreviewQueryCommand = new RelayCommand(_ => PreviewQuery(), _ => !IsBusy && !string.IsNullOrWhiteSpace(QueryText));
			ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => PreviewRows != null && !IsBusy);
			ExportPdfCommand = new RelayCommand(_ => ExportPdf(), _ => PreviewRows != null && !IsBusy);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public string Title { get; }

		public string Description { get; }

		public ObservableCollection<MdfSourceOption> SourceModes { get; }

		public MdfSourceOption SelectedSourceMode
		{
			get => _selectedSourceMode;
			set
			{
				if (SetProperty(ref _selectedSourceMode, value))
				{
					OnPropertyChanged(nameof(IsLocalFileMode));
					RefreshCommandStates();
				}
			}
		}

		public bool IsLocalFileMode => SelectedSourceMode?.Mode == MdfSourceMode.LocalFile;

		public bool IsSqlAuthEnabled => UseSqlAuthentication;

		public string ServerName
		{
			get => _serverName;
			set
			{
				if (SetProperty(ref _serverName, value))
				{
					OnPropertyChanged(nameof(ConnectionSummary));
					RefreshCommandStates();
				}
			}
		}

		public string DatabaseName
		{
			get => _databaseName;
			set
			{
				if (SetProperty(ref _databaseName, value))
				{
					OnPropertyChanged(nameof(ConnectionSummary));
					RefreshCommandStates();
				}
			}
		}

		public string MdfPath
		{
			get => _mdfPath;
			set
			{
				if (SetProperty(ref _mdfPath, value))
				{
					OnPropertyChanged(nameof(ConnectionSummary));
					RefreshCommandStates();
				}
			}
		}

		public string ConnectionSummary => SelectedSourceMode?.Mode == MdfSourceMode.LocalFile
			? $"Server: {ServerName} • MDF: {MdfPath}"
			: $"Server: {ServerName} • Database: {DatabaseName}";

		public bool UseSqlAuthentication
		{
			get => _useSqlAuthentication;
			set
			{
				if (SetProperty(ref _useSqlAuthentication, value))
				{
					OnPropertyChanged(nameof(IsSqlAuthEnabled));
					RefreshCommandStates();
				}
			}
		}

		public string UserName
		{
			get => _userName;
			set
			{
				if (SetProperty(ref _userName, value))
				{
					RefreshCommandStates();
				}
			}
		}

		public string Password
		{
			get => _password;
			set
			{
				if (SetProperty(ref _password, value))
				{
					RefreshCommandStates();
				}
			}
		}

		public bool ReadOnly
		{
			get => _readOnly;
			set => SetProperty(ref _readOnly, value);
		}

		public ObservableCollection<string> Tables { get; }

		public string SelectedTable
		{
			get => _selectedTable;
			set
			{
				if (SetProperty(ref _selectedTable, value))
				{
					RefreshCommandStates();
				}
			}
		}

		public string QueryText
		{
			get => _queryText;
			set
			{
				if (SetProperty(ref _queryText, value))
				{
					RefreshCommandStates();
				}
			}
		}

		public DataView PreviewRows
		{
			get => _previewRows;
			private set
			{
				if (SetProperty(ref _previewRows, value))
				{
					OnPropertyChanged(nameof(HasPreviewRows));
					RefreshCommandStates();
				}
			}
		}

		public bool HasPreviewRows => PreviewRows != null;

		public string PreviewSummary
		{
			get => _previewSummary;
			private set => SetProperty(ref _previewSummary, value);
		}

		public string SchemaDetailsText
		{
			get => _schemaDetailsText;
			private set => SetProperty(ref _schemaDetailsText, value);
		}

		public string StatusMessage
		{
			get => _statusMessage;
			private set => SetProperty(ref _statusMessage, value);
		}

		public bool IsBusy
		{
			get => _isBusy;
			private set
			{
				if (SetProperty(ref _isBusy, value))
				{
					RefreshCommandStates();
				}
			}
		}

		public ICommand BrowseMdfCommand { get; }

		public ICommand LoadSchemaCommand { get; }

		public ICommand LoadSchemaDetailsCommand { get; }

		public ICommand GenerateQueryCommand { get; }

		public ICommand PreviewQueryCommand { get; }

		public ICommand ExportCsvCommand { get; }

		public ICommand ExportPdfCommand { get; }

		private void BrowseForMdf()
		{
			var dialog = new OpenFileDialog
			{
				Filter = "SQL Server data files (*.mdf)|*.mdf|All files (*.*)|*.*",
				Title = "Select MDF file"
			};

			if (dialog.ShowDialog() == true)
			{
				MdfPath = dialog.FileName;
			}
		}

		private void LoadSchema()
		{
			try
			{
				IsBusy = true;
				StatusMessage = "Loading schema...";
				Tables.Clear();

				var tables = _queryService.LoadTables(BuildConnectionOptions());
				foreach (var table in tables)
				{
					Tables.Add(table);
				}

				StatusMessage = Tables.Count == 0 ? "No tables found." : $"Loaded {Tables.Count} tables.";
				PreviewSummary = string.Empty;
			}
			catch (Exception ex)
			{
				StatusMessage = $"Schema load failed: {ex.Message}";
			}
			finally
			{
				IsBusy = false;
			}
		}

		private void GenerateQuery()
		{
			QueryText = $"SELECT TOP (100) *\nFROM {SelectedTable};";
			StatusMessage = "Query template generated.";
		}

		private void LoadSchemaDetails()
		{
			try
			{
				IsBusy = true;
				StatusMessage = "Inspecting schema...";
				var details = _queryService.LoadSchemaDetails(BuildConnectionOptions());
				SchemaDetailsText = BuildDelimitedText(details, '\t');
				StatusMessage = "Schema metadata loaded.";
			}
			catch (Exception ex)
			{
				StatusMessage = $"Schema inspection failed: {ex.Message}";
				SchemaDetailsText = string.Empty;
			}
			finally
			{
				IsBusy = false;
			}
		}

		private void PreviewQuery()
		{
			try
			{
				IsBusy = true;
				StatusMessage = "Running query...";
				var preview = _queryService.LoadPreview(BuildConnectionOptions(), QueryText);
				PreviewRows = preview.DefaultView;
				PreviewSummary = $"{preview.Rows.Count} rows loaded.";
				StatusMessage = "Preview loaded.";
			}
			catch (Exception ex)
			{
				StatusMessage = $"Query failed: {ex.Message}";
				PreviewSummary = string.Empty;
			}
			finally
			{
				IsBusy = false;
			}
		}

		private void ExportCsv()
		{
			if (PreviewRows?.Table == null)
			{
				StatusMessage = "Nothing to export.";
				return;
			}

			var dialog = new SaveFileDialog
			{
				Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
				Title = "Export preview to CSV",
				FileName = $"{DatabaseName}_preview.csv"
			};

			if (dialog.ShowDialog() != true)
			{
				return;
			}

			try
			{
				var csv = BuildCsv(PreviewRows.Table);
				_queryService.WriteText(dialog.FileName, csv);
				StatusMessage = $"Exported CSV to {dialog.FileName}.";
			}
			catch (Exception ex)
			{
				StatusMessage = $"CSV export failed: {ex.Message}";
			}
		}

		private void ExportPdf()
		{
			if (PreviewRows?.Table == null)
			{
				StatusMessage = "Nothing to export.";
				return;
			}

			var dialog = new SaveFileDialog
			{
				Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
				Title = "Export preview to PDF",
				FileName = $"{DatabaseName}_preview.pdf"
			};

			if (dialog.ShowDialog() != true)
			{
				return;
			}

			try
			{
				_queryService.WritePdf(dialog.FileName, PreviewRows.Table);
				StatusMessage = $"Exported PDF to {dialog.FileName}.";
			}
			catch (Exception ex)
			{
				StatusMessage = $"PDF export failed: {ex.Message}";
			}
		}

		private bool CanLoadSchema()
		{
			if (IsBusy || SelectedSourceMode == null)
			{
				return false;
			}

			if (SelectedSourceMode.Mode == MdfSourceMode.AttachedInstance)
			{
				return !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(DatabaseName);
			}

			return !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(MdfPath);
		}

		private MdfConnectionOptions BuildConnectionOptions()
		{
			return new MdfConnectionOptions
			{
				Mode = SelectedSourceMode.Mode,
				ServerName = ServerName,
				DatabaseName = DatabaseName,
				MdfPath = MdfPath,
				UseSqlAuthentication = UseSqlAuthentication,
				UserName = UserName,
				Password = Password,
				ReadOnly = ReadOnly
			};
		}

		private string BuildCsv(DataTable table)
		{
			return BuildDelimitedText(table, ',', EscapeCsv);
		}

		private string BuildDelimitedText(DataTable table, char delimiter, Func<string, string> formatter = null)
		{
			var builder = new StringBuilder();
			for (var i = 0; i < table.Columns.Count; i++)
			{
				if (i > 0)
				{
					builder.Append(delimiter);
				}

				var value = table.Columns[i].ColumnName;
				builder.Append(formatter == null ? value : formatter(value));
			}

			builder.AppendLine();

			foreach (DataRow row in table.Rows)
			{
				for (var i = 0; i < table.Columns.Count; i++)
				{
					if (i > 0)
					{
						builder.Append(delimiter);
					}

					var value = Convert.ToString(row[i]);
					builder.Append(formatter == null ? value : formatter(value));
				}

				builder.AppendLine();
			}

			return builder.ToString();
		}

		private string EscapeCsv(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			var needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n");
			var escaped = value.Replace("\"", "\"\"");
			return needsQuotes ? $"\"{escaped}\"" : escaped;
		}

		private void RefreshCommandStates()
		{
			(BrowseMdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(LoadSchemaCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(LoadSchemaDetailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(GenerateQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(PreviewQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(ExportCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
			(ExportPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
		}

		private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
		{
			if (Equals(storage, value))
			{
				return false;
			}

			storage = value;
			OnPropertyChanged(propertyName);
			return true;
		}

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
