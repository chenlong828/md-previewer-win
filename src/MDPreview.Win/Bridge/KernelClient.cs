using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace MDPreview.Win.Bridge;

public sealed class KernelException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// 内核 v2 客户端：单通道、请求-响应按 id 关联、通知单向、超时兜底。
/// 帧经 <c>PostWebMessageAsString</c> 投递，由注入的前导脚本转给 <c>__mdKernel.receive</c>；
/// 与 macOS 的 KernelClient.swift 同语义（见 doc/history/works/2026-11-04-kernel-v2/spec-delta.md §3）。
/// </summary>
public sealed class KernelClient
{
    readonly CoreWebView2 _core;
    readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = [];
    readonly TaskCompletionSource<bool> _booted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _seq;

    public int ProtocolVersion { get; } = AppInfo.ProtocolVersion;

    /// <summary>内核装配完成（收到 boot 通知）；此前投递的帧会落在页面加载前的空档里丢失。</summary>
    public Task Booted => _booted.Task;

    /// <summary>内核通知（boot/ready/changed/stats/sourceMode/selinfo/jserror）。</summary>
    public event Action<string, JsonObject>? Notification;

    /// <summary>内核发起的请求（resolveResources / importImage）；实现方必须回一次。</summary>
    public Func<string, JsonObject, Task<JsonNode?>>? RequestHandler;

    /// <summary>前导脚本转来的壳级快捷键（save/open/zoomIn…）。</summary>
    public event Action<string>? Shortcut;

    /// <summary>握手结果：kernelVersion / protocolVersion / ops[]。</summary>
    public JsonObject? Handshake { get; private set; }

    public KernelClient(CoreWebView2 core)
    {
        _core = core;
        _core.WebMessageReceived += OnWebMessage;
    }

    public async Task<JsonNode?> SendAsync(string op, JsonObject? payload = null, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);

        // boot 之前页面里还没有 __mdKernel.receive，此时投递会被静默丢弃（R-1 冒烟实测）
        if (!_booted.Task.IsCompleted)
        {
            var race = await Task.WhenAny(_booted.Task, Task.Delay(limit));
            if (race != _booted.Task)
                throw new KernelException("timeout", $"op {op} 等待内核 boot 超时");
        }

        var id = $"s{Interlocked.Increment(ref _seq)}";
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var frame = new JsonObject
        {
            ["v"] = ProtocolVersion,
            ["op"] = op,
            ["id"] = id,
            ["payload"] = payload ?? new JsonObject(),
        };
        _core.PostWebMessageAsString(frame.ToJsonString());

        _ = ExpireAsync(id, op, timeout ?? TimeSpan.FromSeconds(10));
        var result = await tcs.Task;
        // 握手信息在 init 的应答里（kernelVersion / protocolVersion / ops），不是 ready 通知
        if (op == "init" && result is JsonObject handshake) Handshake = handshake;
        return result;
    }

    /// <summary>单向 op：不关心结果，但错误仍会进日志。</summary>
    public void Post(string op, JsonObject? payload = null)
        => _ = SendAsync(op, payload).ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Warn($"kernel op {op} failed: {t.Exception?.GetBaseException().Message}");
        }, TaskScheduler.Default);

    async Task ExpireAsync(string id, string op, TimeSpan timeout)
    {
        await Task.Delay(timeout);
        if (_pending.Remove(id, out var tcs))
            tcs.TrySetException(new KernelException("timeout", $"op {op} 超时"));
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? text;
        try
        {
            text = e.TryGetWebMessageAsString();
        }
        catch (Exception ex)
        {
            Log.Warn($"kernel frame not a string: {ex.Message}");
            return;
        }
        if (string.IsNullOrWhiteSpace(text)) return;

        JsonObject? frame;
        try
        {
            frame = JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Error("kernel frame parse failed", ex);
            return;
        }
        if (frame is null) return;

        if (frame["__host"] is JsonValue hostValue && hostValue.TryGetValue<string>(out var host) && host == "shortcut")
        {
            Shortcut?.Invoke(frame["key"]?.GetValue<string>() ?? "");
            return;
        }

        if (frame["v"]?.GetValue<int>() != ProtocolVersion)
        {
            Log.Warn($"kernel frame version mismatch: {text[..Math.Min(120, text.Length)]}");
            return;
        }

        var id = frame["id"]?.GetValue<string>();
        if (id is not null && (frame.ContainsKey("ok") || frame.ContainsKey("error")))
        {
            SettleResponse(id, frame);
            return;
        }

        var op = frame["op"]?.GetValue<string>();
        if (op is null)
        {
            Log.Warn("kernel frame without op");
            return;
        }
        var payload = frame["payload"] as JsonObject ?? [];

        if (id is not null)
        {
            _ = HandleRequestAsync(id, op, payload);
            return;
        }

        if (op == "boot") _booted.TrySetResult(true);
        Notification?.Invoke(op, payload);
    }

    void SettleResponse(string id, JsonObject frame)
    {
        if (!_pending.Remove(id, out var tcs))
        {
            Log.Warn($"kernel response for unknown id {id}");
            return;
        }
        if (frame["ok"]?.GetValue<bool>() == true)
        {
            tcs.TrySetResult(frame["payload"]);
            return;
        }
        var error = frame["error"] as JsonObject;
        tcs.TrySetException(new KernelException(
            error?["code"]?.GetValue<string>() ?? "internal",
            error?["message"]?.GetValue<string>() ?? "内核请求失败"));
    }

    async Task HandleRequestAsync(string id, string op, JsonObject payload)
    {
        var handler = RequestHandler;
        if (handler is null)
        {
            Respond(id, null, new KernelException("unknown_op", $"壳未实现请求 {op}"));
            return;
        }
        try
        {
            var result = await handler(op, payload);
            Respond(id, result, null);
        }
        catch (KernelException ex)
        {
            Respond(id, null, ex);
        }
        catch (Exception ex)
        {
            Log.Error($"kernel request {op} failed", ex);
            Respond(id, null, new KernelException("internal", ex.Message));
        }
    }

    void Respond(string id, JsonNode? payload, KernelException? error)
    {
        var frame = new JsonObject { ["v"] = ProtocolVersion, ["id"] = id };
        if (error is null)
        {
            frame["ok"] = true;
            frame["payload"] = payload ?? new JsonObject();
        }
        else
        {
            frame["ok"] = false;
            frame["error"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message };
        }
        _core.PostWebMessageAsString(frame.ToJsonString());
    }
}
