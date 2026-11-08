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
    private readonly List<DeviceInfo> _allDevices = new();
    private CancellationTokenSource? _scanCts;
    private int _isScanning = 0;
    private volatile bool _isClosed = false;
    private bool _isArabic;
    private int _languageIndex = 0;

    public MainWindow(IDeviceScanner scanner, IDatabaseService database)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _database = database ?? throw new ArgumentNullException(nameof(database));

        InitializeComponent();

        Loaded += async (_, _) =>
        {
            try
            {
                await _database.InitializeAsync();
                if (!_isClosed && IsLoaded)
                {
                    StatusText.Text = DatabaseReadyMessage(_database.DatabasePath);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[MainWindow] Database initialization failed: {ex}");
                if (!_isClosed && IsLoaded)
                {
                    StatusText.Text = DatabaseErrorMessage(ex.Message);
                }
            }
        };
    }

    public MainWindow() : this(new DeviceScanner(), new DatabaseService())
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
        // Prevent concurrent scans
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
        {
            return;
        }

        ScanButton.IsEnabled = false;
        StatusText.Text = ScanInProgressMessage();

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = _scanCts.Token;

        try
        {
            List<DeviceInfo> devices = await _scanner.ScanAsync(token);

            await _database.SaveScanAsync(devices, token);

            // Ensure background work cannot update a closed or unloaded window
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            _allDevices.Clear();
            _allDevices.AddRange(devices);

            DeviceCount.Text = devices.Count.ToString();
            HealthyCount.Text = devices.Count(d => d.ProblemCode == 0).ToString();
            ProblemCount.Text = devices.Count(d => d.ProblemCode != 0).ToString();

            ApplyFilters(false);

            StatusText.Text = ScanCompletedMessage(devices.Count);
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
            }
        }
    }

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
            filtered = filtered.Where(d => d.ProblemCode != 0);
        else if (FilterBox.SelectedIndex == 2)
            filtered = filtered.Where(d => d.ProblemCode == 0);

        string query = SearchBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(d =>
                (d.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Manufacturer ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.DeviceId ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Status ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        List<DeviceInfo> results = filtered.ToList();
        DevicesGrid.ItemsSource = results;

        if (updateStatus)
        {
            StatusText.Text = FilterResultsMessage(results.Count, _allDevices.Count);
        }
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
            "Gemeldete Probleme",
            "مشاكل مسجلة",
            "Reported problems",
            "Problèmes signalés",
            "Problemas detectados"
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
            new[] { "Alle Geräte", "Nur mit Problemen", "Ohne Probleme" },
            new[] { "كل الأجهزة", "الأجهزة التي بها مشاكل", "الأجهزة السليمة" },
            new[] { "All devices", "Devices with problems", "Devices without problems" },
            new[] { "Tous les appareils", "Avec problèmes", "Sans problème" },
            new[] { "Todos los dispositivos", "Con problemas", "Sin problemas" }
        };

        string[] searchTooltips =
        {
            "Suche nach Name oder Hersteller",
            "ابحث بالاسم أو الشركة المصنعة",
            "Search by name or manufacturer",
            "Rechercher par nom ou fabricant",
            "Buscar por nombre o fabricante"
        };

        string[][] columnHeaders =
        {
            new[] { "Gerät", "Hersteller", "Status", "Problemcode", "Bewertung" },
            new[] { "الجهاز", "الشركة المصنعة", "الحالة", "رمز المشكلة", "التقييم" },
            new[] { "Device", "Manufacturer", "Status", "Problem code", "Health" },
            new[] { "Appareil", "Fabricant", "État", "Code erreur", "Évaluation" },
            new[] { "Dispositivo", "Fabricante", "Estado", "Código de problema", "Evaluación" }
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

        PageTitle.Text = pageTitles[languageIndex];
        PageSubtitle.Text = subtitles[languageIndex];
        ScanButton.Content = scanButtons[languageIndex];

        DevicesLabel.Text = devicesLabels[languageIndex];
        HealthyLabel.Text = healthyLabels[languageIndex];
        ProblemLabel.Text = problemLabels[languageIndex];

        SearchLabel.Text = searchLabels[languageIndex];
        FilterLabel.Text = filterLabels[languageIndex];

        for (int i = 0; i < 3; i++)
            ((ComboBoxItem)FilterBox.Items[i]).Content = filterOptions[languageIndex][i];

        SearchBox.ToolTip = searchTooltips[languageIndex];

        for (int i = 0; i < 5; i++)
            DevicesGrid.Columns[i].Header = columnHeaders[languageIndex][i];

        ApplyFilters(false);
        EditionLabel.Text = editionLabels[languageIndex];
        DashboardButton.Content = dashboardLabels[languageIndex];
        InventoryButton.Content = inventoryLabels[languageIndex];
        LanguageLabel.Text = languageLabels[languageIndex];
        VersionLabel.Text = versionLabels[languageIndex];

        StatusText.Text = readyMessages[languageIndex];
    }
}
