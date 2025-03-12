using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GameGenie
{
    public static class CodeParser
    {
        private static readonly HashSet<string> DangerousKeywords = new HashSet<string>
        {
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "System.Diagnostics.Process.Start",
            "System.Environment",
            "PlayerPrefs.DeleteAll",
            "EditorPrefs.DeleteAll",
            "Application.Quit",
            "EditorApplication.Exit"
        };
        
        public static string ExtractCodeBlocks(string text)
        {
            // Regular expression to match code blocks between ```csharp and ```
            Regex regex = new Regex(@"```csharp\s*([\s\S]*?)\s*```", RegexOptions.Multiline);
            
            StringBuilder codeBuilder = new StringBuilder();
            MatchCollection matches = regex.Matches(text);
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string code = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(code))
                    {
                        codeBuilder.AppendLine(code);
                        codeBuilder.AppendLine();
                    }
                }
            }
            
            return codeBuilder.ToString().Trim();
        }
        
        public static bool ValidateCode(string code, out string warningMessage)
        {
            warningMessage = string.Empty;
            
            // Check for dangerous operations
            foreach (string keyword in DangerousKeywords)
            {
                if (code.Contains(keyword))
                {
                    warningMessage = $"Warning: The code contains potentially dangerous operation: {keyword}";
                    return false;
                }
            }
            
            // Check for file system operations
            if (Regex.IsMatch(code, @"(File\.|Directory\.|Path\.)", RegexOptions.IgnoreCase))
            {
                warningMessage = "Warning: The code contains file system operations that might be dangerous.";
                return false;
            }
            
            // Check for network operations
            if (Regex.IsMatch(code, @"(WebClient|HttpClient|NetworkStream|Socket|TcpClient|UdpClient)", RegexOptions.IgnoreCase))
            {
                warningMessage = "Warning: The code contains network operations that might be dangerous.";
                return false;
            }
            
            // Check for reflection
            if (Regex.IsMatch(code, @"(System\.Reflection|GetType\(\)\.GetMethod|Assembly)", RegexOptions.IgnoreCase))
            {
                warningMessage = "Warning: The code contains reflection operations that might be dangerous.";
                return false;
            }
            
            return true;
        }
        
        public static string FormatCodeForDisplay(string code)
        {
            // Add syntax highlighting or other formatting if needed
            return code;
        }
        
        public static string SanitizeCode(string code)
        {
            // Remove any using directives (we'll add them in the generated script)
            code = Regex.Replace(code, @"^\s*using\s+[^;]+;\s*$", "", RegexOptions.Multiline);
            
            // Remove any namespace declarations
            code = Regex.Replace(code, @"^\s*namespace\s+[^{]+\{", "", RegexOptions.Multiline);
            code = Regex.Replace(code, @"^\s*\}\s*$", "", RegexOptions.Multiline);
            
            // Remove any class declarations
            code = Regex.Replace(code, @"^\s*(public|private|internal|protected)?\s*class\s+[^{]+\{", "", RegexOptions.Multiline);
            
            // Remove any method declarations
            code = Regex.Replace(code, @"^\s*(public|private|internal|protected)?\s*(static)?\s*void\s+\w+\s*\([^)]*\)\s*\{", "", RegexOptions.Multiline);
            
            return code.Trim();
        }
    }
} 