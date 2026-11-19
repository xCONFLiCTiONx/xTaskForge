using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace xTaskForge;

public sealed class LocalIpcServer : IDisposable
{
    private const string PipeName = "xTaskForgePipe";
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly CommandBroker _broker;

    public event Action<string>? LogMessage;
    public bool IsListening { get; private set; }

    public LocalIpcServer(CommandBroker broker)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public void Start()
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        IsListening = true;
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        LogMessage?.Invoke($"Local IPC Named Pipe listening on \\\\.\\pipe\\{PipeName} (Secured via User SID)");
    }

    private PipeSecurity CreatePipeSecurity()
    {
        var ps = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            ps.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }
        else
        {
            var currentPrincipal = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            ps.AddAccessRule(new PipeAccessRule(
                currentPrincipal,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }
        return ps;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = CreatePipeSecurity();
                using var pipeStream = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    pipeSecurity);

                await pipeStream.WaitForConnectionAsync(token);
                _ = Task.Run(() => HandleClientAsync(pipeStream, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    LogMessage?.Invoke($"IPC listener exception: {ex.Message}");
                    await Task.Delay(500, token);
                }
            }
        }
        IsListening = false;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeStream, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(pipeStream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipeStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            string? line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) return;

            string command = line.Trim();
            string jsonBody = "";

            if (line.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                    {
                        command = cmdProp.GetString() ?? "";
                    }
                    if (doc.RootElement.TryGetProperty("params", out var paramsProp))
                    {
                        jsonBody = paramsProp.GetRawText();
                    }
                    else
                    {
                        jsonBody = line;
                    }
                }
                catch { }
            }

            CommandResult result = _broker.Execute(command, jsonBody);

            string responseJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
            await writer.WriteLineAsync(responseJson.AsMemory(), token);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"IPC client handling exception: {ex.Message}");
        }
    }

    public void Stop()
    {
        IsListening = false;
        try
        {
            _cts?.Cancel();
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
