using UnityEngine;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Reflection;
using System.CodeDom.Compiler;
using System.IO;
using Microsoft.CSharp;
#endif

namespace GameGenieUnity
{
    public class CodeExecutionService : MonoBehaviour
    {
        private class LogCapture : IDisposable
        {
            private readonly List<string> capturedLogs = new List<string>();
            
            public LogCapture()
            {
                Application.logMessageReceived += CaptureLog;
            }

            private void CaptureLog(string logString, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception)
                {
                    capturedLogs.Add($"{type}: {logString}\n{stackTrace}");
                }
            }

            public void Dispose()
            {
                Application.logMessageReceived -= CaptureLog;
            }

            public string GetCapturedLogs()
            {
                return string.Join("\n", capturedLogs);
            }

            public bool HasErrors()
            {
                return capturedLogs.Count > 0;
            }
        }

        public static string ExecuteInEditor(string sourceCode)
        {
#if UNITY_EDITOR
        Debug.Log("Received raw code to execute in editor: \n" + sourceCode);

        // Wrap the provided snippet in a minimal boilerplate class.
        string wrappedSourceCode = @"
using UnityEngine;
using UnityEditor;
public static class EditorCodeWrapper {
    public static void Execute() {
" + sourceCode + @"
    }
}";
        try
        {
            // Parse the code first to analyze dependencies
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(wrappedSourceCode);
            var root = syntaxTree.GetRoot();

            // Get all identifiers from the code
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier.ValueText)
                .Distinct()
                .ToList();

            // Get all using directives
            var usingDirectives = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name.ToString())
                .Distinct()
                .ToList();

            // Build list of required assemblies
            var requiredAssemblies = new HashSet<Assembly>();
            
            // Add core assemblies that are always needed
            requiredAssemblies.Add(typeof(object).Assembly); // mscorlib
            requiredAssemblies.Add(typeof(UnityEngine.Object).Assembly); // UnityEngine.CoreModule
            requiredAssemblies.Add(typeof(UnityEditor.EditorApplication).Assembly); // UnityEditor.CoreModule
            requiredAssemblies.Add(typeof(EditorWindow).Assembly); // UnityEditor.CoreModule
            requiredAssemblies.Add(typeof(UnityEngine.UI.Button).Assembly); // UnityEngine.UI
            requiredAssemblies.Add(Assembly.GetExecutingAssembly()); // Current assembly

            // Add Assembly-CSharp and Assembly-CSharp-Editor
            var assemblyCSharp = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (assemblyCSharp != null)
            {
                requiredAssemblies.Add(assemblyCSharp);
                Debug.Log("Added Assembly-CSharp to references");
            }

            var assemblyCSharpEditor = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp-Editor");
            if (assemblyCSharpEditor != null)
            {
                requiredAssemblies.Add(assemblyCSharpEditor);
                Debug.Log("Added Assembly-CSharp-Editor to references");
            }

            requiredAssemblies.Add(AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "netstandard")); // netstandard

            // Get all Unity assemblies
            var unityAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("UnityEngine.") || 
                           a.GetName().Name.StartsWith("UnityEditor.") ||
                           a.GetName().Name == "Assembly-CSharp" ||  // Also include in search
                           a.GetName().Name == "Assembly-CSharp-Editor");  // Also include in search

            // Function to check if an assembly contains a type
            bool AssemblyContainsType(Assembly assembly, string typeName)
            {
                try
                {
                    return assembly.GetTypes().Any(t => t.Name == typeName);
                }
                catch
                {
                    return false; // Handle assembly load errors gracefully
                }
            }

            // For each identifier, try to find its assembly
            foreach (var identifier in identifiers)
            {
                foreach (var unityAssembly in unityAssemblies)
                {
                    if (AssemblyContainsType(unityAssembly, identifier))
                    {
                        // Dyanmically add the assembly to the list of required assemblies
                        requiredAssemblies.Add(unityAssembly);
                        break;
                    }
                }
            }

            // Create MetadataReferences from the assemblies
            var references = requiredAssemblies
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .ToList();

            // Add additional common Unity modules that might be needed
            var commonUnityModules = new[]
            {
                "UnityEngine.PhysicsModule",
                "UnityEngine.AnimationModule",
                "UnityEngine.UIModule",
                "UnityEngine.AudioModule",
                "UnityEngine.InputModule"
            };

            foreach (var moduleName in commonUnityModules)
            {
                var moduleAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == moduleName);
                if (moduleAssembly != null)
                {
                    references.Add(MetadataReference.CreateFromFile(moduleAssembly.Location));
                }
            }

            Debug.Log($"Compiling with {references.Count} assembly references");

            // Create compilation
            var compilation = CSharpCompilation.Create(
                "DynamicAssembly_" + System.Guid.NewGuid().ToString("N"),
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true
                )
            );

            // Emit the compilation to memory
            using var ms = new System.IO.MemoryStream();
            EmitResult emitResult = compilation.Emit(ms);

            // If compilation failed, try to identify missing references
            if (!emitResult.Success)
            {
                var errorMessage = "Code compilation failed:\n";
                var missingTypePattern = new Regex(@"The type or namespace name '(\w+)' could not be found");
                var missingTypes = new HashSet<string>();

                foreach (var diagnostic in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var lineSpan = diagnostic.Location.GetLineSpan();
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;
                    var message = diagnostic.GetMessage();
                    errorMessage += $"Line {lineNumber}: {message}\n";

                    // Extract missing type names
                    var match = missingTypePattern.Match(message);
                    if (match.Success)
                    {
                        missingTypes.Add(match.Groups[1].Value);
                    }
                }

                // Provide helpful information about missing types
                if (missingTypes.Any())
                {
                    errorMessage += "\nMissing types found:\n";
                    foreach (var type in missingTypes)
                    {
                        errorMessage += $"- {type}: This type might be in one of these assemblies:\n";
                        var possibleAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                            .Where(a => AssemblyContainsType(a, type))
                            .Select(a => $"  * {a.GetName().Name}");
                        errorMessage += string.Join("\n", possibleAssemblies);
                        errorMessage += "\n";
                    }
                }

                Debug.LogError(errorMessage);
                return errorMessage;
            }

            // Rest of the execution code remains the same...
            ms.Seek(0, System.IO.SeekOrigin.Begin);
            Assembly assembly = Assembly.Load(ms.ToArray());
            
            Type wrapperType = assembly.GetType("EditorCodeWrapper");
            if (wrapperType == null)
            {
                Debug.LogError("Could not find the 'EditorCodeWrapper' type in the compiled assembly.");
                return "Error executing generated code: Could not find the 'EditorCodeWrapper' type.";
            }

            MethodInfo executeMethod = wrapperType.GetMethod("Execute", 
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (executeMethod == null)
            {
                Debug.LogError("No 'Execute' method found in 'EditorCodeWrapper'.");
                return "Error executing generated code: No 'Execute' method found.";
            }

            Debug.Log("Executing successfully-compiled source code!");
            
            // Use LogCapture to catch any Unity errors during execution
            using (var logCapture = new LogCapture())
            {
                try
                {
                    executeMethod.Invoke(null, null);
                    
                    // Check if any errors were logged during execution
                    if (logCapture.HasErrors())
                    {
                        string errorLogs = logCapture.GetCapturedLogs();
                        Debug.LogError($"Code execution generated errors:\n{errorLogs}");
                        return $"Code execution generated errors:\n{errorLogs}";
                    }
                }
                catch (TargetInvocationException tie)
                {
                    // Get the actual exception that occurred during execution
                    var innerException = tie.InnerException;
                    string errorMessage = $"Runtime error during code execution: {innerException?.Message}";
                    string stackTrace = innerException?.StackTrace ?? "";
                    
                    // Also include any Unity error logs that were captured
                    if (logCapture.HasErrors())
                    {
                        errorMessage += $"\n\nAdditional Unity error logs:\n{logCapture.GetCapturedLogs()}";
                    }
                    
                    Debug.LogError($"{errorMessage}\n{stackTrace}");
                    return errorMessage;
                }
                catch (Exception ex)
                {
                    string errorMessage = $"Unexpected error during code execution: {ex.Message}";
                    
                    // Include any Unity error logs that were captured
                    if (logCapture.HasErrors())
                    {
                        errorMessage += $"\n\nAdditional Unity error logs:\n{logCapture.GetCapturedLogs()}";
                    }
                    
                    Debug.LogError($"{errorMessage}\n{ex.StackTrace}");
                    return errorMessage;
                }
            }
            
            return "Code executed successfully from external source.";
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error executing generated code: {ex.Message}\n{ex.StackTrace}");
            return $"Error executing generated code: {ex.Message}\n{ex.StackTrace}";
        }
#endif
        }

        public static string AddScriptToProject(string relativePath, string sourceCode)
        {
#if UNITY_EDITOR
        try
        {
            // Remove "Assets/" from the relative path (if it's there) and combine with Application.dataPath to get a full path
            string relativeSubPath = relativePath.StartsWith("Assets/") ? relativePath.Substring("Assets/".Length) : relativePath;
            string fullPath = Path.Combine(Application.dataPath, relativeSubPath);

            // Ensure the directory exists (and create it if not)
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Write the source code to the file
            File.WriteAllText(fullPath, sourceCode);

            // Finally, import the new asset into Unity
            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("Script added to project at: " + relativePath);
            return "Script added to project at: " + relativePath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error adding script to project: {ex.Message}\n{ex.StackTrace}");
            return $"Error adding script to project: {ex.Message}\n{ex.StackTrace}";
        }
#endif
        }

        public static string EditExistingScript(string relativePath, string newSourceCode)
        {
#if UNITY_EDITOR
            try
            {
                // Remove "Assets/" from the relative path (if it's there) and combine with Application.dataPath to get a full path
                string relativeSubPath = relativePath.StartsWith("Assets/") ? relativePath.Substring("Assets/".Length) : relativePath;
                string fullPath = Path.Combine(Application.dataPath, relativeSubPath);

                // Check if the file exists
                if (!File.Exists(fullPath))
                {
                    Debug.LogError("Script to edit does not exist at: " + fullPath + " use add_script_to_project to create a new script");
                    return $"Error editing script: Script to edit does not exist at: {fullPath} use add_script_to_project to create a new script";
                }

                // Write the source code to the file
                // this should automatically overwrite the existing file
                File.WriteAllText(fullPath, newSourceCode);

                // Finally, import the new asset into Unity
                AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
                Debug.Log("Script edited in project at: " + relativePath);
                return "Script edited in project at: " + relativePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error editing script: {ex.Message}\n{ex.StackTrace}");
                return $"Error editing script: {ex.Message}\n{ex.StackTrace}";
            }
#endif
        }
    }
}