using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;

namespace GameGenie
{
    public class CodeExecutor
    {
        private const string GeneratedScriptsPath = "Assets/Editor/GameGenie/Generated";
        
        public event Action<string> OnStatusUpdate;
        public event Action<string> OnError;
        public event Action OnSuccess;
        
        public async Task ExecuteCodeAsync(string code)
        {
            try
            {
                OnStatusUpdate?.Invoke("Preparing code for execution...");
                
                // Create a unique script name based on timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string scriptName = $"GameGenieScript_{timestamp}";
                string scriptPath = $"{GeneratedScriptsPath}/{scriptName}.cs";
                
                // Ensure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
                
                // Create the script content with proper namespace and class structure
                string scriptContent = GenerateScriptContent(scriptName, code);
                
                // Write the script to disk
                File.WriteAllText(scriptPath, scriptContent);
                
                OnStatusUpdate?.Invoke("Compiling script...");
                
                // Force Unity to compile the script
                AssetDatabase.ImportAsset(scriptPath);
                AssetDatabase.Refresh();
                
                // Wait for compilation to finish (give Unity some time to compile)
                await Task.Delay(1000);
                
                OnStatusUpdate?.Invoke("Executing code...");
                
                // Execute the script
                bool success = ExecuteGeneratedScript(scriptName);
                
                if (success)
                {
                    OnStatusUpdate?.Invoke("Code executed successfully");
                    OnSuccess?.Invoke();
                }
                else
                {
                    OnError?.Invoke("Failed to execute the generated script");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error: {ex.Message}");
                OnError?.Invoke($"Error: {ex.Message}");
            }
        }
        
        private string GenerateScriptContent(string scriptName, string code)
        {
            return $@"
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace GameGenie.Generated
{{
    public class {scriptName}
    {{
        [MenuItem(""Game Genie/Execute/{scriptName}"", false, 100)]
        public static void Execute()
        {{
            try
            {{
                Debug.Log(""Game Genie: Executing generated code..."");
                
                // Begin Undo group
                Undo.SetCurrentGroupName(""Game Genie: Execute Generated Code"");
                int undoGroup = Undo.GetCurrentGroup();
                
                // Generated code
                {code}
                
                // End Undo group
                Undo.CollapseUndoOperations(undoGroup);
                
                Debug.Log(""Game Genie: Code executed successfully"");
            }}
            catch (Exception ex)
            {{
                Debug.LogError($""Game Genie Error: {{ex.Message}}"");
                EditorUtility.DisplayDialog(""Game Genie Error"", $""Error executing code: {{ex.Message}}"", ""OK"");
            }}
        }}
    }}
}}";
        }
        
        private bool ExecuteGeneratedScript(string scriptName)
        {
            try
            {
                // Execute the script using the menu item
                EditorApplication.ExecuteMenuItem($"Game Genie/Execute/{scriptName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error executing script: {ex.Message}");
                return false;
            }
        }
        
        public void CleanupGeneratedScripts(int maxScriptsToKeep = 5)
        {
            try
            {
                // Get all generated script files
                string[] files = Directory.GetFiles(GeneratedScriptsPath, "GameGenieScript_*.cs");
                
                // If we have more than the max number of scripts to keep, delete the oldest ones
                if (files.Length > maxScriptsToKeep)
                {
                    // Sort files by creation time (oldest first)
                    Array.Sort(files, (a, b) => File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)));
                    
                    // Delete the oldest files
                    int filesToDelete = files.Length - maxScriptsToKeep;
                    for (int i = 0; i < filesToDelete; i++)
                    {
                        File.Delete(files[i]);
                        string metaFile = files[i] + ".meta";
                        if (File.Exists(metaFile))
                        {
                            File.Delete(metaFile);
                        }
                    }
                    
                    // Refresh the asset database
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error cleaning up scripts: {ex.Message}");
            }
        }
    }
} 