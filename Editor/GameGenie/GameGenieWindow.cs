using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GameGenie
{
    public class GameGenieWindow : EditorWindow
    {
        // UI Elements
        private string userQuery = "";

        private string aiResponse = "";

        private Vector2 responseScrollPosition;

        private bool isProcessing = false;

        private string statusMessage = "Ready";

        private bool showPreview = false;

        private string extractedCode = "";

        private Vector2 codeScrollPosition;
        
        private string warningMessage = "";

        // Settings
        private string apiKey = "";

        private bool rememberApiKey = false;

        private const string ApiKeyPrefKey = "GameGenie_ApiKey";

        // Styles
        private GUIStyle responseBoxStyle;

        private GUIStyle codeBoxStyle;

        private GUIStyle statusLabelStyle;

        private GUIStyle headerLabelStyle;
        
        private GUIStyle warningLabelStyle;
        
        // Services
        private SceneContextExtractor contextExtractor;
        private ClaudeApiClient claudeApiClient;
        private CodeExecutor codeExecutor;

        [MenuItem("Window/Game Genie")]
        public static void ShowWindow()
        {
            GameGenieWindow window = GetWindow<GameGenieWindow>("Game Genie");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            // Load saved API key if available
            apiKey = EditorPrefs.GetString(ApiKeyPrefKey, "");
            rememberApiKey = !string.IsNullOrEmpty(apiKey);

            // Initialize styles
            InitializeStyles();
            
            // Initialize services
            contextExtractor = new SceneContextExtractor();
            codeExecutor = new CodeExecutor();
            
            // Subscribe to code executor events
            codeExecutor.OnStatusUpdate += UpdateStatus;
            codeExecutor.OnError += HandleError;
            codeExecutor.OnSuccess += HandleSuccess;
        }
        
        private void OnDisable()
        {
            // Unsubscribe from code executor events
            if (codeExecutor != null)
            {
                codeExecutor.OnStatusUpdate -= UpdateStatus;
                codeExecutor.OnError -= HandleError;
                codeExecutor.OnSuccess -= HandleSuccess;
            }
        }

        private void InitializeStyles()
        {
            responseBoxStyle =
                new GUIStyle(EditorStyles.textArea)
                { wordWrap = true, richText = true };

            codeBoxStyle =
                new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    font =
                        EditorGUIUtility
                            .Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as
                        Font
                };

            statusLabelStyle =
                new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

            headerLabelStyle =
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                
            warningLabelStyle = 
                new GUIStyle(EditorStyles.label) 
                { 
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.red }
                };
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSettingsSection();
            DrawQuerySection();
            DrawResponseSection();
            DrawPreviewSection();
            DrawActionButtons();
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space();
            EditorGUILayout
                .LabelField("Game Genie - AI-Powered Unity Scene Editor",
                headerLabelStyle);
            EditorGUILayout.Space();
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            apiKey = EditorGUILayout.PasswordField("Claude API Key:", apiKey);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool newRememberApiKey =
                EditorGUILayout.Toggle("Remember API Key", rememberApiKey);
            if (newRememberApiKey != rememberApiKey)
            {
                rememberApiKey = newRememberApiKey;
                if (rememberApiKey)
                {
                    EditorPrefs.SetString (ApiKeyPrefKey, apiKey);
                }
                else
                {
                    EditorPrefs.DeleteKey (ApiKeyPrefKey);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawQuerySection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout
                .LabelField("Ask Game Genie", EditorStyles.boldLabel);
            EditorGUILayout
                .HelpBox("Describe what you want to create or modify in your scene.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            userQuery =
                EditorGUILayout.TextArea(userQuery, GUILayout.Height(60));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled =
                !isProcessing &&
                !string.IsNullOrEmpty(apiKey) &&
                !string.IsNullOrEmpty(userQuery);
            if (GUILayout.Button("Send to Claude", GUILayout.Height(30)))
            {
                SendQueryToClaude();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawResponseSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout
                .LabelField("Claude's Response", EditorStyles.boldLabel);

            responseScrollPosition =
                EditorGUILayout
                    .BeginScrollView(responseScrollPosition,
                    GUILayout.Height(150));
            EditorGUILayout.TextArea (aiResponse, responseBoxStyle);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewSection()
        {
            if (!showPreview || string.IsNullOrEmpty(extractedCode)) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Code Preview", EditorStyles.boldLabel);
            
            if (!string.IsNullOrEmpty(warningMessage))
            {
                EditorGUILayout.LabelField(warningMessage, warningLabelStyle);
                EditorGUILayout.HelpBox("The code contains potentially dangerous operations. Review carefully before executing.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Review the code before executing it in your scene.", MessageType.Info);
            }

            codeScrollPosition =
                EditorGUILayout
                    .BeginScrollView(codeScrollPosition, GUILayout.Height(150));
            EditorGUILayout.TextArea (extractedCode, codeBoxStyle);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            if (!showPreview || string.IsNullOrEmpty(extractedCode)) return;

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !isProcessing && (string.IsNullOrEmpty(warningMessage) || EditorPrefs.GetBool("GameGenie_AllowDangerousCode", false));
            if (GUILayout.Button("Execute Code", GUILayout.Height(30)))
            {
                ExecuteCode();
            }
            
            if (!string.IsNullOrEmpty(warningMessage) && !EditorPrefs.GetBool("GameGenie_AllowDangerousCode", false))
            {
                if (GUILayout.Button("Override Safety", GUILayout.Height(30)))
                {
                    bool confirm = EditorUtility.DisplayDialog(
                        "Override Safety Checks",
                        "Are you sure you want to allow potentially dangerous code to be executed? This could potentially harm your project or system.",
                        "Yes, I understand the risks",
                        "Cancel"
                    );
                    
                    if (confirm)
                    {
                        EditorPrefs.SetBool("GameGenie_AllowDangerousCode", true);
                        Repaint();
                    }
                }
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                showPreview = false;
                extractedCode = "";
                warningMessage = "";
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUIStyle statusStyle = new GUIStyle(statusLabelStyle);
            if (isProcessing) statusStyle.normal.textColor = Color.yellow;

            EditorGUILayout.LabelField($"Status: {statusMessage}", statusStyle);

            EditorGUILayout.EndHorizontal();
        }

        private async void SendQueryToClaude()
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                statusMessage = "Error: API Key is required";
                return;
            }

            isProcessing = true;
            statusMessage = "Processing request...";
            
            // Clear previous results
            warningMessage = "";
            
            // Initialize Claude API client with the current API key
            claudeApiClient = new ClaudeApiClient(apiKey);

            try
            {
                // Extract scene context
                string sceneContext = contextExtractor.ExtractSceneContext();
                
                // Send query to Claude
                ClaudeResponse response = await claudeApiClient.SendMessageAsync(userQuery, sceneContext);
                
                if (!string.IsNullOrEmpty(response.Error))
                {
                    statusMessage = "Error: " + response.Error;
                    isProcessing = false;
                    return;
                }
                
                // Process the response
                aiResponse = response.GetTextContent();
                
                // Extract code blocks from the response
                extractedCode = CodeParser.ExtractCodeBlocks(aiResponse);
                
                if (!string.IsNullOrEmpty(extractedCode))
                {
                    // Validate the code for safety
                    bool isCodeSafe = CodeParser.ValidateCode(extractedCode, out warningMessage);
                    
                    // Format the code for display
                    extractedCode = CodeParser.FormatCodeForDisplay(extractedCode);
                    
                    showPreview = true;
                }
                
                isProcessing = false;
                statusMessage = "Ready";
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error: {ex.Message}");
                statusMessage = "Error: " + ex.Message;
                isProcessing = false;
            }
            
            Repaint();
        }

        private async void ExecuteCode()
        {
            if (string.IsNullOrEmpty(extractedCode)) return;

            isProcessing = true;
            statusMessage = "Executing code...";
            
            // Show confirmation dialog
            bool shouldExecute = EditorUtility.DisplayDialog(
                "Execute Generated Code",
                "Are you sure you want to execute this code in your scene?\n\nThis operation can be undone with Ctrl+Z after execution.",
                "Execute",
                "Cancel"
            );
            
            if (!shouldExecute)
            {
                isProcessing = false;
                statusMessage = "Code execution cancelled";
                Repaint();
                return;
            }

            try
            {
                // Sanitize the code before execution
                string sanitizedCode = CodeParser.SanitizeCode(extractedCode);
                
                // Execute the code using the CodeExecutor
                await codeExecutor.ExecuteCodeAsync(sanitizedCode);
                
                // Clean up old generated scripts
                codeExecutor.CleanupGeneratedScripts();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error: {ex.Message}");
                statusMessage = "Error: " + ex.Message;
                isProcessing = false;
                Repaint();
            }
        }
        
        private void UpdateStatus(string status)
        {
            statusMessage = status;
            Repaint();
        }
        
        private void HandleError(string error)
        {
            statusMessage = error;
            isProcessing = false;
            Repaint();
        }
        
        private void HandleSuccess()
        {
            showPreview = false;
            extractedCode = "";
            warningMessage = "";
            isProcessing = false;
            statusMessage = "Code executed successfully";
            Repaint();
        }
    }
}
