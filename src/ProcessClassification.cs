using System;
using System.Collections.Generic;

namespace RamLight
{
    internal enum ProcessOwnershipCategory
    {
        WindowsSystem,
        MicrosoftApplication,
        ThirdParty,
        Unclassified
    }

    internal sealed class ProcessClassificationResult
    {
        public ProcessOwnershipCategory Category;
        public string Rule = "Unclassified fallback";
        public string Evidence = "No sufficiently strong ownership evidence was available.";
    }

    internal sealed class ClassificationAuditRecord
    {
        public DateTime TimestampUtc;
        public string StableGroupKey;
        public string FamiliarName;
        public string TechnicalName;
        public string Type;
        public string Publisher;
        public string ProductName;
        public ulong DisplayPrivateBytes;
        public ulong AccountingPrivateBytes;
        public int Instances;
        public int AccountingInstances;
        public ProcessOwnershipCategory AssignedCategory;
        public string ClassificationRule;
        public string Evidence;
    }

    internal sealed class MemoryCategoryBreakdown
    {
        public ProcessOwnershipCategory Category;
        public ulong PrivateBytes;
        public int GroupCount;
        public int InstanceCount;
    }

    internal sealed class MemoryBreakdownSnapshot
    {
        public MemoryCategoryBreakdown WindowsSystem = new MemoryCategoryBreakdown { Category = ProcessOwnershipCategory.WindowsSystem };
        public MemoryCategoryBreakdown MicrosoftApplications = new MemoryCategoryBreakdown { Category = ProcessOwnershipCategory.MicrosoftApplication };
        public MemoryCategoryBreakdown ThirdPartyApplications = new MemoryCategoryBreakdown { Category = ProcessOwnershipCategory.ThirdParty };
        public MemoryCategoryBreakdown Unclassified = new MemoryCategoryBreakdown { Category = ProcessOwnershipCategory.Unclassified };
        public readonly List<ClassificationAuditRecord> ClassificationAudit = new List<ClassificationAuditRecord>();
        public ulong ProcessPrivateTotalBytes;
        public ulong WindowsKernelBytes;
        public ulong WindowsSystemSharedBytes;
        public ulong AccountingLiveBytes;
        public ulong CategoryTotalBytes;
        public ulong NonProcessSystemBytes;
        public ulong FullLiveBreakdownBytes;
        public ulong ProcessReconciliationDifferenceBytes;
        public bool ProcessReconciliationMismatch;
        public ulong TotalLiveReconciliationDifferenceBytes;
        public bool TotalLiveReconciliationMismatch;

        // Compatibility aliases retained for existing diagnostic readers created
        // before the full-LIVE-RAM reconciliation was added.
        public ulong ReconciliationDifferenceBytes { get { return ProcessReconciliationDifferenceBytes; } }
        public bool ReconciliationMismatch { get { return ProcessReconciliationMismatch; } }
    }

    internal sealed class ProcessClassificationCache
    {
        private sealed class CacheEntry
        {
            public string Fingerprint;
            public ProcessClassificationResult Result;
        }

        private readonly Dictionary<string, CacheEntry> _entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public ProcessOwnershipCategory Classify(ProcessGroupSample group)
        {
            return ClassifyDetailed(group).Category;
        }

        public ProcessClassificationResult ClassifyDetailed(ProcessGroupSample group)
        {
            if (group == null) return ProcessClassifier.ClassifyDetailed(group);
            string key = group.Key ?? group.TechnicalName ?? string.Empty;
            string fingerprint = string.Join("|", new string[]
            {
                group.TechnicalName ?? string.Empty,
                group.Type ?? string.Empty,
                group.Services ?? string.Empty,
                group.Company ?? string.Empty,
                group.BelongsTo ?? string.Empty,
                group.Name ?? string.Empty
            });

            CacheEntry existing;
            if (_entries.TryGetValue(key, out existing)
                && string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal)
                && existing.Result != null)
                return existing.Result;

            ProcessClassificationResult result = ProcessClassifier.ClassifyDetailed(group);
            _entries[key] = new CacheEntry { Fingerprint = fingerprint, Result = result };
            return result;
        }

        public void TrimTo(IEnumerable<string> activeKeys)
        {
            HashSet<string> active = new HashSet<string>(activeKeys ?? new string[0], StringComparer.OrdinalIgnoreCase);
            List<string> stale = new List<string>();
            foreach (string key in _entries.Keys)
                if (!active.Contains(key)) stale.Add(key);
            foreach (string key in stale) _entries.Remove(key);
        }
    }

    internal static class ProcessClassifier
    {
        private static readonly HashSet<string> WindowsCore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "Memory Compression", "Secure System", "svchost", "dwm", "lsass", "csrss", "smss",
            "wininit", "winlogon", "services", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
            "SearchApp", "SearchIndexer", "SearchProtocolHost", "SearchFilterHost", "RuntimeBroker", "WmiPrvSE", "fontdrvhost",
            "audiodg", "spoolsv", "conhost", "dllhost", "explorer", "Taskmgr", "SecurityHealthSystray", "SecurityHealthService",
            "smartscreen", "appmodel", "vmmem", "vmmemWSL", "ctfmon", "taskhostw", "backgroundTaskHost", "ApplicationFrameHost",
            "TextInputHost", "LockApp", "LogonUI", "WUDFHost", "dasHost", "SgrmBroker", "MsMpEng"
        };

        private static readonly HashSet<string> MicrosoftApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedge", "msedgewebview2", "OneDrive", "OfficeClickToRun", "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK",
            "ONENOTE", "Teams", "ms-teams", "msteams", "Microsoft.CmdPal.UI", "CmdPal"
        };

        private static readonly HashSet<string> KnownThirdParty = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brave", "crosvm", "Dropbox", "ShareX", "Discord", "steam", "steamwebhelper", "GoogleDriveFS",
            "AdobeIPCBroker", "AcroCEF", "Acrobat", "RAM-Trace", "RamLight", "mc-fw-host", "mc-web-view", "mc-neo-host",
            "TeamViewer", "TeamViewer_Service", "WhatsApp", "WhatsApp.Root", "GooglePlayGamesServices"
        };

        public static ProcessOwnershipCategory Classify(ProcessGroupSample group)
        {
            return ClassifyDetailed(group).Category;
        }

        public static ProcessClassificationResult ClassifyDetailed(ProcessGroupSample group)
        {
            if (group == null)
                return Result(ProcessOwnershipCategory.Unclassified, "Null group", "No process group was supplied.");

            string technical = group.TechnicalName ?? string.Empty;
            string company = group.Company ?? string.Empty;
            string product = group.BelongsTo ?? string.Empty;
            string type = group.Type ?? string.Empty;

            // 1. Explicit known Windows core/system mapping. This intentionally
            // includes vmmem because RAM Trace defines ownership by the Windows
            // host process, not by whichever guest workload happens to use Hyper-V.
            if (WindowsCore.Contains(technical))
                return Result(ProcessOwnershipCategory.WindowsSystem, "Known Windows core/system mapping", technical + " is an explicit Windows/Hyper-V host component.");

            // 2. Windows service hosts remain operating-system memory even when
            // executable metadata is unavailable or generic.
            if (technical.Equals("svchost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(group.Services))
                return Result(ProcessOwnershipCategory.WindowsSystem, "Windows service-host mapping", "svchost is hosting Windows service entries already resolved by RAM Trace.");

            // Strong cached Windows product evidence is OS-level only when paired
            // with Microsoft ownership and a Windows system/shell/security type.
            if (IsMicrosoft(company) && IsWindowsProduct(product, type))
                return Result(ProcessOwnershipCategory.WindowsSystem, "Cached Microsoft Windows product evidence", "Publisher and existing product/type metadata identify a Windows operating-system component.");

            // 3. Explicit Microsoft application mappings, including PowerToys.
            if (MicrosoftApplications.Contains(technical) || technical.StartsWith("PowerToys.", StringComparison.OrdinalIgnoreCase))
                return Result(ProcessOwnershipCategory.MicrosoftApplication, "Known Microsoft application mapping", technical + " is a Microsoft application/runtime rather than core Windows.");

            // 4. Existing cached Microsoft publisher/product evidence means
            // Microsoft application unless an earlier Windows-system rule matched.
            if (IsMicrosoft(company) || IsMicrosoft(product))
                return Result(ProcessOwnershipCategory.MicrosoftApplication, "Cached Microsoft publisher/product evidence", "Existing metadata identifies Microsoft ownership without Windows-core evidence.");

            // 5. Existing cached non-Microsoft publisher/product evidence.
            if (HasThirdPartyPublisher(company) || HasThirdPartyProduct(product))
                return Result(ProcessOwnershipCategory.ThirdParty, "Cached non-Microsoft publisher/product evidence", "Existing metadata identifies a non-Microsoft software owner.");

            // 6. Explicit known third-party mappings and families.
            if (KnownThirdParty.Contains(technical)
                || technical.StartsWith("mc-", StringComparison.OrdinalIgnoreCase)
                || technical.StartsWith("Dell", StringComparison.OrdinalIgnoreCase)
                || technical.StartsWith("TeamViewer", StringComparison.OrdinalIgnoreCase))
                return Result(ProcessOwnershipCategory.ThirdParty, "Known third-party mapping", technical + " matches RAM Trace's existing third-party knowledge.");

            // 7. Honest uncertainty rather than path/name guessing.
            return Result(ProcessOwnershipCategory.Unclassified, "Unclassified fallback", "No sufficiently strong cached ownership evidence was available.");
        }

        public static string CategoryDisplayName(ProcessOwnershipCategory category)
        {
            switch (category)
            {
                case ProcessOwnershipCategory.WindowsSystem: return "Windows system";
                case ProcessOwnershipCategory.MicrosoftApplication: return "Microsoft applications";
                case ProcessOwnershipCategory.ThirdParty: return "Third-party applications";
                default: return "Unclassified";
            }
        }

        private static ProcessClassificationResult Result(ProcessOwnershipCategory category, string rule, string evidence)
        {
            return new ProcessClassificationResult { Category = category, Rule = rule, Evidence = evidence };
        }

        private static bool IsMicrosoft(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWindowsProduct(string product, string type)
        {
            if (!string.IsNullOrWhiteSpace(type)
                && (type.IndexOf("Windows system", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Windows security", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Windows shell", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Windows audio", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Windows memory", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            if (string.IsNullOrWhiteSpace(product)) return false;
            return product.IndexOf("Windows Operating System", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Microsoft® Windows® Operating System", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Windows desktop", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Windows shell", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Windows Search", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Windows security", StringComparison.OrdinalIgnoreCase) >= 0
                || product.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasThirdPartyPublisher(string company)
        {
            if (string.IsNullOrWhiteSpace(company)) return false;
            if (IsMicrosoft(company)) return false;
            if (company.Equals("Local legacy build", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static bool HasThirdPartyProduct(string product)
        {
            if (string.IsNullOrWhiteSpace(product) || IsMicrosoft(product)) return false;
            string[] known = new string[]
            {
                "Brave", "Google", "Dropbox", "McAfee", "ShareX", "Dell", "Adobe", "Steam", "Discord", "RAM Trace", "RamLight", "TeamViewer", "WhatsApp"
            };
            foreach (string token in known)
                if (product.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    internal static class MemoryBreakdownCalculator
    {
        private const ulong MismatchToleranceBytes = 1024UL * 1024UL;

        public static MemoryBreakdownSnapshot Calculate(
            Dictionary<string, ProcessGroupSample> groups,
            ulong processPrivateTotal,
            ulong windowsKernel,
            ulong windowsSystemShared,
            ulong accountingLive,
            ProcessClassificationCache cache)
        {
            MemoryBreakdownSnapshot result = new MemoryBreakdownSnapshot();
            result.ProcessPrivateTotalBytes = processPrivateTotal;
            result.WindowsKernelBytes = windowsKernel;
            result.WindowsSystemSharedBytes = windowsSystemShared;
            result.AccountingLiveBytes = accountingLive;
            result.NonProcessSystemBytes = windowsKernel + windowsSystemShared;

            Dictionary<string, ProcessGroupSample> safeGroups = groups ?? new Dictionary<string, ProcessGroupSample>(StringComparer.OrdinalIgnoreCase);
            DateTime auditTimestamp = DateTime.UtcNow;
            foreach (ProcessGroupSample group in safeGroups.Values)
            {
                if (group == null) continue;
                ProcessClassificationResult classification = cache == null
                    ? ProcessClassifier.ClassifyDetailed(group)
                    : cache.ClassifyDetailed(group);

                result.ClassificationAudit.Add(new ClassificationAuditRecord
                {
                    TimestampUtc = auditTimestamp,
                    StableGroupKey = group.Key ?? string.Empty,
                    FamiliarName = group.Name ?? string.Empty,
                    TechnicalName = group.TechnicalName ?? string.Empty,
                    Type = group.Type ?? string.Empty,
                    Publisher = group.Company ?? string.Empty,
                    ProductName = group.BelongsTo ?? string.Empty,
                    DisplayPrivateBytes = group.LiveBytes,
                    AccountingPrivateBytes = group.AccountingPrivateBytes,
                    Instances = group.InstanceCount,
                    AccountingInstances = group.AccountingInstanceCount,
                    AssignedCategory = classification.Category,
                    ClassificationRule = classification.Rule ?? string.Empty,
                    Evidence = classification.Evidence ?? string.Empty
                });

                // Only reliable Private Working Set samples participate in the
                // accepted process-private accounting total. Classification of a
                // fallback/unreliable row is still audited, but it is not forced
                // into accounting and therefore cannot change accepted accounting semantics.
                if (group.AccountingInstanceCount <= 0) continue;
                MemoryCategoryBreakdown target = Select(result, classification.Category);
                target.PrivateBytes += group.AccountingPrivateBytes;
                target.GroupCount++;
                target.InstanceCount += group.AccountingInstanceCount;
            }

            if (cache != null) cache.TrimTo(safeGroups.Keys);

            result.CategoryTotalBytes = result.WindowsSystem.PrivateBytes
                + result.MicrosoftApplications.PrivateBytes
                + result.ThirdPartyApplications.PrivateBytes
                + result.Unclassified.PrivateBytes;

            result.ProcessReconciliationDifferenceBytes = Difference(result.CategoryTotalBytes, result.ProcessPrivateTotalBytes);
            result.ProcessReconciliationMismatch = result.ProcessReconciliationDifferenceBytes > MismatchToleranceBytes;

            result.FullLiveBreakdownBytes = result.CategoryTotalBytes + result.WindowsKernelBytes + result.WindowsSystemSharedBytes;
            result.TotalLiveReconciliationDifferenceBytes = Difference(result.FullLiveBreakdownBytes, result.AccountingLiveBytes);
            result.TotalLiveReconciliationMismatch = result.TotalLiveReconciliationDifferenceBytes > MismatchToleranceBytes;
            return result;
        }

        private static ulong Difference(ulong a, ulong b)
        {
            return a >= b ? a - b : b - a;
        }

        private static MemoryCategoryBreakdown Select(MemoryBreakdownSnapshot result, ProcessOwnershipCategory category)
        {
            switch (category)
            {
                case ProcessOwnershipCategory.WindowsSystem: return result.WindowsSystem;
                case ProcessOwnershipCategory.MicrosoftApplication: return result.MicrosoftApplications;
                case ProcessOwnershipCategory.ThirdParty: return result.ThirdPartyApplications;
                default: return result.Unclassified;
            }
        }
    }
}
