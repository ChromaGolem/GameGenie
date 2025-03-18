using UnityEngine;
using UnityEditor;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;

namespace UnityMCP.Editor
{
    [InitializeOnLoad]
    public class UnityMCPClient
    {
        // TODO: Make these configurable
        private const string HOST = "localhost";
        private const int PORT = 6076;
        private static Process mcpServerProcess;

        private static TcpClient tcpClient;
        private static NetworkStream networkStream;
        private static bool isConnected = false;
        private static string lastErrorMessage = "";
        private static readonly Queue<LogEntry> logBuffer = new Queue<LogEntry>();
        private static readonly int maxLogBufferSize = 1000;
        private static bool isLoggingEnabled = true;
        private static bool isConnecting = false;

        // Public properties for the debug window
        public static bool IsConnected => isConnected;
        public static bool IsConnecting => isConnecting;
        public static string ServerAddress => $"{HOST}:{PORT}";
        public static string LastErrorMessage => lastErrorMessage;
        public static bool IsServerRunning => IsServerAvailable();
        public static bool IsLoggingEnabled
        {
            get => isLoggingEnabled;
            set
            {
                isLoggingEnabled = value;
                if (value)
                {
                    Application.logMessageReceived += HandleLogMessage;
                }
                else
                {
                    Application.logMessageReceived -= HandleLogMessage;
                }
            }
        }

        private class LogEntry
        {
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public LogType Type { get; set; }
            public DateTime Timestamp { get; set; }
        }

        // Public method to start the MCP connection
        public static void StartConnection()
        {
            if (isConnected || (tcpClient?.Connected ?? false))
            {
                Debug.Log("[Game Genie] Already connected or connecting");
                return;
            }

            if (isConnecting)
            {
                Debug.Log("[Game Genie] Connection attempt already in progress");
                return;
            }

            Debug.Log("Game Genie Plugin starting connection");
            ConnectToServer();
        }

        // Public method to disconnect
        public static void Disconnect()
        {
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
            isConnected = false;
            isConnecting = false;
            Debug.Log("[Game Genie] Disconnected from MCP Server");
        }

        // Public method to manually retry connection
        public static void RetryConnection()
        {
            if (isConnected)
            {
                Disconnect();
            }
            Debug.Log("[UnityMCP] Manually retrying connection...");
            ConnectToServer();
        }

        private static readonly CancellationTokenSource cts = new CancellationTokenSource();

        // Constructor called on editor startup
        static UnityMCPClient()
        {
            // Start capturing logs before anything else
            Application.logMessageReceived += HandleLogMessage;
            isLoggingEnabled = true;

            Debug.Log("Game Genie Plugin initialized");
        }

        private static void HandleLogMessage(string message, string stackTrace, LogType type)
        {
            if (!isLoggingEnabled) return;

            var logEntry = new LogEntry
            {
                Message = message,
                StackTrace = stackTrace,
                Type = type,
                Timestamp = DateTime.UtcNow
            };

            lock (logBuffer)
            {
                logBuffer.Enqueue(logEntry);
                while (logBuffer.Count > maxLogBufferSize)
                {
                    logBuffer.Dequeue();
                }
            }

            // Send log to server if connected
            if (isConnected && (tcpClient?.Connected ?? false))
            {
                SendLogToServer(logEntry);
            }
        }

        private static async void SendLogToServer(LogEntry logEntry)
        {
            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "log",
                    data = new
                    {
                        message = logEntry.Message,
                        stackTrace = logEntry.StackTrace,
                        logType = logEntry.Type.ToString(),
                        timestamp = logEntry.Timestamp
                    }
                });

                var buffer = Encoding.UTF8.GetBytes(message);
                if (networkStream != null && networkStream.CanWrite)
                {
                    await networkStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] Failed to send log to server: {e.Message}");
            }
        }

        public static string[] GetRecentLogs(LogType[] types = null, int count = 100)
        {
            lock (logBuffer)
            {
                var logs = logBuffer.ToArray()
                    .Where(log => types == null || types.Contains(log.Type))
                    .TakeLast(count)
                    .Select(log => $"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Type}] {log.Message}")
                    .ToArray();
                return logs;
            }
        }

        private static async void ConnectToServer()
        {
            if (tcpClient != null && tcpClient.Connected)
            {
                Debug.Log("[Game Genie] Already connected or connecting");
                return;
            }

            isConnecting = true;
            lastErrorMessage = "";

            try
            {
                Debug.Log($"[Game Genie] Attempting to connect to MCP Server at {HOST}:{PORT}");
                Debug.Log($"[Game Genie] Current Unity version: {Application.unityVersion}");
                Debug.Log($"[Game Genie] Current platform: {Application.platform}");

                tcpClient = new TcpClient();
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeout.Token);

                Debug.Log("[Game Genie] Starting connection attempt...");
                await tcpClient.ConnectAsync(HOST, PORT);
                networkStream = tcpClient.GetStream();
                
                Debug.Log("[Game Genie] Connection successful");
                isConnected = true;
                Debug.Log("[UnityMCP] Successfully connected to MCP Server");
                StartReceiving();
            }
            catch (OperationCanceledException)
            {
                lastErrorMessage = "[UnityMCP] Connection attempt timed out";
                Debug.LogError(lastErrorMessage);
                isConnected = false;
            }
            catch (SocketException se)
            {
                lastErrorMessage = $"[UnityMCP] Connection refused. Make sure the MCP server is running at {HOST}:{PORT}";
                Debug.LogError(lastErrorMessage);
                Debug.LogError($"[UnityMCP] Stack trace: {se.StackTrace}");
                isConnected = false;
            }
            catch (Exception e)
            {
                lastErrorMessage = $"[UnityMCP] Failed to connect to MCP Server: {e.Message}\nType: {e.GetType().Name}";
                Debug.LogError(lastErrorMessage);
                Debug.LogError($"[UnityMCP] Stack trace: {e.StackTrace}");
                isConnected = false;
            }
            finally
            {
                isConnecting = false;
            }
        }

        private static async void StartReceiving()
        {
            var buffer = new byte[1024 * 4];
            try
            {
                while (tcpClient != null && tcpClient.Connected)
                {
                    var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        HandleMessage(message);
                    }
                    else
                    {
                        // Connection closed by server
                        isConnected = false;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error receiving message: {e.Message}");
                isConnected = false;
            }
        }

        private static void HandleMessage(string message)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                var command = data["command"]?.ToString();
                var parameters = data["params"] as Dictionary<string, object>;

                switch (command)
                {
                    case "get_scene_context":
                        GetSceneContext();
                        break;
                    case "toggle_play_mode":
                        TogglePlayMode();
                        break;
                    case "execute_unity_code":
                        if (parameters != null && parameters.ContainsKey("code"))
                        {
                            ExecuteUnityCode(parameters["code"].ToString());
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error handling message: {e.Message}");
            }
        }

        /// <summary>
        /// THIS SECTION CONTAINS THE FUNCTIONS THAT THE MCP EXPOSES TO THE LLM
        /// </summary>
        private static void GetSceneContext()
        {
            // TODO: Implement
            Debug.Log("GetSceneContext");
        }

        private static void TogglePlayMode()
        {
            // TODO: Implement
            Debug.Log("TogglePlayMode");
        }

        private static void ExecuteUnityCode(string code)
        {
            // TODO: Implement  
            Debug.Log("ExecuteUnityCode");
        }

        private static bool IsServerAvailable()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(HOST, PORT, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                    if (success)
                    {
                        client.EndConnect(result);
                        client.Close();
                        return true;
                    }
                }
            }
            catch
            {
                // Server is not available
            }
            return false;
        }

        public static void StartMCPServer()
        {
            try
            {
                // Get the path to the Python script
                string scriptPath = Path.Combine(Application.dataPath, "..", "mcp_server", "game_genie_mcp.py");
                
                if (!File.Exists(scriptPath))
                {
                    lastErrorMessage = $"MCP server script not found at: {scriptPath}";
                    Debug.LogError(lastErrorMessage);
                    return;
                }

                // Start the Python script
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                mcpServerProcess = new Process { StartInfo = startInfo };
                mcpServerProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.Log($"[MCP Server] {e.Data}");
                };
                mcpServerProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.LogError($"[MCP Server Error] {e.Data}");
                };

                mcpServerProcess.Start();
                mcpServerProcess.BeginOutputReadLine();
                mcpServerProcess.BeginErrorReadLine();

                Debug.Log("[MCP Server] Started successfully");
            }
            catch (Exception e)
            {
                lastErrorMessage = $"Failed to start MCP server: {e.Message}";
                Debug.LogError(lastErrorMessage);
            }
        }

        public static void StopMCPServer()
        {
            if (mcpServerProcess != null && !mcpServerProcess.HasExited)
            {
                try
                {
                    mcpServerProcess.Kill();
                    mcpServerProcess.WaitForExit();
                    Debug.Log("[MCP Server] Stopped successfully");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MCP Server] Failed to stop: {e.Message}");
                }
            }
        }
    }
}