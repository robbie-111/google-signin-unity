// <copyright file="GoogleSignInConfiguration.cs" company="Google Inc.">
// Copyright (C) 2017 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

namespace Google {
  using System.Collections.Generic;

  /// <summary>
  /// Configuration properties for Google Sign-In.
  /// </summary>
  public class GoogleSignInConfiguration {
    /// <summary>Set to true to use games signin, false for default signin.
    /// </summary>
    /// <remarks>Note: The Games configuration is not supported on non-Android
    /// platforms.
    /// If games configuration is used, you must also add the
    /// play-services-games libraries and dependencies.
    /// See the README for more details.
    /// </remarks>
    public bool UseGameSignIn = false;
#if UNITY_EDITOR || UNITY_STANDALONE
    /// <summary>Fixed localhost port for the Editor and desktop GIS login page.</summary>
    /// <remarks>Register http://localhost and http://localhost:PORT as authorized
    /// JavaScript origins for WebClientId. An occupied port fails sign-in;
    /// a different port is never selected automatically.</remarks>
    public int DesktopLoginPort = 49215;
#endif
    /// <summary>Web client id associated with this app.</summary>
    /// <remarks>Required for requesting auth code or id token.</remarks>
    public string WebClientId = null;
    /// <summary>Set to true for getting an auth code when authenticating.
    /// </summary>
    /// <remarks>Not supported by Editor/desktop GIS sign-in; requesting an auth
    /// code there returns DEVELOPER_ERROR.</remarks>
    public bool RequestAuthCode = false;
    /// <summary>Set to true to request to reset the refresh token.
    ///   Causes re-consent.
    /// </summary>
    /// <remarks>Ignored on Editor/desktop, where both sign-in methods always
    /// start the same interactive GIS flow without a token cache.</remarks>
    public bool ForceTokenRefresh = false;
    /// <summary>Request email address, requires consent.</summary>
    public bool RequestEmail = false;
    /// <summary>Request id token, requires consent.</summary>
    public bool RequestIdToken = false;
    /// <summary>Request profile information, requires consent.</summary>
    public bool RequestProfile = true;
    /// <summary>Controls Android Credential Manager account selection behavior.</summary>
    /// <remarks>Both Android sign-in methods prefer immediately available credentials
    /// when true. SignIn also filters to authorized accounts and permits automatic
    /// selection. This does not guarantee that every popup is hidden.
    /// Ignored on iOS, Editor and desktop. Defaults to false.
    /// </remarks>
    public bool HidePopups = false;
    /// <summary>Optional user ID or email hint for iOS and Editor/desktop sign-in.</summary>
    /// <remarks>Null or empty means no hint. The hint does not enforce an account.
    /// Unsupported and ignored on Android; it is never used as an ID-token nonce.</remarks>
    public string AccountName = null;
    /// <summary>Additional scopes to request, requires consent.</summary>
    public IEnumerable<string> AdditionalScopes = null;
  }
}

