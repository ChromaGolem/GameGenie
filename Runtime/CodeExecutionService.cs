using UnityEngine;
using System.Linq;

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
            using var provider = new CSharpCodeProvider();
            var compilerParams = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false
            };

            // Add all the assemblies we might need for Unity Editor scripting
            // TODO we probably want to dynamically add these based on the types used in the source code,
            //      but this should work for now, and we can just add more as needed until we figure out a better solution
            compilerParams.ReferencedAssemblies.Add(typeof(UnityEngine.Object).Assembly.Location);
            compilerParams.ReferencedAssemblies.Add(typeof(EditorWindow).Assembly.Location);
            compilerParams.ReferencedAssemblies.Add(typeof(Editor).Assembly.Location);
            compilerParams.ReferencedAssemblies.Add(typeof(System.Linq.Enumerable).Assembly.Location);
            
            // Also reference the current assembly to give the compiled code access to the current project
            compilerParams.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location); // TODO we should probably actually test that this brings in project namespace

            // Grab the current project's netstandard assembly for the correct Object reference (very important, or else compiler can't disambiguate Object source)
            compilerParams.ReferencedAssemblies.Add(AppDomain.CurrentDomain.GetAssemblies()
                    .First(a => a.GetName().Name == "netstandard").Location);

            Debug.Log("Compiling source code: " + wrappedSourceCode);

            // Compile the wrapped code
            CompilerResults results = provider.CompileAssemblyFromSource(compilerParams, wrappedSourceCode);
            if (results.Errors.HasErrors)
            {
                string errorMessage = "Code compilation failed:\n";
                foreach (CompilerError error in results.Errors)
                    errorMessage += $"Line {error.Line}: {error.ErrorText}\n";

                Debug.LogError(errorMessage);
                return errorMessage;
            }

            // Get the compiled assembly and search for our wrapper type.
            Assembly assembly = results.CompiledAssembly;
            Type wrapperType = assembly.GetType("EditorCodeWrapper");
            if (wrapperType == null)
            {
                Debug.LogError("Could not find the 'EditorCodeWrapper' type in the compiled assembly. Did it compile correctly?");
                Debug.LogError("Source code:\n" + wrappedSourceCode);
                return $"Error executing generated code: Could not find the 'EditorCodeWrapper' type in the compiled assembly. Did it compile correctly?";
            }

            // Look for any method named "Execute" regardless of visibility and just run the first one found (#codesmell)
            MethodInfo executeMethod = wrapperType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (executeMethod == null)
            {
                Debug.LogError("No 'Execute' method found in 'EditorCodeWrapper'. Did it compile correctly?");
                Debug.LogError("Source code:\n" + wrappedSourceCode);
                return $"Error executing generated code: No 'Execute' method found in 'EditorCodeWrapper'. Did it compile correctly?";
            }

            // Finally, run the code!
            Debug.Log("Executing successfully-compiled source code!");
            executeMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error executing generated code: {ex.Message}\n{ex.StackTrace}");
            return $"Error executing generated code: {ex.Message}\n{ex.StackTrace}";
        }

        // If we made it all the way here... we should have compiled and executed some code without any errors!
        Debug.Log("Code executed successfully from external source.");
        return "Code executed successfully from external source.";
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