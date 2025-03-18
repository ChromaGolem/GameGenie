using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;
using UnityEditor.Compilation;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Scripting.ScriptCompilation;
using System.Linq;

namespace GameGenie
{
    public class CodeExecutor
    {
        private const string GeneratedScriptsPath = "Assets/Editor/GameGenie/Generated";
        private const int MaxScriptsToKeep = 10;
        
        public event Action<string> OnStatusUpdate;
        public event Action<string> OnError;
        public event Action OnSuccess;
        
        // Add this static method for MCP integration
        [MenuItem("GameGenie/ExecuteCodeFromFile")]
        public static void ExecuteCodeFromFile()
        {
            try
            {
                // Get the code path from command line arguments
                string codePath = null;
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-codePath" && i + 1 < args.Length)
                    {
                        codePath = args[i + 1];
                        break;
                    }
                }
                
                if (string.IsNullOrEmpty(codePath) || !File.Exists(codePath))
                {
                    Debug.LogError("Code file not found or path not specified.");
                    return;
                }
                
                // Read the code from the file
                string code = File.ReadAllText(codePath);
                
                // Create an instance and execute the code
                CodeExecutor executor = new CodeExecutor();
                
                // We can't use async/await in static methods called from command line
                // So we'll use a synchronous approach
                executor.ExecuteCodeSync(code);
                
                Debug.Log("Code executed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error executing code: {ex.Message}");
            }
        }
        
        // Synchronous version for command-line execution
        public void ExecuteCodeSync(string code)
        {
            try
            {
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
                
                // Force Unity to compile the script
                AssetDatabase.ImportAsset(scriptPath);
                AssetDatabase.Refresh();
                
                // Wait for compilation to finish
                System.Threading.Thread.Sleep(1000);
                
                // Execute the script
                bool success = ExecuteGeneratedScript(scriptName);
                
                if (!success)
                {
                    throw new Exception("Failed to execute the generated script");
                }
                
                // Clean up old scripts
                CleanupGeneratedScripts();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error: {ex.Message}");
                throw;
            }
        }
        
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
            return $@"using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameGenie
{{
    public class {scriptName} : EditorWindow
    {{
        [MenuItem(""GameGenie/Execute/{scriptName}"")]
        public static void Execute()
        {{
            try
            {{
                // Your code here
                {code}
            }}
            catch (Exception ex)
            {{
                Debug.LogError($""Error executing {scriptName}: {{ex.Message}}"");
            }}
        }}
    }}
}}";
        }
        
        private bool ExecuteGeneratedScript(string scriptName)
        {
            try
            {
                // Get the menu item path
                string menuItemPath = $"GameGenie/Execute/{scriptName}";
                
                // Execute the menu item using EditorApplication
                EditorApplication.ExecuteMenuItem(menuItemPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error executing generated script: {ex.Message}");
                return false;
            }
        }
        
        private void CleanupGeneratedScripts()
        {
            try
            {
                string directory = Path.GetDirectoryName($"{GeneratedScriptsPath}/dummy.cs");
                if (!Directory.Exists(directory))
                    return;
                
                var files = Directory.GetFiles(directory, "GameGenieScript_*.cs")
                    .OrderByDescending(f => f)
                    .Skip(MaxScriptsToKeep);
                
                foreach (var file in files)
                {
                    File.Delete(file);
                    string metaFile = file + ".meta";
                    if (File.Exists(metaFile))
                    {
                        File.Delete(metaFile);
                    }
                }
                
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error cleaning up generated scripts: {ex.Message}");
            }
        }
    }
} 