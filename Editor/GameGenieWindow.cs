using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityMCP.Editor;

namespace GameGenie
{
    public class GameGenieWindow : EditorWindow
    {
        private string connectionStatus = "Not Connected";
        private string debugInfo = "";
        private Vector2 scrollPosition;

        [MenuItem("Window/Game Genie")]
        public static void ShowWindow()
        {
            GetWindow<GameGenieWindow>("Game Genie");
        }

        private void OnGUI()
        {
            GUILayout.Label("Game Genie Connection", EditorStyles.boldLabel);
            
            EditorGUILayout.Space();
            
            // Server Status and Controls
            EditorGUILayout.LabelField("Server Status:", EditorStyles.boldLabel);
            if (UnityMCPClient.IsServerRunning)
            {
                EditorGUILayout.HelpBox("MCP Server is running", MessageType.Info);
                if (GUILayout.Button("Stop Server"))
                {
                    UnityMCPClient.StopMCPServer();
                    debugInfo += "Stopping MCP server...\n";
                }
            }
            else
            {
                EditorGUILayout.HelpBox("MCP Server is not running", MessageType.Warning);
                if (GUILayout.Button("Start Server"))
                {
                    UnityMCPClient.StartMCPServer();
                    debugInfo += "Starting MCP server...\n";
                }
            }
            
            EditorGUILayout.Space();
            
            // Connection Status
            EditorGUILayout.LabelField("Connection Status:", EditorStyles.boldLabel);
            if (UnityMCPClient.IsConnecting)
            {
                connectionStatus = $"Connecting to {UnityMCPClient.ServerAddress}...";
                EditorGUILayout.LabelField(connectionStatus, EditorStyles.boldLabel);
            }
            else if (UnityMCPClient.IsConnected)
            {
                connectionStatus = $"Connected to {UnityMCPClient.ServerAddress}";
                EditorGUILayout.LabelField(connectionStatus, EditorStyles.boldLabel);
            }
            else
            {
                connectionStatus = "Not Connected";
                EditorGUILayout.LabelField(connectionStatus, EditorStyles.boldLabel);
                
                if (!string.IsNullOrEmpty(UnityMCPClient.LastErrorMessage))
                {
                    EditorGUILayout.HelpBox(UnityMCPClient.LastErrorMessage, MessageType.Error);
                }
            }
            
            EditorGUILayout.Space();
            
            // Connection Button
            GUI.enabled = !UnityMCPClient.IsConnecting && UnityMCPClient.IsServerRunning;
            if (GUILayout.Button(UnityMCPClient.IsConnected ? "Disconnect" : "Connect"))
            {
                if (UnityMCPClient.IsConnected)
                {
                    UnityMCPClient.Disconnect();
                    debugInfo += "Disconnected from MCP server\n";
                }
                else
                {
                    UnityMCPClient.StartConnection();
                    debugInfo += "Attempting to connect to MCP server...\n";
                }
            }
            GUI.enabled = true;
            
            if (!UnityMCPClient.IsServerRunning)
            {
                EditorGUILayout.HelpBox("Start the MCP server before attempting to connect", MessageType.Info);
            }
            
            EditorGUILayout.Space();
            
            // Debug Info
            EditorGUILayout.LabelField("Debug Information:", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            EditorGUILayout.TextArea(debugInfo, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // Add recent logs
            if (GUILayout.Button("Refresh Logs"))
            {
                var recentLogs = UnityMCPClient.GetRecentLogs();
                debugInfo = string.Join("\n", recentLogs);
            }
        }

        private void OnEnable()
        {
            // Subscribe to log messages
            Application.logMessageReceived += HandleLogMessage;
        }

        private void OnDisable()
        {
            // Unsubscribe from log messages
            Application.logMessageReceived -= HandleLogMessage;
        }

        private void HandleLogMessage(string message, string stackTrace, LogType type)
        {
            if (message.Contains("[UnityMCP]") || message.Contains("[Game Genie]") || message.Contains("[MCP Server]"))
            {
                debugInfo += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                Repaint();
            }
        }
    }
}
