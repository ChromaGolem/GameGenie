using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace GameGenie
{
    public class ClaudeApiClient
    {
        private const string ApiBaseUrl = "https://api.anthropic.com/v1/messages";
        private const string ModelName = "claude-3-opus-20240229";
        private const int MaxTokens = 4096;
        
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        
        public ClaudeApiClient(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        
        public async Task<ClaudeResponse> SendMessageAsync(string userMessage, string sceneContext)
        {
            try
            {
                // Prepare the system prompt with MCP instructions
                string systemPrompt = GetSystemPrompt();
                
                // Prepare the user message with scene context
                string fullUserMessage = $"Scene Context:\n{sceneContext}\n\nUser Query: {userMessage}";
                
                // Create the request payload
                var requestPayload = new
                {
                    model = ModelName,
                    max_tokens = MaxTokens,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = fullUserMessage }
                    }
                };
                
                // Serialize the payload
                string jsonPayload = JsonConvert.SerializeObject(requestPayload);
                
                // Create the request content
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // Send the request
                var response = await _httpClient.PostAsync(ApiBaseUrl, content);
                
                // Check if the request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var claudeResponse = JsonConvert.DeserializeObject<ClaudeResponse>(jsonResponse);
                    return claudeResponse;
                }
                else
                {
                    // Handle error
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"Claude API Error: {response.StatusCode} - {errorContent}");
                    return new ClaudeResponse
                    {
                        Error = $"API Error: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Claude API Exception: {ex.Message}");
                return new ClaudeResponse
                {
                    Error = $"Exception: {ex.Message}"
                };
            }
        }
        
        private string GetSystemPrompt()
        {
            return @"You are Game Genie, an AI assistant specialized in helping Unity developers create and modify game scenes through code.

Your responses should follow these guidelines:

1. Analyze the scene context provided by the user to understand the current state of their Unity project.

2. When the user asks you to create or modify something in their scene, respond with executable C# code that can be run within the Unity Editor.

3. Always wrap your code in triple backticks with the csharp language identifier, like this:
```csharp
// Your code here
```

4. Provide clear explanations of what your code does and how it works.

5. Focus on writing code that is:
   - Safe to execute in the Editor
   - Compatible with Unity's Undo system
   - Well-commented and easy to understand
   - Efficient and follows Unity best practices

6. If you need more information about the scene to provide accurate code, ask specific questions.

7. If the user's request is unclear or potentially harmful, ask for clarification rather than providing potentially destructive code.

Remember that your code will be executed directly in the user's Unity Editor, so ensure it's safe, correct, and accomplishes the user's goals.";
        }
    }
    
    public class ClaudeResponse
    {
        [JsonProperty("content")]
        public List<Content> Content { get; set; }
        
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("model")]
        public string Model { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
        
        // Custom property to handle errors
        [JsonIgnore]
        public string Error { get; set; }
        
        public string GetTextContent()
        {
            if (!string.IsNullOrEmpty(Error))
            {
                return $"Error: {Error}";
            }
            
            if (Content == null || Content.Count == 0)
            {
                return "No content received from Claude.";
            }
            
            StringBuilder sb = new StringBuilder();
            foreach (var content in Content)
            {
                if (content.Type == "text")
                {
                    sb.AppendLine(content.Text);
                }
            }
            
            return sb.ToString();
        }
    }
    
    public class Content
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("text")]
        public string Text { get; set; }
    }
} 