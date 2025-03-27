// Logger for GameGenie Unity

using UnityEngine;
using UnityEditor;

using System;
using System.Collections.Generic;
using System.IO;

namespace GameGenieUnity
{
    public class Logger
    {
        private static List<string> messageLog = new List<string>();
        // Path to the log file based on platform
        private static string logPath = GetLogFilePath();

        private static string GetLogFilePath()
        {
            // Check the current operating system
            if (Application.platform == RuntimePlatform.WindowsEditor || 
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                return "C:\\Users\\druse\\OneDrive\\Desktop\\genie_mcp_server-unity.log";
            }
            else
            {
                // For macOS and other platforms
                return "/tmp/genie_client_log.log";
            }
        }

        public static List<string> GetMessageLog()
        {
            return messageLog;
        }

        public static void AddToLog(string message)
        {
            messageLog.Add(message);
            Debug.Log(message);
            WriteToLogFile(message);
        }

        private static void WriteToLogFile(string message)
        {
            using (System.IO.StreamWriter writer = System.IO.File.AppendText(logPath))
            {
                writer.WriteLine($"{DateTime.Now}: {message}");
            }
        }
    }
}