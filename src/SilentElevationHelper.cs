using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace xTaskForge;

internal static class SilentElevationHelper
{
    public const string TaskFolder = @"\xTaskForge";

    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string GetTaskFullName(string id)
    {
        var safe = new StringBuilder(id.Length);
        foreach (char c in id)
        {
            safe.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }
        string clean = safe.ToString();
        if (string.IsNullOrWhiteSpace(clean)) clean = "task";
        return $@"{TaskFolder}\{clean}";
    }

    public static bool TaskExists(string id)
    {
        try
        {
            string fullName = GetTaskFullName(id);
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{fullName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static (string Exe, string Args, string WorkingDir) ResolveExecution(
        string command,
        string arguments,
        string? launcher,
        string? workingDir,
        bool runHidden)
    {
        string exe;
        string args;
        var ext = Path.GetExtension(command).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(launcher))
        {
            exe = launcher.Trim();
            args = (arguments ?? "").Replace("{script}", Quote(command), StringComparison.OrdinalIgnoreCase);
        }
        else if (ext == ".ps1")
        {
            exe = "powershell.exe";
            string hiddenArg = runHidden ? "-WindowStyle Hidden " : "";
            args = $"-NoLogo -NoProfile {hiddenArg}-ExecutionPolicy Bypass -File {Quote(command)} {arguments}".Trim();
        }
        else if (ext is ".bat" or ".cmd")
        {
            exe = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            args = $"/d /s /c {Quote(command)} {arguments}".Trim();
        }
        else
        {
            exe = command;
            args = arguments ?? "";
        }

        string targetWorkingDir = !string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir)
            ? workingDir
            : (Path.GetDirectoryName(command) is string dir && Directory.Exists(dir)
                ? dir
                : Environment.CurrentDirectory);

        return (exe, args, targetWorkingDir);
    }

    public static bool RegisterOrUpdateTask(
        string id,
        string command,
        string arguments,
        string? launcher,
        string? workingDir,
        bool runHidden,
        out string? error)
    {
        error = null;
        try
        {
            var (exe, args, wd) = ResolveExecution(command, arguments, launcher, workingDir, runHidden);
            string taskName = Path.GetFileName(GetTaskFullName(id));

            string psScript = BuildRegisterScript(taskName, exe, args, wd);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "Failed to launch Task Scheduler registration.";
                return false;
            }
            proc.WaitForExit(15000);
            if (proc.ExitCode != 0)
            {
                error = $"Task Scheduler registration exited with code {proc.ExitCode}.";
                return false;
            }
            return true;
        }
        catch (System.ComponentModel.Win32Exception wEx) when (wEx.NativeErrorCode == 1223)
        {
            error = "UAC prompt was canceled. Silent elevation requires one-time Task Scheduler registration.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool RunScheduledTask(string id, out string? error)
    {
        error = null;
        try
        {
            string fullName = GetTaskFullName(id);
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{fullName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc == null)
            {
                error = "Could not start schtasks.exe.";
                return false;
            }
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? $"schtasks exited with code {proc.ExitCode}." : stderr.Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void TryDeleteTask(string id)
    {
        try
        {
            string fullName = GetTaskFullName(id);
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{fullName}\" /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            proc?.WaitForExit(3000);
        }
        catch { }
    }

    private static string BuildRegisterScript(string taskName, string exe, string args, string workingDir)
    {
        static string PsQuote(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

        return $@"
$ErrorActionPreference = 'Stop'
$svc = New-Object -ComObject Schedule.Service
$svc.Connect()
try {{ $folder = $svc.GetFolder('\xTaskForge') }} catch {{ $folder = $svc.GetFolder('\').CreateFolder('xTaskForge') }}
$td = $svc.NewTask(0)
$td.RegistrationInfo.Description = 'xTaskForge silent elevation task'
$td.Principal.UserId = ""$env:USERDOMAIN\$env:USERNAME""
$td.Principal.LogonType = 3
$td.Principal.RunLevel = 1
$td.Settings.AllowDemandStart = $true
$td.Settings.ExecutionTimeLimit = 'PT0S'
$td.Settings.MultipleInstances = 0
$td.Settings.DisallowStartIfOnBatteries = $false
$td.Settings.StopIfGoingOnBatteries = $false
$action = $td.Actions.Create(0)
$action.Path = {PsQuote(exe)}
$action.Arguments = {PsQuote(args)}
$action.WorkingDirectory = {PsQuote(workingDir)}
$sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$sddl = ""D:(A;;GA;;;BA)(A;;GA;;;SY)(A;;GRGX;;;$sid)""
$folder.RegisterTaskDefinition({PsQuote(taskName)}, $td, 6, $null, $null, 3, $sddl) | Out-Null
";
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}

