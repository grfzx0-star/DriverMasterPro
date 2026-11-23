using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DriverMasterPro.Models;
using DriverMasterPro.Services;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro;

public partial class MainWindow : Window
{
    private readonly IDeviceScanner _scanner;
    private readonly IDatabaseService _database;
    private readonly IDiagnosticsService _diagnostics;
    private readonly List<DeviceInfo> _allDevices = new();
    private List<ScanSnapshot> _snapshots = new();
    private CancellationTokenSource? _scanCts;
    private int _isScanning = 0;
    private bool _isStartupLoading = true;
    private volatile bool _isClosed = false;
    private bool _isUpdatingHistorySelection;
    private bool _isArabic;
    private int _languageIndex = 0;
    private long? _latestScanId;
    private long? _freshScanId;
    private string? _displayScanTimeUtc;
    private InventoryDisplayState _displayState = InventoryDisplayState.Empty;

    public MainWindow(IDeviceScanner scanner, IDatabaseService database, IDiagnosticsService diagnostics)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

        InitializeComponent();

        Loaded += async (_, _) => await LoadPersistedInventoryAsync();
    }

    public MainWindow() : this(new DeviceScanner(), new DatabaseService(), new RuleBasedDiagnosticsService())
    {
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosed = true;
        _scanCts?.Cancel();
        base.OnClosing(e);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isStartupLoading)
        {
            return;
        }

        // Prevent concurrent scans
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
        {
            return;
        }

        ScanButton.IsEnabled = false;
        SnapshotHistoryBox.IsEnabled = false;
        StatusText.Text = ScanInProgressMessage();

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = _scanCts.Token;

        try
        {
            List<DeviceInfo> devices = await _scanner.ScanAsync(token);
            _diagnostics.EnrichAll(devices, _isArabic);
            await _database.SaveScanAsync(devices, token);

            // Ensure background work cannot update a closed or unloaded window
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            ScanSnapshot? latest = await _database.GetLatestScanAsync();
            List<ScanSnapshot> history = await _database.GetScanHistoryAsync();
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            _latestScanId = latest?.ScanId ?? history.FirstOrDefault()?.ScanId;
            _freshScanId = latest?.ScanId;
            SetHistorySnapshots(history, latest?.ScanId);
            if (latest is not null)
            {
                DisplaySnapshot(latest, InventoryDisplayState.Fresh);
                StatusText.Text = ScanCompletedMessage(devices.Count);
            }
            else
            {
                SetDisplayedDevices(devices, InventoryDisplayState.Fresh);
                StatusText.Text = FreshScanHistoryUnavailableMessage();
            }
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("[MainWindow] Scan cancelled or timed out.");
            if (!_isClosed && IsLoaded)
            {
                StatusText.Text = ScanCancelledMessage();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[MainWindow] Scan failed: {ex}");
            if (!_isClosed && IsLoaded)
            {
                StatusText.Text = ScanErrorMessage(ex.Message);
                MessageBox.Show(ex.Message, ScanErrorTitle(),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
            Interlocked.Exchange(ref _isScanning, 0);

            if (!_isClosed && IsLoaded)
            {
                ScanButton.IsEnabled = true;
                SnapshotHistoryBox.IsEnabled = _snapshots.Count > 0;
            }
        }
    }

    private async Task LoadPersistedInventoryAsync()
    {
        try
        {
            await _database.InitializeAsync();
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            StatusText.Text = DatabaseReadyMessage(_database.DatabasePath);
            ScanSnapshot? latest = await _database.GetLatestScanAsync();
            List<ScanSnapshot> history = await _database.GetScanHistoryAsync();
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            _latestScanId = latest?.ScanId;
            SetHistorySnapshots(history, latest?.ScanId);

            if (latest is not null)
            {
                DisplaySnapshot(latest, InventoryDisplayState.Cached);
                return;
            }

            List<DeviceInfo> legacyDevices = await _database.GetCachedDevicesAsync();
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            SetDisplayedDevices(
                legacyDevices,
                legacyDevices.Count > 0 ? InventoryDisplayState.LegacyCached : InventoryDisplayState.Empty,
                scanTimeUtc: null);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[MainWindow] Database initialization or cache loading failed: {ex}");
            if (!_isClosed && IsLoaded)
            {
                StatusText.Text = DatabaseErrorMessage(ex.Message);
                SetDisplayedDevices(Array.Empty<DeviceInfo>(), InventoryDisplayState.Empty);
            }
        }
        finally
        {
            _isStartupLoading = false;
            if (!_isClosed && IsLoaded)
            {
                ScanButton.IsEnabled = true;
            }
        }
    }

    private void SetHistorySnapshots(IEnumerable<ScanSnapshot> snapshots, long? selectedScanId)
    {
        _snapshots = snapshots
            .GroupBy(snapshot => snapshot.ScanId)
            .Select(group => group.First())
            .OrderByDescending(snapshot => snapshot.ScanId)
            .ToList();

        _isUpdatingHistorySelection = true;
        try
        {
            SnapshotHistoryBox.ItemsSource = _snapshots
                .Select(snapshot => new HistoryEntry(snapshot, FormatSnapshotLabel(snapshot)))
                .ToList();
            SnapshotHistoryBox.IsEnabled = _snapshots.Count > 0;
            SnapshotHistoryBox.SelectedValue = selectedScanId;
        }
        finally
        {
            _isUpdatingHistorySelection = false;
        }
    }

    private void RefreshHistoryLabels()
    {
        long? selectedScanId = SnapshotHistoryBox.SelectedValue is long id ? id : null;
        SetHistorySnapshots(_snapshots, selectedScanId);
    }

    private string FormatSnapshotLabel(ScanSnapshot snapshot) =>
        string.Format(
            Translate(
                "{0} — {1} Geräte",
                "{0} — {1} أجهزة",
                "{0} — {1} devices",
                "{0} — {1} appareils",
                "{0} — {1} dispositivos"),
            snapshot.ScanTimeUtc,
            snapshot.DeviceCount);

    private void SnapshotHistoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingHistorySelection || SnapshotHistoryBox.SelectedItem is not HistoryEntry entry)
        {
            return;
        }

        InventoryDisplayState state = entry.Snapshot.ScanId == _freshScanId
            ? InventoryDisplayState.Fresh
            : entry.Snapshot.ScanId == _latestScanId
                ? InventoryDisplayState.Cached
                : InventoryDisplayState.Historical;

        DisplaySnapshot(entry.Snapshot, state);
    }

    private void DisplaySnapshot(ScanSnapshot snapshot, InventoryDisplayState state)
    {
        SetDisplayedDevices(snapshot.Observations, state, snapshot.ScanTimeUtc);
    }

    private void SetDisplayedDevices(IEnumerable<DeviceInfo> devices, InventoryDisplayState state, string? scanTimeUtc = null)
    {
        _allDevices.Clear();
        _allDevices.AddRange(devices);
        _diagnostics.EnrichAll(_allDevices, _isArabic);
        _displayState = state;
        _displayScanTimeUtc = scanTimeUtc;

        DeviceCount.Text = _allDevices.Count.ToString();
        HealthyCount.Text = _allDevices.Count(device => device.Diagnostics?.Outcome == DiagnosticOutcome.Healthy).ToString();
        ProblemCount.Text = _allDevices.Count(device => device.Diagnostics?.Outcome != DiagnosticOutcome.Healthy).ToString();

        ApplyFilters(false);
        DevicesGrid.SelectedIndex = DevicesGrid.Items.Count > 0 ? 0 : -1;
        UpdateDiagnosticDetails();
        UpdateInventoryStateText();
    }

    private void UpdateInventoryStateText()
    {
        string timestamp = _displayScanTimeUtc ?? "";
        InventoryStateText.Text = _displayState switch
        {
            InventoryDisplayState.Fresh when !string.IsNullOrEmpty(timestamp) => string.Format(
                Translate(
                    "Frischer Scan — {0}",
                    "فحص حديث — {0}",
                    "Fresh scan — {0}",
                    "Analyse récente — {0}",
                    "Análisis reciente — {0}"),
                timestamp),
            InventoryDisplayState.Fresh => Translate(
                    "Frischer Live-Scan — aktueller Snapshot nicht verfügbar.",
                    "فحص مباشر حديث — اللقطة الحالية غير متاحة.",
                    "Fresh live scan — latest snapshot unavailable.",
                    "Analyse récente — instantané le plus récent indisponible.",
                    "Análisis en vivo reciente — la instantánea más reciente no está disponible."),
            InventoryDisplayState.Cached => string.Format(
                Translate(
                    "Zwischengespeicherter Bestand — veraltet; letzter Scan: {0}",
                    "بيانات مخزنة مؤقتاً — قديمة؛ آخر فحص: {0}",
                    "Cached inventory — stale; last scan: {0}",
                    "Inventaire en cache — obsolète ; dernière analyse : {0}",
                    "Inventario en caché — desactualizado; último análisis: {0}"),
                timestamp),
            InventoryDisplayState.Historical => string.Format(
                Translate(
                    "Historischer Snapshot — nicht aktuell; Scan: {0}",
                    "لقطة تاريخية — ليست حالية؛ الفحص: {0}",
                    "Historical snapshot — not current; scan: {0}",
                    "Instantané historique — non actuel ; analyse : {0}",
                    "Instantánea histórica — no actual; análisis: {0}"),
                timestamp),
            InventoryDisplayState.LegacyCached => Translate(
                "Älterer zwischengespeicherter Bestand — veraltet; Zeitstempel nicht verfügbar.",
                "بيانات قديمة مخزنة مؤقتاً — غير محدثة؛ الطابع الزمني غير متاح.",
                "Legacy cached inventory — stale; timestamp unavailable.",
                "Ancien inventaire en cache — obsolète ; horodatage indisponible.",
                "Inventario heredado en caché — desactualizado; marca de tiempo no disponible."),
            _ => Translate(
                "Kein gespeicherter Bestand. Bitte einen Scan starten.",
                "لا توجد بيانات محفوظة. ابدأ الفحص.",
                "No persisted inventory. Start a scan.",
                "Aucun inventaire enregistré. Lancez une analyse.",
                "No hay inventario guardado. Inicie un análisis.")
        };
    }

    private enum InventoryDisplayState
    {
        Empty,
        LegacyCached,
        Cached,
        Historical,
        Fresh
    }

    private sealed record HistoryEntry(ScanSnapshot Snapshot, string Label);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void ApplyFilters(bool updateStatus = true)
    {
        if (!IsLoaded || DevicesGrid == null)
            return;

        IEnumerable<DeviceInfo> filtered = _allDevices;

        if (FilterBox.SelectedIndex == 1)
            filtered = filtered.Where(d => d.Diagnostics?.Outcome != DiagnosticOutcome.Healthy);
        else if (FilterBox.SelectedIndex == 2)
            filtered = filtered.Where(d => d.Diagnostics?.Outcome == DiagnosticOutcome.Healthy);

        string query = SearchBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(d =>
                (d.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Manufacturer ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.DeviceId ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Status ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DiagnosticSummary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DiagnosticEvidence.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        List<DeviceInfo> results = filtered.ToList();
        DevicesGrid.ItemsSource = results;
        UpdateDiagnosticDetails();

        if (updateStatus)
        {
            StatusText.Text = FilterResultsMessage(results.Count, _allDevices.Count);
        }
    }

    private void DevicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDiagnosticDetails();
    }

    private void UpdateDiagnosticDetails()
    {
        if (DiagnosticDetailsExpander is null || DevicesGrid is null)
        {
            return;
        }

        if (DevicesGrid.SelectedItem is not DeviceInfo device || device.Diagnostics is not { } report)
        {
            DiagnosticDetailsExpander.IsExpanded = false;
            return;
        }

        string problemCode = device.ProblemCode?.ToString() ??
            Translate("Nicht verfügbar", "غير متاح", "Unavailable", "Indisponible", "No disponible");
        string status = string.IsNullOrWhiteSpace(device.Status)
            ? Translate("Nicht verfügbar", "غير متاح", "Unavailable", "Indisponible", "No disponible")
            : device.Status;

        DiagnosticDetailsExpander.Header = Translate(
            "Gerätediagnose",
            "تشخيص الجهاز",
            "Device diagnostics",
            "Diagnostic de l’appareil",
            "Diagnóstico del dispositivo");
        DiagnosticDeviceText.Text = $"{device.Name} — {device.Manufacturer}";
        DiagnosticStatusText.Text = string.Format(
            Translate(
                "WMI-Status: {0} | Konfigurationscode: {1}",
                "حالة WMI: {0} | رمز التكوين: {1}",
                "WMI status: {0} | Configuration code: {1}",
                "État WMI : {0} | Code de configuration : {1}",
                "Estado WMI: {0} | Código de configuración: {1}"),
            status, problemCode);
        DiagnosticAssessmentText.Text = string.Format(
            Translate(
                "Bewertung: {0} | Schweregrad: {1}",
                "التقييم: {0} | الشدة: {1}",
                "Assessment: {0} | Severity: {1}",
                "Évaluation : {0} | Gravité : {1}",
                "Evaluación: {0} | Gravedad: {1}"),
            report.OutcomeLabel, report.SeverityLabel);
        DiagnosticIssueText.Text = string.Format(
            Translate(
                "Befund: {0} — {1}",
                "النتيجة: {0} — {1}",
                "Detected issue: {0} — {1}",
                "Constat : {0} — {1}",
                "Hallazgo: {0} — {1}"),
            report.Summary, report.Description);
        DiagnosticEvidenceText.Text = string.Format(
            Translate(
                "Belege: {0}",
                "الأدلة: {0}",
                "Evidence: {0}",
                "Éléments probants : {0}",
                "Evidencia: {0}"),
            report.Evidence);
        DiagnosticRecommendationText.Text = string.Format(
            Translate(
                "Nächster Schritt: {0}",
                "الخطوة التالية: {0}",
                "Next step: {0}",
                "Étape suivante : {0}",
                "Siguiente paso: {0}"),
            report.SafeRecommendation);
        DiagnosticDetailsExpander.IsExpanded = true;
    }

    private string Translate(string de, string ar, string en, string fr, string es)
    {
        return _languageIndex switch
        {
            1 => ar,
            2 => en,
            3 => fr,
            4 => es,
            _ => de
        };
    }

    private string DatabaseReadyMessage(string path)
    {
        return string.Format(
            Translate(
                "Bereit. Lokale Datenbank: {0}",
                "جاهز. قاعدة البيانات المحلية: {0}",
                "Ready. Local database: {0}",
                "Prêt. Base de données locale : {0}",
                "Listo. Base de datos local: {0}"),
            path);
    }

    private string DatabaseErrorMessage(string message)
    {
        return Translate(
            $"Datenbankfehler: {message}",
            $"خطأ في قاعدة البيانات: {message}",
            $"Database error: {message}",
            $"Erreur de base de données : {message}",
            $"Error de base de datos: {message}");
    }

    private string ScanInProgressMessage()
    {
        return Translate(
            "Geräte werden gelesen...",
            "جارٍ فحص الأجهزة...",
            "Scanning devices...",
            "Analyse des appareils en cours...",
            "Analizando dispositivos...");
    }

    private string ScanCompletedMessage(int count)
    {
        return string.Format(
            Translate(
                "Scan abgeschlossen: {0} Geräte. Es wurden keine Treiber verändert.",
                "اكتمل الفحص: {0} جهازاً. لم يتم تغيير أي تعريف.",
                "Scan completed: {0} devices. No drivers were changed.",
                "Analyse terminée : {0} appareils. Aucun pilote n’a été modifié.",
                "Análisis completado: {0} dispositivos. No se modificó ningún controlador."),
            count);
    }

    private string FreshScanHistoryUnavailableMessage()
    {
        return Translate(
            "Scan abgeschlossen. Der aktuelle Snapshot ist nicht verfügbar; Live-Ergebnisse werden angezeigt.",
            "اكتمل الفحص. اللقطة الحالية غير متاحة؛ يتم عرض نتائج الفحص المباشر.",
            "Scan completed. The latest snapshot is unavailable; live results are displayed.",
            "Analyse terminée. L’instantané le plus récent est indisponible ; les résultats en direct sont affichés.",
            "Análisis completado. La instantánea más reciente no está disponible; se muestran resultados en vivo.");
    }

    private string ScanCancelledMessage()
    {
        return Translate(
            "Scan abgebrochen oder Zeitüberschreitung.",
            "تم إلغاء الفحص أو انتهت مهلة الاستجابة.",
            "Scan cancelled or timed out.",
            "Analyse annulée ou délai d’attente dépassé.",
            "Análisis cancelado o tiempo de espera agotado.");
    }

    private string ScanErrorMessage(string message)
    {
        return Translate(
            $"Scan-Fehler: {message}",
            $"خطأ أثناء الفحص: {message}",
            $"Scan error: {message}",
            $"Erreur d’analyse : {message}",
            $"Error de análisis: {message}");
    }

    private string ScanErrorTitle()
    {
        return Translate(
            "Scan-Fehler",
            "خطأ في الفحص",
            "Scan error",
            "Erreur d’analyse",
            "Error de análisis");
    }

    private string FilterResultsMessage(int shown, int total)
    {
        return string.Format(
            Translate(
                "Anzeige: {0} von {1} Geräten.",
                "عرض {0} من أصل {1} جهاز.",
                "Showing {0} of {1} devices.",
                "{0} appareils affichés sur {1}.",
                "Mostrando {0} de {1} dispositivos."),
            shown, total);
    }
    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        int languageIndex = LanguageBox.SelectedIndex;
        if (languageIndex < 0 || languageIndex > 4)
            languageIndex = 0;

        _languageIndex = languageIndex;
        _isArabic = languageIndex == 1;

        FlowDirection = _isArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        string[] pageTitles =
        {
            "System Dashboard",
            "لوحة معلومات النظام",
            "System Dashboard",
            "Tableau de bord",
            "Panel del sistema"
        };

        string[] subtitles =
        {
            "Lokale Geräteübersicht — keine Änderungen am System",
            "قائمة محلية للأجهزة — دون تغيير النظام",
            "Local device overview — no system changes",
            "Vue locale des appareils — aucune modification du système",
            "Resumen local de dispositivos — sin cambios en el sistema"
        };

        string[] scanButtons =
        {
            "Jetzt scannen",
            "ابدأ الفحص",
            "Scan now",
            "Analyser maintenant",
            "Analizar ahora"
        };

        string[] devicesLabels =
        {
            "Erkannte Geräte",
            "الأجهزة المكتشفة",
            "Detected devices",
            "Appareils détectés",
            "Dispositivos detectados"
        };

        string[] healthyLabels =
        {
            "Ohne gemeldeten Fehler",
            "بدون أخطاء مسجلة",
            "No reported errors",
            "Sans erreur signalée",
            "Sin errores registrados"
        };

        string[] problemLabels =
        {
            "Probleme / unbekannt",
            "مشاكل / غير معروف",
            "Issues / unknown",
            "Problèmes / inconnus",
            "Problemas / desconocido"
        };

        string[] searchLabels =
        {
            "Suche:",
            "بحث:",
            "Search:",
            "Recherche :",
            "Buscar:"
        };

        string[] filterLabels =
        {
            "Filter:",
            "تصفية:",
            "Filter:",
            "Filtre :",
            "Filtro:"
        };

        string[][] filterOptions =
        {
            new[] { "Alle Geräte", "Probleme oder unbekannt", "Ohne gemeldete Probleme" },
            new[] { "كل الأجهزة", "مشاكل أو حالة غير معروفة", "دون مشاكل مُبلّغ عنها" },
            new[] { "All devices", "Issues or unknown", "No reported issues" },
            new[] { "Tous les appareils", "Problèmes ou inconnus", "Aucun problème signalé" },
            new[] { "Todos los dispositivos", "Problemas o desconocidos", "Sin problemas reportados" }
        };

        string[] searchTooltips =
        {
            "Suche nach Name, Hersteller oder Diagnose",
            "ابحث بالاسم أو الشركة المصنعة أو التشخيص",
            "Search by name, manufacturer, or diagnostic finding",
            "Rechercher par nom, fabricant ou constat",
            "Buscar por nombre, fabricante o diagnóstico"
        };

        string[][] columnHeaders =
        {
            new[] { "Gerät", "Hersteller", "WMI-Status", "Problemcode", "Bewertung", "Schweregrad", "Befund" },
            new[] { "الجهاز", "الشركة المصنعة", "حالة WMI", "رمز المشكلة", "التقييم", "الشدة", "النتيجة" },
            new[] { "Device", "Manufacturer", "WMI status", "Problem code", "Assessment", "Severity", "Finding" },
            new[] { "Appareil", "Fabricant", "État WMI", "Code erreur", "Évaluation", "Gravité", "Constat" },
            new[] { "Dispositivo", "Fabricante", "Estado WMI", "Código", "Evaluación", "Gravedad", "Hallazgo" }
        };

        // Sidebar translations
        string[] editionLabels =
        {
            "Unabhängige Windows-Edition",
            "إصدار مستقل لنظام Windows",
            "Independent Windows Edition",
            "Édition Windows indépendante",
            "Edición independiente de Windows"
        };

        string[] dashboardLabels =
        {
            "Übersicht",
            "لوحة المعلومات",
            "Dashboard",
            "Tableau de bord",
            "Panel"
        };

        string[] inventoryLabels =
        {
            "Geräteübersicht",
            "قائمة الأجهزة",
            "Device inventory",
            "Inventaire des appareils",
            "Inventario de dispositivos"
        };

        string[] languageLabels =
        {
            "Sprache / Language",
            "اللغة",
            "Language",
            "Langue",
            "Idioma"
        };

        string[] versionLabels =
        {
            "Version 0.2 — Nur-Lese-MVP",
            "الإصدار 0.2 — نسخة للقراءة فقط",
            "Version 0.2 — Read-only MVP",
            "Version 0.2 — Version en lecture seule",
            "Versión 0.2 — Solo lectura"
        };

        string[] readyMessages =
        {
            "Bereit.",
            "جاهز.",
            "Ready.",
            "Prêt.",
            "Listo."
        };

        string[] historyLabels =
        {
            "Scan-Verlauf:",
            "سجل الفحوصات:",
            "Scan history:",
            "Historique des analyses :",
            "Historial de análisis:"
        };

        PageTitle.Text = pageTitles[languageIndex];
        PageSubtitle.Text = subtitles[languageIndex];
        ScanButton.Content = scanButtons[languageIndex];

        DevicesLabel.Text = devicesLabels[languageIndex];
        HealthyLabel.Text = healthyLabels[languageIndex];
        ProblemLabel.Text = problemLabels[languageIndex];

        SearchLabel.Text = searchLabels[languageIndex];
        FilterLabel.Text = filterLabels[languageIndex];
        HistoryLabel.Text = historyLabels[languageIndex];

        // Math.Min: UI count vs translation count - prefer visible stale over crash
        int filterCount = Math.Min(FilterBox.Items.Count, filterOptions[languageIndex].Length);
        for (int i = 0; i < filterCount; i++)
            ((ComboBoxItem)FilterBox.Items[i]).Content = filterOptions[languageIndex][i];

        SearchBox.ToolTip = searchTooltips[languageIndex];

        // Math.Min: UI count vs translation count - prefer visible stale over crash
        int columnCount = Math.Min(DevicesGrid.Columns.Count, columnHeaders[languageIndex].Length);
        for (int i = 0; i < columnCount; i++)
            DevicesGrid.Columns[i].Header = columnHeaders[languageIndex][i];

        _diagnostics.EnrichAll(_allDevices, _isArabic);
        ApplyFilters(false);
        UpdateDiagnosticDetails();
        RefreshHistoryLabels();
        UpdateInventoryStateText();
        EditionLabel.Text = editionLabels[languageIndex];
        DashboardButton.Content = dashboardLabels[languageIndex];
        InventoryButton.Content = inventoryLabels[languageIndex];
        LanguageLabel.Text = languageLabels[languageIndex];
        VersionLabel.Text = versionLabels[languageIndex];

        StatusText.Text = readyMessages[languageIndex];
    }
}
