// <copyright file="GoogleSignInSample.cs" company="Google Inc.">
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
//  limitations

using System.Collections.Generic;
using System.Threading.Tasks;
using Google;
using UnityEngine;
using UnityEngine.UI;


public class GoogleSignInSample : MonoBehaviour
{
    public Text statusText;

    // Must be the *web* client ID on every platform, including Android —
    // CredentialManager takes it as the server client ID.
    public string webClientId = "<your client id here>";

#if UNITY_EDITOR || UNITY_STANDALONE
    // Register http://localhost and http://localhost:49215 as authorized
    // JavaScript origins for the web client ID. No client secret is needed.
    public int desktopLoginPort = 49215;
#endif

    private GoogleSignInConfiguration configuration;

    // Defer the configuration creation until Awake so the web Client ID
    // Can be set via the property inspector in the Editor.
    void Awake()
    {
        configuration = new GoogleSignInConfiguration
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            DesktopLoginPort = desktopLoginPort,
#endif
            WebClientId = webClientId,
            RequestIdToken = true
        };
    }

    public void OnSignIn()
    {
        GoogleSignIn.Configuration = configuration;
        GoogleSignIn.Configuration.UseGameSignIn = false;
        AddStatusText("Calling SignInLegacy");

        GoogleSignIn.DefaultInstance.SignInLegacy().ContinueWith(OnAuthenticationFinished,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void OnSignOut()
    {
        AddStatusText("Calling SignOut");
        GoogleSignIn.DefaultInstance.SignOut();
    }

    public void OnDisconnect()
    {
        AddStatusText("Calling Disconnect");
        GoogleSignIn.DefaultInstance.Disconnect();
    }

    internal void OnAuthenticationFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsFaulted)
        {
            using (IEnumerator<System.Exception> enumerator =
                   task.Exception.InnerExceptions.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    GoogleSignIn.SignInException error =
                        (GoogleSignIn.SignInException)enumerator.Current;
                    AddStatusText("Got Error: " + error.Status + " " + error.Message);
                }
                else
                {
                    AddStatusText("Got Unexpected Exception?!?" + task.Exception);
                }
            }
        }
        else if (task.IsCanceled)
        {
            AddStatusText("Canceled");
        }
        else
        {
            AddStatusText(task.Result.DisplayName);
            AddStatusText(task.Result.UserId);
            // Pass task.Result.IdToken to your backend; do not write tokens to logs.
            AddStatusText(string.IsNullOrEmpty(task.Result.IdToken) ? "No ID token returned" : "ID token received");
        }
    }

    // Keep this historical method name for serialized scene button bindings.
    // SignIn is an explicit login request, not a silent or cache-only login.
    public void OnSignInSilently()
    {
        GoogleSignIn.Configuration = configuration;
        GoogleSignIn.Configuration.UseGameSignIn = false;
        AddStatusText("Calling SignIn");

        GoogleSignIn.DefaultInstance.SignIn().ContinueWith(OnAuthenticationFinished,
            TaskScheduler.FromCurrentSynchronizationContext());
    }


    public void OnGamesSignIn()
    {
        GoogleSignIn.Configuration = configuration;
        GoogleSignIn.Configuration.UseGameSignIn = true;
        GoogleSignIn.Configuration.RequestIdToken = false;

        AddStatusText("Calling Games SignInLegacy");

        GoogleSignIn.DefaultInstance.SignInLegacy().ContinueWith(OnAuthenticationFinished,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private List<string> messages = new List<string>();

    void AddStatusText(string text)
    {
        Debug.Log(text);
        if (messages.Count == 5)
        {
            messages.RemoveAt(0);
        }

        messages.Add(text);
        string txt = "";
        foreach (string s in messages)
        {
            txt += "\n" + s;
        }

        statusText.text = txt;
    }
}
