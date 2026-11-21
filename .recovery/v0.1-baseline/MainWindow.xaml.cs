using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DriverMasterPro.Models;
using DriverMasterPro.Services;

namespace DriverMasterPro;

public partial class MainWindow : Window
{
    private readonly DeviceScanner _scanner = new();
    private readonly DatabaseService _database = new();
    private readonly List<DeviceInfo> _allDevices = new();
    private bool _isArabic;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            try
            {
                await _database.InitializeAsync();
                StatusText.Text = $"Bereit. Lokale Datenbank: {_database.DatabasePath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Datenbankfehler: {ex.Message}";
            }
        };
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        StatusText.Text = _isArabic ? "جارٍ فحص الأجهزة..." : "Geräte werden gelesen...";

        try
        {
            List<DeviceInfo> devices = await _scanner.ScanAsync();

            await _database.SaveScanAsync(devices);

            _allDevices.Clear();
            _allDevices.AddRange(devices);

            DeviceCount.Text = devices.Count.ToString();
            HealthyCount.Text = devices.Count(d => d.ProblemCode == 0).ToString();
            ProblemCount.Text = devices.Count(d => d.ProblemCode != 0).ToString();

            ApplyFilters(false);

            StatusText.Text = _isArabic
                ? $"اكتمل الفحص: {devices.Count} جهازاً. لم يتم تغيير أي تعريف."
                : $"Scan abgeschlossen: {devices.Count} Geräte. Es wurden keine Treiber verändert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan-Fehler: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Driver Master Pro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanButton.IsEnabled = true;
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
            StatusText.Text = _isArabic
                ? $"عرض {results.Count} من أصل {_allDevices.Count} جهاز."
                : $"Anzeige: {results.Count} von {_allDevices.Count} Geräten.";
        }
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _isArabic = LanguageBox.SelectedIndex == 1;

        FlowDirection = _isArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        PageTitle.Text = _isArabic ? "لوحة معلومات النظام" : "System Dashboard";
        PageSubtitle.Text = _isArabic
            ? "قائمة محلية للأجهزة — دون تغيير النظام"
            : "Lokale Geräteübersicht — keine Änderungen am System";

        ScanButton.Content = _isArabic ? "ابدأ الفحص" : "Jetzt scannen";

        DevicesLabel.Text = _isArabic ? "الأجهزة المكتشفة" : "Erkannte Geräte";
        HealthyLabel.Text = _isArabic ? "بدون أخطاء مسجلة" : "Ohne gemeldeten Fehler";
        ProblemLabel.Text = _isArabic ? "مشاكل مسجلة" : "Gemeldete Probleme";

        SearchLabel.Text = _isArabic ? "بحث:" : "Suche:";
        FilterLabel.Text = _isArabic ? "تصفية:" : "Filter:";

        ((ComboBoxItem)FilterBox.Items[0]).Content = _isArabic ? "كل الأجهزة" : "Alle Geräte";
        ((ComboBoxItem)FilterBox.Items[1]).Content = _isArabic ? "الأجهزة التي بها مشاكل" : "Nur mit Problemen";
        ((ComboBoxItem)FilterBox.Items[2]).Content = _isArabic ? "الأجهزة السليمة" : "Ohne Probleme";

        SearchBox.ToolTip = _isArabic
            ? "ابحث بالاسم أو الشركة المصنعة"
            : "Suche nach Name oder Hersteller";

        DevicesGrid.Columns[0].Header = _isArabic ? "الجهاز" : "Gerät";
        DevicesGrid.Columns[1].Header = _isArabic ? "الشركة المصنعة" : "Hersteller";
        DevicesGrid.Columns[2].Header = _isArabic ? "الحالة" : "Status";
        DevicesGrid.Columns[3].Header = _isArabic ? "رمز المشكلة" : "Problemcode";
        DevicesGrid.Columns[4].Header = _isArabic ? "التقييم" : "Bewertung";

        ApplyFilters(false);
        StatusText.Text = _isArabic ? "جاهز." : "Bereit.";
    }
}
