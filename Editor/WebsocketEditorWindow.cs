using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

public class WebSocketEditorWindow : EditorWindow
{
    private ClientWebSocket websocket;
    private bool isConnected = false;
    private string serverUrl = "ws://localhost:6076";
    private string serverHost = "localhost";
    private int serverPort = 6076;
    private string messageToSend = "";
    private Vector2 scrollPosition;
    private List<string> messageLog = new List<string>();
    private CancellationTokenSource cancellationTokenSource;
    private readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
    private bool isProcessingMessages = false;

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
        serverHost = EditorGUILayout.TextField("Host", serverHost);
        serverPort = EditorGUILayout.IntField("Port", serverPort);
        serverUrl = $"ws://{serverHost}:{serverPort}";
        EditorGUILayout.LabelField("Full URL:", serverUrl);

        // Connection status
        EditorGUILayout.LabelField("Status:", isConnected ? "Connected" : "Disconnected");

        // Connect/Disconnect button
        if (!isConnected)
        {
            if (GUILayout.Button("Connect"))
            {
                ConnectToServer();
            }
        }
        else
        {
            if (GUILayout.Button("Disconnect"))
            {
                DisconnectFromServer();
            }
        }

        EditorGUILayout.Space();

        // Message sending section
        if (isConnected)
        {
            EditorGUILayout.LabelField("Send Message", EditorStyles.boldLabel);
            messageToSend = EditorGUILayout.TextField("Message", messageToSend);
            if (GUILayout.Button("Send"))
            {
                if (!string.IsNullOrEmpty(messageToSend))
                {
                    SendMessage(messageToSend);
                    AddToLog($"Sent: {messageToSend}");
                    messageToSend = "";
                }
            }
        }

        EditorGUILayout.Space();

        // Message log section
        EditorGUILayout.LabelField("Message Log", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        foreach (string message in messageLog)
        {
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();

        // Process any queued messages
        ProcessQueuedMessages();
    }

    async void ConnectToServer()
    {
        try
        {
            AddToLog($"Attempting to connect to {serverUrl}...");
            
            // Create a new WebSocket with detailed diagnostics
            websocket = new ClientWebSocket();
            websocket.Options.SetRequestHeader("User-Agent", "Unity WebSocket Client");
            websocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            
            // Initialize cancellationTokenSource before using it
            cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            
            AddToLog("WebSocket created, attempting connection...");
            
            // Connect to the WebSocket server
            await websocket.ConnectAsync(new Uri(serverUrl), cancellationTokenSource.Token);
            
            // Reset the cancellation token for normal operation
            cancellationTokenSource = new CancellationTokenSource();
            
            isConnected = true;
            AddToLog($"Connected to WebSocket server! State: {websocket.State}");

            // Start receiving messages
            _ = ReceiveMessagesAsync();
            
            // Send initial handshake message
            await SendHandshakeMessage();
        }
        catch (OperationCanceledException)
        {
            AddToLog("Connection timed out after 10 seconds");
            isConnected = false;
        }
        catch (Exception e)
        {
            AddToLog($"Connection error: {e.GetType().Name}: {e.Message}");
            
            // Add more detailed error info
            AddToLog($"Error details: {e}");
            
            if (e.InnerException != null)
            {
                AddToLog($"Inner exception: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
                AddToLog($"Inner exception details: {e.InnerException}");
            }
            
            isConnected = false;
        }
    }

    async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[1024];
        try
        {
            while (websocket.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationTokenSource.Token);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageQueue.Enqueue($"Received: {message}");
            }
        }
        catch (Exception e)
        {
            if (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                messageQueue.Enqueue($"Error receiving message: {e.Message}");
            }
        }
    }

    async void SendMessage(string message)
    {
        if (websocket.State != WebSocketState.Open)
        {
            AddToLog("Cannot send message: WebSocket is not connected");
            return;
        }

        try
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            await websocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
        }
        catch (Exception e)
        {
            AddToLog($"Error sending message: {e.Message}");
        }
    }

    void DisconnectFromServer()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            cancellationTokenSource?.Cancel();
            websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            isConnected = false;
            AddToLog("Disconnected from server");
        }
    }

    void ProcessQueuedMessages()
    {
        if (isProcessingMessages) return;
        isProcessingMessages = true;

        while (messageQueue.TryDequeue(out string message))
        {
            AddToLog(message);
        }

        isProcessingMessages = false;
    }

    void AddToLog(string message)
    {
        // Add the message to the log list
        messageLog.Add(message);
        Debug.Log(message);
        Repaint();
    }

    void OnDestroy()
    {
        DisconnectFromServer();
        cancellationTokenSource?.Dispose();
    }

    // Send an initial handshake message
    async Task SendHandshakeMessage()
    {
        try {
            // Make sure this is valid JSON with proper escaping
            string handshake = "{\"type\":\"handshake\",\"client\":\"Unity\",\"version\":\"1.0\"}";
            AddToLog($"Sending handshake: {handshake}");
            var messageBytes = Encoding.UTF8.GetBytes(handshake);
            await websocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
        }
        catch (Exception e) {
            AddToLog($"Error sending handshake: {e.Message}");
        }
    }
} 