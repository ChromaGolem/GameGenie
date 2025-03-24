// Logger for GameGenie Unity

using UnityEngine;
using UnityEditor;

using System;
using System.Collections.Generic;

namespace GameGenieUnity
{
    public class Logger
    {
        private static List<string> messageLog = new List<string>();
        // Path to the log file one directory up from this file
        //private static string logPath = "/tmp/genie_client_log.log";
        private static string logPath = "C:\\Users\\druse\\OneDrive\\Desktop\\genie_mcp_server-unity.log";

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