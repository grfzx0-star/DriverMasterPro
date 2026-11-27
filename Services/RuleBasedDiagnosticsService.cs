using System.Collections.Generic;
using DriverMasterPro.Models;
using DriverMasterPro.Services.Abstractions;

namespace DriverMasterPro.Services;

public sealed class RuleBasedDiagnosticsService : IDiagnosticsService
{
    private sealed record Rule(
        string Symbol,
        string Summary,
        string ArabicSummary,
        ProblemSeverity Severity,
        string Recommendation,
        string ArabicRecommendation);

    private static readonly IReadOnlyDictionary<uint, Rule> Rules = new Dictionary<uint, Rule>
    {
        [1] = new("CM_PROB_NOT_CONFIGURED", "Device not configured (Code 1)", "الجهاز غير مهيأ (رمز 1)", ProblemSeverity.Warning, "Check Windows Update or the device manufacturer's support information.", "تحقق من Windows Update أو معلومات الدعم من الشركة المصنعة."),
        [3] = new("CM_PROB_OUT_OF_MEMORY", "Insufficient system resources (Code 3)", "موارد النظام غير كافية (رمز 3)", ProblemSeverity.Critical, "Restart Windows; if the code remains, check the device manufacturer's support information.", "أعد تشغيل Windows؛ وإذا استمر الرمز، فتحقق من معلومات دعم الشركة المصنعة."),
        [10] = new("CM_PROB_FAILED_START", "Device failed to start (Code 10)", "تعذر بدء تشغيل الجهاز (رمز 10)", ProblemSeverity.Critical, "Check Device Manager and the device manufacturer's support information.", "تحقق من إدارة الأجهزة ومعلومات دعم الشركة المصنعة."),
        [12] = new("CM_PROB_NORMAL_CONFLICT", "Resource conflict reported (Code 12)", "تم الإبلاغ عن تعارض في الموارد (رمز 12)", ProblemSeverity.Warning, "Review the device's resource status in Device Manager.", "راجع حالة موارد الجهاز في إدارة الأجهزة."),
        [14] = new("CM_PROB_NEED_RESTART", "Restart required (Code 14)", "إعادة التشغيل مطلوبة (رمز 14)", ProblemSeverity.Notice, "Restart Windows to complete device setup.", "أعد تشغيل Windows لإكمال إعداد الجهاز."),
        [18] = new("CM_PROB_REINSTALL", "Device installation needs attention (Code 18)", "يحتاج تثبيت الجهاز إلى مراجعة (رمز 18)", ProblemSeverity.Warning, "Review the device in Device Manager and consult the manufacturer's support information.", "راجع الجهاز في إدارة الأجهزة ومعلومات دعم الشركة المصنعة."),
        [21] = new("CM_PROB_WILL_BE_REMOVED", "Device removal is pending (Code 21)", "إزالة الجهاز معلقة (رمز 21)", ProblemSeverity.Notice, "Wait for Windows to finish updating the device inventory, then scan again.", "انتظر حتى ينتهي Windows من تحديث قائمة الأجهزة، ثم أعد الفحص."),
        [22] = new("CM_PROB_DISABLED", "Device is disabled (Code 22)", "الجهاز معطل (رمز 22)", ProblemSeverity.Notice, "If the device is needed, review its state in Device Manager.", "إذا كنت بحاجة إلى الجهاز، فراجع حالته في إدارة الأجهزة."),
        [24] = new("CM_PROB_DEVLOADER_NOT_READY", "Device is not present or is not responding (Code 24)", "الجهاز غير موجود أو لا يستجيب (رمز 24)", ProblemSeverity.Warning, "Check whether the device is connected and review its status in Device Manager.", "تحقق من توصيل الجهاز وراجع حالته في إدارة الأجهزة."),
        [28] = new("CM_PROB_FAILED_INSTALL", "Device drivers are not installed (Code 28)", "لم يتم تثبيت برامج تشغيل الجهاز (رمز 28)", ProblemSeverity.Critical, "Check Windows Update or the device manufacturer's official support information.", "تحقق من Windows Update أو معلومات الدعم الرسمية من الشركة المصنعة."),
        [29] = new("CM_PROB_HARDWARE_DISABLED", "Device is disabled by firmware (Code 29)", "الجهاز معطل بواسطة البرنامج الثابت (رمز 29)", ProblemSeverity.Warning, "If needed, review the device's firmware settings using the device manufacturer's guidance.", "إذا لزم الأمر، فراجع إعدادات البرنامج الثابت وفق إرشادات الشركة المصنعة."),
        [31] = new("CM_PROB_FAILED_ADD", "Windows could not load the device (Code 31)", "تعذر على Windows تحميل الجهاز (رمز 31)", ProblemSeverity.Warning, "Review the device's status in Device Manager and consult manufacturer support.", "راجع حالة الجهاز في إدارة الأجهزة ومعلومات دعم الشركة المصنعة."),
        [32] = new("CM_PROB_DISABLED_SERVICE", "Device service is disabled (Code 32)", "خدمة الجهاز معطلة (رمز 32)", ProblemSeverity.Warning, "Review the device and its service configuration with an administrator or the device manufacturer.", "راجع الجهاز وإعدادات خدمته مع مسؤول النظام أو الشركة المصنعة."),
        [37] = new("CM_PROB_FAILED_DRIVER_ENTRY", "Windows reported a driver initialization failure (Code 37)", "أبلغ Windows عن فشل تهيئة برنامج التشغيل (رمز 37)", ProblemSeverity.Critical, "Check Device Manager and consult the device manufacturer's support information.", "تحقق من إدارة الأجهزة ومعلومات دعم الشركة المصنعة."),
        [38] = new("CM_PROB_DRIVER_FAILED_PRIOR_UNLOAD", "A previous driver instance could not be unloaded (Code 38)", "تعذر إلغاء تحميل نسخة سابقة من برنامج التشغيل (رمز 38)", ProblemSeverity.Warning, "Restart Windows and scan again.", "أعد تشغيل Windows ثم أعد الفحص."),
        [39] = new("CM_PROB_DRIVER_FAILED_LOAD", "Windows could not load the device driver (Code 39)", "تعذر على Windows تحميل برنامج تشغيل الجهاز (رمز 39)", ProblemSeverity.Critical, "Check Device Manager and consult the device manufacturer's support information.", "تحقق من إدارة الأجهزة ومعلومات دعم الشركة المصنعة."),
        [41] = new("CM_PROB_DRIVER_SERVICE_KEY_INVALID", "Windows could not find the device (Code 41)", "تعذر على Windows العثور على الجهاز (رمز 41)", ProblemSeverity.Warning, "Check whether the device is connected, then scan again.", "تحقق من توصيل الجهاز، ثم أعد الفحص."),
        [43] = new("CM_PROB_DEVICE_PROBLEM", "Windows reported a problem with the device (Code 43)", "أبلغ Windows عن مشكلة في الجهاز (رمز 43)", ProblemSeverity.Critical, "Review the device in Device Manager and consult manufacturer support; this code alone does not identify the underlying cause.", "راجع الجهاز في إدارة الأجهزة ومعلومات دعم الشركة المصنعة؛ فالرمز وحده لا يحدد السبب."),
        [47] = new("CM_PROB_HELD_FOR_EJECT", "Device is prepared for safe removal (Code 47)", "الجهاز مهيأ للإزالة الآمنة (رمز 47)", ProblemSeverity.Notice, "If the device should remain in use, reconnect it and scan again.", "إذا كان يجب استخدام الجهاز، فأعد توصيله ثم أعد الفحص."),
        [48] = new("CM_PROB_DRIVER_BLOCKED", "Windows blocked the device driver (Code 48)", "حظر Windows برنامج تشغيل الجهاز (رمز 48)", ProblemSeverity.Critical, "Check Windows Update or consult the device manufacturer for a compatible driver.", "تحقق من Windows Update أو استشر الشركة المصنعة بشأن برنامج تشغيل متوافق."),
        [52] = new("CM_PROB_UNSIGNED_DRIVER", "Windows could not verify the driver signature (Code 52)", "تعذر على Windows التحقق من توقيع برنامج التشغيل (رمز 52)", ProblemSeverity.Critical, "Consult the device manufacturer for an appropriately signed driver.", "استشر الشركة المصنعة للحصول على برنامج تشغيل ذي توقيع مناسب.")
    };

    public DiagnosticReport Resolve(uint? problemCode, bool isArabic = false, string? deviceStatus = null)
    {
        if (problemCode is null)
        {
            return Unknown(null, isArabic, deviceStatus, missingCode: true);
        }

        if (problemCode == 0)
        {
            if (!string.IsNullOrWhiteSpace(deviceStatus) &&
                !string.Equals(deviceStatus.Trim(), "OK", StringComparison.OrdinalIgnoreCase))
            {
                return Unknown(problemCode, isArabic, deviceStatus, missingCode: false);
            }

            return new DiagnosticReport(
                problemCode,
                "CM_PROB_OK",
                DiagnosticOutcome.Healthy,
                ProblemSeverity.Normal,
                isArabic ? "سليم: لم يتم الإبلاغ عن مشكلة" : "Healthy: no problem reported",
                isArabic ? "لا توجد شدة مشكلة مُبلّغ عنها" : "No reported issue severity",
                isArabic ? "لم يتم الإبلاغ عن مشكلة في التكوين." : "No configuration problem was reported.",
                isArabic
                    ? "الرمز صفر يعني عدم الإبلاغ عن مشكلة من مدير تكوين Windows، ولا يمثل تقييماً شاملاً لصحة الجهاز أو برنامج التشغيل."
                    : "Code zero means Windows Configuration Manager reported no problem; it is not a comprehensive hardware or driver health assessment.",
                Evidence(problemCode, "CM_PROB_OK", deviceStatus),
                isArabic ? "لا يلزم إجراء بناءً على هذا الرمز." : "No action is indicated by this code.");
        }

        if (!Rules.TryGetValue(problemCode.Value, out var rule))
        {
            return Unknown(problemCode, isArabic, deviceStatus, missingCode: false);
        }

        var outcome = rule.Severity switch
        {
            ProblemSeverity.Critical => DiagnosticOutcome.Critical,
            ProblemSeverity.Warning or ProblemSeverity.Notice => DiagnosticOutcome.Warning,
            _ => DiagnosticOutcome.Unknown
        };

        return new DiagnosticReport(
            problemCode,
            rule.Symbol,
            outcome,
            rule.Severity,
            OutcomeLabel(outcome, isArabic),
            SeverityLabel(rule.Severity, isArabic),
            isArabic ? rule.ArabicSummary : rule.Summary,
            isArabic
                ? $"أبلغ مدير تكوين Windows عن الرمز {problemCode} ({rule.Symbol}). يصف الرمز المشكلة المُبلّغ عنها ولا يحدد سببها الجذري."
                : $"Windows Configuration Manager reported code {problemCode} ({rule.Symbol}). The code describes the reported condition but does not establish its root cause.",
            Evidence(problemCode, rule.Symbol, deviceStatus),
            isArabic ? rule.ArabicRecommendation : rule.Recommendation);
    }

    public void Enrich(DeviceInfo device, bool isArabic = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Diagnostics = Resolve(device.ProblemCode, isArabic, device.Status);
    }

    public void EnrichAll(IEnumerable<DeviceInfo> devices, bool isArabic = false)
    {
        ArgumentNullException.ThrowIfNull(devices);
        foreach (var device in devices)
        {
            Enrich(device, isArabic);
        }
    }

    private static DiagnosticReport Unknown(uint? problemCode, bool isArabic, string? deviceStatus, bool missingCode)
    {
        var symbol = missingCode ? "CM_PROB_UNAVAILABLE" : "CM_PROB_UNCLASSIFIED";
        var status = OutcomeLabel(DiagnosticOutcome.Unknown, isArabic);
        var severity = SeverityLabel(ProblemSeverity.Unknown, isArabic);
        var evidence = missingCode
            ? isArabic ? "لم تتوفر قيمة ConfigManagerErrorCode." : "No ConfigManagerErrorCode value was available."
            : isArabic
                ? $"أبلغ Windows عن ConfigManagerErrorCode = {problemCode}، ولا توجد قاعدة تشخيصية موثقة لهذا الرمز."
                : $"Windows reported ConfigManagerErrorCode = {problemCode}; no verified diagnostic rule is available for this code.";

        if (!string.IsNullOrWhiteSpace(deviceStatus))
        {
            evidence += isArabic
                ? $" حالة WMI: {deviceStatus.Trim()}."
                : $" WMI Status: {deviceStatus.Trim()}.";
        }

        return new DiagnosticReport(
            problemCode,
            symbol,
            DiagnosticOutcome.Unknown,
            ProblemSeverity.Unknown,
            status,
            severity,
            isArabic ? "الأدلة غير كافية لتحديد الحالة" : "Insufficient evidence to classify device state",
            missingCode
                ? isArabic
                    ? "لم تتوفر قيمة رمز التكوين. لا يمكن استنتاج أن الجهاز سليم أو معطل."
                    : "The configuration problem code was unavailable. Device health cannot be inferred from the remaining data."
                : isArabic
                    ? "لا توجد قاعدة تشخيصية موثقة لهذا الرمز أو أن حالة الجهاز متضاربة."
                    : "No verified rule is available for this code, or the reported device status conflicts with it.",
            evidence,
            isArabic
                ? "راجع الجهاز في إدارة الأجهزة للحصول على معلومات إضافية. لم يتم اقتراح إصلاح تلقائي."
                : "Review the device in Device Manager for more information. No automatic fix is suggested.");
    }

    private static string Evidence(uint? problemCode, string symbol, string? deviceStatus)
    {
        var evidence = $"ConfigManagerErrorCode = {problemCode} ({symbol})";
        return string.IsNullOrWhiteSpace(deviceStatus)
            ? evidence
            : $"{evidence}; WMI Status = {deviceStatus.Trim()}";
    }

    private static string OutcomeLabel(DiagnosticOutcome outcome, bool isArabic) => (outcome, isArabic) switch
    {
        (DiagnosticOutcome.Healthy, true) => "سليم: لم يتم الإبلاغ عن مشكلة",
        (DiagnosticOutcome.Warning, true) => "تحذير",
        (DiagnosticOutcome.Critical, true) => "حرج",
        (DiagnosticOutcome.Unknown, true) => "غير معروف",
        (DiagnosticOutcome.Healthy, false) => "Healthy: no problem reported",
        (DiagnosticOutcome.Warning, false) => "Warning",
        (DiagnosticOutcome.Critical, false) => "Critical",
        _ => "Unknown"
    };

    private static string SeverityLabel(ProblemSeverity severity, bool isArabic) => (severity, isArabic) switch
    {
        (ProblemSeverity.Normal, true) => "لا توجد مشكلة مُبلّغ عنها",
        (ProblemSeverity.Notice, true) => "ملاحظة",
        (ProblemSeverity.Warning, true) => "تحذير",
        (ProblemSeverity.Critical, true) => "حرج",
        (ProblemSeverity.Unknown, true) => "غير معروف",
        (ProblemSeverity.Normal, false) => "No reported issue",
        (ProblemSeverity.Notice, false) => "Notice",
        (ProblemSeverity.Warning, false) => "Warning",
        (ProblemSeverity.Critical, false) => "Critical",
        _ => "Unknown"
    };
}
