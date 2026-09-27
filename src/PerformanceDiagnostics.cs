using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RamLight
{
    internal sealed class DiagnosticCycleSample
    {
        public DateTime TimestampUtc;
        public double ApplicationUptimeSeconds;
        public int ExpectedSamplingIntervalMs;
        public double ActualSamplingIntervalMs;
        public double TimerDriftMs;
        public double MainRefreshCycleMs;
        public double BackgroundProcessScanMs;
        public double UiMarshalDelayMs;
        public double SnapshotUiApplyMs;
        public double TotalRamSamplingMs;
        public double ProcessEnumerationMs;
        public double ProcessMemoryCollectionMs;
        public double ProcessGroupingMs;
        public double ServiceMappingMs;
        public double ServiceDiscoveryMs;
        public double MetadataLookupMs;
        public int MetadataCacheHits;
        public int MetadataCacheMisses;
        public double RamAccountingMs;
        public double HistoricalStatsUpdateMs;
        public double GridDataPreparationMs;
        public double GridUiRefreshMs;
        public double ProcessInformationPanelMs;
        public double FooterStatusUpdateMs;
        public double LocalDataHistoryWriteMs;
        public double DiagnosticLoggingWriteMs;
        public double TotalCompleteCycleMs;
        public int WindowsProcessCount;
        public int GroupedRowCount;
        public int ServiceCount;
        public int SvchostServiceGroupCount;
        public int MetadataLookupsAttempted;
        public int MetadataLookupsDeferred;
        public long WorkingSetBytes;
        public long PrivateBytes;
        public long ManagedHeapBytes;
        public double CpuPercent;
        public double ProcessCpuTimeMs;
        public int ThreadCount;
        public int HandleCount;
        public int GcGen0;
        public int GcGen1;
        public int GcGen2;
        public bool CpuThrottled;
        public ulong LiveSystemRamBytes;
        public ulong ProcessPrivateRamBytes;
        public ulong KernelPoolRamBytes;
        public ulong SystemSharedRamBytes;
        public double MemoryBreakdownCalculationMs;
        public double WindowsSystemPrivateMB;
        public double MicrosoftAppsPrivateMB;
        public double ThirdPartyPrivateMB;
        public double UnclassifiedPrivateMB;
        public int WindowsSystemGroups;
        public int MicrosoftAppGroups;
        public int ThirdPartyGroups;
        public int UnclassifiedGroups;
        public int MemoryBreakdownMismatchCount;
        public int ProcessBreakdownMismatchCount;
        public int TotalLiveBreakdownMismatchCount;
        public string SelectedProcess;
        public string WindowState;
        public int ProcessGridRowCount;
        public int SkippedProcessScans;
        public int GridRowsAdded;
        public int GridRowsUpdated;
        public int GridRowsRemoved;
        public int FullGridRebuildCount;
        public int GridStructuralChanges;
        public int GridValueOnlyUpdates;
        public double GridSortDurationMs;
        public int GridInteractionDeferredRefreshCount;
        public int GridSnapshotsCoalesced;
        public int GridDuplicateKeyCount;
        public int DiagnosticQueueSize;
        public double DiagnosticGenerationMs;
        public double DiagnosticFormattingMs;
    }

    internal static class StartupPerformance
    {
        private static readonly object Sync = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<StartupMarker> Markers = new List<StartupMarker>();

        static StartupPerformance()
        {
            Add("Process start");
        }

        public static void Mark(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Add(name.Trim());
        }

        public static List<StartupMarker> Snapshot()
        {
            lock (Sync)
            {
                return Markers.Select(x => new StartupMarker { Name = x.Name, ElapsedMs = x.ElapsedMs }).ToList();
            }
        }

        private static void Add(string name)
        {
            lock (Sync)
            {
                if (Markers.Count >= 32) return;
                Markers.Add(new StartupMarker { Name = name, ElapsedMs = Clock.Elapsed.TotalMilliseconds });
            }
        }
    }

    internal sealed class StartupMarker
    {
        public string Name;
        public double ElapsedMs;
    }

    internal sealed class PerformanceDiagnostics : IDisposable
    {
        private const int MaxRetainedSessions = 10;
        private const long MaxSessionBytes = 30L * 1024L * 1024L;
        private const int FlushIntervalMs = 5000;
        private const int FlushThresholdChars = 65536;

        private readonly object _sync = new object();
        private readonly Dictionary<string, TimingSeries> _timings = new Dictionary<string, TimingSeries>(StringComparer.OrdinalIgnoreCase);
        private readonly List<double> _workingSetMb = new List<double>();
        private readonly List<double> _privateMb = new List<double>();
        private readonly List<double> _cpuPercent = new List<double>();
        private readonly List<double> _threadCounts = new List<double>();
        private readonly List<double> _handleCounts = new List<double>();
        private readonly StringBuilder _csvBuffer = new StringBuilder(131072);
        private readonly StringBuilder _eventBuffer = new StringBuilder(16384);
        private readonly StringBuilder _classificationAuditBuffer = new StringBuilder(65536);

        private BlockingCollection<WriteBatch> _writeQueue;
        private Thread _writerThread;
        private Stopwatch _sessionClock;
        private DateTime _sessionStartUtc;
        private DateTime _lastFlushUtc;
        private string _sessionDirectory;
        private string _performancePath;
        private string _eventsPath;
        private string _classificationAuditPath;
        private string _summaryPath;
        private long _bytesWritten;
        private long _recordsWritten;
        private long _recordsGenerated;
        private long _bufferedRecordCount;
        private readonly List<WriteBatch> _failedBatches = new List<WriteBatch>();
        private long _lastWriteTicks;
        private long _formatTicks;
        private double _lastFormatMs;
        private long _generationTicks;
        private long _warningCount;
        private long _exceptionCount;
        private long _flushCount;
        private int _gc0Start;
        private int _gc1Start;
        private int _gc2Start;
        private int _throttleEvents;
        private bool _throttled;
        private DateTime _throttleStartedUtc;
        private TimeSpan _totalThrottled;
        private long _uiOver50;
        private long _uiOver100;
        private long _uiOver250;
        private long _uiOver500;
        private long _uiOver1000;
        private long _timerDriftEvents;
        private long _skippedProcessScans;
        private long _gridRowsAdded;
        private long _gridRowsUpdated;
        private long _gridRowsRemoved;
        private long _fullGridRebuilds;
        private long _gridStructuralChanges;
        private long _gridValueOnlyUpdates;
        private long _gridInteractionDeferredRefreshes;
        private long _gridSnapshotsCoalesced;
        private long _gridDuplicateKeys;
        private long _memoryBreakdownMismatchCount;
        private long _processBreakdownMismatchCount;
        private long _totalLiveBreakdownMismatchCount;
        private double _maxUiDelayMs;
        private volatile bool _sizeLimitReached;
        private bool _enabled;
        private bool _disposed;

        public bool IsEnabled { get { return _enabled; } }
        public string SessionDirectory { get { return _sessionDirectory ?? string.Empty; } }
        public int QueueSize { get { return _writeQueue == null ? 0 : _writeQueue.Count; } }
        public long RecordsWritten { get { return Interlocked.Read(ref _recordsWritten); } }
        public double LastWriteMilliseconds { get { return TicksToMilliseconds(Interlocked.Read(ref _lastWriteTicks)); } }

        public string StartSession(string dataDirectory)
        {
            if (_disposed) throw new ObjectDisposedException("PerformanceDiagnostics");
            if (_enabled) return _sessionDirectory;

            string diagnosticsRoot = Path.Combine(dataDirectory, "Diagnostics");
            Directory.CreateDirectory(diagnosticsRoot);
            CleanupOldSessions(diagnosticsRoot);

            string folderName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string candidate = Path.Combine(diagnosticsRoot, folderName);
            int suffix = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(diagnosticsRoot, folderName + "_" + suffix.ToString("00", CultureInfo.InvariantCulture));
                suffix++;
            }

            Directory.CreateDirectory(candidate);
            _sessionDirectory = candidate;
            _performancePath = Path.Combine(candidate, "Performance.csv");
            _eventsPath = Path.Combine(candidate, "Events.log");
            _classificationAuditPath = Path.Combine(candidate, "ClassificationAudit.csv");
            _summaryPath = Path.Combine(candidate, "Summary.txt");
            _sessionStartUtc = DateTime.UtcNow;
            _sessionClock = Stopwatch.StartNew();
            _lastFlushUtc = _sessionStartUtc;
            _bytesWritten = 0;
            _recordsWritten = 0;
            _recordsGenerated = 0;
            _bufferedRecordCount = 0;
            _lastWriteTicks = 0;
            _formatTicks = 0;
            _lastFormatMs = 0;
            _generationTicks = 0;
            _warningCount = 0;
            _exceptionCount = 0;
            _flushCount = 0;
            _throttleEvents = 0;
            _throttled = false;
            _totalThrottled = TimeSpan.Zero;
            _uiOver50 = _uiOver100 = _uiOver250 = _uiOver500 = _uiOver1000 = 0;
            _timerDriftEvents = 0;
            _skippedProcessScans = 0;
            _gridRowsAdded = _gridRowsUpdated = _gridRowsRemoved = _fullGridRebuilds = 0;
            _gridStructuralChanges = _gridValueOnlyUpdates = _gridInteractionDeferredRefreshes = _gridSnapshotsCoalesced = _gridDuplicateKeys = 0;
            _memoryBreakdownMismatchCount = 0;
            _processBreakdownMismatchCount = 0;
            _totalLiveBreakdownMismatchCount = 0;
            _maxUiDelayMs = 0;
            _sizeLimitReached = false;
            _gc0Start = GC.CollectionCount(0);
            _gc1Start = GC.CollectionCount(1);
            _gc2Start = GC.CollectionCount(2);

            lock (_sync)
            {
                _timings.Clear();
                _workingSetMb.Clear();
                _privateMb.Clear();
                _cpuPercent.Clear();
                _failedBatches.Clear();
                _threadCounts.Clear();
                _handleCounts.Clear();
                _csvBuffer.Clear();
                _eventBuffer.Clear();
                _classificationAuditBuffer.Clear();
                _csvBuffer.AppendLine(GetCsvHeader());
                _classificationAuditBuffer.AppendLine(GetClassificationAuditHeader());
            }

            _writeQueue = new BlockingCollection<WriteBatch>(64);
            _writerThread = new Thread(WriterLoop);
            _writerThread.IsBackground = true;
            _writerThread.Name = "RAM Trace diagnostics writer";
            _enabled = true;
            _writerThread.Start();

            LogEvent("Diagnostics enabled", "New diagnostic session started.", 0);
            foreach (StartupMarker marker in StartupPerformance.Snapshot())
                LogEvent("Startup", marker.Name, marker.ElapsedMs);
            TryQueueFlush(true);
            return _sessionDirectory;
        }

        public void StopSession(string reason)
        {
            if (!_enabled) return;
            if (string.IsNullOrWhiteSpace(reason)) reason = "Diagnostics stopped";

            UpdateThrottleState(false);
            LogEvent("Diagnostics disabled", reason, 0);
            TryQueueFlush(true);

            _enabled = false;
            BlockingCollection<WriteBatch> queue = _writeQueue;
            if (queue != null)
            {
                queue.CompleteAdding();
                Thread writer = _writerThread;
                if (writer != null && writer.IsAlive) writer.Join(10000);
            }

            // Recover anything the asynchronous writer could not finish. This also
            // drains any last in-memory buffer so Summary.txt is written only after
            // every final Performance.csv record has had a synchronous write attempt.
            FlushRemainingSynchronously();
            ReconcilePerformanceRecordCount();
            WriteSummary(reason);
            _writeQueue = null;
            _writerThread = null;
        }

        public void RecordCycle(DiagnosticCycleSample sample)
        {
            if (!_enabled || sample == null || _sizeLimitReached) return;
            Stopwatch generation = Stopwatch.StartNew();

            sample.DiagnosticLoggingWriteMs = LastWriteMilliseconds;
            sample.DiagnosticQueueSize = QueueSize;
            sample.DiagnosticFormattingMs = _lastFormatMs;

            AddTiming("Complete refresh-cycle duration", sample.TotalCompleteCycleMs);
            AddTiming("Process scan duration", sample.MainRefreshCycleMs);
            AddTiming("Background process scan duration", sample.BackgroundProcessScanMs);
            AddTiming("Snapshot-to-UI transfer delay", sample.UiMarshalDelayMs);
            AddTiming("Snapshot/UI apply duration", sample.SnapshotUiApplyMs);
            AddTiming("Timer drift duration", sample.TimerDriftMs);
            AddTiming("Service scan duration", sample.ServiceDiscoveryMs);
            AddTiming("UI refresh duration", sample.GridUiRefreshMs);
            AddTiming("Metadata lookup duration", sample.MetadataLookupMs);
            AddTiming("RAM accounting duration", sample.RamAccountingMs);
            AddTiming("History/data write duration", sample.LocalDataHistoryWriteMs);
            AddTiming("Diagnostic logging overhead", sample.DiagnosticLoggingWriteMs);
            AddTiming("Process enumeration duration", sample.ProcessEnumerationMs);
            AddTiming("Process memory collection duration", sample.ProcessMemoryCollectionMs);
            AddTiming("Process grouping duration", sample.ProcessGroupingMs);
            AddTiming("Grid data preparation duration", sample.GridDataPreparationMs);
            AddTiming("Grid sort duration", sample.GridSortDurationMs);
            AddTiming("Process Information panel duration", sample.ProcessInformationPanelMs);
            AddTiming("Footer/status duration", sample.FooterStatusUpdateMs);
            AddTiming("Total RAM sampling duration", sample.TotalRamSamplingMs);
            AddTiming("Memory breakdown calculation duration", sample.MemoryBreakdownCalculationMs);

            lock (_sync)
            {
                AddBounded(_workingSetMb, sample.WorkingSetBytes / 1048576.0);
                AddBounded(_privateMb, sample.PrivateBytes / 1048576.0);
                AddBounded(_cpuPercent, sample.CpuPercent);
                _skippedProcessScans += sample.SkippedProcessScans;
                _gridRowsAdded += sample.GridRowsAdded;
                _gridRowsUpdated += sample.GridRowsUpdated;
                _gridRowsRemoved += sample.GridRowsRemoved;
                _fullGridRebuilds += sample.FullGridRebuildCount;
                _gridStructuralChanges += sample.GridStructuralChanges;
                _gridValueOnlyUpdates += sample.GridValueOnlyUpdates;
                _gridInteractionDeferredRefreshes += sample.GridInteractionDeferredRefreshCount;
                _gridSnapshotsCoalesced += sample.GridSnapshotsCoalesced;
                _gridDuplicateKeys += sample.GridDuplicateKeyCount;
                _memoryBreakdownMismatchCount += sample.MemoryBreakdownMismatchCount;
                _processBreakdownMismatchCount += sample.ProcessBreakdownMismatchCount;
                _totalLiveBreakdownMismatchCount += sample.TotalLiveBreakdownMismatchCount;
                AddBounded(_threadCounts, sample.ThreadCount);
                AddBounded(_handleCounts, sample.HandleCount);
            }

            if (sample.TimerDriftMs > 250.0)
            {
                Interlocked.Increment(ref _timerDriftEvents);
                LogWarning("Timer delay beyond threshold", "Process timer drift was " + F(sample.TimerDriftMs) + " ms.", sample.TimerDriftMs);
            }
            if (sample.MainRefreshCycleMs > 250.0)
                LogWarning("Process scan exceeded expected duration", "Core process scan was " + F(sample.MainRefreshCycleMs) + " ms.", sample.MainRefreshCycleMs);
            if (sample.TotalCompleteCycleMs > 500.0)
                LogWarning("Complete refresh cycle exceeded threshold", "Complete process refresh cycle was " + F(sample.TotalCompleteCycleMs) + " ms.", sample.TotalCompleteCycleMs);
            if (sample.ServiceDiscoveryMs > 500.0)
                LogWarning("Service refresh exceeded expected duration", "Service discovery was " + F(sample.ServiceDiscoveryMs) + " ms.", sample.ServiceDiscoveryMs);
            if (sample.GridUiRefreshMs > 100.0)
                LogWarning("UI refresh exceeded expected duration", "Grid/UI refresh was " + F(sample.GridUiRefreshMs) + " ms.", sample.GridUiRefreshMs);
            if (sample.MetadataLookupMs > 100.0)
                LogWarning("Metadata lookup unusually slow", "Metadata lookup total was " + F(sample.MetadataLookupMs) + " ms.", sample.MetadataLookupMs);

            sample.DiagnosticGenerationMs = generation.Elapsed.TotalMilliseconds;
            Stopwatch format = Stopwatch.StartNew();
            string line = FormatCsv(sample);
            format.Stop();
            _lastFormatMs = format.Elapsed.TotalMilliseconds;
            Interlocked.Add(ref _formatTicks, format.ElapsedTicks);

            lock (_sync)
            {
                _csvBuffer.AppendLine(line);
                _bufferedRecordCount++;
                _recordsGenerated++;
            }

            generation.Stop();
            Interlocked.Add(ref _generationTicks, generation.ElapsedTicks);
            AddTiming("Diagnostic metric generation", sample.DiagnosticGenerationMs);
            AddTiming("Diagnostic record formatting", sample.DiagnosticFormattingMs);

            TryQueueFlush(false);
        }

        public void RecordClassificationAudit(IEnumerable<ClassificationAuditRecord> records)
        {
            if (!_enabled || records == null || _sizeLimitReached) return;
            StringBuilder batch = new StringBuilder();
            foreach (ClassificationAuditRecord record in records)
            {
                if (record == null) continue;
                batch.Append(Csv(record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))); batch.Append(',');
                batch.Append(Csv(record.StableGroupKey)); batch.Append(',');
                batch.Append(Csv(record.FamiliarName)); batch.Append(',');
                batch.Append(Csv(record.TechnicalName)); batch.Append(',');
                batch.Append(Csv(record.Type)); batch.Append(',');
                batch.Append(Csv(record.Publisher)); batch.Append(',');
                batch.Append(Csv(record.ProductName)); batch.Append(',');
                batch.Append((record.DisplayPrivateBytes / 1048576.0).ToString("0.000", CultureInfo.InvariantCulture)); batch.Append(',');
                batch.Append((record.AccountingPrivateBytes / 1048576.0).ToString("0.000", CultureInfo.InvariantCulture)); batch.Append(',');
                batch.Append(record.Instances.ToString(CultureInfo.InvariantCulture)); batch.Append(',');
                batch.Append(record.AccountingInstances.ToString(CultureInfo.InvariantCulture)); batch.Append(',');
                batch.Append(Csv(ProcessClassifier.CategoryDisplayName(record.AssignedCategory))); batch.Append(',');
                batch.Append(Csv(record.ClassificationRule)); batch.Append(',');
                batch.Append(Csv(record.Evidence));
                batch.AppendLine();
            }
            if (batch.Length == 0) return;
            lock (_sync) _classificationAuditBuffer.Append(batch.ToString());
            TryQueueFlush(false);
        }

        public void RecordUiHeartbeat(double expectedMs, double actualMs, double delayMs)
        {
            if (!_enabled) return;
            if (delayMs < 0) delayMs = 0;
            AddTiming("UI heartbeat/scheduling delay", delayMs);
            lock (_sync)
            {
                if (delayMs > _maxUiDelayMs) _maxUiDelayMs = delayMs;
                if (delayMs > 50) _uiOver50++;
                if (delayMs > 100) _uiOver100++;
                if (delayMs > 250) _uiOver250++;
                if (delayMs > 500) _uiOver500++;
                if (delayMs > 1000) _uiOver1000++;
            }
            if (delayMs > 250.0)
                LogWarning("UI heartbeat delayed", "Expected " + F(expectedMs) + " ms, actual " + F(actualMs) + " ms, scheduling delay " + F(delayMs) + " ms.", delayMs);
        }

        public void UpdateThrottleState(bool throttled)
        {
            if (!_enabled) return;
            if (throttled == _throttled) return;

            if (throttled)
            {
                _throttleEvents++;
                _throttleStartedUtc = DateTime.UtcNow;
                LogEvent("CPU throttling activated", "RAM Trace process sampling interval is currently being throttled by the existing resource-budget logic.", 0);
            }
            else
            {
                if (_throttled && _throttleStartedUtc != DateTime.MinValue)
                    _totalThrottled += DateTime.UtcNow - _throttleStartedUtc;
                _throttleStartedUtc = DateTime.MinValue;
                LogEvent("CPU throttling cleared", "RAM Trace returned to its normal process sampling interval.", 0);
            }
            _throttled = throttled;
        }

        public void LogEvent(string eventName, string message, double elapsedMs)
        {
            if (!_enabled) return;
            AppendEvent("INFO", eventName, message, elapsedMs);
        }

        public void LogWarning(string eventName, string message, double elapsedMs)
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _warningCount);
            AppendEvent("WARN", eventName, message, elapsedMs);
        }

        public void LogException(string eventName, Exception ex)
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _exceptionCount);
            string message = ex == null ? "Unknown exception" : ex.GetType().Name + ": " + ex.Message;
            AppendEvent("ERROR", eventName, message, 0);
        }

        public void FlushIfDue()
        {
            if (!_enabled) return;
            TryQueueFlush(false);
        }

        private void AppendEvent(string level, string eventName, string message, double elapsedMs)
        {
            if (_sizeLimitReached) return;
            string safe = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + " [" + level + "] " + (eventName ?? "Event")
                + (elapsedMs > 0 ? " | " + F(elapsedMs) + " ms" : string.Empty)
                + (safe.Length > 0 ? " | " + safe : string.Empty);
            lock (_sync)
            {
                _eventBuffer.AppendLine(line);
            }
            TryQueueFlush(false);
        }

        private void TryQueueFlush(bool force)
        {
            if (!_enabled && !force) return;
            if (_writeQueue == null || _writeQueue.IsAddingCompleted) return;

            string csv = null;
            string events = null;
            string audit = null;
            long recordCount = 0;
            bool due = force || (DateTime.UtcNow - _lastFlushUtc).TotalMilliseconds >= FlushIntervalMs;
            lock (_sync)
            {
                if (!due && (_csvBuffer.Length + _eventBuffer.Length + _classificationAuditBuffer.Length) < FlushThresholdChars) return;
                if (_csvBuffer.Length == 0 && _eventBuffer.Length == 0 && _classificationAuditBuffer.Length == 0) return;
                csv = _csvBuffer.ToString();
                events = _eventBuffer.ToString();
                audit = _classificationAuditBuffer.ToString();
                recordCount = _bufferedRecordCount;
                _csvBuffer.Clear();
                _eventBuffer.Clear();
                _classificationAuditBuffer.Clear();
                _bufferedRecordCount = 0;
                _lastFlushUtc = DateTime.UtcNow;
            }

            WriteBatch batch = new WriteBatch { Csv = csv, Events = events, Audit = audit, RecordCount = recordCount };
            if (!_writeQueue.TryAdd(batch))
            {
                lock (_sync)
                {
                    if (!string.IsNullOrEmpty(csv)) _csvBuffer.Insert(0, csv);
                    if (!string.IsNullOrEmpty(events)) _eventBuffer.Insert(0, events);
                    if (!string.IsNullOrEmpty(audit)) _classificationAuditBuffer.Insert(0, audit);
                    _bufferedRecordCount += recordCount;
                }
            }
        }

        private void WriterLoop()
        {
            try
            {
                foreach (WriteBatch batch in _writeQueue.GetConsumingEnumerable())
                {
                    if (!WriteBatchToDisk(batch, 3))
                    {
                        lock (_sync) _failedBatches.Add(batch);
                    }
                }
            }
            catch
            {
                // The final synchronous drain in StopSession retries any batches that
                // remain queued. Diagnostics must never destabilise RAM Trace.
            }
        }

        private bool WriteBatchToDisk(WriteBatch batch, int attempts)
        {
            if (batch == null) return true;
            if (attempts < 1) attempts = 1;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                long bytes = 0;
                try
                {
                    if (!batch.CsvWritten && !string.IsNullOrEmpty(batch.Csv))
                    {
                        File.AppendAllText(_performancePath, batch.Csv, new UTF8Encoding(false));
                        batch.CsvWritten = true;
                        bytes += Encoding.UTF8.GetByteCount(batch.Csv);
                        if (batch.RecordCount > 0) Interlocked.Add(ref _recordsWritten, batch.RecordCount);
                    }
                    if (!batch.EventsWritten && !string.IsNullOrEmpty(batch.Events))
                    {
                        File.AppendAllText(_eventsPath, batch.Events, new UTF8Encoding(false));
                        batch.EventsWritten = true;
                        bytes += Encoding.UTF8.GetByteCount(batch.Events);
                    }
                    if (!batch.AuditWritten && !string.IsNullOrEmpty(batch.Audit))
                    {
                        File.AppendAllText(_classificationAuditPath, batch.Audit, new UTF8Encoding(false));
                        batch.AuditWritten = true;
                        bytes += Encoding.UTF8.GetByteCount(batch.Audit);
                    }
                }
                catch
                {
                    // Retry the unwritten side of the batch. Already-written CSV is
                    // flagged so it cannot be duplicated when only Events.log failed.
                }
                finally
                {
                    sw.Stop();
                    Interlocked.Exchange(ref _lastWriteTicks, sw.ElapsedTicks);
                    AddTiming("Diagnostic logging overhead", sw.Elapsed.TotalMilliseconds);
                    if (bytes > 0)
                    {
                        Interlocked.Increment(ref _flushCount);
                        long total = Interlocked.Add(ref _bytesWritten, bytes);
                        if (total >= MaxSessionBytes) _sizeLimitReached = true;
                    }
                }

                bool csvDone = batch.CsvWritten || string.IsNullOrEmpty(batch.Csv);
                bool eventsDone = batch.EventsWritten || string.IsNullOrEmpty(batch.Events);
                bool auditDone = batch.AuditWritten || string.IsNullOrEmpty(batch.Audit);
                if (csvDone && eventsDone && auditDone) return true;
                Thread.Sleep(50 * (attempt + 1));
            }
            return (batch.CsvWritten || string.IsNullOrEmpty(batch.Csv))
                && (batch.EventsWritten || string.IsNullOrEmpty(batch.Events))
                && (batch.AuditWritten || string.IsNullOrEmpty(batch.Audit));
        }

        private void FlushRemainingSynchronously()
        {
            List<WriteBatch> pending = new List<WriteBatch>();
            lock (_sync)
            {
                if (_failedBatches.Count > 0)
                {
                    pending.AddRange(_failedBatches);
                    _failedBatches.Clear();
                }
                if (_csvBuffer.Length > 0 || _eventBuffer.Length > 0 || _classificationAuditBuffer.Length > 0)
                {
                    pending.Add(new WriteBatch
                    {
                        Csv = _csvBuffer.ToString(),
                        Events = _eventBuffer.ToString(),
                        Audit = _classificationAuditBuffer.ToString(),
                        RecordCount = _bufferedRecordCount
                    });
                    _csvBuffer.Clear();
                    _eventBuffer.Clear();
                    _classificationAuditBuffer.Clear();
                    _bufferedRecordCount = 0;
                }
            }

            BlockingCollection<WriteBatch> queue = _writeQueue;
            if (queue != null)
            {
                WriteBatch batch;
                while (queue.TryTake(out batch)) pending.Add(batch);
            }

            foreach (WriteBatch batch in pending)
                WriteBatchToDisk(batch, 5);
        }

        private void ReconcilePerformanceRecordCount()
        {
            try
            {
                long lines = 0;
                if (File.Exists(_performancePath))
                {
                    using (StreamReader reader = new StreamReader(_performancePath, Encoding.UTF8))
                    {
                        while (reader.ReadLine() != null) lines++;
                    }
                }
                Interlocked.Exchange(ref _recordsWritten, Math.Max(0, lines - 1));
            }
            catch { }
        }

        private void WriteSummary(string reason)
        {
            try
            {
                StringBuilder sb = new StringBuilder(32768);
                TimeSpan duration = _sessionClock == null ? TimeSpan.Zero : _sessionClock.Elapsed;
                sb.AppendLine("RAM Trace Performance Diagnostics Summary");
                sb.AppendLine("========================================");
                sb.AppendLine("Session folder: " + _sessionDirectory);
                sb.AppendLine("Started UTC: " + _sessionStartUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.AppendLine("Session duration: " + duration.ToString());
                sb.AppendLine("Stop reason: " + reason);
                sb.AppendLine("Diagnostics are local only. No telemetry or network upload is performed.");
                sb.AppendLine();

                sb.AppendLine("Startup milestones");
                sb.AppendLine("------------------");
                foreach (StartupMarker marker in StartupPerformance.Snapshot())
                    sb.AppendLine(marker.Name + ": " + F(marker.ElapsedMs) + " ms");
                sb.AppendLine();

                sb.AppendLine("Timing statistics (milliseconds)");
                sb.AppendLine("--------------------------------");
                string[] required = new string[]
                {
                    "Complete refresh-cycle duration", "Process scan duration", "Background process scan duration",
                    "Process memory collection duration", "Snapshot-to-UI transfer delay", "Snapshot/UI apply duration",
                    "UI refresh duration", "UI heartbeat/scheduling delay", "Timer drift duration", "Service scan duration", "Metadata lookup duration",
                    "RAM accounting duration", "Memory breakdown calculation duration", "History/data write duration", "Diagnostic logging overhead",
                    "Grid sort duration", "Diagnostic metric generation", "Diagnostic record formatting"
                };
                foreach (string name in required)
                    AppendTimingSummary(sb, name);
                sb.AppendLine();

                lock (_sync)
                {
                    AppendResourceSummary(sb, "RAM Trace working set", _workingSetMb, " MB");
                    AppendResourceSummary(sb, "RAM Trace private bytes", _privateMb, " MB");
                    AppendResourceSummary(sb, "RAM Trace CPU", _cpuPercent, "%");
                    AppendResourceSummary(sb, "Thread count", _threadCounts, string.Empty);
                    AppendResourceSummary(sb, "Handle count", _handleCounts, string.Empty);
                }

                sb.AppendLine();
                sb.AppendLine("GC collections during diagnostic session");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine("Generation 0: " + Math.Max(0, GC.CollectionCount(0) - _gc0Start).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Generation 1: " + Math.Max(0, GC.CollectionCount(1) - _gc1Start).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Generation 2: " + Math.Max(0, GC.CollectionCount(2) - _gc2Start).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();

                sb.AppendLine("Responsiveness and warnings");
                sb.AppendLine("---------------------------");
                sb.AppendLine("Maximum UI scheduling delay: " + F(_maxUiDelayMs) + " ms");
                sb.AppendLine("UI delays >50 ms: " + _uiOver50.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("UI delays >100 ms: " + _uiOver100.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("UI delays >250 ms: " + _uiOver250.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("UI delays >500 ms: " + _uiOver500.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("UI delays >1000 ms: " + _uiOver1000.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("CPU throttling events: " + _throttleEvents.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Total time throttled: " + _totalThrottled.ToString());
                sb.AppendLine("Timer drift events >250 ms: " + Interlocked.Read(ref _timerDriftEvents).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Process scans skipped/coalesced: " + Interlocked.Read(ref _skippedProcessScans).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid rows added: " + Interlocked.Read(ref _gridRowsAdded).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid rows updated: " + Interlocked.Read(ref _gridRowsUpdated).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid rows removed: " + Interlocked.Read(ref _gridRowsRemoved).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Full grid rebuild count: " + Interlocked.Read(ref _fullGridRebuilds).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid structural changes: " + Interlocked.Read(ref _gridStructuralChanges).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid value-only updates: " + Interlocked.Read(ref _gridValueOnlyUpdates).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid interaction-deferred refreshes: " + Interlocked.Read(ref _gridInteractionDeferredRefreshes).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid snapshots coalesced: " + Interlocked.Read(ref _gridSnapshotsCoalesced).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Grid duplicate stable keys: " + Interlocked.Read(ref _gridDuplicateKeys).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Memory breakdown reconciliation mismatches: " + Interlocked.Read(ref _memoryBreakdownMismatchCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Process breakdown reconciliation mismatches: " + Interlocked.Read(ref _processBreakdownMismatchCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Total LIVE breakdown reconciliation mismatches: " + Interlocked.Read(ref _totalLiveBreakdownMismatchCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Classification audit: " + _classificationAuditPath);
                sb.AppendLine("Slow-operation warnings: " + Interlocked.Read(ref _warningCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Exceptions logged: " + Interlocked.Read(ref _exceptionCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();

                sb.AppendLine("Diagnostic system overhead");
                sb.AppendLine("--------------------------");
                sb.AppendLine("Performance records generated: " + Interlocked.Read(ref _recordsGenerated).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Performance records written: " + Interlocked.Read(ref _recordsWritten).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Buffer flushes: " + Interlocked.Read(ref _flushCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Bytes written: " + Interlocked.Read(ref _bytesWritten).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Average metric-generation time: " + AverageTicks(_generationTicks, Math.Max(1, _recordsWritten)).ToString("0.000", CultureInfo.InvariantCulture) + " ms");
                sb.AppendLine("Average CSV-formatting time: " + AverageTicks(_formatTicks, Math.Max(1, _recordsWritten)).ToString("0.000", CultureInfo.InvariantCulture) + " ms");
                sb.AppendLine("Last batch-write time: " + F(LastWriteMilliseconds) + " ms");
                sb.AppendLine("Session size limit reached: " + (_sizeLimitReached ? "Yes" : "No"));

                File.WriteAllText(_summaryPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Summary generation must not affect normal application shutdown.
            }
        }

        private void AppendTimingSummary(StringBuilder sb, string name)
        {
            TimingSnapshot snapshot;
            lock (_sync)
            {
                TimingSeries series;
                if (!_timings.TryGetValue(name, out series)) series = new TimingSeries();
                snapshot = series.Snapshot();
            }
            sb.Append(name);
            sb.Append(": Samples="); sb.Append(snapshot.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(" Min="); sb.Append(F(snapshot.Min));
            sb.Append(" Avg="); sb.Append(F(snapshot.Average));
            sb.Append(" P50="); sb.Append(F(snapshot.P50));
            sb.Append(" P90="); sb.Append(F(snapshot.P90));
            sb.Append(" P95="); sb.Append(F(snapshot.P95));
            sb.Append(" P99="); sb.Append(F(snapshot.P99));
            sb.Append(" Max="); sb.AppendLine(F(snapshot.Max));
        }

        private static void AppendResourceSummary(StringBuilder sb, string name, List<double> values, string suffix)
        {
            if (values == null || values.Count == 0)
            {
                sb.AppendLine(name + ": no samples");
                return;
            }
            double avg = values.Average();
            double min = values.Min();
            double max = values.Max();
            sb.AppendLine(name + ": Average=" + avg.ToString("0.00", CultureInfo.InvariantCulture) + suffix
                + " Lowest=" + min.ToString("0.00", CultureInfo.InvariantCulture) + suffix
                + " Peak=" + max.ToString("0.00", CultureInfo.InvariantCulture) + suffix);
        }

        private void AddTiming(string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return;
            lock (_sync)
            {
                TimingSeries series;
                if (!_timings.TryGetValue(name, out series))
                {
                    series = new TimingSeries();
                    _timings[name] = series;
                }
                series.Add(value);
            }
        }

        private static void AddBounded(List<double> list, double value)
        {
            if (list.Count < 100000) list.Add(value);
        }

        private static string GetCsvHeader()
        {
            return string.Join(",", new string[]
            {
                "Timestamp","ApplicationUptimeSeconds","ExpectedSamplingIntervalMs","ActualSamplingIntervalMs","TimerDriftMs",
                "MainRefreshCycleMs","BackgroundProcessScanMs","UiMarshalDelayMs","SnapshotUiApplyMs","TotalRamSamplingMs","ProcessEnumerationMs","ProcessMemoryCollectionMs","ProcessGroupingMs",
                "ServiceMappingMs","ServiceDiscoveryMs","MetadataLookupMs","MetadataCacheHits","MetadataCacheMisses",
                "RamAccountingMs","HistoricalStatsUpdateMs","GridDataPreparationMs","GridUiRefreshMs","ProcessInformationPanelMs",
                "FooterStatusUpdateMs","LocalDataHistoryWriteMs","DiagnosticLoggingWriteMs","TotalCompleteCycleMs",
                "WindowsProcessCount","GroupedRowCount","ServiceCount","SvchostServiceGroupCount","MetadataLookupsAttempted","MetadataLookupsDeferred",
                "WorkingSetBytes","PrivateBytes","ManagedHeapBytes","CpuPercent","ProcessCpuTimeMs","ThreadCount","HandleCount",
                "GcGen0","GcGen1","GcGen2","CpuThrottled","LiveSystemRamBytes","ProcessPrivateRamBytes","KernelPoolRamBytes","SystemSharedRamBytes",
                "MemoryBreakdownCalculationMs","WindowsSystemPrivateMB","MicrosoftAppsPrivateMB","ThirdPartyPrivateMB","UnclassifiedPrivateMB",
                "WindowsSystemGroups","MicrosoftAppGroups","ThirdPartyGroups","UnclassifiedGroups","MemoryBreakdownMismatchCount","ProcessBreakdownMismatchCount","TotalLiveBreakdownMismatchCount",
                "SelectedProcess","WindowState","ProcessGridRowCount","SkippedProcessScans","GridRowsAdded","GridRowsUpdated","GridRowsRemoved","FullGridRebuildCount",
                "GridStructuralChanges","GridValueOnlyUpdates","GridSortDurationMs","GridInteractionDeferredRefreshCount","GridSnapshotsCoalesced","GridDuplicateKeyCount",
                "DiagnosticQueueSize","DiagnosticGenerationMs","DiagnosticFormattingMs"
            });
        }

        private static string FormatCsv(DiagnosticCycleSample s)
        {
            string[] values = new string[]
            {
                Csv(s.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                F(s.ApplicationUptimeSeconds), s.ExpectedSamplingIntervalMs.ToString(CultureInfo.InvariantCulture), F(s.ActualSamplingIntervalMs), F(s.TimerDriftMs),
                F(s.MainRefreshCycleMs), F(s.BackgroundProcessScanMs), F(s.UiMarshalDelayMs), F(s.SnapshotUiApplyMs), F(s.TotalRamSamplingMs), F(s.ProcessEnumerationMs), F(s.ProcessMemoryCollectionMs), F(s.ProcessGroupingMs),
                F(s.ServiceMappingMs), F(s.ServiceDiscoveryMs), F(s.MetadataLookupMs), s.MetadataCacheHits.ToString(CultureInfo.InvariantCulture), s.MetadataCacheMisses.ToString(CultureInfo.InvariantCulture),
                F(s.RamAccountingMs), F(s.HistoricalStatsUpdateMs), F(s.GridDataPreparationMs), F(s.GridUiRefreshMs), F(s.ProcessInformationPanelMs),
                F(s.FooterStatusUpdateMs), F(s.LocalDataHistoryWriteMs), F(s.DiagnosticLoggingWriteMs), F(s.TotalCompleteCycleMs),
                s.WindowsProcessCount.ToString(CultureInfo.InvariantCulture), s.GroupedRowCount.ToString(CultureInfo.InvariantCulture), s.ServiceCount.ToString(CultureInfo.InvariantCulture), s.SvchostServiceGroupCount.ToString(CultureInfo.InvariantCulture), s.MetadataLookupsAttempted.ToString(CultureInfo.InvariantCulture), s.MetadataLookupsDeferred.ToString(CultureInfo.InvariantCulture),
                s.WorkingSetBytes.ToString(CultureInfo.InvariantCulture), s.PrivateBytes.ToString(CultureInfo.InvariantCulture), s.ManagedHeapBytes.ToString(CultureInfo.InvariantCulture), F(s.CpuPercent), F(s.ProcessCpuTimeMs), s.ThreadCount.ToString(CultureInfo.InvariantCulture), s.HandleCount.ToString(CultureInfo.InvariantCulture),
                s.GcGen0.ToString(CultureInfo.InvariantCulture), s.GcGen1.ToString(CultureInfo.InvariantCulture), s.GcGen2.ToString(CultureInfo.InvariantCulture), s.CpuThrottled ? "1" : "0",
                s.LiveSystemRamBytes.ToString(CultureInfo.InvariantCulture), s.ProcessPrivateRamBytes.ToString(CultureInfo.InvariantCulture), s.KernelPoolRamBytes.ToString(CultureInfo.InvariantCulture), s.SystemSharedRamBytes.ToString(CultureInfo.InvariantCulture),
                F(s.MemoryBreakdownCalculationMs), F(s.WindowsSystemPrivateMB), F(s.MicrosoftAppsPrivateMB), F(s.ThirdPartyPrivateMB), F(s.UnclassifiedPrivateMB),
                s.WindowsSystemGroups.ToString(CultureInfo.InvariantCulture), s.MicrosoftAppGroups.ToString(CultureInfo.InvariantCulture), s.ThirdPartyGroups.ToString(CultureInfo.InvariantCulture), s.UnclassifiedGroups.ToString(CultureInfo.InvariantCulture), s.MemoryBreakdownMismatchCount.ToString(CultureInfo.InvariantCulture), s.ProcessBreakdownMismatchCount.ToString(CultureInfo.InvariantCulture), s.TotalLiveBreakdownMismatchCount.ToString(CultureInfo.InvariantCulture),
                Csv(s.SelectedProcess), Csv(s.WindowState), s.ProcessGridRowCount.ToString(CultureInfo.InvariantCulture),
                s.SkippedProcessScans.ToString(CultureInfo.InvariantCulture), s.GridRowsAdded.ToString(CultureInfo.InvariantCulture), s.GridRowsUpdated.ToString(CultureInfo.InvariantCulture), s.GridRowsRemoved.ToString(CultureInfo.InvariantCulture), s.FullGridRebuildCount.ToString(CultureInfo.InvariantCulture),
                s.GridStructuralChanges.ToString(CultureInfo.InvariantCulture), s.GridValueOnlyUpdates.ToString(CultureInfo.InvariantCulture), F(s.GridSortDurationMs), s.GridInteractionDeferredRefreshCount.ToString(CultureInfo.InvariantCulture), s.GridSnapshotsCoalesced.ToString(CultureInfo.InvariantCulture), s.GridDuplicateKeyCount.ToString(CultureInfo.InvariantCulture),
                s.DiagnosticQueueSize.ToString(CultureInfo.InvariantCulture), F(s.DiagnosticGenerationMs), F(s.DiagnosticFormattingMs)
            };
            return string.Join(",", values);
        }

        private static string GetClassificationAuditHeader()
        {
            return string.Join(",", new string[]
            {
                "Timestamp","StableGroupKey","FamiliarName","TechnicalName","Type","Publisher","ProductName",
                "DisplayPrivateRAMMB","AccountingPrivateRAMMB","Instances","AccountingInstances",
                "AssignedCategory","ClassificationRule","Evidence"
            });
        }

        private static string Csv(string value)
        {
            if (value == null) value = string.Empty;
            if (value.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string F(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static double AverageTicks(long ticks, long count)
        {
            if (count <= 0) return 0;
            return TicksToMilliseconds(ticks) / count;
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void CleanupOldSessions(string diagnosticsRoot)
        {
            try
            {
                DirectoryInfo root = new DirectoryInfo(diagnosticsRoot);
                DirectoryInfo[] dirs = root.GetDirectories().OrderByDescending(x => x.CreationTimeUtc).ToArray();
                for (int i = MaxRetainedSessions - 1; i < dirs.Length; i++)
                {
                    try { dirs[i].Delete(true); }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_enabled) StopSession("Application shutdown");
            _disposed = true;
        }

        private sealed class WriteBatch
        {
            public string Csv;
            public string Events;
            public string Audit;
            public long RecordCount;
            public bool CsvWritten;
            public bool EventsWritten;
            public bool AuditWritten;
        }
    }

    internal sealed class TimingSeries
    {
        private readonly List<double> _values = new List<double>();

        public void Add(double value)
        {
            if (_values.Count < 100000) _values.Add(value);
        }

        public TimingSnapshot Snapshot()
        {
            if (_values.Count == 0) return new TimingSnapshot();
            double[] sorted = _values.ToArray();
            Array.Sort(sorted);
            TimingSnapshot s = new TimingSnapshot();
            s.Count = sorted.Length;
            s.Min = sorted[0];
            s.Max = sorted[sorted.Length - 1];
            s.Average = sorted.Average();
            s.P50 = Percentile(sorted, 0.50);
            s.P90 = Percentile(sorted, 0.90);
            s.P95 = Percentile(sorted, 0.95);
            s.P99 = Percentile(sorted, 0.99);
            return s;
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted == null || sorted.Length == 0) return 0;
            if (sorted.Length == 1) return sorted[0];
            double position = (sorted.Length - 1) * p;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return sorted[lower];
            double fraction = position - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
        }
    }

    internal sealed class TimingSnapshot
    {
        public int Count;
        public double Min;
        public double Average;
        public double P50;
        public double P90;
        public double P95;
        public double P99;
        public double Max;
    }
}
