namespace DriverMasterPro.Services.Localization;

/// <summary>Localized UI strings, LanguageBox order: 0=de, 1=ar, 2=en, 3=fr, 4=es. Verbatim from ApplyLanguage (task 1a).</summary>
internal static class LocalizationCatalog
{
    public static readonly string[] PageTitles = {
        "System Dashboard",
        "لوحة معلومات النظام",
        "System Dashboard",
        "Tableau de bord",
        "Panel del sistema"
    };
    public static readonly string[] Subtitles = {
        "Lokale Geräteübersicht — keine Änderungen am System",
        "قائمة محلية للأجهزة — دون تغيير النظام",
        "Local device overview — no system changes",
        "Vue locale des appareils — aucune modification du système",
        "Resumen local de dispositivos — sin cambios en el sistema"
    };
    public static readonly string[] ScanButtons = {
        "Jetzt scannen",
        "ابدأ الفحص",
        "Scan now",
        "Analyser maintenant",
        "Analizar ahora"
    };
    public static readonly string[] DevicesLabels = {
        "Erkannte Geräte",
        "الأجهزة المكتشفة",
        "Detected devices",
        "Appareils détectés",
        "Dispositivos detectados"
    };
    public static readonly string[] HealthyLabels = {
        "Ohne gemeldeten Fehler",
        "بدون أخطاء مسجلة",
        "No reported errors",
        "Sans erreur signalée",
        "Sin errores registrados"
    };
    public static readonly string[] ProblemLabels = {
        "Probleme / unbekannt",
        "مشاكل / غير معروف",
        "Issues / unknown",
        "Problèmes / inconnus",
        "Problemas / desconocido"
    };
    public static readonly string[] SearchLabels = {
        "Suche:",
        "بحث:",
        "Search:",
        "Recherche :",
        "Buscar:"
    };
    public static readonly string[] FilterLabels = {
        "Filter:",
        "تصفية:",
        "Filter:",
        "Filtre :",
        "Filtro:"
    };
    public static readonly string[][] FilterOptions = {
        new[] { "Alle Geräte", "Probleme oder unbekannt", "Ohne gemeldete Probleme" },
        new[] { "كل الأجهزة", "مشاكل أو حالة غير معروفة", "دون مشاكل مُبلّغ عنها" },
        new[] { "All devices", "Issues or unknown", "No reported issues" },
        new[] { "Tous les appareils", "Problèmes ou inconnus", "Aucun problème signalé" },
        new[] { "Todos los dispositivos", "Problemas o desconocidos", "Sin problemas reportados" }
    };
    public static readonly string[] SearchTooltips = {
        "Suche nach Name, Hersteller oder Diagnose",
        "ابحث بالاسم أو الشركة المصنعة أو التشخيص",
        "Search by name, manufacturer, or diagnostic finding",
        "Rechercher par nom, fabricant ou constat",
        "Buscar por nombre, fabricante o diagnóstico"
    };
    public static readonly string[][] ColumnHeaders = {
        new[] { "Gerät", "Hersteller", "WMI-Status", "Problemcode", "Bewertung", "Schweregrad", "Befund" },
        new[] { "الجهاز", "الشركة المصنعة", "حالة WMI", "رمز المشكلة", "التقييم", "الشدة", "النتيجة" },
        new[] { "Device", "Manufacturer", "WMI status", "Problem code", "Assessment", "Severity", "Finding" },
        new[] { "Appareil", "Fabricant", "État WMI", "Code erreur", "Évaluation", "Gravité", "Constat" },
        new[] { "Dispositivo", "Fabricante", "Estado WMI", "Código", "Evaluación", "Gravedad", "Hallazgo" }
    };
    public static readonly string[] EditionLabels = {
        "Unabhängige Windows-Edition",
        "إصدار مستقل لنظام Windows",
        "Independent Windows Edition",
        "Édition Windows indépendante",
        "Edición independiente de Windows"
    };
    public static readonly string[] DashboardLabels = {
        "Übersicht",
        "لوحة المعلومات",
        "Dashboard",
        "Tableau de bord",
        "Panel"
    };
    public static readonly string[] InventoryLabels = {
        "Geräteübersicht",
        "قائمة الأجهزة",
        "Device inventory",
        "Inventaire des appareils",
        "Inventario de dispositivos"
    };
    public static readonly string[] LanguageLabels = {
        "Sprache / Language",
        "اللغة",
        "Language",
        "Langue",
        "Idioma"
    };
    public static readonly string[] ReadyMessages = {
        "Bereit.",
        "جاهز.",
        "Ready.",
        "Prêt.",
        "Listo."
    };
    public static readonly string[] HistoryLabels = {
        "Scan-Verlauf:",
        "سجل الفحوصات:",
        "Scan history:",
        "Historique des analyses :",
        "Historial de análisis:"
    };
}
