// Client that handles connection to the GameGenie MCP Server for 

using UnityEngine;
using UnityEditor;

using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameGenieUnity
{

    public class ClientConfig
    {
        public string serverHost { get; set; }
        public int serverPort { get; set; }

        public string GetURL() { return $"ws://{serverHost}:{serverPort}"; }
    }

    public static class UnityClient
    {
        private static ClientWebSocket websocket;
        public static bool isConnected = false;
        public static ClientConfig clientConfig = new ClientConfig{serverHost = "localhost", serverPort = 6076};
        private static CancellationTokenSource cancellationTokenSource;
        private static readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
        private static bool isProcessingMessages = false;

        public static async void ConnectToServer()
        {
            try
            {
                // If already connected or connecting, disconnect first
                if (websocket != null && (websocket.State == WebSocketState.Open || 
                                         websocket.State == WebSocketState.Connecting))
                {
                    DisconnectFromServer();
                }
                
                Logger.AddToLog($"Attempting to connect to {clientConfig.serverHost}:{clientConfig.serverPort}...");

                // Create a new WebSocket with detailed diagnostics
                websocket = new ClientWebSocket();
                websocket.Options.SetRequestHeader("User-Agent", "Unity WebSocket Client");
                websocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

                // Initialize cancellationTokenSource before using it
                cancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));

                Logger.AddToLog("WebSocket created, attempting connection...");

                // Connect to the WebSocket server
                await websocket.ConnectAsync(new Uri(clientConfig.GetURL()), cancellationTokenSource.Token);

                // Reset the cancellation token for normal operation
                cancellationTokenSource = new CancellationTokenSource();

                isConnected = true;
                Logger.AddToLog($"Connected to WebSocket server! State: {websocket.State}");

                // Start receiving messages
                _ = ReceiveMessagesAsync();

                // Send initial handshake message
                await SendHandshakeMessage();
            }
            catch (OperationCanceledException)
            {
                Logger.AddToLog("Connection timed out after 10 seconds");
                isConnected = false;
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Connection error: {e.GetType().Name}: {e.Message}");

                // Add more detailed error info
                Logger.AddToLog($"Error details: {e}");

                if (e.InnerException != null)
                {
                    Logger.AddToLog($"Inner exception: {e.InnerException.GetType().Name}: {e.InnerException.Message}");
                    Logger.AddToLog($"Inner exception details: {e.InnerException}");
                }

                isConnected = false;

                // Add auto-reconnect logic
                Logger.AddToLog("Will attempt to reconnect in 5 seconds...");
                await Task.Delay(5000);
                ConnectToServer();
            }
        }

        private static async Task ReceiveMessagesAsync()
        {
            // Use a larger buffer
            var buffer = new byte[8192];
            var messageBuffer = new List<byte>();

            try
            {
                while (websocket.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        // Receive a message fragment
                        result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationTokenSource.Token);
                            return;
                        }

                        // Add the fragment to our message buffer
                        messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                        // Continue until we have the complete message
                    } while (!result.EndOfMessage);

                    // Now we have the complete message
                    var completeMessage = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageQueue.Enqueue($"Received: {completeMessage}");

                    // Process the complete message
                    try
                    {

                        MCPTOOLS.MCPSWITCH(completeMessage);

                    }
                    catch (Exception e)
                    {
                        Logger.AddToLog($"Error parsing message: {e.Message}");
                    }

                        // Clear the buffer for the next message
                        messageBuffer.Clear();
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

        public static async void SendUserMessage(string message)
        {
            if (websocket.State != WebSocketState.Open)
            {
                Logger.AddToLog("Cannot send message: WebSocket is not connected");
                return;
            }

            try
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await websocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Error sending message: {e.Message}");
            }
        }

        public static void DisconnectFromServer()
        {
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                cancellationTokenSource?.Cancel();
                websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                isConnected = false;
                Logger.AddToLog("Disconnected from server");
            }
            else
            {
                Logger.AddToLog("Cannot disconnect from server: WebSocket is not connected");
            }

            // Reset the cancellation token for normal operation
            cancellationTokenSource?.Dispose();
        }

        public static void ProcessQueuedMessages()
        {
            if (isProcessingMessages) return;
            isProcessingMessages = true;

            while (messageQueue.TryDequeue(out string message))
            {
                Logger.AddToLog(message);
            }

            isProcessingMessages = false;
        }

        // Send an initial handshake message
        private static async Task SendHandshakeMessage()
        {
            try
            {
                // Make sure this is valid JSON with proper escaping
                string handshake = "{\"type\":\"handshake\",\"client\":\"Unity\",\"version\":\"1.0\"}";
                Logger.AddToLog($"Sending handshake: {handshake}");
                var messageBytes = Encoding.UTF8.GetBytes(handshake);
                await websocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Error sending handshake: {e.Message}");
            }
        }

        // Add a helper method for sending raw messages
        public static async Task SendRawMessage(string jsonMessage)
        {
            if (websocket.State != WebSocketState.Open)
            {
                Logger.AddToLog("Cannot send message: WebSocket is not connected");
                return;
            }

            try
            {
                Logger.AddToLog($"Sending message: {jsonMessage}");
                var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
                await websocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Error sending message: {e.Message}");
            }
        }

        // Add this method to track state changes
        public static void MonitorWebSocketState()
        {
            if (websocket != null && websocket.State != WebSocketState.Open && isConnected)
            {
                Logger.AddToLog($"WebSocket state changed to: {websocket.State}");
                if (websocket.State == WebSocketState.Closed || websocket.State == WebSocketState.Aborted)
                {
                    isConnected = false;
                    Logger.AddToLog("Connection was lost. You may need to reconnect.");
                }
            }
        }
    }
}
