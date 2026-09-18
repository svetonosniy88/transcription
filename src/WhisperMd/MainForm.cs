using System.Diagnostics;
using System.Text;

namespace WhisperMd;

public sealed class MainForm : Form
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string ScriptPath = Path.Combine(ProjectRoot, "transcribe.ps1");

    private readonly TextBox _inputPath = new()
    {
        ReadOnly = true,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
    };

    private readonly Button _browseButton = new()
    {
        Text = "Выбрать аудио",
        AutoSize = true
    };

    private readonly Button _transcribeButton = new()
    {
        Text = "Транскрибировать",
        AutoSize = true,
        Enabled = false
    };

    private readonly Label _statusLabel = new()
    {
        Text = "Выберите аудиофайл.",
        AutoSize = true
    };

    private readonly TextBox _logBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
    };

    public MainForm()
    {
        Text = "Whisper → Markdown";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 430);
        Size = new Size(820, 500);

        var inputLabel = new Label
        {
            Text = "Аудиофайл:",
            AutoSize = true
        };

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(12)
        };

        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        topPanel.Controls.Add(inputLabel, 0, 0);
        topPanel.Controls.Add(_inputPath, 1, 0);
        topPanel.Controls.Add(_browseButton, 2, 0);

        topPanel.Controls.Add(_transcribeButton, 1, 1);
        topPanel.Controls.Add(_statusLabel, 1, 2);

        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 12, 12)
        };
        _logBox.Dock = DockStyle.Fill;
        logPanel.Controls.Add(_logBox);

        Controls.Add(logPanel);
        Controls.Add(topPanel);

        _browseButton.Click += BrowseButton_Click;
        _transcribeButton.Click += TranscribeButton_Click;
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите аудиозапись",
            Filter = "Аудиофайлы|*.mp3;*.m4a;*.wav;*.aac;*.flac;*.ogg;*.opus;*.mp4;*.mkv|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _inputPath.Text = dialog.FileName;
        _transcribeButton.Enabled = true;
        _statusLabel.Text = "Готово к запуску.";
        _logBox.Clear();
    }

    private async void TranscribeButton_Click(object? sender, EventArgs e)
    {
        var input = _inputPath.Text;

        if (!File.Exists(input))
        {
            MessageBox.Show(this, "Исходный файл не найден.", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!File.Exists(ScriptPath))
        {
            MessageBox.Show(this,
                $"Не найден скрипт:\r\n{ScriptPath}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        SetBusy(true);
        _logBox.Clear();
        _statusLabel.Text = "Транскрибация...";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(ScriptPath);
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add("-Backend");
            psi.ArgumentList.Add("auto");
            psi.ArgumentList.Add("-Language");
            psi.ArgumentList.Add("ru");
            psi.ArgumentList.Add("-Threads");
            psi.ArgumentList.Add("8");

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
                throw new InvalidOperationException("Не удалось запустить PowerShell.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
                _logBox.AppendText(stdout);

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (_logBox.TextLength > 0)
                    _logBox.AppendText(Environment.NewLine);

                _logBox.AppendText(stderr);
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Скрипт завершился с кодом {process.ExitCode}.");

            var resultDirectory = GetResultDirectory(stdout);

            if (resultDirectory is null || !Directory.Exists(resultDirectory))
                throw new InvalidOperationException(
                    "Транскрибация завершилась, но папка результата не найдена.");

            var txtPath = Directory
                .EnumerateFiles(resultDirectory, "*.txt", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (txtPath is null)
                throw new InvalidOperationException(
                    "Транскрибация завершилась, но TXT-файл не найден.");

            var sourceDirectory = Path.GetDirectoryName(input)
                ?? throw new InvalidOperationException("Не удалось определить папку исходного файла.");

            var mdPath = Path.Combine(
                sourceDirectory,
                Path.GetFileNameWithoutExtension(input) + ".md");

            File.Copy(txtPath, mdPath, overwrite: true);

            _statusLabel.Text = $"Готово: {mdPath}";

            MessageBox.Show(this,
                $"Транскрипция сохранена:\r\n{mdPath}",
                "Готово",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Ошибка.";

            MessageBox.Show(this,
                ex.Message,
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string? GetResultDirectory(string stdout)
    {
        foreach (var line in stdout.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            const string marker = "Results:";

            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return line[marker.Length..].Trim();
        }

        return null;
    }

    private void SetBusy(bool busy)
    {
        _browseButton.Enabled = !busy;
        _transcribeButton.Enabled = !busy && File.Exists(_inputPath.Text);
        UseWaitCursor = busy;
    }

    private static string FindProjectRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("WHISPERMD_ROOT");
        if (IsProjectRoot(configuredRoot))
            return Path.GetFullPath(configuredRoot!);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (IsProjectRoot(directory.FullName))
                return directory.FullName;
        }

        const string localFallback = @"C:\Base\Transcription";
        return localFallback;
    }

    private static bool IsProjectRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(Path.Combine(path, "transcribe.ps1"))
               && File.Exists(Path.Combine(path, "models", "ggml-small.bin"));
    }
}
