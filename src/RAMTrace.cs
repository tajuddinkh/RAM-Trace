using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("RAM Trace V2.0, Lightweight Windows Memory Monitor")]
[assembly: System.Reflection.AssemblyProduct("RAM Trace V2.0")]
[assembly: System.Reflection.AssemblyDescription("RAM Trace, Lightweight Windows Memory Monitor")]
[assembly: System.Reflection.AssemblyCompany("Tajud Din")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Tajud Din")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("2.0.0")]

namespace RamLight
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            StartupPerformance.Mark("Application entry");
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\RamLight.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("RAM Trace is already running. Check the system tray.", "RAM Trace", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StartupPerformance.Mark("WinForms initialised");
                Application.Run(new MainForm());
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label _liveValue = new Label();
        private readonly Label _avgValue = new Label();
        private readonly Label _peakValue = new Label();
        private readonly Label _lowValue = new Label();
        private readonly Label _secondaryMetrics = new Label();
        private readonly Label _breakdownMetrics = new Label();
        private readonly Label _selfMetrics = new Label();
        private readonly DoubleBufferedDataGridView _processGrid = new DoubleBufferedDataGridView();
        private readonly Panel _contentHost = new Panel();
        private readonly DataGridView _historyGrid = new DataGridView();
        private readonly DoubleBufferedDataGridView _memoryBreakdownGrid = new DoubleBufferedDataGridView();
        private readonly TextBox _detailsBox = new TextBox();
        private readonly Label _memoryWindowsValue = new Label();
        private readonly Label _memoryMicrosoftValue = new Label();
        private readonly Label _memoryThirdPartyValue = new Label();
        private readonly Label _memoryUnclassifiedValue = new Label();
        private readonly Label _memorySystemSharedValue = new Label();
        private readonly Label _memoryProcessPrivateValue = new Label();
        private readonly Label _memoryTotalLiveValue = new Label();
        private readonly Label _memoryBreakdownExplanation = new Label();
        private readonly Label _memoryReconciliation = new Label();
        private readonly List<Label> _cardTitleLabels = new List<Label>();
        private readonly List<Label> _cardValueLabels = new List<Label>();
        private readonly List<Label> _memoryCardTitleLabels = new List<Label>();
        private readonly List<Label> _memoryCardValueLabels = new List<Label>();
        private TableLayoutPanel _rootLayout;
        private TableLayoutPanel _cardsLayout;
        private TableLayoutPanel _metricsLayout;
        private TableLayoutPanel _processLayout;
        private TableLayoutPanel _memoryBreakdownLayout;
        private TableLayoutPanel _memoryBreakdownCards;
        private TableLayoutPanel _memoryReconciliationLayout;
        private TabControl _tabs;
        private Label _detailsHeader;
        private Label _detailsLegend;
        private Label _footerHint;
        private Label _footerAuthor;
        private Button _folderButton;
        private Button _glossaryButton;
        private Button _diagnosticsButton;
        private readonly System.Windows.Forms.Timer _layoutDebounceTimer = new System.Windows.Forms.Timer();
        private int _lastLayoutDpi = -1;
        private FormWindowState _lastLayoutWindowState = FormWindowState.Normal;
        private Font _gridBoldFont;
        private Font _memoryBreakdownBoldFont;
        private readonly NotifyIcon _trayIcon = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _systemTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _processTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _persistTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _diagnosticHeartbeatTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _processGridInteractionTimer = new System.Windows.Forms.Timer();
        private readonly PerformanceDiagnostics _diagnostics = new PerformanceDiagnostics();
        private readonly Stopwatch _applicationUptime = Stopwatch.StartNew();

        private readonly Dictionary<string, AggregateStat> _processStats = new Dictionary<string, AggregateStat>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExecutableMetadata> _metadataCache = new Dictionary<string, ExecutableMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly ProcessClassificationCache _processClassificationCache = new ProcessClassificationCache();
        private Dictionary<string, ProcessGroupSample> _lastGroups = new Dictionary<string, ProcessGroupSample>(StringComparer.OrdinalIgnoreCase);
        private MemoryBreakdownSnapshot _lastMemoryBreakdown = new MemoryBreakdownSnapshot();
        private readonly Queue<double> _cpuWindow = new Queue<double>();
        private readonly AutoResetEvent _processWorkSignal = new AutoResetEvent(false);
        private readonly object _processRequestSync = new object();
        private Thread _processWorkerThread;
        private ProcessScanRequest _pendingProcessRequest;
        private volatile bool _processWorkerStopping;
        private int _processCycleGate;
        private int _skippedProcessScansSinceLastSample;
        private int _totalSkippedProcessScans;
        private ProcessGuideForm _processGuideForm;

        private DateTime _statDate = DateTime.Today;
        private long _systemSamples;
        private double _systemSumBytes;
        private ulong _systemMinBytes = ulong.MaxValue;
        private ulong _systemPeakBytes;
        private ulong _totalPhysicalBytes;
        private DateTime _lastCpuWall = DateTime.UtcNow;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private bool _reallyExit;
        private bool _updatingProcessGrid;
        private bool _initialProcessSelectionDone;
        private int _metadataLookupsThisScan;
        private int _lowCpuSeconds;
        private int _cpuAboveSoftSeconds;
        private int _cpuAboveHardSeconds;
        private int _normalProcessInterval = 3000;
        private Dictionary<int, List<string>> _cachedServicesByPid = new Dictionary<int, List<string>>();
        private Dictionary<int, string> _cachedServiceTextByPid = new Dictionary<int, string>();
        private DateTime _lastServiceRefreshUtc = DateTime.MinValue;
        private ulong _lastAccountingProcessPrivateBytes;
        private ulong _lastAccountingKernelBytes;
        private ulong _lastAccountingKernelPagedBytes;
        private ulong _lastAccountingKernelNonPagedBytes;
        private ulong _lastAccountingOtherBytes;
        private ulong _lastAccountingLiveBytes;
        private int _lastUnclassifiedProcessCount;
        private double _diagLastMemoryBreakdownMs;
        private DateTime _diagLastProcessRequestUtc = DateTime.MinValue;
        private int _diagLastProcessRequestIntervalMs;
        private DateTime _diagHeartbeatLastUtc = DateTime.MinValue;
        private double _diagLastSystemSampleMs;
        private double _diagLastGridPrepareMs;
        private double _diagLastGridUiMs;
        private double _diagLastDetailsMs;
        private double _diagLastFooterMs;
        private double _diagLastDataWriteMs;
        private bool _diagWasMinimized;
        private bool _diagLastThrottleState;
        private ulong _diagLastLiveSystemRamBytes;
        private int _diagGridRowsAdded;
        private int _diagGridRowsUpdated;
        private int _diagGridRowsRemoved;
        private int _diagFullGridRebuilds;
        private bool _startupProcessScanPending;
        private bool _startupReadyMarked;
        private readonly Dictionary<string, DataGridViewRow> _processRowsByKey = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ProcessGroupSample> _pendingGridGroups;
        private bool _processGridInteractionActive;
        private bool _suppressProcessGridInteractionEvents;
        private int _diagGridStructuralChanges;
        private int _diagGridValueOnlyUpdates;
        private int _diagGridInteractionDeferredRefreshCount;
        private int _diagGridSnapshotsCoalesced;
        private int _diagGridDuplicateKeyCount;
        private double _diagLastGridSortMs;
        private readonly Dictionary<string, string> _classificationAuditFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly string _dataDir;
        private readonly string _statePath;
        private readonly string _historyPath;
        private readonly string _selfProcessStableKey;

        public MainForm()
        {
            StartupPerformance.Mark("Main window construction started");
            Text = "RAM Trace V2.0";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(940, 620);
            Size = new Size(1180, 760);
            AutoScaleMode = AutoScaleMode.None;
            Font = CreateUiFont(9.5F, FontStyle.Regular);
            BackColor = Color.FromArgb(244, 246, 248);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RamLight");
            _statePath = Path.Combine(_dataDir, "today_state.tsv");
            _historyPath = Path.Combine(_dataDir, "history.tsv");
            Directory.CreateDirectory(_dataDir);

            BuildUi();
            StartupPerformance.Mark("UI construction completed");
            ApplyDeterministicLayout(true);
            ConfigureTray();
            LoadState();
            StartupPerformance.Mark("State loading completed");
            RefreshHistoryGrid();
            StartupPerformance.Mark("History loading completed");

            Process me = Process.GetCurrentProcess();
            _selfProcessStableKey = "pws2:proc:" + SafeProcessName(me);
            _lastCpuTime = me.TotalProcessorTime;
            me.Dispose();

            StartProcessWorker();

            _systemTimer.Interval = 1000;
            _systemTimer.Tick += delegate { SystemTick(); };
            _systemTimer.Start();

            _processTimer.Interval = 3000;
            _processTimer.Tick += delegate { RequestProcessSnapshot(false); };
            _processTimer.Start();

            _persistTimer.Interval = 30000;
            _persistTimer.Tick += delegate { PersistState(); RefreshHistoryGrid(); };
            _persistTimer.Start();

            _diagnosticHeartbeatTimer.Interval = 1000;
            _diagnosticHeartbeatTimer.Tick += delegate { DiagnosticHeartbeatTick(); };

            _processGridInteractionTimer.Interval = 400;
            _processGridInteractionTimer.Tick += delegate { ProcessGridInteractionTimerTick(); };

            _layoutDebounceTimer.Interval = 125;
            _layoutDebounceTimer.Tick += delegate
            {
                _layoutDebounceTimer.Stop();
                if (WindowState != FormWindowState.Minimized) ApplyDeterministicLayout(false);
            };
            StartupPerformance.Mark("Monitoring timers configured");

            Shown += delegate
            {
                ApplyDeterministicLayout(true);
                SystemTick();
                StartupPerformance.Mark("First RAM sample completed");
                _startupProcessScanPending = true;
                RequestProcessSnapshot(true);
            };
        }

        private void BuildUi()
        {
            _rootLayout = new TableLayoutPanel();
            _rootLayout.Dock = DockStyle.Fill;
            _rootLayout.ColumnCount = 1;
            _rootLayout.RowCount = 4;
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 109F));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _rootLayout.Padding = new Padding(12, 10, 12, 8);
            _rootLayout.BackColor = Color.FromArgb(244, 246, 248);

            // All resizing now flows through native containers. Fixed design
            // constants are re-applied from the current DPI, while width is
            // handled by Dock/percentage columns rather than by persisted pixel
            // geometry from a previous window size.
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = BackColor;
            _contentHost.Controls.Add(_rootLayout);
            Controls.Add(_contentHost);

            _cardsLayout = new TableLayoutPanel();
            _cardsLayout.Dock = DockStyle.Fill;
            _cardsLayout.ColumnCount = 4;
            _cardsLayout.RowCount = 1;
            _cardsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _cardsLayout.BackColor = BackColor;
            for (int i = 0; i < 4; i++)
                _cardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            _cardsLayout.Controls.Add(CreateCard("LIVE RAM", _liveValue), 0, 0);
            _cardsLayout.Controls.Add(CreateCard("TODAY AVERAGE", _avgValue), 1, 0);
            _cardsLayout.Controls.Add(CreateCard("TODAY PEAK", _peakValue), 2, 0);
            _cardsLayout.Controls.Add(CreateCard("TODAY LOW", _lowValue), 3, 0);
            _rootLayout.Controls.Add(_cardsLayout, 0, 0);

            _metricsLayout = new TableLayoutPanel();
            _metricsLayout.Dock = DockStyle.Fill;
            _metricsLayout.ColumnCount = 2;
            _metricsLayout.RowCount = 2;
            _metricsLayout.BackColor = BackColor;
            _metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            _metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            _metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            _metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _secondaryMetrics.Dock = DockStyle.Fill;
            _secondaryMetrics.TextAlign = ContentAlignment.MiddleLeft;
            _secondaryMetrics.ForeColor = Color.FromArgb(102, 112, 133);
            _selfMetrics.Dock = DockStyle.Fill;
            _selfMetrics.TextAlign = ContentAlignment.MiddleRight;
            _selfMetrics.ForeColor = Color.FromArgb(102, 112, 133);
            _breakdownMetrics.Dock = DockStyle.Fill;
            _breakdownMetrics.TextAlign = ContentAlignment.MiddleLeft;
            _breakdownMetrics.ForeColor = Color.FromArgb(102, 112, 133);

            _metricsLayout.Controls.Add(_secondaryMetrics, 0, 0);
            _metricsLayout.Controls.Add(_selfMetrics, 1, 0);
            _metricsLayout.Controls.Add(_breakdownMetrics, 0, 1);
            _metricsLayout.SetColumnSpan(_breakdownMetrics, 2);
            _rootLayout.Controls.Add(_metricsLayout, 0, 1);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.Padding = new Point(12, 4);
            _tabs.DrawItem += DrawMainTab;
            _tabs.SelectedIndexChanged += delegate { _tabs.Invalidate(); };

            TabPage processes = new TabPage("Processes & services");
            TabPage memoryBreakdown = new TabPage("Memory breakdown");
            TabPage history = new TabPage("Daily history");
            processes.Padding = new Padding(6);
            memoryBreakdown.Padding = new Padding(6);
            history.Padding = new Padding(6);
            processes.BackColor = Color.White;
            memoryBreakdown.BackColor = Color.White;
            history.BackColor = Color.White;
            _tabs.TabPages.Add(processes);
            _tabs.TabPages.Add(memoryBreakdown);
            _tabs.TabPages.Add(history);
            _rootLayout.Controls.Add(_tabs, 0, 2);

            ConfigureProcessGrid();
            ConfigureMemoryBreakdownGrid();
            ConfigureHistoryGrid();

            _processLayout = new TableLayoutPanel();
            _processLayout.Dock = DockStyle.Fill;
            _processLayout.ColumnCount = 1;
            _processLayout.RowCount = 2;
            _processLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _processLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
            _processLayout.Controls.Add(_processGrid, 0, 0);
            _processLayout.Controls.Add(CreateDetailsPanel(), 0, 1);
            processes.Controls.Add(_processLayout);
            memoryBreakdown.Controls.Add(CreateMemoryBreakdownPanel());
            history.Controls.Add(_historyGrid);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(250, 251, 252);
            footer.Padding = new Padding(2, 0, 2, 0);

            Label hint = new Label();
            hint.Text = "RAM Trace V2.0 | Private working set | Local only";
            hint.Dock = DockStyle.Left;
            hint.Width = 320;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.ForeColor = Color.FromArgb(102, 112, 133);

            _folderButton = new Button();
            _folderButton.Text = "Open data folder";
            _folderButton.AutoSize = true;
            _folderButton.Dock = DockStyle.Right;
            _folderButton.FlatStyle = FlatStyle.System;
            _folderButton.Click += delegate
            {
                if (_diagnostics.IsEnabled) _diagnostics.LogEvent("Data folder opened", "User opened the RAM Trace local data folder.", 0);
                try { Process.Start("explorer.exe", _dataDir); }
                catch { }
            };

            _glossaryButton = new Button();
            _glossaryButton.Text = "Process guide";
            _glossaryButton.AutoSize = true;
            _glossaryButton.Dock = DockStyle.Right;
            _glossaryButton.FlatStyle = FlatStyle.System;
            _glossaryButton.Click += delegate { OpenProcessGuide(); };

            _diagnosticsButton = new Button();
            _diagnosticsButton.Text = "Diagnostics: OFF";
            _diagnosticsButton.AutoSize = true;
            _diagnosticsButton.Dock = DockStyle.Right;
            _diagnosticsButton.FlatStyle = FlatStyle.System;
            _diagnosticsButton.Click += delegate { ToggleDiagnostics(); };

            Label author = new Label();
            author.Text = "Designed and developed by Tajud Din";
            author.Dock = DockStyle.Right;
            author.Width = 220;
            author.Padding = new Padding(8, 0, 8, 0);
            author.TextAlign = ContentAlignment.MiddleRight;
            author.ForeColor = Color.FromArgb(102, 112, 133);
            author.AutoEllipsis = false;

            _footerHint = hint;
            _footerAuthor = author;
            footer.Controls.Add(author);
            footer.Controls.Add(_diagnosticsButton);
            footer.Controls.Add(_folderButton);
            footer.Controls.Add(_glossaryButton);
            footer.Controls.Add(hint);
            _rootLayout.Controls.Add(footer, 0, 3);
        }

        private Control CreateCard(string title, Label valueLabel)
        {
            Panel outer = new Panel();
            outer.Dock = DockStyle.Fill;
            outer.Margin = new Padding(5);
            outer.BackColor = Color.White;
            outer.BorderStyle = BorderStyle.FixedSingle;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 30;
            titleLabel.Padding = new Padding(12, 9, 0, 0);
            titleLabel.Font = CreateUiFont(8.75F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(102, 112, 133);
            _cardTitleLabels.Add(titleLabel);

            valueLabel.Text = "--";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Padding = new Padding(11, 0, 5, 6);
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Font = CreateUiFont(18F, FontStyle.Bold);
            valueLabel.ForeColor = Color.FromArgb(24, 34, 48);
            _cardValueLabels.Add(valueLabel);

            outer.Controls.Add(valueLabel);
            outer.Controls.Add(titleLabel);
            return outer;
        }

        private Control CreateDetailsPanel()
        {
            Panel outer = new Panel();
            outer.Dock = DockStyle.Fill;
            outer.Padding = new Padding(0, 7, 0, 0);

            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Color.White;
            card.BorderStyle = BorderStyle.FixedSingle;

            Label header = new Label();
            _detailsHeader = header;
            header.Text = "PROCESS INFORMATION";
            header.Dock = DockStyle.Top;
            header.Height = 27;
            header.Padding = new Padding(9, 7, 0, 0);
            header.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            header.ForeColor = Color.FromArgb(80, 87, 96);

            Label legend = new Label();
            _detailsLegend = legend;
            legend.Text = "Live RAM colour:  250+ MB light blue   |   500+ MB yellow   |   1 GB+ orange   |   2 GB+ red";
            legend.Dock = DockStyle.Bottom;
            legend.Height = 23;
            legend.Padding = new Padding(9, 2, 0, 0);
            legend.ForeColor = Color.FromArgb(100, 105, 112);

            _detailsBox.Dock = DockStyle.Fill;
            _detailsBox.Multiline = true;
            _detailsBox.ReadOnly = true;
            _detailsBox.BorderStyle = BorderStyle.None;
            _detailsBox.BackColor = Color.White;
            _detailsBox.ScrollBars = ScrollBars.Vertical;
            _detailsBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            _detailsBox.Text = "The largest RAM consumer will be selected automatically. Select any other row to see what it is and what it belongs to.";

            card.Controls.Add(_detailsBox);
            card.Controls.Add(legend);
            card.Controls.Add(header);
            outer.Controls.Add(card);
            return outer;
        }

        private Control CreateMemoryBreakdownPanel()
        {
            _memoryBreakdownLayout = new TableLayoutPanel();
            _memoryBreakdownLayout.Dock = DockStyle.Fill;
            _memoryBreakdownLayout.ColumnCount = 1;
            _memoryBreakdownLayout.RowCount = 4;
            _memoryBreakdownLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            _memoryBreakdownLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            _memoryBreakdownLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            _memoryBreakdownLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _memoryBreakdownLayout.BackColor = Color.White;

            _memoryBreakdownExplanation.Dock = DockStyle.Fill;
            _memoryBreakdownExplanation.Padding = new Padding(8, 5, 8, 4);
            _memoryBreakdownExplanation.TextAlign = ContentAlignment.MiddleLeft;
            _memoryBreakdownExplanation.ForeColor = Color.FromArgb(102, 112, 133);
            _memoryBreakdownExplanation.Text =
                "Process-private RAM is classified by ownership. Kernel / system / shared memory remains separate because it cannot be attributed reliably to individual apps. Microsoft apps are separated from Windows system processes.";

            _memoryBreakdownCards = new TableLayoutPanel();
            _memoryBreakdownCards.Dock = DockStyle.Fill;
            _memoryBreakdownCards.ColumnCount = 4;
            _memoryBreakdownCards.RowCount = 1;
            _memoryBreakdownCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _memoryBreakdownCards.BackColor = Color.White;
            for (int i = 0; i < 4; i++) _memoryBreakdownCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            _memoryBreakdownCards.Controls.Add(CreateMemoryBreakdownCard("WINDOWS SYSTEM", _memoryWindowsValue), 0, 0);
            _memoryBreakdownCards.Controls.Add(CreateMemoryBreakdownCard("MICROSOFT APPS", _memoryMicrosoftValue), 1, 0);
            _memoryBreakdownCards.Controls.Add(CreateMemoryBreakdownCard("THIRD-PARTY APPS", _memoryThirdPartyValue), 2, 0);
            _memoryBreakdownCards.Controls.Add(CreateMemoryBreakdownCard("UNCLASSIFIED", _memoryUnclassifiedValue), 3, 0);

            Panel reconciliationCard = new Panel();
            reconciliationCard.Dock = DockStyle.Fill;
            reconciliationCard.Margin = new Padding(5);
            reconciliationCard.Padding = new Padding(8, 6, 8, 5);
            reconciliationCard.BackColor = Color.FromArgb(250, 251, 252);
            reconciliationCard.BorderStyle = BorderStyle.FixedSingle;

            _memoryReconciliationLayout = new TableLayoutPanel();
            _memoryReconciliationLayout.Dock = DockStyle.Fill;
            _memoryReconciliationLayout.ColumnCount = 3;
            _memoryReconciliationLayout.RowCount = 2;
            _memoryReconciliationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            _memoryReconciliationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            _memoryReconciliationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            _memoryReconciliationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 72F));
            _memoryReconciliationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
            _memoryReconciliationLayout.Controls.Add(CreateReconciliationMetric("PROCESS-PRIVATE RAM", _memoryProcessPrivateValue), 0, 0);
            _memoryReconciliationLayout.Controls.Add(CreateReconciliationMetric("KERNEL / SYSTEM / SHARED", _memorySystemSharedValue), 1, 0);
            _memoryReconciliationLayout.Controls.Add(CreateReconciliationMetric("TOTAL LIVE RAM", _memoryTotalLiveValue), 2, 0);

            _memoryReconciliation.Dock = DockStyle.Fill;
            _memoryReconciliation.TextAlign = ContentAlignment.MiddleLeft;
            _memoryReconciliation.ForeColor = Color.FromArgb(102, 112, 133);
            _memoryReconciliation.Text = "Process-private RAM + kernel / system / shared = total LIVE RAM";
            _memoryReconciliationLayout.Controls.Add(_memoryReconciliation, 0, 1);
            _memoryReconciliationLayout.SetColumnSpan(_memoryReconciliation, 3);
            reconciliationCard.Controls.Add(_memoryReconciliationLayout);

            _memoryBreakdownLayout.Controls.Add(_memoryBreakdownExplanation, 0, 0);
            _memoryBreakdownLayout.Controls.Add(_memoryBreakdownCards, 0, 1);
            _memoryBreakdownLayout.Controls.Add(reconciliationCard, 0, 2);
            _memoryBreakdownLayout.Controls.Add(_memoryBreakdownGrid, 0, 3);
            return _memoryBreakdownLayout;
        }

        private Control CreateMemoryBreakdownCard(string title, Label valueLabel)
        {
            Panel outer = new Panel();
            outer.Dock = DockStyle.Fill;
            outer.Margin = new Padding(5);
            outer.BackColor = Color.White;
            outer.BorderStyle = BorderStyle.FixedSingle;

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 27;
            titleLabel.Padding = new Padding(11, 8, 0, 0);
            titleLabel.Font = CreateUiFont(8.25F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(102, 112, 133);
            _memoryCardTitleLabels.Add(titleLabel);

            valueLabel.Text = "--";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Padding = new Padding(10, 0, 5, 5);
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Font = CreateUiFont(13F, FontStyle.Bold);
            valueLabel.ForeColor = Color.FromArgb(24, 34, 48);
            valueLabel.AutoEllipsis = true;
            _memoryCardValueLabels.Add(valueLabel);

            outer.Controls.Add(valueLabel);
            outer.Controls.Add(titleLabel);
            return outer;
        }

        private Control CreateReconciliationMetric(string title, Label valueLabel)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(7, 2, 7, 1);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 20;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.ForeColor = Color.FromArgb(102, 112, 133);
            titleLabel.Font = CreateUiFont(8F, FontStyle.Bold);

            valueLabel.Text = "--";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.ForeColor = Color.FromArgb(24, 34, 48);
            valueLabel.Font = CreateUiFont(10.5F, FontStyle.Bold);

            panel.Controls.Add(valueLabel);
            panel.Controls.Add(titleLabel);
            return panel;
        }

        private void ConfigureMemoryBreakdownGrid()
        {
            _memoryBreakdownGrid.Dock = DockStyle.Fill;
            _memoryBreakdownGrid.ReadOnly = true;
            _memoryBreakdownGrid.AllowUserToAddRows = false;
            _memoryBreakdownGrid.AllowUserToDeleteRows = false;
            _memoryBreakdownGrid.AllowUserToResizeRows = false;
            _memoryBreakdownGrid.RowHeadersVisible = false;
            _memoryBreakdownGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _memoryBreakdownGrid.MultiSelect = false;
            _memoryBreakdownGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _memoryBreakdownGrid.Columns.Add("Category", "Category");
            _memoryBreakdownGrid.Columns.Add("Ram", "RAM");
            _memoryBreakdownGrid.Columns.Add("LivePercent", "% of total LIVE RAM");
            _memoryBreakdownGrid.Columns.Add("ProcessPercent", "% of process-private");
            _memoryBreakdownGrid.Columns.Add("Groups", "Process groups");
            _memoryBreakdownGrid.Columns.Add("Instances", "Instances");
            _memoryBreakdownGrid.Columns[0].FillWeight = 170;
            _memoryBreakdownGrid.Columns[1].FillWeight = 90;
            _memoryBreakdownGrid.Columns[2].FillWeight = 105;
            _memoryBreakdownGrid.Columns[3].FillWeight = 105;
            _memoryBreakdownGrid.Columns[4].FillWeight = 75;
            _memoryBreakdownGrid.Columns[5].FillWeight = 65;
            foreach (DataGridViewColumn column in _memoryBreakdownGrid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;

            _memoryBreakdownGrid.Rows.Add("Windows system processes", "--", "--", "--", "--", "--");
            _memoryBreakdownGrid.Rows.Add("Microsoft applications", "--", "--", "--", "--", "--");
            _memoryBreakdownGrid.Rows.Add("Third-party applications", "--", "--", "--", "--", "--");
            _memoryBreakdownGrid.Rows.Add("Unclassified process RAM", "--", "--", "--", "--", "--");
            _memoryBreakdownGrid.Rows.Add("Kernel / system / shared", "--", "--", "n/a", "n/a", "n/a");
            int totalRow = _memoryBreakdownGrid.Rows.Add("TOTAL LIVE RAM", "--", "100.0%", "n/a", "n/a", "n/a");
            _memoryBreakdownGrid.Rows[totalRow].DefaultCellStyle.BackColor = Color.FromArgb(247, 248, 250);
            ApplyGridProfessionalStyle(_memoryBreakdownGrid);
        }

        private void ApplyMemoryBreakdownSnapshot(MemoryBreakdownSnapshot breakdown)
        {
            _lastMemoryBreakdown = breakdown ?? new MemoryBreakdownSnapshot();
            RefreshMemoryBreakdownDisplay();
        }

        private void RefreshMemoryBreakdownDisplay()
        {
            MemoryBreakdownSnapshot b = _lastMemoryBreakdown;
            if (b == null) return;

            UpdateMemoryCard(_memoryWindowsValue, b.WindowsSystem.PrivateBytes, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryCard(_memoryMicrosoftValue, b.MicrosoftApplications.PrivateBytes, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryCard(_memoryThirdPartyValue, b.ThirdPartyApplications.PrivateBytes, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryCard(_memoryUnclassifiedValue, b.Unclassified.PrivateBytes, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);

            ulong nonProcessSystem = b.WindowsKernelBytes + b.WindowsSystemSharedBytes;
            UpdateReconciliationMetric(_memoryProcessPrivateValue, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateReconciliationMetric(_memorySystemSharedValue, nonProcessSystem, b.AccountingLiveBytes);
            UpdateReconciliationMetric(_memoryTotalLiveValue, b.AccountingLiveBytes, b.AccountingLiveBytes);

            _memoryReconciliation.Text =
                FormatBytes(b.ProcessPrivateTotalBytes) + " process-private"
                + "  +  " + FormatBytes(b.WindowsKernelBytes) + " Windows kernel"
                + "  +  " + FormatBytes(b.WindowsSystemSharedBytes) + " system/shared"
                + "  =  " + FormatBytes(b.AccountingLiveBytes) + " total LIVE RAM";

            UpdateMemoryBreakdownRow(0, b.WindowsSystem, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryBreakdownRow(1, b.MicrosoftApplications, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryBreakdownRow(2, b.ThirdPartyApplications, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);
            UpdateMemoryBreakdownRow(3, b.Unclassified, b.ProcessPrivateTotalBytes, b.AccountingLiveBytes);

            if (_memoryBreakdownGrid.Rows.Count >= 6)
            {
                DataGridViewRow system = _memoryBreakdownGrid.Rows[4];
                system.Cells[1].Value = FormatBytes(nonProcessSystem);
                system.Cells[2].Value = PercentText(nonProcessSystem, b.AccountingLiveBytes);
                system.Cells[3].Value = "n/a";
                system.Cells[4].Value = "n/a";
                system.Cells[5].Value = "n/a";

                DataGridViewRow total = _memoryBreakdownGrid.Rows[5];
                total.Cells[1].Value = FormatBytes(b.AccountingLiveBytes);
                total.Cells[2].Value = b.AccountingLiveBytes > 0 ? "100.0%" : "--";
                total.Cells[3].Value = "n/a";
                total.Cells[4].Value = "n/a";
                total.Cells[5].Value = "n/a";
            }
        }

        private static void UpdateMemoryCard(Label label, ulong bytes, ulong processPrivate, ulong liveBytes)
        {
            if (label == null) return;
            string livePercent = liveBytes > 0 ? (bytes * 100.0 / liveBytes).ToString("0.0", CultureInfo.InvariantCulture) + "% live" : "-- live";
            string processPercent = processPrivate > 0 ? (bytes * 100.0 / processPrivate).ToString("0.0", CultureInfo.InvariantCulture) + "% process" : "-- process";
            label.Text = FormatBytes(bytes) + Environment.NewLine + livePercent + " | " + processPercent;
        }

        private static void UpdateReconciliationMetric(Label label, ulong bytes, ulong liveBytes)
        {
            if (label == null) return;
            label.Text = FormatBytes(bytes) + "   " + PercentText(bytes, liveBytes) + " of live";
        }

        private static string PercentText(ulong bytes, ulong total)
        {
            return total > 0 ? (bytes * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
        }

        private void UpdateMemoryBreakdownRow(int rowIndex, MemoryCategoryBreakdown category, ulong processPrivate, ulong liveBytes)
        {
            if (category == null || rowIndex < 0 || rowIndex >= _memoryBreakdownGrid.Rows.Count) return;
            DataGridViewRow row = _memoryBreakdownGrid.Rows[rowIndex];
            row.Cells[1].Value = FormatBytes(category.PrivateBytes);
            row.Cells[2].Value = PercentText(category.PrivateBytes, liveBytes);
            row.Cells[3].Value = processPrivate > 0 ? (category.PrivateBytes * 100.0 / processPrivate).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
            row.Cells[4].Value = category.GroupCount.ToString(CultureInfo.InvariantCulture);
            row.Cells[5].Value = category.InstanceCount.ToString(CultureInfo.InvariantCulture);
        }

        private void ConfigureProcessGrid()
        {
            _processGrid.Dock = DockStyle.Fill;
            _processGrid.ReadOnly = true;
            _processGrid.AllowUserToAddRows = false;
            _processGrid.AllowUserToDeleteRows = false;
            _processGrid.AllowUserToResizeRows = false;
            _processGrid.RowHeadersVisible = false;
            _processGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _processGrid.MultiSelect = false;
            _processGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _processGrid.BackgroundColor = Color.White;
            _processGrid.BorderStyle = BorderStyle.None;
            _processGrid.EnableHeadersVisualStyles = false;
            _processGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 243, 246);
            _processGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            _gridBoldFont = new Font(_processGrid.Font, FontStyle.Bold);

            _processGrid.Columns.Add("Friendly", "Familiar name");
            _processGrid.Columns.Add("Technical", "Technical name");
            _processGrid.Columns.Add("Type", "Type");
            _processGrid.Columns.Add("Count", "Instances");
            _processGrid.Columns.Add("Services", "Windows service(s)");
            _processGrid.Columns.Add("Live", "Private RAM");
            _processGrid.Columns.Add("Average", "Today avg");
            _processGrid.Columns.Add("Peak", "Today peak");

            _processGrid.Columns[0].ToolTipText = "A familiar/product description where Windows or RAM Trace can identify one reliably.";
            _processGrid.Columns[1].ToolTipText = "The actual Windows process/executable name used for diagnostics.";
            _processGrid.Columns[5].ToolTipText = "Private working set: non-shared physical RAM currently resident for the process. This matches the memory style used by current Windows Task Manager.";
            _processGrid.Columns[6].ToolTipText = "Average private working set sampled while RAM Trace has been running today.";
            _processGrid.Columns[7].ToolTipText = "Highest private working set sampled while RAM Trace has been running today.";

            // Familiar names are the primary user-facing identity, so give them
            // more room than the raw service list. Technical names remain visible
            // for diagnostics without dominating the table.
            _processGrid.Columns[0].FillWeight = 175;
            _processGrid.Columns[1].FillWeight = 92;
            _processGrid.Columns[2].FillWeight = 68;
            _processGrid.Columns[3].FillWeight = 48;
            _processGrid.Columns[4].FillWeight = 155;
            _processGrid.Columns[5].FillWeight = 70;
            _processGrid.Columns[6].FillWeight = 70;
            _processGrid.Columns[7].FillWeight = 70;

            _processGrid.SelectionChanged += delegate
            {
                if (!_updatingProcessGrid) UpdateDetailsPanel();
            };
            _processGrid.MouseWheel += delegate { MarkProcessGridInteraction(); };
            _processGrid.Scroll += delegate
            {
                if (!_updatingProcessGrid) MarkProcessGridInteraction();
            };
            _processGrid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
                    MarkProcessGridInteraction();
            };
            _processGrid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            for (int i = 5; i <= 7; i++)
                _processGrid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            ApplyGridProfessionalStyle(_processGrid);
        }

        private void ConfigureHistoryGrid()
        {
            _historyGrid.Dock = DockStyle.Fill;
            _historyGrid.ReadOnly = true;
            _historyGrid.AllowUserToAddRows = false;
            _historyGrid.AllowUserToDeleteRows = false;
            _historyGrid.AllowUserToResizeRows = false;
            _historyGrid.RowHeadersVisible = false;
            _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _historyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _historyGrid.BackgroundColor = Color.White;
            _historyGrid.BorderStyle = BorderStyle.None;
            _historyGrid.EnableHeadersVisualStyles = false;
            _historyGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 243, 246);
            _historyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);

            _historyGrid.Columns.Add("Date", "Date");
            _historyGrid.Columns.Add("Average", "Average RAM");
            _historyGrid.Columns.Add("Low", "Lowest RAM");
            _historyGrid.Columns.Add("Peak", "Peak RAM");
            _historyGrid.Columns.Add("Samples", "Tracked");
            for (int i = 1; i <= 4; i++)
                _historyGrid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            ApplyGridProfessionalStyle(_historyGrid);
        }

        private void ConfigureTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem show = new ToolStripMenuItem("Show RAM Trace");
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            show.Click += delegate { RestoreFromTray(); };
            exit.Click += delegate
            {
                _reallyExit = true;
                PersistState();
                _trayIcon.Visible = false;
                Close();
            };
            menu.Items.Add(show);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _trayIcon.Text = "RAM Trace V2.0";
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void UpdateResponsiveLayout()
        {
            // Compatibility wrapper retained for internal validation and older
            // legacy validation callers. The implementation is deterministic.
            ApplyDeterministicLayout(false);
        }

        private void ApplyDeterministicLayout(bool force)
        {
            if (_rootLayout == null) return;

            int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            float scale = Math.Max(0.90F, Math.Min(1.75F, dpi / 96F));
            bool dpiChanged = _lastLayoutDpi != dpi;
            int availableWidth = Math.Max(1, ClientSize.Width);
            int availableHeight = Math.Max(1, ClientSize.Height);

            if (_diagnostics.IsEnabled && (force || dpiChanged))
                _diagnostics.LogEvent(
                    "Deterministic layout applied",
                    "Client=" + availableWidth.ToString(CultureInfo.InvariantCulture)
                    + "x" + availableHeight.ToString(CultureInfo.InvariantCulture)
                    + " dpi=" + dpi.ToString(CultureInfo.InvariantCulture)
                    + " scale=" + scale.ToString("0.00", CultureInfo.InvariantCulture),
                    0);

            SuspendLayout();
            _rootLayout.SuspendLayout();
            if (_cardsLayout != null) _cardsLayout.SuspendLayout();
            if (_metricsLayout != null) _metricsLayout.SuspendLayout();
            if (_processLayout != null) _processLayout.SuspendLayout();
            if (_memoryBreakdownLayout != null) _memoryBreakdownLayout.SuspendLayout();
            try
            {
                // Geometry is always derived from fixed design constants and the
                // current effective DPI. No control is resized from its previous
                // bounds, which makes Normal -> Maximised/Wide -> Normal idempotent.
                _rootLayout.Padding = new Padding(ScalePx(12, scale), ScalePx(10, scale), ScalePx(12, scale), ScalePx(8, scale));
                _rootLayout.RowStyles[0].Height = ScalePx(109, scale);
                _rootLayout.RowStyles[1].Height = ScalePx(54, scale);
                _rootLayout.RowStyles[3].Height = ScalePx(34, scale);

                if (_processLayout != null)
                    _processLayout.RowStyles[1].Height = ScalePx(160, scale);
                if (_memoryBreakdownLayout != null)
                {
                    _memoryBreakdownLayout.RowStyles[0].Height = ScalePx(48, scale);
                    _memoryBreakdownLayout.RowStyles[1].Height = ScalePx(112, scale);
                    _memoryBreakdownLayout.RowStyles[2].Height = ScalePx(92, scale);
                }

                foreach (Control cardControl in _cardsLayout.Controls)
                {
                    Panel card = cardControl as Panel;
                    if (card != null) card.Margin = new Padding(ScalePx(5, scale));
                }
                foreach (Control cardControl in _memoryBreakdownCards.Controls)
                {
                    Panel card = cardControl as Panel;
                    if (card != null) card.Margin = new Padding(ScalePx(5, scale));
                }

                foreach (Label title in _cardTitleLabels)
                {
                    title.Height = ScalePx(30, scale);
                    title.Padding = new Padding(ScalePx(12, scale), ScalePx(9, scale), 0, 0);
                }
                foreach (Label value in _cardValueLabels)
                    value.Padding = new Padding(ScalePx(11, scale), 0, ScalePx(5, scale), ScalePx(6, scale));

                foreach (Label title in _memoryCardTitleLabels)
                {
                    title.Height = ScalePx(27, scale);
                    title.Padding = new Padding(ScalePx(11, scale), ScalePx(8, scale), 0, 0);
                }
                foreach (Label value in _memoryCardValueLabels)
                    value.Padding = new Padding(ScalePx(10, scale), 0, ScalePx(5, scale), ScalePx(5, scale));

                foreach (TabPage page in _tabs.TabPages)
                    page.Padding = new Padding(ScalePx(6, scale));

                if (_detailsHeader != null)
                {
                    _detailsHeader.Height = ScalePx(25, scale);
                    _detailsHeader.Padding = new Padding(ScalePx(9, scale), ScalePx(6, scale), 0, 0);
                }
                if (_detailsLegend != null)
                {
                    _detailsLegend.Height = ScalePx(22, scale);
                    _detailsLegend.Padding = new Padding(ScalePx(9, scale), ScalePx(2, scale), 0, 0);
                }

                ApplyGridGeometry(_processGrid, scale);
                ApplyGridGeometry(_memoryBreakdownGrid, scale);
                ApplyGridGeometry(_historyGrid, scale);

                if (dpiChanged || _lastLayoutDpi < 0)
                {
                    ApplyDpiFonts(scale);
                    _lastLayoutDpi = dpi;
                }

                UpdateFooterLayout(scale, availableWidth);
                _lastLayoutWindowState = WindowState;
            }
            finally
            {
                if (_memoryBreakdownLayout != null) _memoryBreakdownLayout.ResumeLayout(true);
                if (_processLayout != null) _processLayout.ResumeLayout(true);
                if (_metricsLayout != null) _metricsLayout.ResumeLayout(true);
                if (_cardsLayout != null) _cardsLayout.ResumeLayout(true);
                _rootLayout.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        private void UpdateFooterLayout(float scale, int availableWidth)
        {
            if (_footerAuthor == null) return;

            TextFormatFlags measureFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int authorTextWidth = TextRenderer.MeasureText(_footerAuthor.Text, _footerAuthor.Font, new Size(int.MaxValue, int.MaxValue), measureFlags).Width;
            int authorWidth = authorTextWidth + ScalePx(18, scale);
            int buttonWidth = 0;
            if (_diagnosticsButton != null) buttonWidth += _diagnosticsButton.PreferredSize.Width;
            if (_folderButton != null) buttonWidth += _folderButton.PreferredSize.Width;
            if (_glossaryButton != null) buttonWidth += _glossaryButton.PreferredSize.Width;

            int preferredHintWidth = ScalePx(320, scale);
            int comfortReserve = ScalePx(44, scale);
            bool showAuthor = availableWidth >= preferredHintWidth + buttonWidth + authorWidth + comfortReserve;

            _footerAuthor.Visible = showAuthor;
            _footerAuthor.Width = showAuthor ? authorWidth : 0;
            if (_footerHint != null)
                _footerHint.Width = ScalePx(showAuthor ? 320 : 350, scale);
        }

        private void ApplyDpiFonts(float scale)
        {
            SetControlFont(_secondaryMetrics, 9.5F * scale, FontStyle.Regular);
            SetControlFont(_selfMetrics, 9.5F * scale, FontStyle.Regular);
            SetControlFont(_breakdownMetrics, 9.5F * scale, FontStyle.Regular);
            SetControlFont(_tabs, 9.5F * scale, FontStyle.Regular);
            SetControlFont(_detailsBox, 9.25F * scale, FontStyle.Regular);
            SetControlFont(_footerHint, 8.75F * scale, FontStyle.Regular);
            SetControlFont(_footerAuthor, 8.5F * scale, FontStyle.Regular);
            SetControlFont(_folderButton, 9F * scale, FontStyle.Regular);
            SetControlFont(_glossaryButton, 9F * scale, FontStyle.Regular);
            SetControlFont(_diagnosticsButton, 9F * scale, FontStyle.Regular);
            SetControlFont(_memoryBreakdownExplanation, 8.75F * scale, FontStyle.Regular);
            SetControlFont(_memoryReconciliation, 8.25F * scale, FontStyle.Regular);

            foreach (Label title in _cardTitleLabels) SetControlFont(title, 8.75F * scale, FontStyle.Bold);
            foreach (Label value in _cardValueLabels) SetControlFont(value, 18F * scale, FontStyle.Bold);
            foreach (Label title in _memoryCardTitleLabels) SetControlFont(title, 8.25F * scale, FontStyle.Bold);
            foreach (Label value in _memoryCardValueLabels) SetControlFont(value, 13F * scale, FontStyle.Bold);

            if (_memoryReconciliationLayout != null)
            {
                foreach (Control c in _memoryReconciliationLayout.Controls)
                {
                    Panel panel = c as Panel;
                    if (panel == null) continue;
                    foreach (Control child in panel.Controls)
                    {
                        Label label = child as Label;
                        if (label == null) continue;
                        bool title = label.Dock == DockStyle.Top;
                        SetControlFont(label, (title ? 8F : 10.5F) * scale, FontStyle.Bold);
                    }
                }
            }

            if (_detailsHeader != null) SetControlFont(_detailsHeader, 8.5F * scale, FontStyle.Bold);
            if (_detailsLegend != null) SetControlFont(_detailsLegend, 8.5F * scale, FontStyle.Regular);

            ApplyGridFonts(_processGrid, scale);
            ApplyGridFonts(_memoryBreakdownGrid, scale);
            ApplyGridFonts(_historyGrid, scale);
        }

        private static int ScalePx(float value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            Font variableFont = new Font("Segoe UI Variable Text", Math.Max(7F, size), style, GraphicsUnit.Point);
            if (variableFont.FontFamily.Name.IndexOf("Segoe UI Variable", StringComparison.OrdinalIgnoreCase) >= 0)
                return variableFont;
            variableFont.Dispose();
            return new Font("Segoe UI", Math.Max(7F, size), style, GraphicsUnit.Point);
        }

        private static void SetControlFont(Control control, float size, FontStyle style)
        {
            if (control == null) return;
            Font old = control.Font;
            bool inheritedFromParent = control.Parent != null && Object.ReferenceEquals(old, control.Parent.Font);
            Font next = CreateUiFont(size, style);
            control.Font = next;
            if (old != null && old != SystemFonts.DefaultFont && !inheritedFromParent)
                old.Dispose();
        }

        private void ApplyGridProfessionalStyle(DataGridView grid)
        {
            if (grid == null) return;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Color.FromArgb(226, 231, 237);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 246);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(37, 48, 64);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 253);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(24, 34, 48);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 238, 249);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(24, 34, 48);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        }

        private void ApplyGridGeometry(DataGridView grid, float scale)
        {
            if (grid == null) return;
            int rowHeight = Math.Max(24, ScalePx(27, scale));
            int headerHeight = Math.Max(26, ScalePx(29, scale));
            grid.RowTemplate.Height = rowHeight;
            grid.ColumnHeadersHeight = headerHeight;
            grid.DefaultCellStyle.Padding = new Padding(ScalePx(7, scale), ScalePx(2, scale), ScalePx(7, scale), ScalePx(2, scale));
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(ScalePx(7, scale), ScalePx(2, scale), ScalePx(7, scale), ScalePx(2, scale));
            foreach (DataGridViewRow row in grid.Rows) row.Height = rowHeight;
        }

        private void ApplyGridFonts(DataGridView grid, float scale)
        {
            if (grid == null) return;
            Font oldGridFont = grid.Font;
            bool gridFontInherited = grid.Parent != null && Object.ReferenceEquals(oldGridFont, grid.Parent.Font);
            grid.Font = CreateUiFont(9.25F * scale, FontStyle.Regular);
            if (oldGridFont != null && oldGridFont != SystemFonts.DefaultFont && !gridFontInherited) oldGridFont.Dispose();

            Font oldHeaderFont = grid.ColumnHeadersDefaultCellStyle.Font;
            grid.ColumnHeadersDefaultCellStyle.Font = CreateUiFont(9F * scale, FontStyle.Bold);
            if (oldHeaderFont != null && oldHeaderFont != SystemFonts.DefaultFont) oldHeaderFont.Dispose();

            if (grid == _processGrid)
            {
                if (_gridBoldFont != null) _gridBoldFont.Dispose();
                _gridBoldFont = new Font(grid.Font, FontStyle.Bold);
                foreach (DataGridViewRow row in _processGrid.Rows)
                {
                    if (row.Cells.Count > 5) row.Cells[5].Style.Font = _gridBoldFont;
                    string key = Convert.ToString(row.Tag, CultureInfo.InvariantCulture);
                    ProcessGroupSample group;
                    _lastGroups.TryGetValue(key ?? string.Empty, out group);
                    ApplyProcessRowEmphasis(row, group);
                }
            }
            else if (grid == _memoryBreakdownGrid && _memoryBreakdownGrid.Rows.Count >= 6)
            {
                if (_memoryBreakdownBoldFont != null) _memoryBreakdownBoldFont.Dispose();
                _memoryBreakdownBoldFont = new Font(grid.Font, FontStyle.Bold);
                _memoryBreakdownGrid.Rows[5].DefaultCellStyle.Font = _memoryBreakdownBoldFont;
            }
        }

        private void DrawMainTab(object sender, DrawItemEventArgs e)
        {
            if (_tabs == null || e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            Rectangle bounds = _tabs.GetTabRect(e.Index);
            bool selected = e.Index == _tabs.SelectedIndex;
            e.Graphics.FillRectangle(SystemBrushes.Window, bounds);
            Color textColor = selected ? Color.FromArgb(15, 108, 189) : Color.FromArgb(57, 65, 80);
            TextRenderer.DrawText(
                e.Graphics,
                _tabs.TabPages[e.Index].Text,
                _tabs.Font,
                new Rectangle(bounds.Left + 8, bounds.Top, Math.Max(1, bounds.Width - 16), bounds.Height - 2),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(15, 108, 189)))
                    e.Graphics.FillRectangle(accent, new Rectangle(bounds.Left + 8, bounds.Bottom - 2, Math.Max(1, bounds.Width - 16), 2));
            }
        }

        private void QueueLayoutRefresh()
        {
            if (WindowState == FormWindowState.Minimized) return;
            _layoutDebounceTimer.Stop();
            _layoutDebounceTimer.Start();
        }

        private void RestoreFromTray()
        {
            if (_diagnostics.IsEnabled) _diagnostics.LogEvent("Window restored", "RAM Trace restored from the notification area.", 0);
            _diagWasMinimized = false;
            _normalProcessInterval = 3000;
            if (_processTimer.Interval > 3000 && GetRollingCpu() < 0.8)
                SetProcessSamplingInterval(3000, "Window restored to normal sampling.");
            else
                RebaseProcessScheduleDiagnostics();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                _layoutDebounceTimer.Stop();
                if (_diagnostics.IsEnabled && !_diagWasMinimized) _diagnostics.LogEvent("Window minimised", "RAM Trace moved to the notification area.", 0);
                _diagWasMinimized = true;
                _normalProcessInterval = 5000;
                if (_processTimer.Interval < 5000)
                    SetProcessSamplingInterval(5000, "Window minimised to tray.");
                else
                    RebaseProcessScheduleDiagnostics();
                Hide();
                return;
            }

            bool stateChanged = WindowState != _lastLayoutWindowState;
            if (stateChanged)
                ApplyDeterministicLayout(true);
            else
                QueueLayoutRefresh();

            if (_diagWasMinimized && _diagnostics.IsEnabled)
                _diagnostics.LogEvent("Window restored", "RAM Trace window restored from a minimised state.", 0);
            _diagWasMinimized = false;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            _layoutDebounceTimer.Stop();
            ApplyDeterministicLayout(true);
            base.OnResizeEnd(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0;
            bool dpiChanged = m.Msg == WM_DPICHANGED;
            if (dpiChanged && _diagnostics.IsEnabled)
                _diagnostics.LogEvent("DPI change", "Windows reported a per-monitor DPI change.", 0);
            base.WndProc(ref m);
            if (dpiChanged && IsHandleCreated)
                BeginInvoke(new Action(delegate { ApplyDeterministicLayout(true); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _normalProcessInterval = 5000;
                if (_processTimer.Interval < 5000)
                    SetProcessSamplingInterval(5000, "Window closed to tray.");
                else
                    RebaseProcessScheduleDiagnostics();
                Hide();
                return;
            }
            _processTimer.Stop();
            _processGridInteractionTimer.Stop();
            _layoutDebounceTimer.Stop();
            StopProcessWorker();
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.LogEvent("Application shutdown", "RAM Trace is closing normally.", 0);
                _diagnosticHeartbeatTimer.Stop();
                _diagnostics.StopSession("Application shutdown");
                if (_diagnosticsButton != null) _diagnosticsButton.Text = "Diagnostics: OFF";
            }
            PersistState();
            _trayIcon.Visible = false;
            if (_gridBoldFont != null)
            {
                _gridBoldFont.Dispose();
                _gridBoldFont = null;
            }
            if (_memoryBreakdownBoldFont != null)
            {
                _memoryBreakdownBoldFont.Dispose();
                _memoryBreakdownBoldFont = null;
            }
            _diagnostics.Dispose();
            try { _processWorkSignal.Dispose(); } catch { }
            base.OnFormClosing(e);
        }

        private void SystemTick()
        {
            if (DateTime.Today != _statDate)
            {
                ArchiveCurrentDay();
                ResetForNewDay(DateTime.Today);
            }

            long diagSystemStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
            MemorySnapshot mem = NativeMemory.Read();
            if (_diagnostics.IsEnabled)
                _diagLastSystemSampleMs = (Stopwatch.GetTimestamp() - diagSystemStart) * 1000.0 / Stopwatch.Frequency;
            if (mem.TotalPhysical == 0)
                return;

            _totalPhysicalBytes = mem.TotalPhysical;
            ulong used = mem.TotalPhysical - mem.AvailablePhysical;
            _diagLastLiveSystemRamBytes = used;
            _systemSamples++;
            _systemSumBytes += used;
            if (used < _systemMinBytes) _systemMinBytes = used;
            if (used > _systemPeakBytes) _systemPeakBytes = used;

            double avg = _systemSamples > 0 ? _systemSumBytes / _systemSamples : 0;
            double percent = mem.TotalPhysical > 0 ? (used * 100.0 / mem.TotalPhysical) : 0;
            _liveValue.Text = FormatBytes(used) + "  (" + percent.ToString("0.0", CultureInfo.InvariantCulture) + "%)";
            _avgValue.Text = FormatBytes((ulong)Math.Max(0, avg));
            _peakValue.Text = FormatBytes(_systemPeakBytes);
            _lowValue.Text = _systemMinBytes == ulong.MaxValue ? "--" : FormatBytes(_systemMinBytes);

            _secondaryMetrics.Text = "Available: " + FormatBytes(mem.AvailablePhysical)
                + "    Cache: " + FormatBytes(mem.SystemCache)
                + "    Total: " + FormatBytes(mem.TotalPhysical);

            _breakdownMetrics.Text = "RAM accounting: Process " + FormatBytes(_lastAccountingProcessPrivateBytes)
                + "    |    Windows kernel " + FormatBytes(_lastAccountingKernelBytes)
                + "    |    Windows / system / shared " + FormatBytes(_lastAccountingOtherBytes);

            long diagFooterStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
            UpdateSelfMetrics();
            UpdateTrayText(percent, used);
            if (_diagnostics.IsEnabled)
            {
                _diagLastFooterMs = (Stopwatch.GetTimestamp() - diagFooterStart) * 1000.0 / Stopwatch.Frequency;
                bool throttled = _processTimer.Interval > _normalProcessInterval;
                if (throttled != _diagLastThrottleState)
                {
                    _diagnostics.UpdateThrottleState(throttled);
                    _diagLastThrottleState = throttled;
                }
                _diagnostics.FlushIfDue();
            }
        }

        private void UpdateSelfMetrics()
        {
            try
            {
                using (Process me = Process.GetCurrentProcess())
                {
                    DateTime now = DateTime.UtcNow;
                    TimeSpan cpu = me.TotalProcessorTime;
                    double elapsedMs = (now - _lastCpuWall).TotalMilliseconds;
                    double cpuMs = (cpu - _lastCpuTime).TotalMilliseconds;
                    double cpuPercent = 0;
                    if (elapsedMs > 0)
                        cpuPercent = cpuMs / (elapsedMs * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                    if (cpuPercent < 0) cpuPercent = 0;

                    _lastCpuWall = now;
                    _lastCpuTime = cpu;
                    _cpuWindow.Enqueue(cpuPercent);
                    while (_cpuWindow.Count > 10) _cpuWindow.Dequeue();
                    double avgCpu = GetRollingCpu();
                    double selfMb = NativeMemory.ReadPrivateWorkingSet(me) / 1048576.0;

                    // v1.2.6 real-user diagnostics showed five throttle transitions even
                    // though session CPU averaged about 0.30% and peaked around 0.80%.
                    // Keep throttling, but require sustained pressure before slowing the
                    // process sampler, and use a separate lower recovery threshold.
                    if (avgCpu > 1.0)
                    {
                        _cpuAboveHardSeconds++;
                        _cpuAboveSoftSeconds++;
                        _lowCpuSeconds = 0;
                    }
                    else if (avgCpu > 0.8)
                    {
                        _cpuAboveHardSeconds = 0;
                        _cpuAboveSoftSeconds++;
                        _lowCpuSeconds = 0;
                    }
                    else
                    {
                        _cpuAboveHardSeconds = 0;
                        _cpuAboveSoftSeconds = 0;
                        if (avgCpu < 0.45) _lowCpuSeconds++;
                        else _lowCpuSeconds = 0;
                    }

                    if (_cpuAboveHardSeconds >= 3)
                    {
                        SetProcessSamplingInterval(10000, "Sustained rolling CPU above 1.0%.");
                        _cpuAboveHardSeconds = 0;
                        _cpuAboveSoftSeconds = 0;
                    }
                    else if (_cpuAboveSoftSeconds >= 5)
                    {
                        SetProcessSamplingInterval(Math.Max(_normalProcessInterval, 5000), "Sustained rolling CPU above 0.8%.");
                        _cpuAboveSoftSeconds = 0;
                    }
                    else if (_lowCpuSeconds >= 15 && _processTimer.Interval != _normalProcessInterval)
                    {
                        SetProcessSamplingInterval(_normalProcessInterval, "Rolling CPU remained below the recovery threshold.");
                        _lowCpuSeconds = 0;
                    }

                    string budget = _processTimer.Interval > _normalProcessInterval ? "THROTTLED" : "OK";
                    if (selfMb > 80.0 && budget == "OK") budget = "RAM HIGH";

                    _selfMetrics.Text = "RAM Trace V2.0: " + selfMb.ToString("0.0", CultureInfo.InvariantCulture)
                        + " MB RAM    " + avgCpu.ToString("0.00", CultureInfo.InvariantCulture)
                        + "% CPU    Budget: " + budget;
                }
            }
            catch
            {
                _selfMetrics.Text = "RAM Trace V2.0 resource reading unavailable";
            }
        }

        private double GetRollingCpu()
        {
            if (_cpuWindow.Count == 0) return 0.0;
            double total = 0.0;
            foreach (double value in _cpuWindow) total += value;
            return total / _cpuWindow.Count;
        }

        private void SetProcessSamplingInterval(int intervalMs, string reason)
        {
            intervalMs = Math.Max(_normalProcessInterval, intervalMs);
            if (_processTimer.Interval == intervalMs) return;
            _processTimer.Interval = intervalMs;
            RebaseProcessScheduleDiagnostics();
            if (_diagnostics.IsEnabled)
                _diagnostics.LogEvent("Process sampling interval changed", (reason ?? "Sampling policy update") + " Interval=" + intervalMs.ToString(CultureInfo.InvariantCulture) + " ms.", 0);
        }

        private void RebaseProcessScheduleDiagnostics()
        {
            _diagLastProcessRequestUtc = DateTime.MinValue;
            _diagLastProcessRequestIntervalMs = _processTimer.Interval;
        }

        private void UpdateTrayText(double percent, ulong used)
        {
            string text = "RAM Trace | " + percent.ToString("0.0", CultureInfo.InvariantCulture) + "% | " + FormatBytes(used);
            if (text.Length > 63) text = text.Substring(0, 63);
            _trayIcon.Text = text;
        }

        private void StartProcessWorker()
        {
            _processWorkerStopping = false;
            _processWorkerThread = new Thread(ProcessWorkerLoop);
            _processWorkerThread.IsBackground = true;
            _processWorkerThread.Name = "RAM Trace process sampler";
            _processWorkerThread.Start();
        }

        private void StopProcessWorker()
        {
            _processWorkerStopping = true;
            try { _processWorkSignal.Set(); } catch { }
            Thread worker = _processWorkerThread;
            if (worker != null && worker.IsAlive && worker != Thread.CurrentThread)
            {
                try { worker.Join(10000); } catch { }
            }
            _processWorkerThread = null;
            Interlocked.Exchange(ref _processCycleGate, 0);
        }

        private void RequestProcessSnapshot(bool startupRequest)
        {
            if (_processWorkerStopping || IsDisposed || Disposing) return;

            DateTime nowUtc = DateTime.UtcNow;
            int expectedInterval = _processTimer.Interval;
            double actualIntervalMs = 0;
            double timerDriftMs = 0;
            if (_diagnostics.IsEnabled && _diagLastProcessRequestUtc != DateTime.MinValue
                && _diagLastProcessRequestIntervalMs == expectedInterval)
            {
                actualIntervalMs = (nowUtc - _diagLastProcessRequestUtc).TotalMilliseconds;
                timerDriftMs = Math.Max(0, actualIntervalMs - expectedInterval);
            }
            if (_diagnostics.IsEnabled)
            {
                _diagLastProcessRequestUtc = nowUtc;
                _diagLastProcessRequestIntervalMs = expectedInterval;
            }

            if (Interlocked.CompareExchange(ref _processCycleGate, 1, 0) != 0)
            {
                Interlocked.Increment(ref _skippedProcessScansSinceLastSample);
                Interlocked.Increment(ref _totalSkippedProcessScans);
                return;
            }

            ProcessScanRequest request = new ProcessScanRequest();
            request.RequestedUtc = nowUtc;
            request.RequestTimestamp = Stopwatch.GetTimestamp();
            request.ExpectedIntervalMs = expectedInterval;
            request.ActualIntervalMs = actualIntervalMs;
            request.TimerDriftMs = timerDriftMs;
            request.IsStartup = startupRequest;
            request.DiagnosticsEnabledAtRequest = _diagnostics.IsEnabled;

            lock (_processRequestSync)
            {
                _pendingProcessRequest = request;
            }
            _processWorkSignal.Set();
        }

        private void ProcessWorkerLoop()
        {
            while (!_processWorkerStopping)
            {
                try { _processWorkSignal.WaitOne(); }
                catch { break; }
                if (_processWorkerStopping) break;

                ProcessScanRequest request = null;
                lock (_processRequestSync)
                {
                    request = _pendingProcessRequest;
                    _pendingProcessRequest = null;
                }
                if (request == null)
                {
                    Interlocked.Exchange(ref _processCycleGate, 0);
                    continue;
                }

                ProcessScanSnapshot snapshot = null;
                try
                {
                    snapshot = BuildProcessSnapshot(request);
                }
                catch (Exception ex)
                {
                    if (_diagnostics.IsEnabled) _diagnostics.LogException("Process scan exception", ex);
                }

                if (_processWorkerStopping)
                {
                    Interlocked.Exchange(ref _processCycleGate, 0);
                    break;
                }

                if (snapshot == null)
                {
                    Interlocked.Exchange(ref _processCycleGate, 0);
                    continue;
                }

                try
                {
                    BeginInvoke(new Action<ProcessScanSnapshot>(ApplyProcessSnapshot), snapshot);
                }
                catch
                {
                    Interlocked.Exchange(ref _processCycleGate, 0);
                }
            }
        }

        private ProcessScanSnapshot BuildProcessSnapshot(ProcessScanRequest request)
        {
            bool diagEnabled = request.DiagnosticsEnabledAtRequest && _diagnostics.IsEnabled;
            Stopwatch scanTotal = Stopwatch.StartNew();
            double diagServiceMs = 0;
            double diagEnumerationMs = 0;
            double diagMemoryMs = 0;
            double diagGroupingMs = 0;
            double diagMetadataMs = 0;
            double diagAccountingMs = 0;
            int diagMetadataHits = 0;
            int diagMetadataMisses = 0;
            int diagMetadataDeferred = 0;

            DateTime nowUtc = DateTime.UtcNow;
            if (_lastServiceRefreshUtc == DateTime.MinValue || (nowUtc - _lastServiceRefreshUtc).TotalSeconds >= 30.0)
            {
                long diagServiceStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
                if (diagEnabled) _diagnostics.LogEvent("Service refresh started", "Refreshing Windows service-to-process mapping.", 0);
                Dictionary<int, List<string>> refreshed = ServiceEnumerator.GetServicesByProcess();
                Dictionary<int, string> serviceText = new Dictionary<int, string>(refreshed.Count);
                foreach (KeyValuePair<int, List<string>> item in refreshed)
                {
                    item.Value.Sort(StringComparer.OrdinalIgnoreCase);
                    serviceText[item.Key] = item.Value.Count == 0 ? string.Empty : string.Join(", ", item.Value);
                }
                _cachedServicesByPid = refreshed;
                _cachedServiceTextByPid = serviceText;
                _lastServiceRefreshUtc = nowUtc;
                if (diagEnabled)
                {
                    diagServiceMs = (Stopwatch.GetTimestamp() - diagServiceStart) * 1000.0 / Stopwatch.Frequency;
                    _diagnostics.LogEvent("Service refresh completed", "Windows service mapping refresh completed.", diagServiceMs);
                }
            }

            Dictionary<int, List<string>> servicesByPid = _cachedServicesByPid;
            Dictionary<int, string> serviceTextByPid = _cachedServiceTextByPid;
            _metadataLookupsThisScan = 0;
            Dictionary<string, ProcessGroupSample> groups = new Dictionary<string, ProcessGroupSample>(StringComparer.OrdinalIgnoreCase);
            ulong reliablePrivateTotal = 0;
            int unclassifiedProcessCount = 0;

            long diagEnumerationStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
            Process[] processes = Process.GetProcesses();
            if (diagEnabled)
                diagEnumerationMs = (Stopwatch.GetTimestamp() - diagEnumerationStart) * 1000.0 / Stopwatch.Frequency;

            foreach (Process p in processes)
            {
                try
                {
                    ulong privateWorkingSet;
                    long diagMemoryStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
                    bool reliablePrivate = NativeMemory.TryReadPrivateWorkingSet(p, out privateWorkingSet);
                    bool countsTowardAccounting = reliablePrivate && p.Id != 0 && p.Id != 4;
                    if (!reliablePrivate)
                    {
                        privateWorkingSet = NativeMemory.ReadFallbackProcessMemory(p);
                        unclassifiedProcessCount++;
                    }
                    else if (countsTowardAccounting)
                    {
                        reliablePrivateTotal += privateWorkingSet;
                    }
                    if (diagEnabled)
                        diagMemoryMs += (Stopwatch.GetTimestamp() - diagMemoryStart) * 1000.0 / Stopwatch.Frequency;

                    string procName = SafeProcessName(p);
                    List<string> services;
                    servicesByPid.TryGetValue(p.Id, out services);
                    int hostedServiceCount = services == null ? 0 : services.Count;
                    string serviceText;
                    if (!serviceTextByPid.TryGetValue(p.Id, out serviceText)) serviceText = string.Empty;

                    bool diagHadMetadata = diagEnabled && _metadataCache.ContainsKey(procName);
                    int diagLookupBefore = _metadataLookupsThisScan;
                    long diagMetadataStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
                    ProcessIdentity identity = ResolveProcessIdentity(p, procName, hostedServiceCount > 0);
                    if (diagEnabled)
                    {
                        double elapsedMeta = (Stopwatch.GetTimestamp() - diagMetadataStart) * 1000.0 / Stopwatch.Frequency;
                        diagMetadataMs += elapsedMeta;
                        if (diagHadMetadata) diagMetadataHits++;
                        else if (_metadataLookupsThisScan > diagLookupBefore) diagMetadataMisses++;
                        else diagMetadataDeferred++;
                        if (elapsedMeta > 100.0) _diagnostics.LogWarning("Metadata lookup unusually slow", "Process=" + procName, elapsedMeta);
                    }

                    string type = hostedServiceCount > 0 ? "Service" : identity.Type;
                    string displayName = identity.FriendlyName;
                    if (procName.Equals("svchost", StringComparison.OrdinalIgnoreCase) && hostedServiceCount > 0)
                    {
                        if (hostedServiceCount == 1) displayName = "Service Host: " + services[0];
                        else displayName = "Service Host: " + services[0] + " +" + (hostedServiceCount - 1).ToString(CultureInfo.InvariantCulture);
                    }
                    if (procName.Equals("System", StringComparison.OrdinalIgnoreCase) || p.Id == 0 || p.Id == 4)
                        type = "System";

                    long diagGroupingStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
                    string key = hostedServiceCount > 0 ? "pws2:svc:" + procName + "|" + serviceText : "pws2:proc:" + procName;
                    ProcessGroupSample group;
                    if (!groups.TryGetValue(key, out group))
                    {
                        group = new ProcessGroupSample();
                        group.Key = key;
                        group.Name = displayName;
                        group.TechnicalName = procName;
                        group.Type = type;
                        group.Services = serviceText;
                        group.Company = identity.Company;
                        group.BelongsTo = identity.BelongsTo;
                        group.Purpose = identity.Purpose;
                        groups[key] = group;
                    }
                    if (string.IsNullOrWhiteSpace(group.Company) && !string.IsNullOrWhiteSpace(identity.Company)) group.Company = identity.Company;
                    if (string.IsNullOrWhiteSpace(group.BelongsTo) && !string.IsNullOrWhiteSpace(identity.BelongsTo)) group.BelongsTo = identity.BelongsTo;
                    group.LiveBytes += privateWorkingSet;
                    group.InstanceCount++;
                    if (countsTowardAccounting)
                    {
                        group.AccountingPrivateBytes += privateWorkingSet;
                        group.AccountingInstanceCount++;
                    }
                    if (diagEnabled)
                        diagGroupingMs += (Stopwatch.GetTimestamp() - diagGroupingStart) * 1000.0 / Stopwatch.Frequency;
                }
                catch { }
                finally { p.Dispose(); }
            }

            long diagAccountingStart = diagEnabled ? Stopwatch.GetTimestamp() : 0L;
            ulong accountingProcessPrivate = 0;
            ulong accountingKernel = 0;
            ulong accountingPaged = 0;
            ulong accountingNonPaged = 0;
            ulong accountingOther = 0;
            ulong accountingUsed = 0;
            MemorySnapshot accountingMem = NativeMemory.Read();
            if (accountingMem.TotalPhysical > 0)
            {
                accountingUsed = accountingMem.TotalPhysical - accountingMem.AvailablePhysical;
                accountingProcessPrivate = Math.Min(reliablePrivateTotal, accountingUsed);
                ulong remaining = accountingUsed > accountingProcessPrivate ? accountingUsed - accountingProcessPrivate : 0UL;
                accountingKernel = Math.Min(accountingMem.KernelTotal, remaining);
                accountingOther = remaining > accountingKernel ? remaining - accountingKernel : 0UL;
                accountingPaged = accountingMem.KernelPaged;
                accountingNonPaged = accountingMem.KernelNonPaged;
            }
            if (diagEnabled)
                diagAccountingMs = (Stopwatch.GetTimestamp() - diagAccountingStart) * 1000.0 / Stopwatch.Frequency;

            int serviceCount = 0;
            foreach (List<string> serviceList in servicesByPid.Values) serviceCount += serviceList.Count;
            int svchostGroups = 0;
            foreach (ProcessGroupSample g in groups.Values)
            {
                if (g.TechnicalName.Equals("svchost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(g.Services)) svchostGroups++;
            }

            scanTotal.Stop();
            ProcessScanSnapshot snapshot = new ProcessScanSnapshot();
            snapshot.Request = request;
            snapshot.Groups = groups;
            snapshot.WindowsProcessCount = processes.Length;
            snapshot.ServiceCount = serviceCount;
            snapshot.SvchostServiceGroupCount = svchostGroups;
            snapshot.UnclassifiedProcessCount = unclassifiedProcessCount;
            snapshot.AccountingProcessPrivateBytes = accountingProcessPrivate;
            snapshot.AccountingKernelBytes = accountingKernel;
            snapshot.AccountingKernelPagedBytes = accountingPaged;
            snapshot.AccountingKernelNonPagedBytes = accountingNonPaged;
            snapshot.AccountingOtherBytes = accountingOther;
            snapshot.AccountingLiveBytes = accountingUsed;
            snapshot.ProcessEnumerationMs = diagEnumerationMs;
            snapshot.ProcessMemoryCollectionMs = diagMemoryMs;
            snapshot.ProcessGroupingMs = diagGroupingMs;
            snapshot.ServiceDiscoveryMs = diagServiceMs;
            snapshot.MetadataLookupMs = diagMetadataMs;
            snapshot.MetadataCacheHits = diagMetadataHits;
            snapshot.MetadataCacheMisses = diagMetadataMisses;
            snapshot.MetadataLookupsAttempted = _metadataLookupsThisScan;
            snapshot.MetadataLookupsDeferred = diagMetadataDeferred;
            snapshot.RamAccountingMs = diagAccountingMs;
            snapshot.BackgroundScanMs = scanTotal.Elapsed.TotalMilliseconds;
            snapshot.ScanCompletedTimestamp = Stopwatch.GetTimestamp();
            return snapshot;
        }

        private void ApplyProcessSnapshot(ProcessScanSnapshot snapshot)
        {
            if (snapshot == null)
            {
                Interlocked.Exchange(ref _processCycleGate, 0);
                return;
            }

            try
            {
                if (_processWorkerStopping || IsDisposed || Disposing) return;

                long uiApplyStart = Stopwatch.GetTimestamp();
                double uiMarshalDelayMs = (uiApplyStart - snapshot.ScanCompletedTimestamp) * 1000.0 / Stopwatch.Frequency;
                long diagStatsStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;

                _lastUnclassifiedProcessCount = snapshot.UnclassifiedProcessCount;
                _lastAccountingProcessPrivateBytes = snapshot.AccountingProcessPrivateBytes;
                _lastAccountingKernelBytes = snapshot.AccountingKernelBytes;
                _lastAccountingKernelPagedBytes = snapshot.AccountingKernelPagedBytes;
                _lastAccountingKernelNonPagedBytes = snapshot.AccountingKernelNonPagedBytes;
                _lastAccountingOtherBytes = snapshot.AccountingOtherBytes;
                _lastAccountingLiveBytes = snapshot.AccountingLiveBytes;

                long memoryBreakdownStart = Stopwatch.GetTimestamp();
                MemoryBreakdownSnapshot memoryBreakdown = MemoryBreakdownCalculator.Calculate(
                    snapshot.Groups,
                    snapshot.AccountingProcessPrivateBytes,
                    snapshot.AccountingKernelBytes,
                    snapshot.AccountingOtherBytes,
                    snapshot.AccountingLiveBytes,
                    _processClassificationCache);
                _diagLastMemoryBreakdownMs = (Stopwatch.GetTimestamp() - memoryBreakdownStart) * 1000.0 / Stopwatch.Frequency;
                ApplyMemoryBreakdownSnapshot(memoryBreakdown);
                if (memoryBreakdown.ProcessReconciliationMismatch && _diagnostics.IsEnabled)
                    _diagnostics.LogWarning(
                        "PROCESS MEMORY BREAKDOWN RECONCILIATION MISMATCH",
                        "Ownership category total differs from process-private accounting by " + FormatBytes(memoryBreakdown.ProcessReconciliationDifferenceBytes) + ".",
                        _diagLastMemoryBreakdownMs);
                if (memoryBreakdown.TotalLiveReconciliationMismatch && _diagnostics.IsEnabled)
                    _diagnostics.LogWarning(
                        "TOTAL LIVE MEMORY BREAKDOWN RECONCILIATION MISMATCH",
                        "Ownership categories plus kernel/system/shared differ from total LIVE RAM by " + FormatBytes(memoryBreakdown.TotalLiveReconciliationDifferenceBytes) + ".",
                        _diagLastMemoryBreakdownMs);
                if (_diagnostics.IsEnabled)
                    RecordClassificationAudit(memoryBreakdown);

                foreach (KeyValuePair<string, ProcessGroupSample> item in snapshot.Groups)
                {
                    AggregateStat stat;
                    if (!_processStats.TryGetValue(item.Key, out stat))
                    {
                        stat = new AggregateStat();
                        stat.Key = item.Key;
                        stat.Name = item.Value.Name;
                        stat.Type = item.Value.Type;
                        stat.Services = item.Value.Services;
                        _processStats[item.Key] = stat;
                    }
                    stat.Name = item.Value.Name;
                    stat.Type = item.Value.Type;
                    stat.Services = item.Value.Services;
                    stat.Samples++;
                    stat.SumBytes += item.Value.LiveBytes;
                    if (item.Value.LiveBytes > stat.PeakBytes) stat.PeakBytes = item.Value.LiveBytes;
                }
                double diagStatsMs = _diagnostics.IsEnabled ? (Stopwatch.GetTimestamp() - diagStatsStart) * 1000.0 / Stopwatch.Frequency : 0;

                _lastGroups = snapshot.Groups;
                _diagLastGridPrepareMs = 0;
                _diagLastGridUiMs = 0;
                _diagLastDetailsMs = 0;
                _diagLastGridSortMs = 0;
                if (Visible)
                {
                    if (_processGridInteractionActive)
                    {
                        if (_pendingGridGroups != null) _diagGridSnapshotsCoalesced++;
                        _pendingGridGroups = snapshot.Groups;
                        _diagGridInteractionDeferredRefreshCount++;
                    }
                    else
                    {
                        _pendingGridGroups = null;
                        UpdateProcessGrid(snapshot.Groups);
                    }
                }

                long uiApplyEnd = Stopwatch.GetTimestamp();
                double snapshotUiApplyMs = (uiApplyEnd - uiApplyStart) * 1000.0 / Stopwatch.Frequency;

                if (snapshot.Request.IsStartup && _startupProcessScanPending && !_startupReadyMarked)
                {
                    _startupProcessScanPending = false;
                    _startupReadyMarked = true;
                    StartupPerformance.Mark("First process scan completed");
                    StartupPerformance.Mark("Application ready");
                    if (_diagnostics.IsEnabled) _diagnostics.LogEvent("Application ready", "RAM Trace completed its first background process snapshot and UI application.", 0);
                }

                if (_diagnostics.IsEnabled && snapshot.Request.DiagnosticsEnabledAtRequest)
                {
                    using (Process me = Process.GetCurrentProcess())
                    {
                        DiagnosticCycleSample sample = new DiagnosticCycleSample();
                        sample.TimestampUtc = DateTime.UtcNow;
                        sample.ApplicationUptimeSeconds = _applicationUptime.Elapsed.TotalSeconds;
                        sample.ExpectedSamplingIntervalMs = snapshot.Request.ExpectedIntervalMs;
                        sample.ActualSamplingIntervalMs = snapshot.Request.ActualIntervalMs;
                        sample.TimerDriftMs = snapshot.Request.TimerDriftMs;
                        sample.MainRefreshCycleMs = snapshot.BackgroundScanMs;
                        sample.BackgroundProcessScanMs = snapshot.BackgroundScanMs;
                        sample.UiMarshalDelayMs = uiMarshalDelayMs;
                        sample.SnapshotUiApplyMs = snapshotUiApplyMs;
                        sample.TotalRamSamplingMs = _diagLastSystemSampleMs;
                        sample.ProcessEnumerationMs = snapshot.ProcessEnumerationMs;
                        sample.ProcessMemoryCollectionMs = snapshot.ProcessMemoryCollectionMs;
                        sample.ProcessGroupingMs = snapshot.ProcessGroupingMs;
                        sample.ServiceMappingMs = snapshot.ServiceDiscoveryMs;
                        sample.ServiceDiscoveryMs = snapshot.ServiceDiscoveryMs;
                        sample.MetadataLookupMs = snapshot.MetadataLookupMs;
                        sample.MetadataCacheHits = snapshot.MetadataCacheHits;
                        sample.MetadataCacheMisses = snapshot.MetadataCacheMisses;
                        sample.RamAccountingMs = snapshot.RamAccountingMs;
                        sample.HistoricalStatsUpdateMs = diagStatsMs;
                        sample.GridDataPreparationMs = _diagLastGridPrepareMs;
                        sample.GridUiRefreshMs = _diagLastGridUiMs;
                        sample.ProcessInformationPanelMs = _diagLastDetailsMs;
                        sample.FooterStatusUpdateMs = _diagLastFooterMs;
                        sample.LocalDataHistoryWriteMs = _diagLastDataWriteMs;
                        sample.TotalCompleteCycleMs = (uiApplyEnd - snapshot.Request.RequestTimestamp) * 1000.0 / Stopwatch.Frequency;
                        sample.WindowsProcessCount = snapshot.WindowsProcessCount;
                        sample.GroupedRowCount = snapshot.Groups.Count;
                        sample.ServiceCount = snapshot.ServiceCount;
                        sample.SvchostServiceGroupCount = snapshot.SvchostServiceGroupCount;
                        sample.MetadataLookupsAttempted = snapshot.MetadataLookupsAttempted;
                        sample.MetadataLookupsDeferred = snapshot.MetadataLookupsDeferred;
                        sample.WorkingSetBytes = me.WorkingSet64;
                        sample.PrivateBytes = me.PrivateMemorySize64;
                        sample.ManagedHeapBytes = GC.GetTotalMemory(false);
                        sample.CpuPercent = GetRollingCpu();
                        sample.ProcessCpuTimeMs = me.TotalProcessorTime.TotalMilliseconds;
                        sample.ThreadCount = me.Threads.Count;
                        sample.HandleCount = me.HandleCount;
                        sample.GcGen0 = GC.CollectionCount(0);
                        sample.GcGen1 = GC.CollectionCount(1);
                        sample.GcGen2 = GC.CollectionCount(2);
                        sample.CpuThrottled = _processTimer.Interval > _normalProcessInterval;
                        sample.LiveSystemRamBytes = _diagLastLiveSystemRamBytes;
                        sample.ProcessPrivateRamBytes = _lastAccountingProcessPrivateBytes;
                        sample.KernelPoolRamBytes = _lastAccountingKernelBytes;
                        sample.SystemSharedRamBytes = _lastAccountingOtherBytes;
                        sample.MemoryBreakdownCalculationMs = _diagLastMemoryBreakdownMs;
                        sample.WindowsSystemPrivateMB = _lastMemoryBreakdown.WindowsSystem.PrivateBytes / 1048576.0;
                        sample.MicrosoftAppsPrivateMB = _lastMemoryBreakdown.MicrosoftApplications.PrivateBytes / 1048576.0;
                        sample.ThirdPartyPrivateMB = _lastMemoryBreakdown.ThirdPartyApplications.PrivateBytes / 1048576.0;
                        sample.UnclassifiedPrivateMB = _lastMemoryBreakdown.Unclassified.PrivateBytes / 1048576.0;
                        sample.WindowsSystemGroups = _lastMemoryBreakdown.WindowsSystem.GroupCount;
                        sample.MicrosoftAppGroups = _lastMemoryBreakdown.MicrosoftApplications.GroupCount;
                        sample.ThirdPartyGroups = _lastMemoryBreakdown.ThirdPartyApplications.GroupCount;
                        sample.UnclassifiedGroups = _lastMemoryBreakdown.Unclassified.GroupCount;
                        sample.MemoryBreakdownMismatchCount = _lastMemoryBreakdown.ProcessReconciliationMismatch ? 1 : 0;
                        sample.ProcessBreakdownMismatchCount = _lastMemoryBreakdown.ProcessReconciliationMismatch ? 1 : 0;
                        sample.TotalLiveBreakdownMismatchCount = _lastMemoryBreakdown.TotalLiveReconciliationMismatch ? 1 : 0;
                        sample.SelectedProcess = GetSelectedProcessForDiagnostics();
                        sample.WindowState = WindowState.ToString();
                        sample.ProcessGridRowCount = _processGrid.Rows.Count;
                        sample.SkippedProcessScans = Interlocked.Exchange(ref _skippedProcessScansSinceLastSample, 0);
                        sample.GridRowsAdded = _diagGridRowsAdded;
                        sample.GridRowsUpdated = _diagGridRowsUpdated;
                        sample.GridRowsRemoved = _diagGridRowsRemoved;
                        sample.FullGridRebuildCount = _diagFullGridRebuilds;
                        sample.GridStructuralChanges = _diagGridStructuralChanges;
                        sample.GridValueOnlyUpdates = _diagGridValueOnlyUpdates;
                        sample.GridSortDurationMs = _diagLastGridSortMs;
                        sample.GridInteractionDeferredRefreshCount = _diagGridInteractionDeferredRefreshCount;
                        sample.GridSnapshotsCoalesced = _diagGridSnapshotsCoalesced;
                        sample.GridDuplicateKeyCount = _diagGridDuplicateKeyCount;
                        sample.DiagnosticQueueSize = _diagnostics.QueueSize;
                        _diagnostics.RecordCycle(sample);
                        ResetGridDiagnosticCounters();
                    }
                }
                else
                {
                    Interlocked.Exchange(ref _skippedProcessScansSinceLastSample, 0);
                    ResetGridDiagnosticCounters();
                }
            }
            catch (Exception ex)
            {
                if (_diagnostics.IsEnabled) _diagnostics.LogException("Snapshot UI application exception", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _processCycleGate, 0);
            }
        }

        private void MarkProcessGridInteraction()
        {
            if (_updatingProcessGrid || _suppressProcessGridInteractionEvents || IsDisposed || Disposing) return;
            _processGridInteractionActive = true;
            _processGridInteractionTimer.Stop();
            _processGridInteractionTimer.Start();
        }

        private void ProcessGridInteractionTimerTick()
        {
            _processGridInteractionTimer.Stop();
            _processGridInteractionActive = false;
            if (!Visible || _pendingGridGroups == null || IsDisposed || Disposing) return;

            Dictionary<string, ProcessGroupSample> latest = _pendingGridGroups;
            _pendingGridGroups = null;
            UpdateProcessGrid(latest);
        }

        private void ResetGridDiagnosticCounters()
        {
            _diagGridRowsAdded = 0;
            _diagGridRowsUpdated = 0;
            _diagGridRowsRemoved = 0;
            _diagFullGridRebuilds = 0;
            _diagGridStructuralChanges = 0;
            _diagGridValueOnlyUpdates = 0;
            _diagGridInteractionDeferredRefreshCount = 0;
            _diagGridSnapshotsCoalesced = 0;
            _diagGridDuplicateKeyCount = 0;
        }

        private void UpdateProcessGrid(Dictionary<string, ProcessGroupSample> groups)
        {
            if (groups == null) return;

            string selectedKey = null;
            if (_processGrid.SelectedRows.Count > 0)
                selectedKey = Convert.ToString(_processGrid.SelectedRows[0].Tag, CultureInfo.InvariantCulture);

            int firstVisibleIndex = -1;
            string firstVisibleKey = null;
            int horizontalOffset = 0;
            try
            {
                firstVisibleIndex = _processGrid.FirstDisplayedScrollingRowIndex;
                if (firstVisibleIndex >= 0 && firstVisibleIndex < _processGrid.Rows.Count)
                    firstVisibleKey = Convert.ToString(_processGrid.Rows[firstVisibleIndex].Tag, CultureInfo.InvariantCulture);
                horizontalOffset = _processGrid.HorizontalScrollingOffset;
            }
            catch { }

            long prepareStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
            List<ProcessGroupSample> ordered = new List<ProcessGroupSample>(groups.Values);
            ordered.Sort(delegate(ProcessGroupSample a, ProcessGroupSample b)
            {
                int byRam = b.LiveBytes.CompareTo(a.LiveBytes);
                if (byRam != 0) return byRam;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (_diagnostics.IsEnabled)
                _diagLastGridPrepareMs = (Stopwatch.GetTimestamp() - prepareStart) * 1000.0 / Stopwatch.Frequency;

            long uiStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
            int rowsAdded = 0;
            int rowsUpdated = 0;
            int rowsRemoved = 0;
            bool usedFallbackRebuild = false;

            _updatingProcessGrid = true;
            _processGrid.SuspendLayout();
            try
            {
                if (!ProcessGridIndexIsConsistent())
                {
                    int previousRows = _processGrid.Rows.Count;
                    RebuildProcessGrid(ordered);
                    rowsAdded = ordered.Count;
                    rowsRemoved = previousRows;
                    usedFallbackRebuild = true;
                }
                else
                {
                    List<string> disappearedKeys = new List<string>();
                    foreach (string key in _processRowsByKey.Keys)
                    {
                        if (!groups.ContainsKey(key)) disappearedKeys.Add(key);
                    }

                    foreach (string key in disappearedKeys)
                    {
                        DataGridViewRow row;
                        if (_processRowsByKey.TryGetValue(key, out row))
                        {
                            try { _processGrid.Rows.Remove(row); } catch { }
                            _processRowsByKey.Remove(key);
                            rowsRemoved++;
                        }
                    }

                    foreach (ProcessGroupSample group in ordered)
                    {
                        DataGridViewRow row;
                        if (_processRowsByKey.TryGetValue(group.Key, out row))
                        {
                            if (UpdateProcessGridRow(row, group)) rowsUpdated++;
                        }
                        else
                        {
                            row = CreateProcessGridRow(group);
                            _processGrid.Rows.Add(row);
                            _processRowsByKey[group.Key] = row;
                            rowsAdded++;
                        }
                    }

                    long sortStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
                    if (_processGrid.Rows.Count > 1)
                        _processGrid.Sort(new ProcessGridRowComparer(groups));
                    if (_diagnostics.IsEnabled)
                        _diagLastGridSortMs = (Stopwatch.GetTimestamp() - sortStart) * 1000.0 / Stopwatch.Frequency;
                }
            }
            catch (Exception ex)
            {
                if (_diagnostics.IsEnabled) _diagnostics.LogException("Incremental process-grid update failed", ex);
                try
                {
                    int previousRows = _processGrid.Rows.Count;
                    RebuildProcessGrid(ordered);
                    rowsAdded = ordered.Count;
                    rowsUpdated = 0;
                    rowsRemoved = previousRows;
                    usedFallbackRebuild = true;
                }
                catch (Exception rebuildEx)
                {
                    if (_diagnostics.IsEnabled) _diagnostics.LogException("Process-grid fallback rebuild failed", rebuildEx);
                }
            }
            finally
            {
                _processGrid.ResumeLayout();
                _updatingProcessGrid = false;
            }

            _diagGridRowsAdded += rowsAdded;
            _diagGridRowsUpdated += rowsUpdated;
            _diagGridRowsRemoved += rowsRemoved;
            _diagGridStructuralChanges += rowsAdded + rowsRemoved;
            if (!usedFallbackRebuild && rowsAdded == 0 && rowsRemoved == 0 && rowsUpdated > 0)
                _diagGridValueOnlyUpdates++;
            if (usedFallbackRebuild) _diagFullGridRebuilds++;

            if (!_initialProcessSelectionDone && _processGrid.Rows.Count > 0)
            {
                _processGrid.ClearSelection();
                _processGrid.Rows[0].Selected = true;
                selectedKey = Convert.ToString(_processGrid.Rows[0].Tag, CultureInfo.InvariantCulture);
                _initialProcessSelectionDone = true;
            }
            else if (!string.IsNullOrEmpty(selectedKey))
            {
                DataGridViewRow selectedRow;
                if (_processRowsByKey.TryGetValue(selectedKey, out selectedRow) && !selectedRow.Selected)
                {
                    _processGrid.ClearSelection();
                    selectedRow.Selected = true;
                }
            }

            int restoreIndex = -1;
            if (!string.IsNullOrEmpty(firstVisibleKey))
            {
                DataGridViewRow visibleRow;
                if (_processRowsByKey.TryGetValue(firstVisibleKey, out visibleRow)) restoreIndex = visibleRow.Index;
            }
            if (restoreIndex < 0 && firstVisibleIndex >= 0 && _processGrid.Rows.Count > 0)
                restoreIndex = Math.Min(firstVisibleIndex, _processGrid.Rows.Count - 1);

            _suppressProcessGridInteractionEvents = true;
            try
            {
                if (restoreIndex >= 0)
                {
                    try { _processGrid.FirstDisplayedScrollingRowIndex = restoreIndex; }
                    catch { }
                }
                try { _processGrid.HorizontalScrollingOffset = horizontalOffset; }
                catch { }
            }
            finally
            {
                _suppressProcessGridInteractionEvents = false;
            }

            ValidateProcessGridConsistency(groups);

            long detailsStart = _diagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0L;
            UpdateDetailsPanel();
            if (_diagnostics.IsEnabled)
            {
                _diagLastDetailsMs = (Stopwatch.GetTimestamp() - detailsStart) * 1000.0 / Stopwatch.Frequency;
                _diagLastGridUiMs = (Stopwatch.GetTimestamp() - uiStart) * 1000.0 / Stopwatch.Frequency;
            }
        }

        private bool ProcessGridIndexIsConsistent()
        {
            if (_processRowsByKey.Count != _processGrid.Rows.Count) return false;
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _processGrid.Rows)
            {
                string key = Convert.ToString(row.Tag, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(key) || !keys.Add(key)) return false;
                DataGridViewRow mapped;
                if (!_processRowsByKey.TryGetValue(key, out mapped) || !Object.ReferenceEquals(mapped, row)) return false;
            }
            return true;
        }

        private void RebuildProcessGrid(List<ProcessGroupSample> ordered)
        {
            _processGrid.Rows.Clear();
            _processRowsByKey.Clear();
            foreach (ProcessGroupSample group in ordered)
            {
                DataGridViewRow row = CreateProcessGridRow(group);
                _processGrid.Rows.Add(row);
                _processRowsByKey[group.Key] = row;
            }
        }

        private DataGridViewRow CreateProcessGridRow(ProcessGroupSample group)
        {
            DataGridViewRow row = new DataGridViewRow();
            row.CreateCells(_processGrid);
            row.Tag = group.Key;
            row.Height = _processGrid.RowTemplate.Height;
            UpdateProcessGridRow(row, group);
            return row;
        }

        private bool UpdateProcessGridRow(DataGridViewRow row, ProcessGroupSample group)
        {
            AggregateStat stat;
            _processStats.TryGetValue(group.Key, out stat);
            ulong avg = 0;
            ulong peak = group.LiveBytes;
            if (stat != null && stat.Samples > 0)
            {
                avg = (ulong)Math.Max(0, stat.SumBytes / stat.Samples);
                peak = stat.PeakBytes;
            }

            string[] values = new string[]
            {
                group.Name,
                group.TechnicalName,
                group.Type,
                group.InstanceCount.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(group.Services) ? string.Empty : group.Services,
                FormatBytes(group.LiveBytes),
                FormatBytes(avg),
                FormatBytes(peak)
            };

            bool changed = false;
            for (int i = 0; i < values.Length; i++)
            {
                string current = Convert.ToString(row.Cells[i].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(current, values[i], StringComparison.Ordinal))
                {
                    row.Cells[i].Value = values[i];
                    changed = true;
                }
            }

            ulong previousLive = 0;
            object previousTag = row.Cells[5].Tag;
            if (previousTag is ulong) previousLive = (ulong)previousTag;
            row.Cells[5].Tag = group.LiveBytes;
            if (previousTag == null || GetRamColourBand(previousLive) != GetRamColourBand(group.LiveBytes))
                ApplyRamColour(row, group.LiveBytes);

            ApplyProcessRowEmphasis(row, group);

            return changed;
        }

        private void ApplyProcessRowEmphasis(DataGridViewRow row, ProcessGroupSample group)
        {
            if (row == null) return;
            row.DefaultCellStyle.Font = group != null && string.Equals(group.Key, _selfProcessStableKey, StringComparison.OrdinalIgnoreCase) && _gridBoldFont != null ? _gridBoldFont : null;
        }

        private static int GetRamColourBand(ulong bytes)
        {
            const ulong MB = 1024UL * 1024UL;
            if (bytes >= 2UL * 1024UL * MB) return 4;
            if (bytes >= 1024UL * MB) return 3;
            if (bytes >= 500UL * MB) return 2;
            if (bytes >= 250UL * MB) return 1;
            return 0;
        }

        private void ValidateProcessGridConsistency(Dictionary<string, ProcessGroupSample> groups)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int duplicateCount = 0;
            string firstDuplicate = null;
            foreach (DataGridViewRow row in _processGrid.Rows)
            {
                string key = Convert.ToString(row.Tag, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(key) || !keys.Add(key))
                {
                    duplicateCount++;
                    if (firstDuplicate == null) firstDuplicate = string.IsNullOrEmpty(key) ? "<empty>" : key;
                }
            }

            _diagGridDuplicateKeyCount += duplicateCount;
            if (!_diagnostics.IsEnabled) return;

            if (duplicateCount > 0)
            {
                _diagnostics.LogWarning("GRID DUPLICATE KEY",
                    "Duplicate stable process-grid key detected. Key=" + firstDuplicate
                    + ", rows=" + _processGrid.Rows.Count.ToString(CultureInfo.InvariantCulture)
                    + ", unique=" + keys.Count.ToString(CultureInfo.InvariantCulture)
                    + ", expected=" + groups.Count.ToString(CultureInfo.InvariantCulture) + ".", 0);
            }
            else if (_processGrid.Rows.Count != groups.Count || keys.Count != groups.Count)
            {
                _diagnostics.LogWarning("GRID CONSISTENCY MISMATCH",
                    "Process-grid row count does not match the latest snapshot. rows=" + _processGrid.Rows.Count.ToString(CultureInfo.InvariantCulture)
                    + ", unique=" + keys.Count.ToString(CultureInfo.InvariantCulture)
                    + ", expected=" + groups.Count.ToString(CultureInfo.InvariantCulture) + ".", 0);
            }
        }

        private void ApplyRamColour(DataGridViewRow row, ulong bytes)
        {
            const ulong MB = 1024UL * 1024UL;
            Color colour = Color.White;
            if (bytes >= 2UL * 1024UL * MB) colour = Color.FromArgb(255, 224, 224);
            else if (bytes >= 1024UL * MB) colour = Color.FromArgb(255, 235, 211);
            else if (bytes >= 500UL * MB) colour = Color.FromArgb(255, 248, 218);
            else if (bytes >= 250UL * MB) colour = Color.FromArgb(232, 244, 255);

            if (row.Cells.Count > 5)
            {
                row.Cells[0].Style.BackColor = colour;
                row.Cells[5].Style.BackColor = colour;
                if (_gridBoldFont != null) row.Cells[5].Style.Font = _gridBoldFont;
            }
        }

        private void UpdateDetailsPanel()
        {
            if (_updatingProcessGrid) return;
            if (_processGrid.SelectedRows.Count == 0)
            {
                _detailsBox.Text = "The largest RAM consumer will be selected automatically. Select any other row to see what it is and what it belongs to.";
                return;
            }

            string key = Convert.ToString(_processGrid.SelectedRows[0].Tag, CultureInfo.InvariantCulture);
            ProcessGroupSample group;
            if (string.IsNullOrEmpty(key) || !_lastGroups.TryGetValue(key, out group)) return;

            AggregateStat stat;
            _processStats.TryGetValue(key, out stat);
            ulong avg = group.LiveBytes;
            ulong peak = group.LiveBytes;
            if (stat != null && stat.Samples > 0)
            {
                avg = (ulong)Math.Max(0, stat.SumBytes / stat.Samples);
                peak = stat.PeakBytes;
            }

            StringBuilder sb = new StringBuilder();

            // Keep the information panel human-readable rather than presenting it
            // as a single diagnostic line. The technical identity is still retained.
            sb.AppendLine(group.Name);
            sb.AppendLine();
            sb.Append("Technical name: ");
            sb.AppendLine(group.TechnicalName);
            sb.Append("Type: ");
            sb.AppendLine(group.Type);

            if (!string.IsNullOrWhiteSpace(group.Company))
            {
                sb.Append("Publisher: ");
                sb.AppendLine(group.Company);
            }

            if (!string.IsNullOrWhiteSpace(group.BelongsTo))
            {
                sb.Append("Belongs to: ");
                sb.AppendLine(group.BelongsTo);
            }

            sb.Append("Instances: ");
            sb.AppendLine(group.InstanceCount.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(group.Services))
            {
                if (group.TechnicalName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                {
                    string[] hostedServices = SplitHostedServices(group.Services);
                    sb.Append("Hosted services: ");
                    if (hostedServices.Length == 1)
                    {
                        sb.AppendLine(hostedServices[0]);
                    }
                    else if (hostedServices.Length > 1)
                    {
                        sb.Append(hostedServices[0]);
                        sb.Append(" and ");
                        sb.Append(hostedServices.Length - 1);
                        sb.AppendLine((hostedServices.Length - 1) == 1 ? " other Windows service" : " other Windows services");
                        sb.AppendLine("All hosted services:");
                        foreach (string serviceName in hostedServices)
                        {
                            sb.Append("  - ");
                            sb.AppendLine(serviceName);
                        }
                    }
                }
                else
                {
                    sb.Append("Windows service(s): ");
                    sb.AppendLine(group.Services);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Purpose:");
            sb.AppendLine(string.IsNullOrWhiteSpace(group.Purpose) ? "Running Windows application or background component." : group.Purpose);

            string virtualisationHint = GetVirtualisationHint(group);
            if (!string.IsNullOrWhiteSpace(virtualisationHint))
            {
                sb.AppendLine();
                sb.Append("Possible source: ");
                sb.AppendLine(virtualisationHint);
                sb.AppendLine("This is inferred from related processes currently running; vmmem itself does not identify one exact owner.");
            }

            string measurementNote = GetMeasurementNote(group);
            if (!string.IsNullOrWhiteSpace(measurementNote))
            {
                sb.AppendLine();
                sb.AppendLine("Measurement note:");
                sb.AppendLine(measurementNote);
            }

            sb.AppendLine();
            sb.AppendLine("RAM:");
            sb.Append("Live: ");
            sb.AppendLine(FormatBytes(group.LiveBytes));
            sb.Append("Today average: ");
            sb.AppendLine(FormatBytes(avg));
            sb.Append("Today peak: ");
            sb.Append(FormatBytes(peak));

            _detailsBox.Text = sb.ToString();
        }

        private static string[] SplitHostedServices(string serviceText)
        {
            if (string.IsNullOrWhiteSpace(serviceText)) return new string[0];
            return serviceText.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
        }

        private string GetVirtualisationHint(ProcessGroupSample group)
        {
            if (group == null || !(group.TechnicalName.Equals("vmmem", StringComparison.OrdinalIgnoreCase)
                || group.TechnicalName.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase)))
                return "";

            List<string> hints = new List<string>();
            foreach (ProcessGroupSample g in _lastGroups.Values)
            {
                string t = g.TechnicalName ?? "";
                string n = g.Name ?? "";
                if (t.Equals("crosvm", StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf("Google Play", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddUnique(hints, "Google Play Games / Android virtualisation");
                if (t.Equals("wsl", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("wslhost", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("wslservice", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase))
                    AddUnique(hints, "Windows Subsystem for Linux (WSL2)");
                if (t.IndexOf("docker", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Docker", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Equals("com.docker.backend", StringComparison.OrdinalIgnoreCase))
                    AddUnique(hints, "Docker Desktop");
                if (t.IndexOf("WindowsSandbox", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Windows Sandbox", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddUnique(hints, "Windows Sandbox");
            }

            if (group.TechnicalName.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase))
                AddUnique(hints, "Windows Subsystem for Linux (WSL2)");

            return hints.Count == 0 ? "No related virtualisation application identified from the current process list" : string.Join(", ", hints.ToArray());
        }

        private static void AddUnique(List<string> items, string value)
        {
            if (!items.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase))) items.Add(value);
        }

        private static string GetMeasurementNote(ProcessGroupSample group)
        {
            if (group == null) return "";
            string technical = group.TechnicalName ?? "";
            string type = group.Type ?? "";

            if (technical.Equals("dwm", StringComparison.OrdinalIgnoreCase))
                return "Desktop Window Manager uses shared and GPU-backed surfaces. Task Manager can present a different figure depending on the view and refresh moment; RAM Trace shows process Private Working Set.";

            if (technical.Equals("Secure System", StringComparison.OrdinalIgnoreCase)
                || technical.Equals("System", StringComparison.OrdinalIgnoreCase)
                || type.IndexOf("security", StringComparison.OrdinalIgnoreCase) >= 0
                || technical.StartsWith("mc-", StringComparison.OrdinalIgnoreCase))
                return "Windows and security components can use protected/shared memory or multiple helper processes, so their row may not map one-for-one to a single Task Manager entry.";

            if (technical.Equals("vmmem", StringComparison.OrdinalIgnoreCase)
                || technical.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase))
                return "Virtual-machine memory is managed partly by Windows/Hyper-V. Use this as the RAM attributed to the VM host, while the whole-system LIVE figure remains the authoritative total.";

            return "";
        }

        private ProcessIdentity ResolveProcessIdentity(Process p, string procName, bool isService)
        {
            KnownProcessInfo known;
            if (ProcessKnowledge.TryGet(procName, out known))
            {
                ProcessIdentity result = new ProcessIdentity();
                result.FriendlyName = known.FriendlyName;
                result.Type = string.IsNullOrWhiteSpace(known.Type) ? (isService ? "Service" : "App / process") : known.Type;
                result.Purpose = known.Purpose;
                result.BelongsTo = known.BelongsTo;
                result.Company = known.Company;
                return result;
            }

            ExecutableMetadata meta = GetExecutableMetadata(p, procName);
            ProcessIdentity identity = new ProcessIdentity();
            identity.FriendlyName = ChooseFriendlyName(procName, meta);
            identity.Type = isService ? "Service" : "App / process";
            identity.Company = meta.Company;
            identity.BelongsTo = !string.IsNullOrWhiteSpace(meta.Product) ? meta.Product : meta.Company;
            if (isService)
                identity.Purpose = "Hosts or implements the Windows service(s) shown for this process.";
            else if (!string.IsNullOrWhiteSpace(meta.Product))
                identity.Purpose = "Application or background component belonging to " + meta.Product + ".";
            else
                identity.Purpose = "Running Windows application or background process. The technical name is retained because no more reliable friendly description was available.";
            return identity;
        }

        private ExecutableMetadata GetExecutableMetadata(Process p, string procName)
        {
            ExecutableMetadata cached;
            if (_metadataCache.TryGetValue(procName, out cached)) return cached;

            if (_metadataLookupsThisScan >= 8) return new ExecutableMetadata();
            _metadataLookupsThisScan++;

            ExecutableMetadata meta = new ExecutableMetadata();
            try
            {
                ProcessModule module = p.MainModule;
                if (module != null)
                {
                    FileVersionInfo info = module.FileVersionInfo;
                    if (info != null)
                    {
                        meta.Description = CleanMetadata(info.FileDescription);
                        meta.Product = CleanMetadata(info.ProductName);
                        meta.Company = CleanMetadata(info.CompanyName);
                    }
                }
            }
            catch { }
            _metadataCache[procName] = meta;
            return meta;
        }

        private static string CleanMetadata(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Trim().Replace("\r", " ").Replace("\n", " ");
        }

        private static string ChooseFriendlyName(string procName, ExecutableMetadata meta)
        {
            string description = meta.Description;
            if (!string.IsNullOrWhiteSpace(description) && !description.Equals(procName, StringComparison.OrdinalIgnoreCase)) return description;
            if (!string.IsNullOrWhiteSpace(meta.Product) && !meta.Product.Equals(procName, StringComparison.OrdinalIgnoreCase)) return meta.Product;
            return procName;
        }

        private string SafeProcessName(Process p)
        {
            try { return p.ProcessName; }
            catch { return "PID " + p.Id.ToString(CultureInfo.InvariantCulture); }
        }

        private void OpenProcessGuide()
        {
            try
            {
                if (_processGuideForm == null || _processGuideForm.IsDisposed)
                {
                    _processGuideForm = new ProcessGuideForm();
                    _processGuideForm.FormClosed += delegate { _processGuideForm = null; };
                }
                if (!_processGuideForm.Visible) _processGuideForm.Show(this);
                else _processGuideForm.BringToFront();
                _processGuideForm.Activate();
                if (_diagnostics.IsEnabled) _diagnostics.LogEvent("Process guide opened", "User opened the embedded RAM Trace process guide.", 0);
            }
            catch (Exception ex)
            {
                if (_diagnostics.IsEnabled) _diagnostics.LogException("Process guide open failed", ex);
                MessageBox.Show("RAM Trace could not open the Process guide.\r\n\r\n" + ex.Message, "RAM Trace", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RecordClassificationAudit(MemoryBreakdownSnapshot breakdown)
        {
            if (!_diagnostics.IsEnabled || breakdown == null || breakdown.ClassificationAudit == null) return;
            List<ClassificationAuditRecord> changed = new List<ClassificationAuditRecord>();
            HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ClassificationAuditRecord record in breakdown.ClassificationAudit)
            {
                if (record == null) continue;
                string key = record.StableGroupKey ?? string.Empty;
                active.Add(key);
                string fingerprint = string.Join("|", new string[]
                {
                    record.FamiliarName ?? string.Empty,
                    record.TechnicalName ?? string.Empty,
                    record.Type ?? string.Empty,
                    record.Publisher ?? string.Empty,
                    record.ProductName ?? string.Empty,
                    record.DisplayPrivateBytes.ToString(CultureInfo.InvariantCulture),
                    record.AccountingPrivateBytes.ToString(CultureInfo.InvariantCulture),
                    record.Instances.ToString(CultureInfo.InvariantCulture),
                    record.AccountingInstances.ToString(CultureInfo.InvariantCulture),
                    record.AssignedCategory.ToString(),
                    record.ClassificationRule ?? string.Empty
                });

                string previous;
                if (!_classificationAuditFingerprints.TryGetValue(key, out previous)
                    || !string.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    _classificationAuditFingerprints[key] = fingerprint;
                    changed.Add(record);
                }
            }

            List<string> stale = new List<string>();
            foreach (string key in _classificationAuditFingerprints.Keys)
                if (!active.Contains(key)) stale.Add(key);
            foreach (string key in stale) _classificationAuditFingerprints.Remove(key);

            if (changed.Count > 0) _diagnostics.RecordClassificationAudit(changed);
        }

        private void ToggleDiagnostics()
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnosticHeartbeatTimer.Stop();
                _diagnostics.StopSession("Disabled by user");
                _diagnosticsButton.Text = "Diagnostics: OFF";
                _diagHeartbeatLastUtc = DateTime.MinValue;
                return;
            }

            string session = _diagnostics.StartSession(_dataDir);
            _classificationAuditFingerprints.Clear();
            _diagnosticsButton.Text = "Diagnostics: ON";
            _diagHeartbeatLastUtc = DateTime.UtcNow;
            RebaseProcessScheduleDiagnostics();
            _diagLastThrottleState = _processTimer.Interval > _normalProcessInterval;
            _diagnostics.UpdateThrottleState(_diagLastThrottleState);
            _diagnostics.LogEvent("Application startup context", "Diagnostics were enabled after application startup. Startup milestones were retained in memory and copied into this session.", 0);
            _diagnostics.LogEvent("Diagnostic storage", "Session folder: " + session, 0);
            _diagnosticHeartbeatTimer.Start();
        }

        private void DiagnosticHeartbeatTick()
        {
            if (!_diagnostics.IsEnabled) return;
            DateTime now = DateTime.UtcNow;
            if (_diagHeartbeatLastUtc == DateTime.MinValue)
            {
                _diagHeartbeatLastUtc = now;
                return;
            }
            double actual = (now - _diagHeartbeatLastUtc).TotalMilliseconds;
            _diagHeartbeatLastUtc = now;
            double delay = Math.Max(0, actual - _diagnosticHeartbeatTimer.Interval);
            _diagnostics.RecordUiHeartbeat(_diagnosticHeartbeatTimer.Interval, actual, delay);
            _diagnostics.FlushIfDue();
        }

        private string GetSelectedProcessForDiagnostics()
        {
            try
            {
                if (_processGrid.SelectedRows.Count == 0) return string.Empty;
                DataGridViewRow row = _processGrid.SelectedRows[0];
                if (row.Cells.Count > 1) return Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        private void ResetForNewDay(DateTime date)
        {
            _statDate = date.Date;
            _systemSamples = 0;
            _systemSumBytes = 0;
            _systemMinBytes = ulong.MaxValue;
            _systemPeakBytes = 0;
            _processStats.Clear();
            PersistState();
            RefreshHistoryGrid();
        }

        private void ArchiveCurrentDay()
        {
            if (_systemSamples <= 0 || _statDate == DateTime.MinValue)
                return;

            try
            {
                Directory.CreateDirectory(_dataDir);
                string dateText = _statDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                bool exists = false;
                if (File.Exists(_historyPath))
                {
                    string[] lines = File.ReadAllLines(_historyPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith(dateText + "\t", StringComparison.Ordinal))
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (!exists)
                {
                    ulong avg = (ulong)Math.Max(0, _systemSumBytes / _systemSamples);
                    string line = dateText + "\t"
                        + avg.ToString(CultureInfo.InvariantCulture) + "\t"
                        + (_systemMinBytes == ulong.MaxValue ? 0UL : _systemMinBytes).ToString(CultureInfo.InvariantCulture) + "\t"
                        + _systemPeakBytes.ToString(CultureInfo.InvariantCulture) + "\t"
                        + _systemSamples.ToString(CultureInfo.InvariantCulture);
                    File.AppendAllText(_historyPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        private void PersistState()
        {
            Stopwatch diagPersist = _diagnostics.IsEnabled ? Stopwatch.StartNew() : null;
            try
            {
                Directory.CreateDirectory(_dataDir);
                string temp = _statePath + ".tmp";
                using (StreamWriter sw = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    sw.WriteLine("DATE\t" + _statDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    sw.WriteLine("SYSTEM\t" + _systemSamples.ToString(CultureInfo.InvariantCulture)
                        + "\t" + _systemSumBytes.ToString("R", CultureInfo.InvariantCulture)
                        + "\t" + (_systemMinBytes == ulong.MaxValue ? 0UL : _systemMinBytes).ToString(CultureInfo.InvariantCulture)
                        + "\t" + _systemPeakBytes.ToString(CultureInfo.InvariantCulture));
                    foreach (AggregateStat stat in _processStats.Values)
                    {
                        sw.WriteLine("PROCESS\t" + Encode(stat.Key)
                            + "\t" + Encode(stat.Name)
                            + "\t" + Encode(stat.Type)
                            + "\t" + Encode(stat.Services)
                            + "\t" + stat.Samples.ToString(CultureInfo.InvariantCulture)
                            + "\t" + stat.SumBytes.ToString("R", CultureInfo.InvariantCulture)
                            + "\t" + stat.PeakBytes.ToString(CultureInfo.InvariantCulture));
                    }
                }
                if (File.Exists(_statePath)) File.Delete(_statePath);
                File.Move(temp, _statePath);
            }
            catch (Exception ex)
            {
                if (_diagnostics.IsEnabled) _diagnostics.LogException("Local data save error", ex);
            }
            if (_diagnostics.IsEnabled && diagPersist != null)
            {
                _diagLastDataWriteMs = diagPersist.Elapsed.TotalMilliseconds;
                if (_diagLastDataWriteMs > 100.0) _diagnostics.LogWarning("History saved slowly", "Local state/history write exceeded 100 ms.", _diagLastDataWriteMs);
                else _diagnostics.LogEvent("History saved", "Local RAM Trace state was persisted.", _diagLastDataWriteMs);
            }
        }

        private void LoadState()
        {
            if (!File.Exists(_statePath))
            {
                _statDate = DateTime.Today;
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(_statePath, Encoding.UTF8);
                DateTime fileDate = DateTime.Today;
                foreach (string line in lines)
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0] == "DATE")
                    {
                        DateTime parsed;
                        if (DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                            fileDate = parsed.Date;
                        break;
                    }
                }

                _statDate = fileDate;
                foreach (string line in lines)
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 5 && parts[0] == "SYSTEM")
                    {
                        long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _systemSamples);
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _systemSumBytes);
                        ulong min;
                        ulong peak;
                        ulong.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out min);
                        ulong.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out peak);
                        _systemMinBytes = min == 0 ? ulong.MaxValue : min;
                        _systemPeakBytes = peak;
                    }
                    else if (parts.Length >= 8 && parts[0] == "PROCESS")
                    {
                        AggregateStat stat = new AggregateStat();
                        stat.Key = Decode(parts[1]);
                        stat.Name = Decode(parts[2]);
                        stat.Type = Decode(parts[3]);
                        stat.Services = Decode(parts[4]);
                        long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out stat.Samples);
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out stat.SumBytes);
                        ulong.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out stat.PeakBytes);
                        if (!string.IsNullOrEmpty(stat.Key) && stat.Key.StartsWith("pws2:", StringComparison.OrdinalIgnoreCase))
                            _processStats[stat.Key] = stat;
                    }
                }

                if (_statDate != DateTime.Today)
                {
                    ArchiveCurrentDay();
                    ResetForNewDay(DateTime.Today);
                }
            }
            catch
            {
                ResetForNewDay(DateTime.Today);
            }
        }

        private void RefreshHistoryGrid()
        {
            _historyGrid.Rows.Clear();
            List<HistoryRow> rows = new List<HistoryRow>();

            if (File.Exists(_historyPath))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(_historyPath, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length < 5) continue;
                        DateTime d;
                        ulong avg, low, peak;
                        long samples;
                        if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                        if (!ulong.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out avg)) continue;
                        ulong.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out low);
                        ulong.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out peak);
                        long.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out samples);
                        rows.Add(new HistoryRow { Date = d, Average = avg, Low = low, Peak = peak, Samples = samples });
                    }
                }
                catch { }
            }

            if (_systemSamples > 0)
            {
                rows.RemoveAll(r => r.Date.Date == _statDate.Date);
                rows.Add(new HistoryRow
                {
                    Date = _statDate,
                    Average = (ulong)Math.Max(0, _systemSumBytes / _systemSamples),
                    Low = _systemMinBytes == ulong.MaxValue ? 0 : _systemMinBytes,
                    Peak = _systemPeakBytes,
                    Samples = _systemSamples
                });
            }

            foreach (HistoryRow row in rows.OrderByDescending(r => r.Date).Take(365))
            {
                _historyGrid.Rows.Add(
                    row.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                    FormatBytes(row.Average),
                    FormatBytes(row.Low),
                    FormatBytes(row.Peak),
                    FormatTracked(row.Samples));
            }
        }

        private static string Encode(string value)
        {
            if (value == null) value = "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }

        private static string FormatTracked(long samples)
        {
            if (samples <= 0) return "--";
            TimeSpan span = TimeSpan.FromSeconds(samples);
            if (span.TotalHours >= 24)
                return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            if (span.TotalHours >= 1)
                return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            return span.Minutes.ToString(CultureInfo.InvariantCulture) + "m " + span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
        }

        private static string FormatBytes(ulong bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            if (bytes >= GB) return (bytes / GB).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= MB) return (bytes / MB).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= KB) return (bytes / KB).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }

    internal sealed class ProcessScanRequest
    {
        public DateTime RequestedUtc;
        public long RequestTimestamp;
        public int ExpectedIntervalMs;
        public double ActualIntervalMs;
        public double TimerDriftMs;
        public bool IsStartup;
        public bool DiagnosticsEnabledAtRequest;
    }

    internal sealed class ProcessScanSnapshot
    {
        public ProcessScanRequest Request;
        public Dictionary<string, ProcessGroupSample> Groups;
        public int WindowsProcessCount;
        public int ServiceCount;
        public int SvchostServiceGroupCount;
        public int UnclassifiedProcessCount;
        public ulong AccountingProcessPrivateBytes;
        public ulong AccountingKernelBytes;
        public ulong AccountingKernelPagedBytes;
        public ulong AccountingKernelNonPagedBytes;
        public ulong AccountingOtherBytes;
        public ulong AccountingLiveBytes;
        public double ProcessEnumerationMs;
        public double ProcessMemoryCollectionMs;
        public double ProcessGroupingMs;
        public double ServiceDiscoveryMs;
        public double MetadataLookupMs;
        public int MetadataCacheHits;
        public int MetadataCacheMisses;
        public int MetadataLookupsAttempted;
        public int MetadataLookupsDeferred;
        public double RamAccountingMs;
        public double BackgroundScanMs;
        public long ScanCompletedTimestamp;
    }

    internal sealed class ProcessGuideForm : Form
    {
        public ProcessGuideForm()
        {
            Text = "RAM Trace V2.0 Process guide";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 680);
            MinimumSize = new Size(560, 420);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { Icon = SystemIcons.Application; }

            TextBox text = new TextBox();
            text.Dock = DockStyle.Fill;
            text.Multiline = true;
            text.ReadOnly = true;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = Color.White;
            text.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            text.ScrollBars = ScrollBars.Vertical;
            text.WordWrap = true;
            text.HideSelection = false;
            text.Text = ProcessGuideContent.Text;
            text.SelectionStart = 0;
            text.SelectionLength = 0;

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(14);
            host.BackColor = Color.White;
            host.Controls.Add(text);
            Controls.Add(host);
        }
    }

    internal sealed class AggregateStat
    {
        public string Key;
        public string Name;
        public string Type;
        public string Services;
        public long Samples;
        public double SumBytes;
        public ulong PeakBytes;
    }

    internal sealed class ProcessGroupSample
    {
        public string Key;
        public string Name;
        public string TechnicalName;
        public string Type;
        public string Services;
        public string Company;
        public string BelongsTo;
        public string Purpose;
        public int InstanceCount;
        public ulong LiveBytes;
        public int AccountingInstanceCount;
        public ulong AccountingPrivateBytes;
    }

    internal sealed class ProcessIdentity
    {
        public string FriendlyName;
        public string Type;
        public string Company;
        public string BelongsTo;
        public string Purpose;
    }

    internal sealed class ExecutableMetadata
    {
        public string Description = "";
        public string Product = "";
        public string Company = "";
    }

    internal sealed class KnownProcessInfo
    {
        public string FriendlyName;
        public string Type;
        public string Company;
        public string BelongsTo;
        public string Purpose;

        public KnownProcessInfo(string friendlyName, string type, string company, string belongsTo, string purpose)
        {
            FriendlyName = friendlyName;
            Type = type;
            Company = company;
            BelongsTo = belongsTo;
            Purpose = purpose;
        }
    }

    internal static class ProcessKnowledge
    {
        private static readonly Dictionary<string, KnownProcessInfo> Items = new Dictionary<string, KnownProcessInfo>(StringComparer.OrdinalIgnoreCase)
        {
            { "vmmem", new KnownProcessInfo("Windows Virtual Machine Memory", "Virtual machine", "Microsoft", "Windows virtualisation / Hyper-V", "Represents physical RAM allocated to Hyper-V based virtual environments such as WSL2, Docker Desktop, Windows Sandbox or Android/Google Play virtualisation. It does not identify one exact app by itself.") },
            { "vmmemWSL", new KnownProcessInfo("Windows Subsystem for Linux Memory", "Virtual machine", "Microsoft", "Windows Subsystem for Linux (WSL2)", "Represents memory being used by the WSL2 virtual machine and Linux workloads running inside it.") },
            { "crosvm", new KnownProcessInfo("Google Play Virtual Machine", "Virtual machine", "Google", "Google Play Games / Android virtualisation", "Virtual-machine monitor used by Google's Android/ChromeOS virtualised environment. Memory can fall sharply when Google Play Games is closed.") },
            { "dwm", new KnownProcessInfo("Desktop Window Manager", "Windows system", "Microsoft", "Windows desktop graphics", "Composes and renders desktop windows, visual effects and GPU-accelerated desktop surfaces.") },
            { "svchost", new KnownProcessInfo("Windows Service Host", "Service", "Microsoft", "Windows services", "Generic Windows host process. One instance can contain one or several services; see the Windows service(s) column for the actual services hosted by this row.") },
            { "RuntimeBroker", new KnownProcessInfo("Runtime Broker", "Windows system", "Microsoft", "Windows app permissions", "Broker used by Windows to manage permissions and background behaviour for Store/UWP and some modern Windows applications.") },
            { "explorer", new KnownProcessInfo("Windows Explorer", "Windows shell", "Microsoft", "Windows desktop / File Explorer", "Provides the Windows desktop shell, taskbar integration and File Explorer windows.") },
            { "SearchIndexer", new KnownProcessInfo("Windows Search Indexer", "Service", "Microsoft", "Windows Search", "Indexes files, email and other content so Windows searches can return results quickly.") },
            { "SearchHost", new KnownProcessInfo("Windows Search", "Windows system", "Microsoft", "Windows Search", "User-interface and background component for Windows Search.") },
            { "SearchApp", new KnownProcessInfo("Windows Search", "Windows system", "Microsoft", "Windows Search", "Windows Search interface/background component used on some Windows versions.") },
            { "StartMenuExperienceHost", new KnownProcessInfo("Windows Start Menu", "Windows system", "Microsoft", "Windows Start menu", "Hosts the Windows Start menu user interface.") },
            { "ShellExperienceHost", new KnownProcessInfo("Windows Shell Experience Host", "Windows system", "Microsoft", "Windows shell", "Hosts parts of the Windows shell and visual user-interface experience.") },
            { "sihost", new KnownProcessInfo("Shell Infrastructure Host", "Windows system", "Microsoft", "Windows shell", "Core Windows shell process responsible for several desktop interface elements.") },
            { "Taskmgr", new KnownProcessInfo("Task Manager", "Application", "Microsoft", "Windows Task Manager", "Windows utility for inspecting and managing processes, performance and services.") },
            { "lsass", new KnownProcessInfo("Local Security Authority", "Windows security", "Microsoft", "Windows security", "Core Windows security process responsible for sign-in validation, security policy and credentials. Do not terminate it.") },
            { "csrss", new KnownProcessInfo("Client Server Runtime Process", "Windows system", "Microsoft", "Windows core", "Essential Windows subsystem process used for core user-session functions. Do not terminate it.") },
            { "smss", new KnownProcessInfo("Windows Session Manager", "Windows system", "Microsoft", "Windows core", "Creates and manages Windows user sessions during startup. Essential system process.") },
            { "wininit", new KnownProcessInfo("Windows Start-up Application", "Windows system", "Microsoft", "Windows core", "Starts important Windows background services and system processes during boot.") },
            { "winlogon", new KnownProcessInfo("Windows Logon Application", "Windows system", "Microsoft", "Windows sign-in", "Handles parts of interactive Windows sign-in, lock and secure attention functions.") },
            { "services", new KnownProcessInfo("Windows Service Control Manager", "Windows system", "Microsoft", "Windows services", "Starts, stops and manages Windows services.") },
            { "Registry", new KnownProcessInfo("Windows Registry", "Windows system", "Microsoft", "Windows registry", "Represents memory used by the Windows registry and its in-memory data structures.") },
            { "Memory Compression", new KnownProcessInfo("Windows Memory Compression", "Windows memory", "Microsoft", "Windows memory manager", "Compressed memory kept in RAM to reduce paging to disk. This is normal and can improve responsiveness under memory pressure.") },
            { "Secure System", new KnownProcessInfo("Windows Secure System", "Windows security", "Microsoft", "Virtualisation-based security", "Protected Windows process used by virtualisation-based security features such as Credential Guard and Memory Integrity.") },
            { "System", new KnownProcessInfo("Windows System", "Windows system", "Microsoft", "Windows kernel / drivers", "Represents kernel and driver activity that is not owned by a normal user application.") },
            { "WmiPrvSE", new KnownProcessInfo("WMI Provider Host", "Windows system", "Microsoft", "Windows Management Instrumentation", "Hosts WMI providers used by Windows and management software to query system information.") },
            { "conhost", new KnownProcessInfo("Console Window Host", "Windows system", "Microsoft", "Windows console", "Hosts console windows for Command Prompt, PowerShell and other command-line applications.") },
            { "dllhost", new KnownProcessInfo("COM Surrogate", "Windows system", "Microsoft", "Windows COM components", "Hosts COM components outside their calling application so a component failure is less likely to crash the parent app.") },
            { "fontdrvhost", new KnownProcessInfo("Usermode Font Driver Host", "Windows system", "Microsoft", "Windows font rendering", "Protected host used by Windows font drivers and font rendering components.") },
            { "audiodg", new KnownProcessInfo("Windows Audio Device Graph Isolation", "Windows audio", "Microsoft", "Windows audio", "Hosts Windows audio processing and effects outside the main audio service.") },
            { "spoolsv", new KnownProcessInfo("Print Spooler", "Service", "Microsoft", "Windows printing", "Queues and manages print jobs for Windows printers.") },
            { "OfficeClickToRun", new KnownProcessInfo("Microsoft Office Click-to-Run", "Service", "Microsoft", "Microsoft 365 / Office", "Manages Microsoft 365 and Office streaming, updates, repair and installation services.") },
            { "OneDrive", new KnownProcessInfo("Microsoft OneDrive", "Application", "Microsoft", "Microsoft OneDrive", "Synchronises files between this PC and Microsoft OneDrive cloud storage.") },
            { "brave", new KnownProcessInfo("Brave Browser", "Application", "Brave Software", "Brave Browser", "Web browser. Multiple processes are normal because tabs, extensions, renderers and GPU work are isolated into separate processes.") },
            { "msedgewebview2", new KnownProcessInfo("Microsoft Edge WebView2", "Application component", "Microsoft", "Microsoft Edge WebView2 Runtime", "Embedded web-rendering engine used inside many Windows applications. Several processes are normal because applications isolate renderers, GPU and utility work.") },
            { "Microsoft.CmdPal.UI", new KnownProcessInfo("Microsoft Command Palette", "Application", "Microsoft", "Microsoft PowerToys / Command Palette", "Command Palette user interface used to quickly search for and launch commands, applications and actions.") },
            { "appmodel", new KnownProcessInfo("Windows App Model", "Windows system", "Microsoft", "Windows application infrastructure", "Windows component involved in application model, package and modern app infrastructure.") },
            { "ublockdns", new KnownProcessInfo("uBlock DNS", "Service", "", "Local DNS filtering", "Local/background DNS filtering service. The exact filtering rules and provider depend on the installed uBlock DNS configuration.") },
            { "Dropbox", new KnownProcessInfo("Dropbox", "Application", "Dropbox", "Dropbox", "Synchronises files between this PC and Dropbox cloud storage.") },
            { "ShareX", new KnownProcessInfo("ShareX", "Application", "ShareX Team", "ShareX", "Screenshot, screen-capture and productivity utility.") },
            { "mc-fw-host", new KnownProcessInfo("McAfee Framework Host", "Service", "McAfee", "McAfee security", "McAfee framework/security service used by the installed McAfee protection suite.") },
            { "mc-web-view", new KnownProcessInfo("McAfee Web View", "Application component", "McAfee", "McAfee security", "User-interface or web-rendering component used by McAfee security software.") },
            { "mc-neo-host", new KnownProcessInfo("McAfee Neo Host", "Application component", "McAfee", "McAfee security", "Background component belonging to the installed McAfee security suite.") },
            { "PowerToys.Run", new KnownProcessInfo("PowerToys Run", "Application", "Microsoft", "Microsoft PowerToys", "Quick launcher and search component of Microsoft PowerToys.") },
            { "PowerToys.MouseWithoutBorders", new KnownProcessInfo("PowerToys Mouse Without Borders", "Application", "Microsoft", "Microsoft PowerToys", "Lets one keyboard and mouse control multiple Windows PCs.") },
            { "PowerToys.ColorPickerUI", new KnownProcessInfo("PowerToys Color Picker", "Application", "Microsoft", "Microsoft PowerToys", "Colour picker component of Microsoft PowerToys.") },
            { "PowerToys.Awake", new KnownProcessInfo("PowerToys Awake", "Application", "Microsoft", "Microsoft PowerToys", "Keeps the PC awake without changing the normal Windows power-plan settings.") },
            { "PowerToys.QuickAccess", new KnownProcessInfo("PowerToys Quick Access", "Application", "Microsoft", "Microsoft PowerToys", "PowerToys background/user-interface component used for quick access to PowerToys features.") },
            { "SecurityHealthSystray", new KnownProcessInfo("Windows Security notification icon", "Windows security", "Microsoft", "Windows Security", "Displays Windows Security status and notifications in the system tray.") },
            { "SecurityHealthService", new KnownProcessInfo("Windows Security Service", "Service", "Microsoft", "Windows Security", "Background Windows Security health and status service.") },
            { "smartscreen", new KnownProcessInfo("Windows Defender SmartScreen", "Windows security", "Microsoft", "Windows security", "Checks downloaded files, applications and web content against reputation and safety information.") },
            { "RAM-Trace", new KnownProcessInfo("RAM Trace", "Application", "Tajud Din", "RAM Trace", "This lightweight Windows memory monitoring application.") },
            { "RamLight", new KnownProcessInfo("RamLight", "Application", "Local legacy build", "RamLight", "Legacy pre-rebrand build of this RAM monitoring application.") }
        };

        public static bool TryGet(string processName, out KnownProcessInfo info)
        {
            return Items.TryGetValue(processName, out info);
        }
    }

    internal sealed class ProcessGridRowComparer : System.Collections.IComparer
    {
        private readonly Dictionary<string, ProcessGroupSample> _groups;

        public ProcessGridRowComparer(Dictionary<string, ProcessGroupSample> groups)
        {
            _groups = groups ?? new Dictionary<string, ProcessGroupSample>(StringComparer.OrdinalIgnoreCase);
        }

        public int Compare(object x, object y)
        {
            DataGridViewRow a = x as DataGridViewRow;
            DataGridViewRow b = y as DataGridViewRow;
            if (a == null || b == null) return 0;

            string keyA = Convert.ToString(a.Tag, CultureInfo.InvariantCulture);
            string keyB = Convert.ToString(b.Tag, CultureInfo.InvariantCulture);
            ProcessGroupSample groupA = null;
            ProcessGroupSample groupB = null;
            bool hasA = !string.IsNullOrEmpty(keyA) && _groups.TryGetValue(keyA, out groupA);
            bool hasB = !string.IsNullOrEmpty(keyB) && _groups.TryGetValue(keyB, out groupB);
            if (hasA && hasB)
            {
                int byRam = groupB.LiveBytes.CompareTo(groupA.LiveBytes);
                if (byRam != 0) return byRam;
                int byName = string.Compare(groupA.Name, groupB.Name, StringComparison.OrdinalIgnoreCase);
                if (byName != 0) return byName;
            }
            else if (hasA) return -1;
            else if (hasB) return 1;

            return string.Compare(keyA, keyB, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class DoubleBufferedDataGridView : DataGridView
    {
        public DoubleBufferedDataGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    internal sealed class HistoryRow
    {
        public DateTime Date;
        public ulong Average;
        public ulong Low;
        public ulong Peak;
        public long Samples;
    }

    internal sealed class MemorySnapshot
    {
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong SystemCache;
        public ulong KernelTotal;
        public ulong KernelPaged;
        public ulong KernelNonPaged;
    }

    internal static class NativeMemory
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, ref PROCESS_MEMORY_COUNTERS_EX2 ppsmemCounters, uint cb);

        public static bool TryReadPrivateWorkingSet(Process process, out ulong bytes)
        {
            bytes = 0;
            if (process == null) return false;

            IntPtr processHandle = IntPtr.Zero;
            try
            {
                processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                if (processHandle == IntPtr.Zero) return false;

                PROCESS_MEMORY_COUNTERS_EX2 counters = new PROCESS_MEMORY_COUNTERS_EX2();
                counters.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX2));
                if (!GetProcessMemoryInfo(processHandle, ref counters, counters.cb)) return false;

                bytes = counters.PrivateWorkingSetSize.ToUInt64();
                return true;
            }
            catch { return false; }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    try { CloseHandle(processHandle); }
                    catch { }
                }
            }
        }

        public static ulong ReadPrivateWorkingSet(Process process)
        {
            ulong bytes;
            if (TryReadPrivateWorkingSet(process, out bytes)) return bytes;
            return ReadFallbackProcessMemory(process);
        }

        public static ulong ReadFallbackProcessMemory(Process process)
        {
            if (process == null) return 0;

            // Fallback values are useful for the process table, but they are not used in
            // the system RAM accounting total because they are not guaranteed to be a
            // private resident working-set measurement.
            try
            {
                process.Refresh();
                long privateBytes = process.PrivateMemorySize64;
                if (privateBytes > 0) return (ulong)privateBytes;
            }
            catch { }

            try
            {
                long workingSet = process.WorkingSet64;
                return workingSet > 0 ? (ulong)workingSet : 0UL;
            }
            catch { return 0; }
        }

        public static MemorySnapshot Read()
        {
            MemorySnapshot result = new MemorySnapshot();
            MEMORYSTATUSEX status = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(status))
            {
                result.TotalPhysical = status.ullTotalPhys;
                result.AvailablePhysical = status.ullAvailPhys;
            }

            PERFORMANCE_INFORMATION pi = new PERFORMANCE_INFORMATION();
            pi.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            if (GetPerformanceInfo(out pi, pi.cb))
            {
                ulong pageSize = pi.PageSize.ToUInt64();
                result.SystemCache = pi.SystemCache.ToUInt64() * pageSize;
                result.KernelTotal = pi.KernelTotal.ToUInt64() * pageSize;
                result.KernelPaged = pi.KernelPaged.ToUInt64() * pageSize;
                result.KernelNonPaged = pi.KernelNonpaged.ToUInt64() * pageSize;
            }
            return result;
        }
    }

    internal static class ServiceEnumerator
    {
        private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
        private const int SC_ENUM_PROCESS_INFO = 0;
        private const uint SERVICE_WIN32 = 0x00000030;
        private const uint SERVICE_STATE_ALL = 0x00000003;
        private const int ERROR_MORE_DATA = 234;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ENUM_SERVICE_STATUS_PROCESS
        {
            public IntPtr lpServiceName;
            public IntPtr lpDisplayName;
            public SERVICE_STATUS_PROCESS ServiceStatusProcess;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumServicesStatusEx(
            IntPtr hSCManager,
            int infoLevel,
            uint dwServiceType,
            uint dwServiceState,
            IntPtr lpServices,
            uint cbBufSize,
            out uint pcbBytesNeeded,
            out uint lpServicesReturned,
            ref uint lpResumeHandle,
            string pszGroupName);

        [DllImport("advapi32.dll")]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        public static Dictionary<int, List<string>> GetServicesByProcess()
        {
            Dictionary<int, List<string>> result = new Dictionary<int, List<string>>();
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ENUMERATE_SERVICE);
            if (scm == IntPtr.Zero) return result;

            IntPtr buffer = IntPtr.Zero;
            try
            {
                uint bytesNeeded = 0;
                uint servicesReturned = 0;
                uint resume = 0;
                EnumServicesStatusEx(scm, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
                    IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resume, null);
                int error = Marshal.GetLastWin32Error();
                if (bytesNeeded == 0 && error != ERROR_MORE_DATA) return result;

                buffer = Marshal.AllocHGlobal((int)bytesNeeded);
                resume = 0;
                bool ok = EnumServicesStatusEx(scm, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
                    buffer, bytesNeeded, out bytesNeeded, out servicesReturned, ref resume, null);
                if (!ok) return result;

                int structSize = Marshal.SizeOf(typeof(ENUM_SERVICE_STATUS_PROCESS));
                long current = buffer.ToInt64();
                for (uint i = 0; i < servicesReturned; i++)
                {
                    IntPtr itemPtr = new IntPtr(current + (long)i * structSize);
                    ENUM_SERVICE_STATUS_PROCESS item = (ENUM_SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(itemPtr, typeof(ENUM_SERVICE_STATUS_PROCESS));
                    int pid = unchecked((int)item.ServiceStatusProcess.dwProcessId);
                    if (pid <= 0) continue;
                    string displayName = Marshal.PtrToStringUni(item.lpDisplayName);
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = Marshal.PtrToStringUni(item.lpServiceName);
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    List<string> list;
                    if (!result.TryGetValue(pid, out list))
                    {
                        list = new List<string>();
                        result[pid] = list;
                    }
                    list.Add(displayName);
                }
            }
            catch { }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                CloseServiceHandle(scm);
            }
            return result;
        }
    }
}

