#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Google.Impl
{
    internal class GoogleSignInImplEditor : ISignInImpl
    {
        const int LoginTimeoutMilliseconds = 120000;
        const int MaximumCallbackBytes = 64 * 1024;
        readonly GoogleSignInConfiguration initialConfiguration;
        LoginRequest activeRequest;
        bool debugLogging;

        public GoogleSignInImplEditor(GoogleSignInConfiguration configuration)
        {
            initialConfiguration = configuration;
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void EnableDebugLogging(bool flag)
        {
            debugLogging = flag;
        }

        public Future<GoogleSignInUser> SignInLegacy()
        {
            if (activeRequest != null)
                return Failure("A desktop sign-in request is already in progress.");

            Settings settings;
            try
            {
                settings = GetSettings();
            }
            catch (GoogleSignIn.SignInException error)
            {
                return Failure(error.Message);
            }

            var context = SynchronizationContext.Current;
            if (context == null)
                return Failure("Start desktop sign-in on the Unity main thread.");

            var request = new LoginRequest(settings, context);
            try
            {
                request.Listener.Prefixes.Add(request.Origin + "/");
                request.Listener.Start();
            }
            catch (Exception)
            {
                request.Dispose();
                return Failure("Cannot listen on " + request.Origin +
                               ". Check that DesktopLoginPort is available and permitted on this system.");
            }

            activeRequest = request;
            Application.quitting += CancelActiveRequest;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += CancelActiveRequest;
#endif
            try
            {
                Application.OpenURL(request.Origin + request.PagePath);
                // HTTP runs without requiring a focused Unity window. Completion is
                // dispatched back to the Unity synchronization context.
                _ = ReceiveSignInAsync(request);
            }
            catch (Exception)
            {
                activeRequest = null;
                UnsubscribeFromShutdown();
                request.Dispose();
                request.Result.Complete(GoogleSignInStatusCode.ERROR);
                Debug.LogError("GoogleSignIn: Could not open the desktop sign-in page.");
            }

            return new Future<GoogleSignInUser>(request.Result);
        }

        public Future<GoogleSignInUser> SignIn()
        {
            return SignInLegacy();
        }

        public void SignOut()
        {
            CancelActiveRequest();
        }

        Settings GetSettings()
        {
            // The facade permits replacing Configuration after SignOut while keeping
            // its singleton. Resolve it at the start of each request, then snapshot it.
            var configuration = GoogleSignIn.Configuration ?? initialConfiguration;
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.WebClientId) ||
                !configuration.WebClientId.Trim().EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
                throw new GoogleSignIn.SignInException(GoogleSignInStatusCode.DEVELOPER_ERROR,
                    "Set WebClientId to your Google Web application client ID.");
            if (configuration.RequestAuthCode)
                throw new GoogleSignIn.SignInException(GoogleSignInStatusCode.DEVELOPER_ERROR,
                    "Desktop GIS sign-in returns ID tokens only. Set RequestAuthCode to false.");
            if (configuration.DesktopLoginPort < 1 || configuration.DesktopLoginPort > 65535)
                throw new GoogleSignIn.SignInException(GoogleSignInStatusCode.DEVELOPER_ERROR,
                    "DesktopLoginPort must be between 1 and 65535.");
            return new Settings(configuration);
        }

        async Task ReceiveSignInAsync(LoginRequest request)
        {
            var outcome = new Outcome(GoogleSignInStatusCode.ERROR);
            try
            {
                using (request.Cancellation.Token.Register(request.CloseListener))
                {
                    request.Cancellation.CancelAfter(LoginTimeoutMilliseconds);
                    outcome = await ReceiveRequestsAsync(request).ConfigureAwait(false);
                    request.Cancellation.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception)
            {
                outcome = new Outcome(request.Cancellation.IsCancellationRequested
                    ? GoogleSignInStatusCode.TIMEOUT
                    : GoogleSignInStatusCode.ERROR);
            }
            finally
            {
                request.Dispose();
            }

            request.Context.Post(_ => FinishRequest(request, outcome), null);
        }

        void FinishRequest(LoginRequest request, Outcome outcome)
        {
            // SignOut/domain reload may have canceled this request while a callback
            // was in flight. It must not finish a newer request.
            if (activeRequest != request)
                return;
            activeRequest = null;
            UnsubscribeFromShutdown();
            request.Result.Complete(outcome.Status, outcome.User);
            if (debugLogging)
                Debug.Log("GoogleSignIn desktop result: " + outcome.Status);
        }

        void CancelActiveRequest()
        {
            var request = activeRequest;
            if (request == null)
                return;
            activeRequest = null;
            UnsubscribeFromShutdown();
            request.Result.Complete(GoogleSignInStatusCode.CANCELED);
            request.Cancel();
        }

        void UnsubscribeFromShutdown()
        {
            Application.quitting -= CancelActiveRequest;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= CancelActiveRequest;
#endif
        }

        static async Task<Outcome> ReceiveRequestsAsync(LoginRequest request)
        {
            while (true)
            {
                var context = await request.Listener.GetContextAsync().ConfigureAwait(false);
                try
                {
                    var incoming = context.Request;
                    var address = incoming.RemoteEndPoint?.Address;
                    if (address == null ||
                        !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address) ||
                        incoming.Url == null || incoming.Url.GetLeftPart(UriPartial.Authority) != request.Origin)
                    {
                        await ReplyAsync(context, 403, "text/plain", "Forbidden").ConfigureAwait(false);
                        continue;
                    }

                    string path = incoming.Url.AbsolutePath;
                    if (incoming.HttpMethod == "GET" && path == request.PagePath)
                    {
                        await ReplyAsync(context, 200, "text/html", CreateLoginPage(request)).ConfigureAwait(false);
                        continue;
                    }

                    bool callbackPath = path == request.PagePath + "credential" ||
                                        path == request.PagePath + "cancel" || path == request.PagePath + "error";
                    if (!callbackPath)
                    {
                        await ReplyAsync(context, 404, "text/plain", "Not found").ConfigureAwait(false);
                        continue;
                    }

                    if (incoming.HttpMethod != "POST" || incoming.Headers["Origin"] != request.Origin)
                    {
                        await ReplyAsync(context, 403, "text/plain", "Forbidden").ConfigureAwait(false);
                        continue;
                    }

                    if (incoming.ContentType == null ||
                        !string.Equals(incoming.ContentType.Split(';')[0].Trim(), "application/json",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await ReplyAsync(context, 415, "text/plain", "Expected application/json").ConfigureAwait(false);
                        continue;
                    }

                    if (path == request.PagePath + "cancel" || path == request.PagePath + "error")
                    {
                        await ReplyAsync(context, 200, "application/json", "{}").ConfigureAwait(false);
                        return new Outcome(path.EndsWith("cancel", StringComparison.Ordinal)
                            ? GoogleSignInStatusCode.CANCELED
                            : GoogleSignInStatusCode.NETWORK_ERROR);
                    }

                    string token;
                    GoogleSignInUser user;
                    try
                    {
                        string body = await ReadCallbackAsync(incoming, request.Cancellation.Token)
                            .ConfigureAwait(false);
                        var payload = JObject.Parse(body);
                        token = StringClaim(payload, "credential");
                        var claims = ReadClaims(token, request.Settings.ClientId, request.Nonce);
                        user = CreateUser(token, claims, request.Settings);
                    }
                    catch (Exception error) when (error is JsonException || error is FormatException ||
                                                  error is ArgumentException || error is OverflowException)
                    {
                        await ReplyAsync(context, 400, "text/plain", "Invalid sign-in response").ConfigureAwait(false);
                        return new Outcome(GoogleSignInStatusCode.ERROR);
                    }

                    request.Cancellation.Token.ThrowIfCancellationRequested();
                    await ReplyAsync(context, 200, "application/json", "{}").ConfigureAwait(false);
                    return new Outcome(GoogleSignInStatusCode.SUCCESS, user);
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        static async Task<string> ReadCallbackAsync(HttpListenerRequest request, CancellationToken cancellation)
        {
            if (request.ContentLength64 > MaximumCallbackBytes)
                throw new FormatException("Sign-in response is too large.");
            using (var body = new MemoryStream())
            {
                var buffer = new byte[4096];
                int count;
                while ((count = await request.InputStream.ReadAsync(buffer, 0, buffer.Length, cancellation)
                           .ConfigureAwait(false)) != 0)
                {
                    if (body.Length + count > MaximumCallbackBytes)
                        throw new FormatException("Sign-in response is too large.");
                    body.Write(buffer, 0, count);
                }

                return Encoding.UTF8.GetString(body.ToArray());
            }
        }

        static async Task ReplyAsync(HttpListenerContext context, int status, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType + "; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer-when-downgrade";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        // These checks bind the response to this explicit sign-in request.
        // They do NOT verify the JWT signature. The consuming server must verify
        // Google's signature and claims before trusting this token as authentication.
        static JObject ReadClaims(string token, string clientId, string nonce)
        {
            if (string.IsNullOrEmpty(token) || token.Length > MaximumCallbackBytes)
                throw new FormatException("Missing or oversized ID token.");
            string[] parts = token.Split('.');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
                throw new FormatException("Invalid ID token.");
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            string issuer = StringClaim(claims, "iss");
            var expiry = claims["exp"];
            if ((issuer != "https://accounts.google.com" && issuer != "accounts.google.com") ||
                StringClaim(claims, "aud") != clientId || string.IsNullOrEmpty(StringClaim(claims, "sub")) ||
                expiry == null || expiry.Type != JTokenType.Integer ||
                expiry.Value<long>() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                StringClaim(claims, "nonce") != nonce)
                throw new FormatException("ID token claims do not match this sign-in request.");
            return claims;
        }

        static string StringClaim(JObject claims, string name)
        {
            return claims[name]?.Type == JTokenType.String ? claims[name].Value<string>() : null;
        }

        static GoogleSignInUser CreateUser(string token, JObject claims, Settings settings)
        {
            var user = new GoogleSignInUser
            {
                IdToken = settings.RequestIdToken ? token : null,
                UserId = StringClaim(claims, "sub"),
                DisplayName = StringClaim(claims, "name"),
                Email = settings.RequestEmail ? StringClaim(claims, "email") : null
            };
            if (settings.RequestProfile)
            {
                user.GivenName = StringClaim(claims, "given_name");
                user.FamilyName = StringClaim(claims, "family_name");
                if (Uri.TryCreate(StringClaim(claims, "picture"), UriKind.Absolute, out var picture) &&
                    (picture.Scheme == Uri.UriSchemeHttps || picture.Scheme == Uri.UriSchemeHttp))
                    user.ImageUrl = picture;
            }

            return user;
        }

        static Future<GoogleSignInUser> Failure(string message)
        {
            Debug.LogError("GoogleSignIn: " + message);
            var result = new SignInResult();
            result.Complete(GoogleSignInStatusCode.DEVELOPER_ERROR);
            return new Future<GoogleSignInUser>(result);
        }

        sealed class SignInResult : FutureAPIImpl<GoogleSignInUser>
        {
            volatile bool pending = true;
            public bool Pending => pending;
            public GoogleSignInStatusCode Status { get; private set; }
            public GoogleSignInUser Result { get; private set; }

            public void Complete(GoogleSignInStatusCode status, GoogleSignInUser user = null)
            {
                Status = status;
                Result = user;
                pending = false;
            }
        }

        sealed class Settings
        {
            public readonly string ClientId;
            public readonly int Port;
            public readonly bool RequestIdToken, RequestEmail, RequestProfile;

            public Settings(GoogleSignInConfiguration configuration)
            {
                ClientId = configuration.WebClientId.Trim();
                Port = configuration.DesktopLoginPort;
                RequestIdToken = configuration.RequestIdToken;
                RequestEmail = configuration.RequestEmail;
                RequestProfile = configuration.RequestProfile;
            }
        }

        sealed class LoginRequest : IDisposable
        {
            public readonly Settings Settings;
            public readonly SynchronizationContext Context;
            public readonly SignInResult Result = new SignInResult();
            public readonly HttpListener Listener = new HttpListener();
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly string Origin, PagePath, Nonce;

            public LoginRequest(Settings settings, SynchronizationContext context)
            {
                Settings = settings;
                Context = context;
                Origin = new UriBuilder("http", "localhost", settings.Port).Uri.GetLeftPart(UriPartial.Authority);
                PagePath = "/signin/" + RandomValue() + "/";
                Nonce = RandomValue();
            }

            public void CloseListener()
            {
                try
                {
                    Listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void Cancel()
            {
                try
                {
                    Cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                CloseListener();
            }

            public void Dispose()
            {
                CloseListener();
                Cancellation.Dispose();
            }

            static string RandomValue()
            {
                var bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create())
                    random.GetBytes(bytes);
                return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        sealed class Outcome
        {
            public readonly GoogleSignInStatusCode Status;
            public readonly GoogleSignInUser User;

            public Outcome(GoogleSignInStatusCode status, GoogleSignInUser user = null)
            {
                Status = status;
                User = user;
            }
        }

        static string CreateLoginPage(LoginRequest request)
        {
            var settings = JsonConvert.SerializeObject(new
            {
                clientId = request.Settings.ClientId,
                nonce = request.Nonce,
                callbackPath = request.PagePath,
                timeout = LoginTimeoutMilliseconds
            }, new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml });
            return LoginPageTemplate.Replace("__GIS_SETTINGS__", settings);
        }

        const string LoginPageTemplate = @"<!doctype html>
<html lang='ko'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Google 계정으로 로그인</title>
<style>
body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f4f6f8; color: #202124; font-family: system-ui, sans-serif; }
main { box-sizing: border-box; width: min(420px, 94vw); padding: 32px; background: white; border-radius: 16px; text-align: center; box-shadow: 0 8px 30px #0001; }
h1 { font-size: 22px; } #google-button { display: flex; justify-content: center; margin: 24px 0; }
#status { line-height: 1.6; } #cancel { padding: 10px 24px; background: white; border: 1px solid #dadce0; border-radius: 6px; cursor: pointer; }
</style>
</head>
<body>
<main>
<h1>Google 계정으로 로그인</h1>
<p id='status' role='status' aria-live='polite'>로그인 버튼을 불러오는 중입니다.</p>
<div id='google-button'></div>
<button id='cancel' type='button'>취소</button>
</main>
<script>
(() => {
  const settings = __GIS_SETTINGS__;
  const status = document.getElementById('status');
  const button = document.getElementById('google-button');
  const cancel = document.getElementById('cancel');
  let finished = false;
  let sending = false;
  const timer = setTimeout(() => finish('로그인 시간이 초과되었습니다. 앱에서 다시 시도해 주세요.'), settings.timeout);

  function finish(message) {
    if (finished) return;
    finished = true;
    clearTimeout(timer);
    button.replaceChildren();
    cancel.hidden = true;
    status.textContent = message;
  }
  async function post(endpoint, body) {
    const response = await fetch(settings.callbackPath + endpoint, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), cache: 'no-store', credentials: 'omit'
    });
    if (!response.ok) throw new Error('Sign-in delivery failed');
  }
  async function receiveCredential(response) {
    if (finished || sending) return;
    sending = true;
    cancel.disabled = true;
    status.textContent = '앱으로 로그인 결과를 전달하고 있습니다.';
    try {
      await post('credential', { credential: response.credential });
      finish('로그인이 완료되었습니다. 이 창을 닫고 앱으로 돌아가 주세요.');
    } catch (_) {
      finish('로그인 결과를 전달하지 못했습니다. 앱에서 다시 시도해 주세요.');
    }
  }
  function sdkFailed() {
    if (finished) return;
    finish('Google 로그인을 불러오지 못했습니다. 네트워크 및 브라우저 설정을 확인해 주세요.');
    post('error', {}).catch(() => {});
  }
  cancel.addEventListener('click', async () => {
    if (finished || sending) return;
    sending = true;
    cancel.disabled = true;
    try {
      await post('cancel', {});
      finish('로그인이 취소되었습니다. 이 창을 닫아 주세요.');
    } catch (_) {
      finish('앱과의 연결이 종료되었습니다. 이 창을 닫아 주세요.');
    }
  });
  const script = document.createElement('script');
  script.src = 'https://accounts.google.com/gsi/client';
  script.async = true;
  script.onerror = sdkFailed;
  script.onload = () => {
    if (finished) return;
    try {
      google.accounts.id.initialize({
        client_id: settings.clientId, nonce: settings.nonce,
        callback: receiveCredential, ux_mode: 'popup', auto_select: false
      });
      google.accounts.id.renderButton(button, { theme: 'outline', size: 'large', width: 300, locale: 'ko' });
      status.textContent = '아래 버튼을 눌러 Google 계정을 선택해 주세요.';
    } catch (_) { sdkFailed(); }
  };
  document.head.appendChild(script);
})();
</script>
</body>
</html>";
    }

    public static class EditorExt
    {
        public static Task<string> Post(this HttpWebRequest request, string contentType, string data,
            Encoding encoding = null)
        {
            if (encoding == null)
                encoding = Encoding.UTF8;

            request.Method = "POST";
            request.ContentType = contentType;
            using (var stream = request.GetRequestStream())
                stream.Write(encoding.GetBytes(data));

            return request.GetResponseAsStringAsync(encoding);
        }

        public static async Task<string> GetResponseAsStringAsync(this HttpWebRequest request, Encoding encoding = null)
        {
            using (var response = await request.GetResponseAsync())
            {
                using (var stream = response.GetResponseStream())
                    return stream.ReadToEnd(encoding ?? Encoding.UTF8);
            }
        }

        public static string ReadToEnd(this Stream stream, Encoding encoding = null) =>
            new StreamReader(stream, encoding ?? Encoding.UTF8).ReadToEnd();

        public static void Write(this Stream stream, byte[] data) => stream.Write(data, 0, data.Length);
    }
}

#endif