using System;
using System.Collections.Generic;
using System.Text.Json;

namespace xTaskForge;

public class CommandResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }

    public static CommandResult Ok(object? data = null) => new() { Success = true, Data = data };
    public static CommandResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed class CommandBroker
{
    public Func<string, object?>? ExecuteTaskHandler { get; set; }
    public Func<string, object?>? ApplyIconHandler { get; set; }
    public Func<string, string, object?>? SetItemIconHandler { get; set; }
    public Func<string, object?>? ApplyTooltipHandler { get; set; }
    public Func<CommandResult>? CycleAudioHandler { get; set; }
    public Func<object>? AudioStatusHandler { get; set; }
    public Func<string, string, object?>? SetStateVariableHandler { get; set; }
    public Func<string, object?>? GetStateVariableHandler { get; set; }
    public Func<string, object?>? ToggleStateVariableHandler { get; set; }
    public Func<string, object?>? ClearStateVariableHandler { get; set; }
    public Func<object>? ListStateVariablesHandler { get; set; }
    public Func<string, object?>? EvaluateConditionHandler { get; set; }

    public CommandResult Execute(string commandName, string jsonPayload = "")
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return CommandResult.Fail("Empty command name");

        string normalized = commandName.Trim().ToLowerInvariant();

        if (normalized.StartsWith("icon:") || normalized.StartsWith("icon.set:") || normalized.StartsWith("set_icon:") || normalized.StartsWith("native:icon:"))
        {
            int colonIdx = commandName.IndexOf(':');
            string iconPath = colonIdx >= 0 ? commandName[(colonIdx + 1)..].Trim() : "";
            if (!string.IsNullOrWhiteSpace(iconPath) && ApplyIconHandler != null)
            {
                ApplyIconHandler.Invoke(iconPath);
                return CommandResult.Ok($"Tray icon set to: {iconPath}");
            }
        }

        try
        {
            switch (normalized)
            {
                case "system.getcapabilities":
                case "system.capabilities":
                    return CommandResult.Ok(new
                    {
                        capabilities = new[]
                        {
                            "audio.listDevices", "audio.getStatus", "audio.setDefaultDevice",
                            "audio.cycle", "audio.getVolume", "audio.setVolume",
                            "audio.getMute", "audio.setMute", "display.listModes",
                            "display.applyProfile", "display.setResolution", "display.toggleHdr",
                            "power.preventSleep", "power.releaseSleep", "power.getStatus",
                            "pip.clickThrough", "pip.pause", "pip.unpause",
                            "task.run", "icon.set", "item.icon", "tooltip.set",
                            "state.set", "state.get", "state.toggle", "state.clear", "state.list",
                            "condition.evaluate"
                        },
                        displayModes = DisplayProvider.GetSupportedModes()
                    });

                case "audio.listdevices":
                case "audio.devices":
                    return CommandResult.Ok(AudioProvider.ListDevices());

                case "audio.getstatus":
                case "audio.status":
                    if (AudioStatusHandler != null)
                        return CommandResult.Ok(AudioStatusHandler.Invoke());
                    return CommandResult.Ok(new { isPlaying = false, peak = 0f });

                case "audio.setdefaultdevice":
                case "audio.select":
                {
                    string devId = ExtractParam(jsonPayload, "id", "deviceId");
                    if (string.IsNullOrWhiteSpace(devId))
                        return CommandResult.Fail("Missing 'id' or 'deviceId' parameter");
                    var (ok, msg) = AudioProvider.SetDefaultDevice(devId);
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "audio.cycle":
                case "native:cycle_audio_device":
                case "native:cycle_audio":
                case "native:next_audio_device":
                    if (CycleAudioHandler != null)
                        return CycleAudioHandler.Invoke();
                    return CommandResult.Fail("Audio cycle handler not initialized");

                case "audio.getvolume":
                {
                    var (ok, vol, err) = AudioProvider.GetMasterVolume();
                    return ok ? CommandResult.Ok(new { volume = vol }) : CommandResult.Fail(err ?? "Failed to get volume");
                }

                case "audio.setvolume":
                {
                    string volStr = ExtractParam(jsonPayload, "level", "volume");
                    if (float.TryParse(volStr, out float vol))
                    {
                        var (ok, msg) = AudioProvider.SetMasterVolume(vol);
                        return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                    }
                    return CommandResult.Fail("Invalid or missing 'level' / 'volume' numeric parameter");
                }

                case "audio.getmute":
                {
                    var (ok, isMuted, err) = AudioProvider.GetMute();
                    return ok ? CommandResult.Ok(new { isMuted }) : CommandResult.Fail(err ?? "Failed to get mute status");
                }

                case "audio.setmute":
                {
                    string muteStr = ExtractParam(jsonPayload, "mute", "isMuted");
                    if (bool.TryParse(muteStr, out bool mute))
                    {
                        var (ok, msg) = AudioProvider.SetMute(mute);
                        return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                    }
                    return CommandResult.Fail("Invalid or missing 'mute' boolean parameter");
                }

                case "display.listmodes":
                case "display.modes":
                    return CommandResult.Ok(DisplayProvider.GetSupportedModes());

                case "display.applyprofile":
                case "display.setresolution":
                case "native:res_4k":
                case "native:res_1080":
                {
                    int w = 1920, h = 1080;
                    if (normalized == "native:res_4k") { w = 3840; h = 2160; }
                    else if (normalized == "native:res_1080") { w = 1920; h = 1080; }
                    else
                    {
                        string wStr = ExtractParam(jsonPayload, "width", "w");
                        string hStr = ExtractParam(jsonPayload, "height", "h");
                        if (int.TryParse(wStr, out int parsedW)) w = parsedW;
                        if (int.TryParse(hStr, out int parsedH)) h = parsedH;
                    }

                    var (ok, msg) = DisplayProvider.ApplyResolution(w, h);
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "display.togglehdr":
                case "display.sethdr":
                case "native:toggle_hdr":
                case "native:hdr":
                {
                    var (ok, msg) = HdrProvider.ToggleHdr();
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "power.preventsleep":
                {
                    string reason = ExtractParam(jsonPayload, "reason");
                    PowerProvider.PreventSleep(reason);
                    return CommandResult.Ok($"Sleep prevention held for reason: {reason}");
                }

                case "power.releasesleep":
                {
                    string reason = ExtractParam(jsonPayload, "reason");
                    PowerProvider.ReleaseSleep(reason);
                    return CommandResult.Ok($"Sleep prevention released for reason: {reason}");
                }

                case "power.getstatus":
                case "power.status":
                    return CommandResult.Ok(PowerProvider.GetStatus());

                case "pip.clickthrough":
                case "pip.toggle":
                case "native:pip_clickthrough":
                case "native:pip_toggle":
                {
                    string alphaStr = ExtractParam(jsonPayload, "opacity", "alpha");
                    int.TryParse(alphaStr, out int alpha);
                    var (ok, msg) = PipProvider.ToggleClickThrough(alpha);
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "pip.pause":
                case "native:pip_pause":
                {
                    string secStr = ExtractParam(jsonPayload, "seconds", "duration", "time", "arguments");
                    int seconds = 10;
                    if (!string.IsNullOrWhiteSpace(secStr) && int.TryParse(secStr, out int parsedSec) && parsedSec > 0)
                    {
                        seconds = parsedSec;
                    }
                    var (ok, msg) = PipProvider.PauseClickThrough(seconds);
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "pip.unpause":
                case "native:pip_unpause":
                {
                    var (ok, msg) = PipProvider.ResumeClickThrough();
                    return ok ? CommandResult.Ok(msg) : CommandResult.Fail(msg);
                }

                case "wifi.optimize":
                case "wifi.enforce":
                case "native:wifi_optimize":
                case "native:wifi":
                {
                    string res = NativeWiFiOptimizer.CheckAndOptimize(force: true);
                    return CommandResult.Ok($"Wi-Fi AP Optimization completed: {res}");
                }

                case "wifi.status":
                case "native:wifi_status":
                {
                    string current = NativeWiFiOptimizer.GetCurrentConnectedBssid();
                    bool enabled = NativeWiFiOptimizer.IsEnabled;
                    return CommandResult.Ok(new { Enabled = enabled, CurrentBssid = current, PreferredBssids = NativeWiFiOptimizer.PreferredBssids });
                }

                case "task.run":
                case "task.execute":
                {
                    string taskId = ExtractParam(jsonPayload, "id", "task", "name");
                    if (string.IsNullOrWhiteSpace(taskId))
                        return CommandResult.Fail("Missing task 'id' parameter");

                    if (ExecuteTaskHandler != null)
                    {
                        var res = ExecuteTaskHandler.Invoke(taskId);
                        return CommandResult.Ok(res ?? new { triggered = true, taskId });
                    }
                    return CommandResult.Fail("Task execution handler not registered");
                }

                case "icon.set":
                case "native:icon":
                {
                    string icon = ExtractParam(jsonPayload, "icon", "path");
                    if (string.IsNullOrWhiteSpace(icon))
                        return CommandResult.Fail("Missing 'icon' or 'path' parameter");

                    if (ApplyIconHandler != null)
                    {
                        ApplyIconHandler.Invoke(icon);
                        return CommandResult.Ok($"Tray icon set to: {icon}");
                    }
                    return CommandResult.Fail("Icon handler not registered");
                }

                case "item.icon":
                case "item.seticon":
                case "item.status":
                case "native:item_icon":
                case "/api/item/icon":
                case "/api/item":
                {
                    string targetId = ExtractParam(jsonPayload, "id", "item", "target", "targetItem", "title");
                    string icon = ExtractParam(jsonPayload, "icon", "path");

                    if (string.IsNullOrWhiteSpace(targetId)) targetId = "next_audio_device";
                    if (string.IsNullOrWhiteSpace(icon))
                        return CommandResult.Fail("Missing 'icon' or 'path' parameter");

                    if (SetItemIconHandler != null)
                    {
                        var res = SetItemIconHandler.Invoke(targetId, icon);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("SetItemIconHandler not registered");
                }

                case "tooltip.set":
                case "native:tooltip":
                {
                    string tip = ExtractParam(jsonPayload, "tooltip", "text");
                    if (string.IsNullOrWhiteSpace(tip))
                        return CommandResult.Fail("Missing 'tooltip' parameter");

                    if (ApplyTooltipHandler != null)
                    {
                        ApplyTooltipHandler.Invoke(tip);
                        return CommandResult.Ok($"Tooltip set to: {tip}");
                    }
                    return CommandResult.Fail("Tooltip handler not registered");
                }

                case "state.set":
                case "state.update":
                case "var.set":
                {
                    string key = ExtractParam(jsonPayload, "key", "name", "id");
                    string value = ExtractParam(jsonPayload, "value", "val", "state");
                    if (string.IsNullOrWhiteSpace(key))
                        return CommandResult.Fail("Missing 'key' or 'name' parameter");
                    if (string.IsNullOrWhiteSpace(value)) value = "true";

                    if (SetStateVariableHandler != null)
                    {
                        var res = SetStateVariableHandler.Invoke(key, value);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("SetStateVariableHandler not registered");
                }

                case "state.get":
                case "var.get":
                {
                    string key = ExtractParam(jsonPayload, "key", "name", "id");
                    if (string.IsNullOrWhiteSpace(key))
                        return CommandResult.Fail("Missing 'key' or 'name' parameter");

                    if (GetStateVariableHandler != null)
                    {
                        var res = GetStateVariableHandler.Invoke(key);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("GetStateVariableHandler not registered");
                }

                case "state.toggle":
                case "var.toggle":
                {
                    string key = ExtractParam(jsonPayload, "key", "name", "id");
                    if (string.IsNullOrWhiteSpace(key))
                        return CommandResult.Fail("Missing 'key' or 'name' parameter");

                    if (ToggleStateVariableHandler != null)
                    {
                        var res = ToggleStateVariableHandler.Invoke(key);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("ToggleStateVariableHandler not registered");
                }

                case "state.clear":
                case "var.clear":
                {
                    string key = ExtractParam(jsonPayload, "key", "name", "id");
                    if (string.IsNullOrWhiteSpace(key))
                        return CommandResult.Fail("Missing 'key' or 'name' parameter");

                    if (ClearStateVariableHandler != null)
                    {
                        var res = ClearStateVariableHandler.Invoke(key);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("ClearStateVariableHandler not registered");
                }

                case "state.list":
                case "var.list":
                {
                    if (ListStateVariablesHandler != null)
                        return CommandResult.Ok(ListStateVariablesHandler.Invoke());
                    return CommandResult.Fail("ListStateVariablesHandler not registered");
                }

                case "condition.evaluate":
                case "condition.check":
                {
                    string condition = ExtractParam(jsonPayload, "condition", "expr", "expression", "value");
                    if (string.IsNullOrWhiteSpace(condition))
                        return CommandResult.Fail("Missing 'condition' parameter");

                    if (EvaluateConditionHandler != null)
                    {
                        var res = EvaluateConditionHandler.Invoke(condition);
                        return CommandResult.Ok(res);
                    }
                    return CommandResult.Fail("EvaluateConditionHandler not registered");
                }

                default:
                    return CommandResult.Fail($"Unknown or unsupported command: {commandName}");
            }
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Command execution error: {ex.Message}");
        }
    }

    private static string ExtractParam(string jsonPayload, params string[] paramNames)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload)) return "";
        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            foreach (var p in paramNames)
            {
                if (doc.RootElement.TryGetProperty(p, out var val))
                {
                    return val.ValueKind switch
                    {
                        JsonValueKind.String => val.GetString() ?? "",
                        JsonValueKind.Number => val.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => val.ToString() ?? ""
                    };
                }
            }
        }
        catch { }

        // Fallback for simple key=value or string
        if (!jsonPayload.TrimStart().StartsWith("{"))
            return jsonPayload.Trim();

        return "";
    }
}
