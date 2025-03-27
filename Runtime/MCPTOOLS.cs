using UnityEngine;
using UnityEditor;

using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace GameGenieUnity
{
    public class GameGenieCommand
    {
        public string command { get; set; }
        public JObject @params { get; set; }
    }
    public class MCPTOOLS
    {
        public static async void MCPSWITCH(string completeMessage)
        {
            Logger.AddToLog("MCPSWITCH: " + completeMessage);
            var json = JsonConvert.DeserializeObject<GameGenieCommand>(completeMessage);

            string command = json.command;
            // Get the message ID for the response
            string messageId = json.@params["message_id"]?.ToString() ?? "";

            // MCP Tools
            switch (command)
            {
                case "execute_unity_code_in_editor":
                    string code = json.@params["code"]?.ToString() ?? "";
                    Debug.Log("Executing code in editor: " + code);

                    try
                    {
                        // Execute the code
                        var executionResult = GameGenieUnity.CodeExecutionService.ExecuteInEditor(code);

                        // Send response with the same message ID
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "execute_unity_code_in_editor",
                            message_id = messageId,
                            data = new
                            {
                                success = true,
                                result = executionResult
                            }
                        });
                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        // Send error response
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "execute_unity_code_in_editor",
                            message_id = messageId,
                            data = new
                            {
                                success = false,
                                error = ex.Message
                            }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "get_scene_context":
                    try
                    {
                        // This would be implemented by your SceneContext class
                        // For now just sending a placeholder response
                        object sceneContextJson = SceneContextService.GetSceneContext();

                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "get_scene_context",
                            message_id = messageId,
                            data = new
                            {
                                success = true,
                                context = sceneContextJson
                            }
                        });
                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "get_scene_context",
                            message_id = messageId,
                            data = new
                            {
                                success = false,
                                error = ex.Message
                            }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "get_scene_file":
                    try
                    {
                        string sceneFile = SceneContextService.GetSceneFile();
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "get_scene_file",
                            message_id = messageId,
                            data = new
                            {
                                success = true,
                                sceneFile = sceneFile
                            }
                        });
                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "get_scene_file",
                            message_id = messageId,
                            data = new
                            {
                                success = false,
                                error = ex.Message
                            }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "add_script_to_project":
                    try
                    {
                        string relativePath = json.@params["relative_path"]?.ToString() ?? "";
                        string sourceCode = json.@params["source_code"]?.ToString() ?? "";

                        // Send response BEFORE adding the script
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "add_script_to_project",
                            message_id = messageId,
                            data = new { success = true, result = "Script will be added to project at: " + relativePath }
                        });

                        // Now add the script
                        GameGenieUnity.CodeExecutionService.AddScriptToProject(relativePath, sourceCode);

                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "add_script_to_project",
                            message_id = messageId,
                            data = new { success = false, error = ex.Message }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "edit_existing_script":
                    try
                    {
                        string relativePath = json.@params["relative_path"]?.ToString() ?? "";
                        string newSourceCode = json.@params["new_source_code"]?.ToString() ?? "";

                        // Send response BEFORE editing the script
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "edit_existing_script",
                            message_id = messageId,
                            data = new { success = true, result = "Script will be edited in project at: " + relativePath }
                        });

                        // Now edit the script
                        GameGenieUnity.CodeExecutionService.EditExistingScript(relativePath, newSourceCode);

                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "edit_existing_script",
                            message_id = messageId,
                            data = new { success = false, error = ex.Message }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "edit_prefab":
                    try
                    {
                        string prefabPath = json.@params["prefab_path"]?.ToString() ?? "";
                        string newPrefabYaml = json.@params["new_yaml_data"]?.ToString() ?? "";

                        // Send response BEFORE editing the prefab
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "edit_prefab",
                            message_id = messageId,
                            data = new { success = true, result = "Prefab will be edited at: " + prefabPath }
                        });

                        // Now edit the prefab by just overwriting the file with this new YAML data
                        GameGenieUnity.CodeExecutionService.AddRawFileToProject(prefabPath, newPrefabYaml);

                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "edit_prefab",
                            message_id = messageId,
                            data = new { success = false, error = ex.Message }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "read_file":
                    try
                    {
                        string relativePath = json.@params["relative_path"]?.ToString() ?? "";
                        string fileContent = SceneContextService.ReadFile(relativePath);

                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "read_file",
                            message_id = messageId,
                            data = new
                            {
                                success = true,
                                fileContent = fileContent
                            }
                        });

                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "read_file",
                            message_id = messageId,
                            data = new { success = false, error = ex.Message }
                        });
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;

                case "save_image_to_project":
                    string imageData = json.@params["image"]?.ToString() ?? "";
                    // Decode the base64 string to binary data.
                    try
                    {
                        string result = GameGenieUnity.CodeExecutionService.SaveImageToProject(imageData);
                        string response = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "save_image_to_project",
                            message_id = messageId,
                            data = new { success = true, result = result }
                        });
                        await UnityClient.SendRawMessage(response);
                    }
                    catch (Exception ex)
                    {
                        string errorResponse = JsonConvert.SerializeObject(new
                        {
                            type = "response",
                            command = "save_image_to_project",
                            message_id = messageId,
                            data = new { success = false, error = ex.Message }
                        });
                        Logger.AddToLog("Error saving image to project: " + ex.Message);
                        await UnityClient.SendRawMessage(errorResponse);
                    }
                    break;
            }
        }
    }
}