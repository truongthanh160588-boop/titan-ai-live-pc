using System.Net;
using System.Text;
using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

public sealed class OverlayHttpServer
{
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private string _latestComment = string.Empty;
    private string _latestReply = string.Empty;
    private string _brandName = "TITAN AUDIO VIETNAM";
    private string _brandFontPreset = "Broadcast Bold";

    public bool IsRunning => _listener.IsListening;
    public string Url => "http://localhost:8787/overlay";

    public void UpdateData(LiveComment? latestComment, string currentReply)
    {
        _latestComment = latestComment is null ? string.Empty : $"Viewer: {latestComment.UserName} - {latestComment.CommentText}";
        _latestReply = string.IsNullOrWhiteSpace(currentReply) ? string.Empty : currentReply.Trim();
    }

    public void SetBrandName(string? brandName)
    {
        _brandName = string.IsNullOrWhiteSpace(brandName) ? "TITAN AUDIO VIETNAM" : brandName.Trim();
    }

    public void SetBrandFontPreset(string? preset)
    {
        _brandFontPreset = string.IsNullOrWhiteSpace(preset) ? "Broadcast Bold" : preset.Trim();
    }

    public void Start(Action<string>? logger = null)
    {
        if (_listener.IsListening)
        {
            return;
        }

        _listener.Prefixes.Clear();
        _listener.Prefixes.Add("http://localhost:8787/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token, logger));
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken, Action<string>? logger)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                var path = context.Request.Url?.AbsolutePath?.Trim('/').ToLowerInvariant() ?? string.Empty;
                var html = BuildOverlayHtml();
                if (path != "overlay")
                {
                    context.Response.StatusCode = 404;
                    html = "<html><body style='background:#111;color:#fff'>Not found</body></html>";
                }

                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
                context.Response.Close();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger?.Invoke($"Overlay server warning: {ex.Message}");
                context?.Response.Close();
            }
        }
    }

    private string BuildOverlayHtml()
    {
        return $@"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <style>
    body {{
      margin: 0;
      background: transparent;
      font-family: Segoe UI, Arial, sans-serif;
    }}
    .lower-third {{
      position: absolute;
      left: 40px;
      right: 40px;
      bottom: 50px;
      min-height: 96px;
      background: linear-gradient(90deg, rgba(20,20,20,0.92), rgba(32,32,32,0.8));
      border-left: 8px solid #F5C542;
      border-radius: 10px;
      color: #FFFFFF;
      padding: 18px 22px;
      animation: slideUp .7s ease-out;
    }}
    .brand {{
      color: #F5C542;
      font-size: 24px;
      {ResolveBrandFontCss(_brandFontPreset)}
    }}
    .comment {{ margin-top: 8px; font-size: 22px; }}
    .reply {{ margin-top: 8px; font-size: 20px; color: #E6E6E6; }}
    @keyframes slideUp {{
      from {{ transform: translateY(80px); opacity: 0; }}
      to {{ transform: translateY(0); opacity: 1; }}
    }}
  </style>
</head>
<body>
  <div class=""lower-third"">
    <div class=""brand"">{WebUtility.HtmlEncode(_brandName)}</div>
    <div class=""comment"">{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(_latestComment) ? " " : _latestComment)}</div>
    <div class=""reply"">AI Reply: {WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(_latestReply) ? " " : _latestReply)}</div>
  </div>
</body>
</html>";
    }

    private static string ResolveBrandFontCss(string preset) => preset switch
    {
        "Elegant Serif" => "font-family: 'Georgia', 'Times New Roman', serif; font-weight: 700; letter-spacing: 0.6px;",
        "Tech Condensed" => "font-family: 'Bahnschrift', 'Arial Narrow', Segoe UI, Arial, sans-serif; font-weight: 700; letter-spacing: 1px; text-transform: uppercase;",
        "Neon Clean" => "font-family: 'Trebuchet MS', 'Verdana', Segoe UI, Arial, sans-serif; font-weight: 700; letter-spacing: 0.8px;",
        _ => "font-family: Segoe UI, Arial, sans-serif; font-weight: 700; letter-spacing: 0.3px;",
    };
}
