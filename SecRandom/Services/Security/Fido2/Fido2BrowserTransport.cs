using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Security.Fido2;
using SecRandom.Services.Desktop;

namespace SecRandom.Services.Security.Fido2;

public enum Fido2BrowserOperation
{
    Create,
    Get
}

/// <summary>
///     The raw browser answer for one ceremony. Every binary field is base64url, exactly as the page produced it;
///     verification happens in <see cref="WebAuthnVerifier" />, never here.
/// </summary>
public sealed record Fido2BrowserResponse(
    bool Completed,
    string Origin,
    string? CredentialId,
    string? ClientDataJson,
    string? AttestationObject,
    string? AuthenticatorData,
    string? Signature,
    string? UserHandle,
    IReadOnlyList<string>? Transports,
    string? Error);

/// <summary>
///     Uses the user's own browser as the CTAP2 client: it serves one self-contained page on a loopback address
///     and hands <c>navigator.credentials.create/get</c> the options. No native USB/HID dependency, and the
///     browser's own localized prompt is what the user actually sees.
/// </summary>
/// <remarks>
///     <para>
///         This is the same transport shape the SECTL sign-in already uses (loopback listener + system browser),
///         with two extra rules because it enrolls a credential: the session nonce is part of the URL and must be
///         echoed by the POST, and the listener only accepts a single completion. The origin and relying-party id
///         are fixed to <c>localhost</c> because WebAuthn forbids an IP address as an RP id, and <c>localhost</c>
///         is a secure context in every browser.
///     </para>
///     <para>
///         The page intentionally carries no instructions: the browser renders its own security-key prompt, and a
///         page-local message would be an untranslated user-facing string. Failures travel back as a browser error
///         name so the caller can localize the explanation when the factor is wired into a settings surface.
///     </para>
/// </remarks>
public sealed class Fido2BrowserTransport(ILogger<Fido2BrowserTransport> logger)
{
    public const string RelyingPartyId = "localhost";

    private const int MaxBodyBytes = 64 * 1024;

    public async Task<Fido2BrowserResponse> RunAsync(
        Fido2BrowserOperation operation,
        string optionsJson,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsJson);

        var port = GetFreePort();
        var origin = $"http://localhost:{port}";
        var session = Base64Url.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

        var listener = new HttpListener();
        listener.Prefixes.Add($"{origin}/");
        try
        {
            try
            {
                listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or PlatformNotSupportedException or InvalidOperationException)
            {
                logger.LogWarning(ex, "FIDO2 浏览器回环监听无法启动");
                return Failure(origin, "listener_unavailable");
            }

            var url = $"{origin}/?s={Uri.EscapeDataString(session)}";
            if (!TryOpenBrowser(url))
            {
                logger.LogWarning("FIDO2 无法打开系统浏览器");
                return Failure(origin, "browser_unavailable");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            while (true)
            {
                HttpListenerContext context;
                try
                {
                    var pending = listener.GetContextAsync();
                    // Stopping the listener aborts this task; observe the fault so it never surfaces as an
                    // unobserved task exception on a path the user only sees as "cancelled".
                    _ = pending.ContinueWith(
                        task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    context = await pending.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Failure(origin, "timeout");
                }

                var path = context.Request.Url?.AbsolutePath ?? "/";
                if (context.Request.HttpMethod == "POST" &&
                    string.Equals(path, "/complete", StringComparison.Ordinal))
                {
                    var response = await ReadCompletionAsync(context, session, origin).ConfigureAwait(false);
                    if (response is not null)
                        return response;
                    continue;
                }

                if (context.Request.HttpMethod == "GET" &&
                    (string.IsNullOrEmpty(path) || string.Equals(path, "/", StringComparison.Ordinal)))
                {
                    await ReplyAsync(context.Response, HttpStatusCode.OK,
                        BuildPage(operation, optionsJson, session)).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(path, "/favicon.ico", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    context.Response.Close();
                    continue;
                }

                await ReplyAsync(context.Response, HttpStatusCode.NotFound, string.Empty).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed by a failed Start.
            }
        }
    }

    private async Task<Fido2BrowserResponse?> ReadCompletionAsync(
        HttpListenerContext context,
        string session,
        string origin)
    {
        if (context.Request.ContentLength64 > MaxBodyBytes)
        {
            await ReplyAsync(context.Response, HttpStatusCode.RequestEntityTooLarge, string.Empty).ConfigureAwait(false);
            return null;
        }

        string body;
        using (var buffer = new MemoryStream())
        {
            await context.Request.InputStream.CopyToAsync(buffer).ConfigureAwait(false);
            if (buffer.Length > MaxBodyBytes)
            {
                await ReplyAsync(context.Response, HttpStatusCode.RequestEntityTooLarge, string.Empty).ConfigureAwait(false);
                return null;
            }

            body = Encoding.UTF8.GetString(buffer.ToArray());
        }

        Fido2BrowserResponse? response;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !string.Equals(ReadString(root, "session"), session, StringComparison.Ordinal))
            {
                await ReplyAsync(context.Response, HttpStatusCode.Forbidden, string.Empty).ConfigureAwait(false);
                return null;
            }

            var error = ReadString(root, "error");
            response = new Fido2BrowserResponse(
                Completed: string.IsNullOrEmpty(error),
                Origin: origin,
                CredentialId: ReadString(root, "credentialId"),
                ClientDataJson: ReadString(root, "clientDataJSON"),
                AttestationObject: ReadString(root, "attestationObject"),
                AuthenticatorData: ReadString(root, "authenticatorData"),
                Signature: ReadString(root, "signature"),
                UserHandle: ReadString(root, "userHandle"),
                Transports: ReadTransports(root),
                Error: error);
        }
        catch (JsonException)
        {
            await ReplyAsync(context.Response, HttpStatusCode.BadRequest, string.Empty).ConfigureAwait(false);
            return null;
        }

        await ReplyAsync(context.Response, HttpStatusCode.OK, CompletedPage).ConfigureAwait(false);
        return response;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static IReadOnlyList<string>? ReadTransports(JsonElement root)
    {
        if (!root.TryGetProperty("transports", out var element) || element.ValueKind != JsonValueKind.Array)
            return null;

        var transports = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                transports.Add(value);
        }

        return transports;
    }

    private static async Task ReplyAsync(HttpListenerResponse response, HttpStatusCode status, string html)
    {
        try
        {
            response.StatusCode = (int)status;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] =
                "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'";

            if (string.IsNullOrEmpty(html))
            {
                response.ContentLength64 = 0;
                response.Close();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The browser closed the tab, or the listener was stopped: nothing left to answer.
        }
    }

    private static Fido2BrowserResponse Failure(string origin, string error) =>
        new(false, origin, null, null, null, null, null, null, null, error);

    private static bool TryOpenBrowser(string url)
    {
        var launcher = IAppHost.TryGetService<IExternalLauncher>();
        if (launcher is not null)
            return launcher.TryOpenUri(url);

        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    private static string BuildPage(Fido2BrowserOperation operation, string optionsJson, string session)
    {
        var operationLiteral = operation == Fido2BrowserOperation.Create ? "\"create\"" : "\"get\"";
        return PageTemplate
            .Replace("__OP__", operationLiteral, StringComparison.Ordinal)
            .Replace("__OPTIONS__", JsonSerializer.Serialize(optionsJson), StringComparison.Ordinal)
            .Replace("__SESSION__", JsonSerializer.Serialize(session), StringComparison.Ordinal);
    }

    private const string CompletedPage = """
        <!DOCTYPE html>
        <html lang="zh">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>VeriRandom</title>
        <style>
        html,body{height:100%;margin:0}
        body{display:flex;align-items:center;justify-content:center;background:#101418;color:#e8eaed;
             font:600 15px/1.4 "Segoe UI",system-ui,sans-serif}
        .card{display:flex;align-items:center;gap:12px;padding:20px 24px;border:1px solid #2b333b;
              border-radius:14px;background:#151b21}
        .mark{color:#4a9eff}
        </style>
        </head>
        <body><div class="card"><span class="mark">&#10003;</span><span>VeriRandom</span></div></body>
        </html>
        """;

    private const string PageTemplate = """
        <!DOCTYPE html>
        <html lang="zh">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>VeriRandom</title>
        <style>
        html,body{height:100%;margin:0}
        body{display:flex;align-items:center;justify-content:center;background:#101418;color:#e8eaed;
             font:600 15px/1.4 "Segoe UI",system-ui,sans-serif}
        .card{display:flex;align-items:center;gap:12px;padding:20px 24px;border:1px solid #2b333b;
              border-radius:14px;background:#151b21}
        .dot{width:10px;height:10px;border-radius:50%;background:#4a9eff;animation:p 1s ease-in-out infinite alternate}
        @keyframes p{from{opacity:.25}to{opacity:1}}
        </style>
        </head>
        <body>
        <div class="card"><span class="dot"></span><span>VeriRandom</span></div>
        <script>
        (() => {
          const operation = __OP__;
          const optionsJson = __OPTIONS__;
          const session = __SESSION__;

          const fromB64Url = (value) => {
            const padded = String(value).replace(/-/g, '+').replace(/_/g, '/');
            const binary = atob(padded + '='.repeat((4 - padded.length % 4) % 4));
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            return bytes.buffer;
          };

          const toB64Url = (buffer) => {
            const bytes = new Uint8Array(buffer);
            let binary = '';
            for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
            return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
          };

          const post = async (payload) => {
            payload.session = session;
            await fetch('complete', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(payload)
            });
          };

          (async () => {
            try {
              const publicKey = JSON.parse(optionsJson).publicKey;
              publicKey.challenge = fromB64Url(publicKey.challenge);
              if (publicKey.user) publicKey.user.id = fromB64Url(publicKey.user.id);
              for (const list of [publicKey.allowCredentials, publicKey.excludeCredentials]) {
                if (Array.isArray(list)) for (const item of list) item.id = fromB64Url(item.id);
              }

              if (operation === 'create') {
                const credential = await navigator.credentials.create({ publicKey });
                await post({
                  credentialId: toB64Url(credential.rawId),
                  clientDataJSON: toB64Url(credential.response.clientDataJSON),
                  attestationObject: toB64Url(credential.response.attestationObject),
                  transports: credential.response.getTransports ? credential.response.getTransports() : []
                });
              } else {
                const assertion = await navigator.credentials.get({ publicKey });
                await post({
                  credentialId: toB64Url(assertion.rawId),
                  clientDataJSON: toB64Url(assertion.response.clientDataJSON),
                  authenticatorData: toB64Url(assertion.response.authenticatorData),
                  signature: toB64Url(assertion.response.signature),
                  userHandle: assertion.response.userHandle ? toB64Url(assertion.response.userHandle) : null
                });
              }
            } catch (error) {
              await post({ error: String((error && (error.name || error.message)) || error) });
            }
          })();
        })();
        </script>
        </body>
        </html>
        """;
}
