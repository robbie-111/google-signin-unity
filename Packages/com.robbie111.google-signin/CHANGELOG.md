# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0]

First release under the new package id and repository layout.

### Changed

- **Breaking — package renamed** from `com.google.signin` to `com.robbie111.google-signin`. Consumers must remove the old entry from `manifest.json` and add the new one.
- **Breaking — assembly renamed** from `GoogleSignin` to `Robbie111.GoogleSignIn`. Any `.asmdef` referencing this package needs updating. The C# namespace `Google` and the public API are unchanged, so calling code does not change.
- **Breaking — install URL now requires `?path=`**, because the package moved from the repository root into `Packages/com.robbie111.google-signin/`.
- The package now follows the standard UPM layout: runtime code under `Runtime/`, editor code under `Editor/`.
- Editor code moved into its own assembly (`Robbie111.GoogleSignIn.Editor`, Editor platform only) instead of being compiled into the runtime assembly behind `#if UNITY_EDITOR` guards.

### Removed

- **The `System.Web` dependency and the `csc.rsp` that pulled it in.** The single use of `HttpUtility.ParseQueryString` in the Editor/Standalone OAuth flow was replaced with a local query-string parser. Consumers no longer have to set Api Compatibility Level to `.NET Framework`; the package builds under the default `.NET Standard 2.1`.
- `google-signin-plugin_v1.0.4.txt`, a dead External Dependency Manager VersionHandler manifest listing pre-UPM asset paths that no longer exist.

### Added

- `SampleProject/`, a Unity project wired to the package through a relative `file:` dependency, giving the repository its first way to verify a change compiles. The sample now lives there too (`Assets/SignInSample/`), so the sample script is compiled for the first time instead of sitting uncompiled in `Samples~`.
### Changed

- **The sample was replaced.** The old Unity 5.6.1f1 binary scene and `SigninSampleScript.cs` are gone; the sample is now `Scenes/SampleScene.unity` + `Scripts/GoogleSignInSample.cs`, carrying buttons for interactive sign-in, silent sign-in, games sign-in, sign-out and disconnect. The scene is YAML text, so it is diffable in review.
