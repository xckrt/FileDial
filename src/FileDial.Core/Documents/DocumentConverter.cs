using System.Diagnostics;
using System.Runtime.InteropServices;
using FileDial.Core.Compression;
using FileDial.Core.Localization;
using Microsoft.Win32;

namespace FileDial.Core.Documents;

/// <summary>Сохранение документа в другом формате установленной программой (Office или LibreOffice).</summary>
public interface IDocumentConverter
{
    /// <summary>Для сообщений: «Microsoft Word», «LibreOffice».</summary>
    string Name(DocumentFamily family);

    bool CanWrite(DocumentFamily family, string extension);

    /// <summary>Сохранить <paramref name="source"/> как <paramref name="target"/> (формат — по расширению цели).</summary>
    /// <param name="password">Пароль на открытие результата (только Office).</param>
    Task ConvertAsync(string source, string target, string? password, CancellationToken ct);
}

public static class DocumentConverters
{
    public static string NoConverter => Lang.T.DocNoConverter;
    public static string NoOffice => Lang.T.DocNoOffice;

    private static readonly Lazy<OfficeConverter> Office = new(() => new OfficeConverter());
    private static readonly Lazy<LibreOfficeConverter?> Libre = new(LibreOfficeConverter.Find);

    /// <summary>Кто сохранит документ этого вида в нужном формате: Office в приоритете, затем LibreOffice.</summary>
    public static IDocumentConverter? For(DocumentFamily family, string extension)
    {
        if (Office.Value.CanWrite(family, extension))
            return Office.Value;
        return Libre.Value is { } libre && libre.CanWrite(family, extension) ? libre : null;
    }

    /// <summary>Только Office — пароль умеет ставить только он.</summary>
    public static IDocumentConverter? OfficeFor(DocumentFamily family) => Office.Value.Has(family) ? Office.Value : null;
}

/// <summary>
/// Word, Excel, PowerPoint через COM-автоматизацию: невидимо, без макросов и диалогов, исходник открыт только для чтения.
/// Своё приложение после работы закрываем; если система подключила нас к уже открытому у пользователя
/// (так бывает с PowerPoint), закрываем только свой документ.
/// </summary>
internal sealed class OfficeConverter : IDocumentConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<DocumentFamily, (string ProgId, string Process)> Apps = new()
    {
        [DocumentFamily.Text] = ("Word.Application", "WINWORD"),
        [DocumentFamily.Sheet] = ("Excel.Application", "EXCEL"),
        [DocumentFamily.Slides] = ("PowerPoint.Application", "POWERPNT"),
    };

    // Коды форматов SaveAs из документации Office.
    private static readonly Dictionary<string, int> WordFormats = new()
    {
        [".pdf"] = 17, [".docx"] = 16, [".doc"] = 0, [".rtf"] = 6, [".odt"] = 23, [".txt"] = 2,
    };
    private static readonly Dictionary<string, int> ExcelFormats = new()
    {
        [".xlsx"] = 51, [".xls"] = 56, [".ods"] = 60, [".csv"] = 62,
    };
    private static readonly Dictionary<string, int> PowerPointFormats = new()
    {
        [".pdf"] = 32, [".pptx"] = 24, [".ppt"] = 1, [".odp"] = 35,
    };

    private readonly Dictionary<DocumentFamily, bool> _installed = [];

    public bool Has(DocumentFamily family)
    {
        lock (_installed)
        {
            if (!_installed.TryGetValue(family, out var has))
                _installed[family] = has = OperatingSystem.IsWindows() && Type.GetTypeFromProgID(Apps[family].ProgId) is not null;
            return has;
        }
    }

    public string Name(DocumentFamily family) => family switch
    {
        DocumentFamily.Sheet => "Microsoft Excel",
        DocumentFamily.Slides => "Microsoft PowerPoint",
        _ => "Microsoft Word",
    };

    public bool CanWrite(DocumentFamily family, string extension) =>
        Has(family) && (family switch
        {
            DocumentFamily.Sheet => ExcelFormats.ContainsKey(extension) || extension == ".pdf",
            DocumentFamily.Slides => PowerPointFormats.ContainsKey(extension),
            _ => WordFormats.ContainsKey(extension),
        });

    public Task ConvertAsync(string source, string target, string? password, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var family = OfficeDocs.FamilyOf(source);
        var (progId, processName) = Apps[family];
        var comType = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        var extension = Path.GetExtension(target).ToLowerInvariant();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = ProcessIds(processName);
        int? ownProcess = null;

        // COM-объекты Office — в отдельном STA-потоке; отмена и зависание (скрытый диалог) — завершение своего процесса.
        var thread = new Thread(() =>
        {
            dynamic? app = null;
            dynamic? doc = null;
            var ownApp = false;
            try
            {
                app = Activator.CreateInstance(comType)!;
                var started = ProcessIds(processName).Except(before).ToList();
                ownApp = started.Count > 0;
                if (ownApp)
                    ownProcess = started[0];

                // Макросы не запускаем: документ может быть чужим.
                app.AutomationSecurity = 3;
                switch (family)
                {
                    case DocumentFamily.Text:
                        app.Visible = false;
                        app.DisplayAlerts = 0;
                        // Заведомо неверный пароль: защищённый документ не откроется, а не спросит пароль скрытым окном.
                        doc = app.Documents.Open(FileName: source, ConfirmConversions: false, ReadOnly: true,
                            AddToRecentFiles: false, PasswordDocument: "\u0001", Visible: false, NoEncodingDialog: true);
                        if (password is null)
                            doc.SaveAs2(FileName: target, FileFormat: WordFormats[extension], AddToRecentFiles: false,
                                Encoding: 65001);
                        else
                            doc.SaveAs2(FileName: target, FileFormat: WordFormats[extension], AddToRecentFiles: false,
                                Password: password);
                        doc.Close(SaveChanges: 0);
                        break;
                    case DocumentFamily.Sheet:
                        app.Visible = false;
                        app.DisplayAlerts = false;
                        doc = app.Workbooks.Open(Filename: source, UpdateLinks: 0, ReadOnly: true, Password: "\u0001",
                            AddToMru: false, IgnoreReadOnlyRecommended: true);
                        if (extension == ".pdf")
                            doc.ExportAsFixedFormat(Type: 0, Filename: target);
                        else if (password is null)
                            doc.SaveAs(Filename: target, FileFormat: ExcelFormats[extension]);
                        else
                            doc.SaveAs(Filename: target, FileFormat: ExcelFormats[extension], Password: password);
                        doc.Close(SaveChanges: false);
                        break;
                    case DocumentFamily.Slides:
                        app.DisplayAlerts = 1;
                        // ReadOnly: msoTrue (-1), без окна.
                        doc = app.Presentations.Open(source, -1, 0, 0);
                        if (password is not null)
                            doc.Password = password;
                        doc.SaveAs(target, PowerPointFormats[extension]);
                        doc.Close();
                        break;
                }
                doc = null;
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                    ? new CompressionException(Lang.T.DocAppFailed(Name(family), Path.GetFileName(source)), ex.Message)
                    : ex);
            }
            finally
            {
                try { doc?.Close(0); } catch (Exception) { }
                if (ownApp)
                    try { app?.Quit(); } catch (Exception) { }
                if (app is not null)
                    try { Marshal.FinalReleaseComObject(app); } catch (Exception) { }
            }
        })
        {
            IsBackground = true,
            Name = "FileDial Office",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return WaitAsync(done.Task, () => ownProcess, ct);
    }

    private static async Task WaitAsync(Task work, Func<int?> ownProcess, CancellationToken ct)
    {
        try
        {
            await work.WaitAsync(Timeout, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // Office завис или пользователь отменил — завершаем только тот процесс, который запустили сами.
            if (ownProcess() is { } pid)
                try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
            if (ex is TimeoutException)
                throw new CompressionException(Lang.T.DocOfficeTimeout);
            throw;
        }
    }

    private static HashSet<int> ProcessIds(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Select(p => p.Id).ToHashSet();
        }
        finally
        {
            foreach (var p in processes)
                p.Dispose();
        }
    }
}

/// <summary>LibreOffice в режиме без окон: «soffice --convert-to». Свой профиль — чтобы не мешать открытому LibreOffice.</summary>
internal sealed class LibreOfficeConverter(string soffice) : IDocumentConverter
{
    private static readonly Dictionary<string, string> Filters = new()
    {
        [".pdf"] = "pdf",
        [".docx"] = "docx:MS Word 2007 XML",
        [".odt"] = "odt",
        [".rtf"] = "rtf",
        [".txt"] = "txt:Text (encoded):UTF8",
        [".xlsx"] = "xlsx:Calc MS Excel 2007 XML",
        [".ods"] = "ods",
        // 44 — запятая, 34 — кавычки, 76 — UTF-8.
        [".csv"] = "csv:Text - txt - csv (StarCalc):44,34,76",
        [".pptx"] = "pptx:Impress MS PowerPoint 2007 XML",
        [".odp"] = "odp",
    };

    private static readonly Dictionary<DocumentFamily, string[]> Targets = new()
    {
        [DocumentFamily.Text] = [".pdf", ".docx", ".odt", ".rtf", ".txt"],
        [DocumentFamily.Sheet] = [".pdf", ".xlsx", ".ods", ".csv"],
        [DocumentFamily.Slides] = [".pdf", ".pptx", ".odp"],
    };

    public static LibreOfficeConverter? Find()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var candidates = new List<string>();
        try
        {
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\LibreOffice\UNO\InstallPath", "", null) is string program)
                candidates.Add(Path.Combine(program, "soffice.exe"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException) { }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            candidates.Add(Path.Combine(Environment.GetFolderPath(root), "LibreOffice", "program", "soffice.exe"));
        return candidates.FirstOrDefault(File.Exists) is { } path ? new LibreOfficeConverter(path) : null;
    }

    public string Name(DocumentFamily family) => "LibreOffice";

    public bool CanWrite(DocumentFamily family, string extension) => Targets[family].Contains(extension);

    public async Task ConvertAsync(string source, string target, string? password, CancellationToken ct)
    {
        if (password is not null)
            throw new CompressionException(Lang.T.DocPasswordOfficeOnly);

        var extension = Path.GetExtension(target).ToLowerInvariant();
        var work = Path.Combine(Path.GetTempPath(), "FileDial", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var profile = new Uri(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "libreoffice-profile")).AbsoluteUri;
            var result = await ProcessRunner.RunAsync(soffice,
                ["--headless", "--norestore", "--nologo", "--nodefault", $"-env:UserInstallation={profile}",
                 "--convert-to", Filters[extension], "--outdir", work, source],
                null, captureOutput: false, ct).ConfigureAwait(false);

            var produced = Path.Combine(work, Path.GetFileNameWithoutExtension(source) + extension);
            if (!File.Exists(produced))
                throw new CompressionException(Lang.T.DocLibreFailed(Path.GetFileName(source)), result.ErrorTail);
            File.Move(produced, target);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }
}
