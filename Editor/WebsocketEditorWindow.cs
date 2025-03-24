using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#if UNITY_EDITOR
using GameGenieUnity;
#endif

public class WebSocketEditorWindow : EditorWindow
{
    private string messageToSend = "";
    private Vector2 scrollPosition;

    [MenuItem("Window/WebSocket Test Window")]
    public static void ShowWindow()
    {
        GetWindow<WebSocketEditorWindow>("WebSocket Test");
    }

    void OnGUI()
    {
        GUILayout.Label("WebSocket Connection", EditorStyles.boldLabel);

        // Server configuration
        EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);
        EditorGUILayout.TextField("Host", GameGenieUnity.UnityClient.clientConfig.serverHost);
        EditorGUILayout.IntField("Port", GameGenieUnity.UnityClient.clientConfig.serverPort);
        string serverUrl = GameGenieUnity.UnityClient.clientConfig.GetURL();
        EditorGUILayout.LabelField("Full URL:", serverUrl);

        // Connection status
        EditorGUILayout.LabelField("Status:", GameGenieUnity.UnityClient.isConnected ? "Connected" : "Disconnected");

        // Connect/Disconnect button
        if (!GameGenieUnity.UnityClient.isConnected)
        {
            if (GUILayout.Button("Connect"))
            {
                GameGenieUnity.UnityClient.ConnectToServer();
            }
        }
        else
        {
            if (GUILayout.Button("Disconnect"))
            {
                GameGenieUnity.UnityClient.DisconnectFromServer();
            }
        }

        EditorGUILayout.Space();

        // Message sending section
        if (GameGenieUnity.UnityClient.isConnected)
        {
            EditorGUILayout.LabelField("Send Message", EditorStyles.boldLabel);
            messageToSend = EditorGUILayout.TextField("Message", messageToSend);
            if (GUILayout.Button("Send"))
            {
                if (!string.IsNullOrEmpty(messageToSend))
                {
                    GameGenieUnity.UnityClient.SendUserMessage(messageToSend);
                    GameGenieUnity.Logger.AddToLog($"Sent: {messageToSend}");
                    messageToSend = "";
                }
            }
        }

        EditorGUILayout.Space();

        // Message log section
        EditorGUILayout.LabelField("Message Log", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        foreach (string message in GameGenieUnity.Logger.GetMessageLog())
        {
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();

        // Process any queued messages
        GameGenieUnity.UnityClient.ProcessQueuedMessages();
        
        // Monitor WebSocket state
        GameGenieUnity.UnityClient.MonitorWebSocketState();
    }

    void OnDestroy()
    {
        GameGenieUnity.UnityClient.DisconnectFromServer();
    }
} 