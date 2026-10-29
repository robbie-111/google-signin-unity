# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A fork of `googlesamples/google-signin-unity`, shipped as a **source-only UPM package** (`com.robbie111.google-signin`). It wraps Google Sign-In for four targets behind one C# API:

- **Android** — migrated off the deprecated `GoogleSignInAccount` to `CredentialManager` + `AuthorizationClient`.
- **iOS** — GoogleSignIn-iOS 9.x (`GIDSignIn`), delivered via Swift Package Manager.
- **Editor / Standalone** — an explicit Google Identity Services (GIS) browser flow served through a local `HttpListener`, without a client secret.

There is no compiled artifact in this repo: no `.aar`, no prebuilt library, no gradle wrapper. Unity/Gradle/Xcode compile the Java and Objective-C++ sources in-place inside the consuming project.

## Repository layout

The repository root is **not** a Unity project.

| Path | What it is |
|---|---|
| `Packages/com.robbie111.google-signin/` | The UPM package — the only thing that ships |
| `SampleProject/` | A Unity 6000.3.18f1 project consuming the package via `file:../../Packages/com.robbie111.google-signin` in its `Packages/manifest.json` |

That `file:` path is **two** levels up (`SampleProject/Packages/` → `SampleProject/` → repo root), not three. Getting it wrong makes Unity fail to resolve the package and the project will not open.

The sample exists in **two places on purpose**:

| Copy | Role |
|---|---|
| `Packages/com.robbie111.google-signin/Samples~/SignInSample/` | What ships. `Samples~` is invisible to Unity (the `~`), so it is never compiled |
| `SampleProject/Assets/SignInSample/` | The working copy. This is the one Unity actually compiles and runs |

Edit the working copy, then copy it back before a release. They must stay identical:

```bash
diff -r Packages/com.robbie111.google-signin/Samples~/SignInSample \
        SampleProject/Assets/SignInSample
```

## Build, test, lint

There is **no CI, no linter, no formatter config, no test assembly, and no build script.** Do not invent commands.

The one verification available is a batch-mode compile of the sample project:

```bash
"/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -logFile - -projectPath SampleProject
```

Opening the project also regenerates any missing `.meta` files after files are moved. Run it after any restructuring.

Native paths cannot be verified this way — see "Implementation selection is order-sensitive" below. State plainly when a change has only been compile-checked.

## Constraints that will bite you

**Keep `.meta` files paired with their asset.** Every file and folder inside the package carries a `.meta` holding a stable GUID. Moving or renaming a file without its `.meta` makes Unity mint a new GUID, silently breaking scene and asmdef references. Always `git mv` both. After any move:

```bash
cd Packages/com.robbie111.google-signin
for f in $(git ls-files | grep -v '\.meta$'); do [ -f "$f.meta" ] || echo "NO META: $f"; done
for m in $(git ls-files | grep '\.meta$'); do [ -e "${m%.meta}" ] || echo "ORPHAN META: $m"; done
```

**Two assemblies, split by folder.** `Runtime/Robbie111.GoogleSignIn.asmdef` and `Editor/Robbie111.GoogleSignIn.Editor.asmdef` (Editor platform only, references the runtime one). New editor-only code belongs under `Editor/`; the `#if UNITY_EDITOR` guards still present in `Editor/iOS/*.cs` are now redundant but harmless.

**`Runtime/Impl/GoogleSignInImplEditor.cs` is runtime code, not editor code.** Its file guard is `UNITY_EDITOR || UNITY_STANDALONE`, so it must ship in standalone player builds. Its `UnityEditor.SessionState` calls are individually wrapped in `#if UNITY_EDITOR`. Do not move it to `Editor/`.

**Do not press "Import Sample" in `SampleProject`.** Package Manager would copy the sample a second time into `Assets/Samples/...`, and `GoogleSignInSample` would be defined twice in Assembly-CSharp — a compile error. The sample is already present at `Assets/SignInSample/`.

**`com.unity.ugui` is a sample-only dependency.** `GoogleSignInSample.cs` uses `UnityEngine.UI.Text`, so `SampleProject/Packages/manifest.json` needs `com.unity.ugui`. It is deliberately **not** in the package's `package.json` — the package itself does not use uGUI, and adding it would force the dependency on every consumer.

**Copy `.meta` files when duplicating the sample**, not just the assets. `Scenes/SampleScene.unity` references `Scripts/GoogleSignInSample.cs` by GUID `0e8c849e0354341de888147afa4fc1b9`; a fresh GUID turns the scene's MonoBehaviour into a Missing Script. Duplicate GUIDs across the two copies are harmless because `Samples~` never enters the asset database.

**The sample's method names are load-bearing.** `SampleScene.unity` wires buttons to `OnSignIn`, `OnSignOut`, `OnDisconnect`, `OnSignInSilently` and `OnGamesSignIn` through serialized `UnityEvent` entries that match by string. Renaming one silently breaks its button — nothing fails at compile time. The scene also stores a `m_TargetAssemblyTypeName` per binding, so moving the class into a namespace requires updating those entries too.

**`WebClientId` is required on every platform**, including Android — CredentialManager uses it as the server client ID. Editor/Standalone also uses this Web application client ID through GIS, without a client secret. `DesktopLoginPort` defaults to 49215; consumers must register `http://localhost` and `http://localhost:49215` as authorized JavaScript origins (adjust the latter if the configured port changes).

**Platform-specific configuration options.** `AccountName` is an optional user ID/email hint: iOS forwards it as `hint` and clears the stored hint for null/empty configuration; Editor/Standalone forwards it as GIS `login_hint`. Android retains the shared argument but ignores it because the credential request API has no account-name hint. Never pass an account name to `setNonce`. The desktop flow generates its own random nonce independently.

`HidePopups` is Android-only: both entry points pass it to `setPreferImmediatelyAvailableCredentials`; `SignIn()` also passes it to `setFilterByAuthorizedAccounts` and `setAutoSelectEnabled`. It does not guarantee that no UI appears and is not dependent on `UseGameSignIn`. iOS and Editor/Standalone ignore it. Keep its current behavior when changing unrelated sign-in code.

**Do not reintroduce `System.Web`.** The runtime package must remain compatible with `.NET Standard 2.1`. The desktop GIS flow uses JSON callbacks rather than the old authorization-code query parser.

## Architecture

```
GoogleSignIn (static facade, Google namespace)
  └─ ISignInImpl ──┬── GoogleSignInImpl        Android JNI  /  iOS __Internal
                   └── GoogleSignInImplEditor  Editor + Standalone GIS loopback
  Future<T> ──┬── FutureAPIImpl<T> ──┬── NativeFuture : BaseObject  (polls native)
              │                      └── SignInResult              (one per desktop request)
              └── WaitForResult / WaitForResultAsync → TaskCompletionSource<T>
  SignInHelperObject : MonoBehaviour   hidden DontDestroyOnLoad coroutine runner
```

### Implementation selection is order-sensitive

`Runtime/GoogleSignIn.cs:88-95` checks `#if UNITY_EDITOR || UNITY_STANDALONE` **before** `#elif UNITY_ANDROID || UNITY_IOS`. Consequence: **play mode always runs the desktop GIS flow**, even with the build target set to Android or iOS. Native paths can only be exercised in a real device build. Keep this in mind before concluding a native change "works".

### Async model

`Future<T>` is a façade over `FutureAPIImpl<T>` (`Pending` / `Status` / `Result`). Two bridges to `Task`, both in `Runtime/Future.cs:72-97`: the coroutine path (`WaitUntil(() => !Pending)`) and the task path (`while (Pending) await Task.Yield()`). Both busy-poll and both resolve the `TaskCompletionSource` on the Unity main thread — that is the entire thread-marshalling mechanism. Status mapping is uniform: `CANCELED` → `SetCanceled`, `SUCCESS`/`SUCCESS_CACHE` → `SetResult`, anything else → `SetException(new SignInException(Status))`.

Callers must still hop back themselves; the canonical pattern from the shipped sample is load-bearing:

```csharp
GoogleSignIn.DefaultInstance.SignIn()
  .ContinueWith(OnAuthenticationFinished, TaskScheduler.FromCurrentSynchronizationContext());
```

### Explicit sign-in only

`SignIn()` and `SignInLegacy()` are explicit sign-in entry points. Only Android retains different credential request paths. Do not add silent restoration or cache-only login to the other platforms.

| | `SignInLegacy()` | `SignIn()` |
|---|---|---|
| Android | Existing `GetSignInWithGoogleOption` request | Existing `GetGoogleIdOption` request |
| iOS | `signInWithPresentingViewController` | Delegates to `GoogleSignIn_SignInLegacy` |
| Editor / Standalone | Interactive localhost GIS page | Delegates to `SignInLegacy()` |

All Task, Async and Future variants use these same underlying methods. `SUCCESS_CACHE` and its handling in the shared `Future<T>` bridge remain for compatibility, but the desktop implementation does not return cached results. The sample's historical `OnSignInSilently` method name is retained only for serialized scene bindings; it calls the explicit `SignIn()` API.

### Android (`Runtime/Impl/GoogleSignInImpl.cs:98-229`)

Pure JNI, **no `UnitySendMessage` anywhere in the repo**. C# calls the all-static Java class `com.google.googlesignin.GoogleSignInHelper` (`Plugins/Android/src/main/java/com/google/googlesignin/GoogleSignInHelper.java`, made static so sign-in can repeat), and receives results through `SignInListener : AndroidJavaProxy` implementing `IListener`.

`configure()` only builds and stores a closure; `signInLegacy()`/`signIn()` invoke it with different options. The flow is two chained calls: `CredentialManager.getCredentialAsync` (`GetSignInWithGoogleOption` or `GetGoogleIdOption`) → `onAuthenticated`, then `Identity.getAuthorizationClient().authorize()` with `requestOfflineAccess` → `onAuthorized` for the server auth code.

Two consequences worth knowing: results are cached in **static** fields (`googleIdTokenCredential`, `authorizationResult`) and the `Get*` accessors ignore their `HandleRef` argument entirely, so Android is effectively single-flight. And `GoogleIdTokenCredential` no longer exposes a numeric user ID, so `GoogleSignIn_GetUserId` base64url-decodes the id token's JWT payload and reads `sub` via Newtonsoft — that is why Newtonsoft.Json is a hard runtime dependency.

### iOS (`Plugins/iOS/`)

`GoogleSignIn.mm:182-387` exports an `extern "C"` block that matches the `[DllImport("__Internal")]` declarations in `Runtime/Impl/GoogleSignInImpl.cs:231-301` **one-to-one**. All string getters use a two-pass convention (call with `null, 0` to get the required size, then fill the buffer) implemented on the C# side by `BaseObject.OutParamsToString`.

The app controller is **not** hooked with `IMPL_APP_CONTROLLER_SUBCLASS`. `GoogleSignInAppController.mm:37-60` declares a `UnityAppController (GoogleSignInController)` category and swizzles three selectors in `+load` via `method_exchangeImplementations`, chaining to the original implementation before `[[GIDSignIn sharedInstance] handleURL:url]`.

`GoogleSignIn.mm:31-50` maps `kGIDSignInErrorCode*` onto the Android `CommonStatusCodes` integers, so both platforms feed one `GoogleSignInStatusCode` enum. `GoogleSignIn_Configure` overrides only the *serverClientID* at runtime; the `clientID` itself comes from `GIDClientID` in Info.plist, written at post-build time (below).

### Editor / Standalone (`Runtime/Impl/GoogleSignInImplEditor.cs`)

Both entry points start the same explicit GIS flow. An `HttpListener` serves a temporary local page on the configured fixed `DesktopLoginPort` (default 49215), and `Application.OpenURL` opens it in the system browser. The page uses `google.accounts.id.initialize` and `renderButton` in popup mode; it does not use One Tap. The callback posts its ID-token `credential` to the listener. No authorization-code exchange, client secret or user-info API request is used. `RequestAuthCode` is rejected with `DEVELOPER_ERROR`.

Requests use a random session path and nonce, require same-origin loopback callbacks, and check JWT issuer, audience and expiry. JWT parsing is not signature verification: the consuming backend must verify the original token. `RequestIdToken`, `RequestEmail` and `RequestProfile` control returned fields. Never log the token.

There is no token cache or silent restore. `ForceTokenRefresh` has no effect on Editor/Standalone. Results are held in a per-request `SignInResult`, and a second concurrent request is rejected. A request finishes on success, error, explicit cancellation or a 120-second timeout and closes its listener. Network work avoids the Unity synchronization context; result completion returns to it. `SignOut()` cancels a pending request, and shutdown/domain reload also cancels it. `Disconnect()` remains unimplemented; it must not be documented as Google grant revocation.

## Cross-boundary contracts

Changes here are only correct if both sides move together:

| If you change | Also update |
|---|---|
| A `[DllImport("__Internal")]` signature | the matching `extern "C"` export in `Plugins/iOS/GoogleSignIn.mm:182-387` |
| A `GoogleSignInHelper` Java method signature | the `CallStatic` site in `Runtime/Impl/GoogleSignInImpl.cs:98-229` — **argument order is positional and unchecked** (`configure` passes `accountName` before `additionalScopes`) |
| The `IListener` Java interface | `SignInListener : AndroidJavaProxy` in `Runtime/Impl/GoogleSignInImpl.cs:130-155` — method names are matched by string at runtime |
| `GoogleSignInConfiguration` fields | `GoogleSignIn_Configure` on **both** impls plus the Java `configure` parameter list |
| `GoogleSignInStatusCode` | the iOS error mapping at `GoogleSignIn.mm:31-50` and Java `getStatus()` |
| A sign-in entry point name | all four layers move together: Java `signInLegacy`/`signIn`, the `CallStatic` strings and `DllImport` names in `Runtime/Impl/GoogleSignInImpl.cs`, the `extern "C"` exports in `Plugins/iOS/GoogleSignIn.mm`, `ISignInImpl`, and the facade |
| Android/iOS SDK versions | `Editor/GoogleSignInDependencies.xml` (currently `androidx.credentials 1.3.0`, `googleid 1.1.1`, `play-services-auth 21.3.0`; iOS GoogleSignIn 9.0.0 via both an `iosPod` and a `remoteSwiftPackage` with `replacesPod`) — the SPM entry needs EDM4U ≥ 1.2.187, which is why `package.json` pins it |

## iOS Info.plist pipeline

Two post-build processors write `GIDClientID` into the generated Xcode `Info.plist`, and **both are active**:

- `Editor/iOS/PostBuildProcessor.cs` (class is confusingly named `GameBuilder`), `callbackOrder => 999` — the fallback: derives the client ID by reversing a `com.googleusercontent.apps.*` entry from `PlayerSettings.iOS.iOSUrlSchemes`.
- `Editor/iOS/PListProcessor.cs`, `callbackOrder => 99999` — the real path, so it **wins**: finds the project `.plist` asset whose `BUNDLE_ID` matches the iOS bundle identifier, then writes `GIDClientID` from `CLIENT_ID`, appends `CFBundleURLTypes` with `REVERSED_CLIENT_ID`, and writes `GIDServerClientID` from `WEB_CLIENT_ID`. It throws `KeyNotFoundException` when those keys are absent.

`WEB_CLIENT_ID` is **not** present in the plist downloaded from Google Cloud Console — it must be added by hand if server auth codes are needed. `Editor/iOS/PListImporter.cs` is a `ScriptedImporter` that merely makes `.plist` files importable as `TextAsset` so `AssetDatabase.FindAssets` can locate them. Client plists are credentials; keep them out of the repo.

## Publishing

`com.google.external-dependency-manager` is **not** on Google's `unityregistry-pa.googleapis.com` (that URL 404s for it) and not on Unity's registry. Google ships EDM4U as a `.unitypackage` or a git URL, neither of which a `package.json` dependency can resolve. The only usable source is OpenUPM's mirror, so consumers need this scoped registry:

```json
"scopedRegistries": [
  { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": ["com.google.external-dependency-manager"] }
]
```

`SampleProject/Packages/manifest.json` already carries it. `com.unity.nuget.newtonsoft-json` comes from Unity's registry and needs nothing.

The layout is OpenUPM-compatible — OpenUPM auto-discovers a `package.json` in a subfolder, with no path field to configure. Publishing there additionally requires a semver git tag whose version **exactly** matches `package.json`'s `version` (mismatch fails with build error `E811`), a public GitHub repo, and a PR to `openupm/openupm`. None of that has been done yet; the repo currently has no tags.

## Known rough edges

Pre-existing, not introduced by recent work — worth flagging rather than silently inheriting:

- `Runtime/link.xml` declares the type as `UnityEngine.AndroidJNIModule.AndroidJavaObject`, but `AndroidJNIModule` is the **assembly**, not a namespace; the real type is `UnityEngine.AndroidJavaObject`. As written the rule matches nothing and the IL2CPP preservation is silently inert — which is likely why the discarded-tuple hack in `NativeFuture.cs` exists to defeat tree-shaking.
- `Disconnect()` throws `NotImplementedException` on the editor impl; `EnableDebugLogging()` logs status codes only.
- Java `getStatus()` maps `NoCredentialException` to `DEVELOPER_ERROR`. `SIGN_IN_REQUIRED` would describe "no Google account on the device" better, but the mapping is kept to match the fork it came from.
- The lower half of the package `README.md` is explicitly marked as upstream reference text and is partly outdated.

## Contributing

`CONTRIBUTING.md` is inherited verbatim from Google: contributors sign the Google Individual CLA, and all submissions go through GitHub pull requests. It specifies no code style, formatting, or test requirements. Commit messages here are informal — no conventional-commits prefix, no issue-linking convention.
