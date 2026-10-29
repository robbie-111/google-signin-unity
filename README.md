# Google Sign-In for Unity

Google Sign-In for Unity, covering Android, iOS and the Editor/Standalone player from one C# API.

A fork of [googlesamples/google-signin-unity](https://github.com/googlesamples/google-signin-unity), rebuilt on the current Google identity stack:

- **Android** — migrated off the deprecated `GoogleSignInAccount` to `CredentialManager` + `AuthorizationClient`.
- **iOS** — GoogleSignIn-iOS 9.x (`GIDSignIn`), delivered through Swift Package Manager.
- **Editor / Standalone** — Google Identity Services in the system browser, returning an ID token through a localhost page without a client secret. See the [desktop setup](Packages/com.robbie111.google-signin/README.md#editor--desktop-sign-in).

## Repository layout

This repository root is not a Unity project. It holds the package and a project used to verify it.

| Path | What it is |
|---|---|
| `Packages/com.robbie111.google-signin/` | The UPM package. This is the thing that ships. |
| `SampleProject/` | A Unity 6000.3.18f1 project that consumes the package through a relative `file:` dependency, used for compile checks and device builds. The sample scene is at `Assets/SignInSample/Scenes/SampleScene.unity` and is registered in the build settings. |

## Installation

Add the package via a Git URL in `Packages/manifest.json`. The `?path=` query is required because the package lives in a subfolder:

```json
"com.robbie111.google-signin": "https://github.com/robbie-111/google-signin-unity.git?path=Packages/com.robbie111.google-signin"
```

Pin a specific revision by appending an anchor, for example `...google-signin#v1.0.0`.

### Dependencies

The package declares two dependencies. `com.unity.nuget.newtonsoft-json` resolves from Unity's registry with no extra setup. `com.google.external-dependency-manager` (EDM4U) does not — Google does not publish it to a Unity registry, so add the OpenUPM registry to your `manifest.json`:

```json
"scopedRegistries": [
  {
    "name": "package.openupm.com",
    "url": "https://package.openupm.com",
    "scopes": ["com.google.external-dependency-manager"]
  }
]
```

## Migrating from `com.google.signin`

This package was previously published as `com.google.signin` from the repository root. Both the package id and the install URL changed, so remove the old entry from `manifest.json` before adding the new one:

```diff
- "com.google.signin": "https://github.com/Thaina/google-signin-unity.git#newmigration"
+ "com.robbie111.google-signin": "https://github.com/robbie-111/google-signin-unity.git?path=Packages/com.robbie111.google-signin"
```

The assembly name also changed from `GoogleSignin` to `Robbie111.GoogleSignIn`, so any `.asmdef` that referenced this package needs its reference updated. **Calling code does not change** — the C# namespace is still `Google` and the public API is unchanged.

One requirement was also removed: earlier versions needed Api Compatibility Level set to `.NET Framework` because of a `System.Web` reference. That dependency is gone, and the package now builds under the default `.NET Standard 2.1`.

## Usage

See [the package README](Packages/com.robbie111.google-signin/README.md) for configuration, Google Cloud Console setup, and platform specifics. A runnable sample ships with the package and can be imported from the Package Manager window.

## License

Apache License 2.0. See [LICENSE](Packages/com.robbie111.google-signin/LICENSE).
