using Microsoft.Win32;

namespace FileDial.App.Settings;

/// <summary>
/// Запуск вместе с Windows через HKCU\...\Run — только для текущего пользователя, без прав администратора.
/// Состояние читается из реестра, а не из файла настроек: так оно не расходится с тем, что видит Windows.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileDial";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(ExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Включает или выключает автозапуск. Возвращает текст ошибки или null.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                key.SetValue(ValueName, $"\"{ExePath()}\"", RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("Автозапуск", ex);
            return "Не удалось изменить автозапуск: нет доступа к реестру.";
        }
    }

    private static string ExePath() => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FileDial.exe");
}
