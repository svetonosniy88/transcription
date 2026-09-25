using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WhisperMd.App.Backend;

/// <summary>
/// Runs one transcribe.ps1 invocation and translates its line protocol into typed events.
/// Queue orchestration intentionally lives outside this class and is introduced in stage 4.
/// </summary>
public sealed class WhisperBackendClient : IWhisperBackendClient
{
    private readonly string _projectRoot;
    private readonly string _scriptPath;

    public WhisperBackendClient(string? projectRoot = null)
    {
        _projectRoot = Path.GetFullPath(projectRoot ?? ProjectRootLocator.FindProjectRoot());
        _scriptPath = Path.Combine(_projectRoot, "transcribe.ps1");
    }

    public string ProjectRoot => _projectRoot;

    public async Task<BackendRunResult> RunAsync(
        BackendRunRequest request,
        Action<WhisperBackendEvent>? onEvent = null,
        Action<BackendLogLine>? onTechnicalLog = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        var events = new ConcurrentQueue<WhisperBackendEvent>();
        var technicalLog = new ConcurrentQueue<BackendLogLine>();
        var protocolErrors = new ConcurrentQueue<string>();
        WhisperBackendEvent? resultEvent = null;

        var startInfo = CreateStartInfo(request);
        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить PowerShell backend.");

        using var cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            process);

        var stdoutTask = ConsumeStdoutAsync(
            process,
            events,
            technicalLog,
            protocolErrors,
            backendEvent =>
            {
                if (backendEvent.Kind == WhisperBackendEventKind.Result)
                    resultEvent = backendEvent;

                onEvent?.Invoke(backendEvent);
            },
            onTechnicalLog);

        var stderrTask = ConsumeStderrAsync(process, technicalLog, onTechnicalLog);

        await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);

        if (cancellationToken.IsCancellationRequested && resultEvent is null)
            throw new OperationCanceledException("Транскрибация отменена пользователем.", cancellationToken);

        if (!protocolErrors.IsEmpty)
        {
            throw new InvalidDataException(
                "Backend returned malformed protocol events:" + Environment.NewLine +
                string.Join(Environment.NewLine, protocolErrors));
        }

        if (process.ExitCode != 0 && !(cancellationToken.IsCancellationRequested && resultEvent is not null))
        {
            var backendError = events.LastOrDefault(item => item.Kind == WhisperBackendEventKind.Error)?.Message;
            var message = string.IsNullOrWhiteSpace(backendError)
                ? $"Backend завершился с кодом {process.ExitCode}."
                : backendError;

            throw new InvalidOperationException(message);
        }

        if (resultEvent is null)
            throw new InvalidDataException("Backend завершился успешно, но не отправил событие result.");

        return new BackendRunResult(
            process.ExitCode,
            resultEvent,
            events.ToArray(),
            technicalLog.ToArray());
    }


    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process has already exited.
        }
        catch (NotSupportedException)
        {
            TryKillSingleProcess(process);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            TryKillSingleProcess(process);
        }
    }

    private static void TryKillSingleProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // Cancellation is best-effort here; WaitForExitAsync will surface a real process failure if it survives.
        }
    }

    private ProcessStartInfo CreateStartInfo(BackendRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = _projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_scriptPath);
        startInfo.ArgumentList.Add("-InputPath");
        startInfo.ArgumentList.Add(request.InputPath);
        startInfo.ArgumentList.Add("-Backend");
        startInfo.ArgumentList.Add(request.Backend);
        startInfo.ArgumentList.Add("-Language");
        startInfo.ArgumentList.Add(request.Language);
        startInfo.ArgumentList.Add("-Threads");
        startInfo.ArgumentList.Add(request.Threads.ToString());

        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            startInfo.ArgumentList.Add("-Prompt");
            startInfo.ArgumentList.Add(request.Prompt);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.ArgumentList.Add("-WorkingDirectory");
            startInfo.ArgumentList.Add(request.WorkingDirectory);
        }

        if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            startInfo.ArgumentList.Add("-OutputDirectory");
            startInfo.ArgumentList.Add(request.OutputDirectory);
        }

        if (request.KeepWav)
            startInfo.ArgumentList.Add("-KeepWav");

        return startInfo;
    }

    private static async Task ConsumeStdoutAsync(
        Process process,
        ConcurrentQueue<WhisperBackendEvent> events,
        ConcurrentQueue<BackendLogLine> technicalLog,
        ConcurrentQueue<string> protocolErrors,
        Action<WhisperBackendEvent> onEvent,
        Action<BackendLogLine>? onTechnicalLog)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (WhisperBackendProtocol.IsProtocolLine(line))
            {
                if (WhisperBackendProtocol.TryParse(line, out var backendEvent, out var error) && backendEvent is not null)
                {
                    events.Enqueue(backendEvent);
                    onEvent(backendEvent);
                    continue;
                }

                protocolErrors.Enqueue(error ?? $"Invalid protocol line: {line}");
                var protocolLog = new BackendLogLine(DateTimeOffset.Now, BackendLogStream.Protocol, line);
                technicalLog.Enqueue(protocolLog);
                onTechnicalLog?.Invoke(protocolLog);
                continue;
            }

            var logLine = new BackendLogLine(DateTimeOffset.Now, BackendLogStream.StandardOutput, line);
            technicalLog.Enqueue(logLine);
            onTechnicalLog?.Invoke(logLine);
        }
    }

    private static async Task ConsumeStderrAsync(
        Process process,
        ConcurrentQueue<BackendLogLine> technicalLog,
        Action<BackendLogLine>? onTechnicalLog)
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            var logLine = new BackendLogLine(DateTimeOffset.Now, BackendLogStream.StandardError, line);
            technicalLog.Enqueue(logLine);
            onTechnicalLog?.Invoke(logLine);
        }
    }

    private void ValidateRequest(BackendRunRequest request)
    {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("Не найден backend-скрипт transcribe.ps1.", _scriptPath);

        if (!File.Exists(request.InputPath))
            throw new FileNotFoundException("Исходная запись не найдена.", request.InputPath);

        if (request.Threads is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(request.Threads), "Threads must be between 1 and 32.");

        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("Language must not be empty.", nameof(request.Language));

        if (request.Backend is not ("auto" or "vulkan" or "cpu"))
            throw new ArgumentException("Backend must be auto, vulkan, or cpu.", nameof(request.Backend));
    }
}
