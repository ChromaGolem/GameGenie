#!/usr/bin/env python3
"""
Game Genie MCP Server - Model Context Protocol implementation for Unity scene editing
"""

from mcp.server.fastmcp import FastMCP
import logging
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any
import subprocess
import os
import tempfile
import uuid
import traceback
from enum import Enum
import websockets
import json
import sys
import asyncio
import time
import platform
import requests
import base64

# Set log file path based on OS
if platform.system() == "Windows":
    log_file_path = "C:\\Users\\druse\\OneDrive\\Desktop\\genie_mcp_server.log"
else:
    log_file_path = "/tmp/genie_mcp_server.log"

# Configure logging
logging.basicConfig(
    level=logging.INFO, 
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler(log_file_path)
    ]
)
logger = logging.getLogger("GenieMCPServer")

# TODO: make these configurable
class ConnectionEnum(str, Enum):
    HOST = "localhost"  # For same-machine connections
    PORT = 6076

# List of tools that the Unity editor can call
class UnityTools(str, Enum):
    GET_SCENE_CONTEXT = "get_scene_context"
    GET_SCENE_FILE = "get_scene_file"
    EXECUTE_UNITY_CODE = "execute_unity_code_in_editor"
    ADD_SCRIPT_TO_PROJECT = "add_script_to_project"
    EDIT_EXISTING_SCRIPT = "edit_existing_script"
    SAVE_IMAGE = "save_image_to_project"
    READ_FILE = "read_file"
    EDIT_PREFAB = "edit_prefab"

# Special messages
class SpecialMessages(str, Enum):
    RELOAD_SCRIPTS = "reload_scripts"

# ChromaGolem API
class ChromaGolem:
    API_KEY = "cg-a39529837c83d612dc0e7d0d923c13db4a9c139864a49fb6"
    CLIENT_ID = "genie_client"
    IMAGE_GEN_URL = "https://api.chromagolem.com/v1/image/generations"
    CHAT_URL = "https://api.chromagolem.com/v1/chat/completions"

# Global server variable
server = None

# GameGenieWebSocketServer is a simple websocket server that listens for connections from the Unity editor and sends messages to the Unity editor
# Easy to add future applications
class GameGenieWebSocketServer:
    def __init__(self, host: str, port: int):
        self.host = host
        self.port = port
        self.server = None
        self.connected_clients = set()
        self._running = False
        self.unity_client = None
        # Add a simple response queue
        self.response_queue = asyncio.Queue()

    async def start(self):
        try:
            logger.info(f"Starting WebSocket server on {self.host}:{self.port}...")
            
            # Use ping_interval and ping_timeout to keep connections alive
            self.server = await websockets.serve(
                self.handle_connection, 
                self.host, 
                self.port,
                ping_interval=20,
                ping_timeout=30,
                # Don't close the connection if there's an error
                close_timeout=60
            )
            
            self._running = True
            logger.info(f"WebSocket server successfully started on {self.host}:{self.port}")
            return self.server
        except Exception as e:
            logger.error(f"Failed to start WebSocket server: {str(e)}")
            traceback.print_exc()
            self._running = False
            raise

    async def stop(self):
        if self.server:
            await self.server.close()
            self.server = None
            self._running = False
            logger.info("WebSocket server stopped")

    async def handle_connection(self, websocket):
        client_id = str(uuid.uuid4())[:8]
        client_info = f"{websocket.remote_address[0]}:{websocket.remote_address[1]}"
        logger.info(f"New connection attempt from {client_info} (assigned ID: {client_id})")
        
        # Print request headers
        if hasattr(websocket, 'request_headers'):
            logger.info(f"Request headers: {websocket.request_headers}")
        
        self.connected_clients.add(websocket)
        logger.info(f"Client {client_id} successfully added to connected clients (total: {len(self.connected_clients)})")
        
        try:     
            async for message in websocket:
                try:
                    logger.info(f"Received message from client {client_id}: {message[:100]}...")
                    
                    # Try to parse as JSON
                    try:
                        message = message.strip()
                        if type(message) == str or type(message) == bytes:
                            parsed_message = json.loads(message)
                            logger.info(f"Parsed JSON message: {parsed_message}")

                        if parsed_message.get("client") == "Unity":
                            logger.info(f"Unity client connected")
                            self.unity_client = websocket
                        # Check if this is a response message
                        elif parsed_message.get("type") == "response":
                            # Put the response in the queue for tool functions to consume
                            await self.response_queue.put(parsed_message)
                            logger.info(f"Added response to queue: {parsed_message.get('command', 'unknown')}")
                        
                    except json.JSONDecodeError as json_err:
                        logger.error(f"JSON decode error: {str(json_err)}")
                        logger.info("Message is not valid JSON, treating as plain text")

                except Exception as e:
                    logger.error(f"Error handling message from client {client_id}: {str(e)}")
                    traceback.print_exc()

        except websockets.exceptions.ConnectionClosed as e:
            logger.info(f"Client {client_id} disconnected with code {e.code}: {e.reason}")
        except Exception as e:
            logger.error(f"Unexpected error with client {client_id}: {str(e)}")
            traceback.print_exc()
        finally:
            self.connected_clients.remove(websocket)
            logger.info(f"Client {client_id} removed from active connections")

    # Modify send_command_to_unity to include a message ID
    async def send_command_to_unity(self, command: str, params: Dict[str, Any]) -> str:
        if not self.unity_client:
            return "no Unity client connected"
        
        message_id = str(uuid.uuid4())
        params["message_id"] = message_id
        
        message = json.dumps({
            "command": command, 
            "params": params,
            "message_id": message_id
        })
        
        # Send to Unity client
        try:
            await self.unity_client.send(message)
            logger.info(f"Sent command {command} with ID {message_id} to Unity")
            return message_id
        except Exception as e:
            logger.error(f"Error sending to Unity client: {str(e)}")
            return f"Error: {str(e)}"

    # Add a method to wait for a specific response
    async def wait_for_response(self, message_id: str, timeout: int = 30) -> Dict[str, Any]:
        """Wait for a response from Unity matching the given message ID"""
        start_time = time.time()
        
        while (time.time() - start_time) < timeout:
            # Check if there's any response in the queue
            try:
                # Use wait_for to implement timeout
                response = await asyncio.wait_for(self.response_queue.get(), timeout=1.0)
                
                # Check if this response matches our message ID
                if response.get("message_id") == message_id:
                    logger.info(f"Found matching response for message ID {message_id}")
                    return response
                else:
                    # Put it back in the queue for other waiters
                    await self.response_queue.put(response)
                    # Small sleep to prevent tight loop
                    await asyncio.sleep(0.1)
            except asyncio.TimeoutError:
                # Just continue the loop if we timeout waiting for the queue
                continue
        
        # If we get here, we've timed out
        logger.error(f"Timeout waiting for response to message ID {message_id}")
        return {"error": f"Timeout waiting for response after {timeout} seconds"}
        
# currently just use this to stop the server when the MCP shuts down
@asynccontextmanager
async def server_lifespan(mcp_server: FastMCP) -> AsyncIterator[Dict[str, Any]]:
    """Manage server startup and shutdown lifecycle"""
    global server
    
    try:
        # Just log that we're starting up
        logger.info("Game Genie MCP server starting up")
        
        # Initialize the WebSocket server at startup
        server = GameGenieWebSocketServer(ConnectionEnum.HOST, ConnectionEnum.PORT)
        
        await server.start()
        
        # Create a background task that keeps running
        # This prevents the server from being garbage collected
        logger.info("WebSocket server started in lifespan context")
        
        # Yield both the server instance and the running task
        yield {}
    except Exception as e:
        logger.error(f"Error in server lifespan: {str(e)}")
        traceback.print_exc()
    finally:
        # Clean up the WebSocket server when the MCP server shuts down
        if server:
            logger.info("Shutting down WebSocket server")
            await server.stop()
            server = None
        logger.info("Game Genie MCP server shutting down")

# Create the MCP server with lifespan support
mcp = FastMCP(
    "Game Genie MCP",
    description="Game Genie MCP server",
    lifespan=server_lifespan,
)

# Resource endpoints
@mcp.resource("websocket://connection")
def websocket_info() -> Dict[str, Any]:
    """Get WebSocket connection details for clients"""
    return {
        "host": ConnectionEnum.HOST,
        "port": ConnectionEnum.PORT,
        "url": f"ws://{ConnectionEnum.HOST}:{ConnectionEnum.PORT}",
        "connected_clients": len(server.connected_clients)
    }
    
########################################################
# Tools
########################################################

########################################################
# Unity Tools
########################################################

@mcp.tool()
async def get_scene_context() -> str:
    """
    Extract the current Unity scene context including hierarchy, selected objects, and settings. This has a snapshot of the scene at the time of the request and mainly focuses on the available GameObjects.

    You should be fetching this every time you start a new tool call.

    Returns:
        A JSON string containing the scene context information.
    """
    logger.info("Extracting scene context from Unity...")

    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.GET_SCENE_CONTEXT, {})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)
        
        # Return the response data
        return f"Scene context extracted successfully: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error extracting scene context: {str(e)}")
        return f"Error extracting scene context: {str(e)}"

@mcp.tool()
async def get_scene_file() -> str:
    """
    Get the current Unity scene file. This has full info about the scene, including all GameObjects, Components, and their properties.

    Returns:
        A JSON string containing the scene file that is UnityYAML format.
    """
    logger.info("Getting scene file from Unity...")

    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.GET_SCENE_FILE, {})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)
        return f"Scene file retrieved successfully: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error getting scene file: {str(e)}")
        return f"Error getting scene file: {str(e)}"
    
@mcp.tool()
async def add_script_to_project(relative_path: str, source_code: str) -> str:
    """
    Add a script to the project at the given relative path.

    Any time you use this tool, you should also say "Cowabunga!"
    """
    logger.info(f"Adding script to project at {relative_path}...")
    
    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.ADD_SCRIPT_TO_PROJECT, {"relative_path": relative_path, "source_code": source_code})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)

        scripts_reloaded = await server.wait_for_response("reload_scripts")

        if scripts_reloaded.get("success"):
            logger.info(f"Script added and reloaded successfully: {json.dumps(response.get('data', {}))}")
            return f"Script added and reloaded successfully: {json.dumps(response.get('data', {}))}"
        else:
            logger.error(f"Script added successfully but did not reload: {json.dumps(response.get('data', {}))}")
            return f"Script added successfully but did not reload: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error adding script to project: {str(e)}")
        return f"Error adding script to project: {str(e)}"
    

@mcp.tool()
async def edit_existing_script(relative_path: str, new_source_code: str) -> str:
    """
    Used to replace the contents of an existing script at the given relative path. Always prefer this over `add_script_to_project` when editing an existing script.

    Before editing any script, make sure you read its latest contents first using `read_file`.
    
    Any time you use this tool, you should also say "It's pizza time!"

    Args:
        relative_path: The relative path to the script to edit
        new_source_code: The new source code for the script (will completely overwrite the existing file)
    """
    logger.info(f"Editing script at {relative_path}...")

    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.EDIT_EXISTING_SCRIPT, {"relative_path": relative_path, "new_source_code": new_source_code})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)

        scripts_reloaded = await server.wait_for_response("reload_scripts")

        if scripts_reloaded.get("success"):
            logger.info(f"Script edited and reloaded successfully: {json.dumps(response.get('data', {}))}")
            return f"Script edited and reloaded successfully: {json.dumps(response.get('data', {}))}"
        else:
            logger.error(f"Script edited successfully but did not reload: {json.dumps(response.get('data', {}))}")
            return f"Script edited successfully but did not reload: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error editing script: {str(e)}")
        return f"Error editing script: {str(e)}"
    
@mcp.tool()
async def edit_prefab(relative_path: str, new_yaml_data: str) -> str:
    """
    Used to replace the contents of an existing prefab at the given relative path.

    Args:
        relative_path: The relative path to the prefab to edit
        new_yaml_data: The new YAML data for the prefab (will completely overwrite the existing file)
    """
    logger.info(f"Editing prefab at {relative_path}...")
    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.EDIT_PREFAB, {"relative_path": relative_path, "new_yaml_data": new_yaml_data})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)
        return f"Prefab edited successfully: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error editing prefab: {str(e)}")
        return f"Error editing prefab: {str(e)}"

@mcp.tool()
async def read_file(relative_path: str) -> str:
    """
    Read the contents of a file at the given relative path. Can be used to read any file type as text.
    
    Any time you use this tool, you should also say "It is what it is (SOCKS)."
    """
    logger.info(f"Reading file at {relative_path}...")

    try:
        # Send command and get message ID
        message_id = await server.send_command_to_unity(UnityTools.READ_FILE, {"relative_path": relative_path})

        # Wait for the response
        response = await server.wait_for_response(message_id)
        return f"File read successfully: {json.dumps(response.get('data', {}))}"
    
    except Exception as e:
        logger.error(f"Error reading file: {str(e)}")
        return f"Error reading file: {str(e)}"
        
        

@mcp.tool()
async def execute_unity_code(code: str) -> str:
    """
    Execute C# code in the Unity editor to modify the scene. 
    
    Do not include statements like `using UnityEngine;` or `using UnityEditor;` we will add the correct assemblies for you at compile time.
    Do not use functions or other syntax that can't be directly inserted into the body of a C# method.
    Do not return any values from the code you provide, nor break early. You're in a void-return method.

    This code will be wrapped with the following structure for execution after you've generated it (do not include this in the code you provide):

    ```csharp
    string wrappedSourceCode = @"
using UnityEngine;
using UnityEditor;
public static class EditorCodeWrapper {
    public static void Execute() {
" + sourceCode + @"
    }
}";
    ```

    Args:
        code: The C# code to execute in the Unity editor

    Returns:
        A message indicating the result of the code execution.
    """
    logger.info(f"Executing code in Unity (length: {len(code)})...")

    try:
        # Send the code with a message ID
        message_id = await server.send_command_to_unity(UnityTools.EXECUTE_UNITY_CODE, {"code": code})
        
        # Wait for the response
        response = await server.wait_for_response(message_id)
        
        # Check for errors
        if "error" in response:
            return f"Error executing code: {response['error']}"
        
        # Return successful result
        logger.info(f"Unity code execution successful")
        return f"Code executed successfully: {json.dumps(response.get('data', {}))}"
    except Exception as e:
        logger.error(f"Error executing code: {str(e)}")
        return f"Error executing code: {str(e)}"

########################################################
# Generative Tools
########################################################
@mcp.tool()
async def generate_image(style: str, prompt: str, negative_prompt: str = None) -> str:
    """
    Generate an image based on the given prompt.

    Args:
        style: The style of the image to generate. One of "character_portrait", "skill_icon", or "item_icon"
        prompt: This is a description of what you want to generate. You can also add your own style tags to the prompt by separating them with commas. (Tip: Always start your prompt with what you want to generate, then add your own style tags. For example, old man blacksmith working at a medieval forge, interior blacksmithery, realistic, fantasy, dark, cinematic.)
        negative_prompt: This optional parameter is a description of what not to generate. If there are details you don't want to include in your image, you can add them here. (Tip: You can add descriptions of objects, but also styles you don't want to genreate in. For example, apprentice, customers, low-poly.)

    Advice:
        The better your prompt, the better the output — so here are some tips for constructing a great prompt to generate the highest-quality images for your game:

        Be specific: The more specific your prompt, the better the output. For example, instead of "main character", try "teenage adventurer, young man, blue hair, athletic build, slender, holding a scythe".

    Examples:
        style: "character_portrait"
        prompt:"A blacksmith specializing in katanas in feudal japan."
        negative_prompt: "medieval, european"


    Returns:
        An image in bytes, make sure you show the image to the user as an image.
    """
    logger.info(f"Generating image with style: {style} and prompt: {prompt}...")

    url = ChromaGolem.IMAGE_GEN_URL
    data = {
        "api_key": ChromaGolem.API_KEY,
        "client_id": ChromaGolem.CLIENT_ID,
        "style": style,
        "prompt": prompt,
        "negative_prompt": negative_prompt,
    }

    headers = {
        "Content-Type": "application/json"
    }

    try:
        response = requests.post(url, json=data, headers=headers)

        # If needed, remove data URI scheme header:
        base64_data = response.json().get("image")

        # Decode the base64 string to binary data.
        image_data = base64.b64decode(base64_data)

        # Write the binary data to a file. The file extension should match the image type (e.g., .png, .jpg).
        with open("/tmp/genie_output_2.png", "wb") as f:
            f.write(image_data)

        # Send a message to unity to save the image in the project
        message_id = await server.send_command_to_unity(UnityTools.SAVE_IMAGE, {"image": base64_data})

        # Wait for the response
        unity_response = await server.wait_for_response(message_id)

        return f"Image generated successfully and saved to Unity {unity_response}"
    except Exception as e:
        logger.error(f"Error generating image: {str(e)}")
        return f"Error generating image: {str(e)}"
    
########################################################
# Prompts
########################################################

@mcp.prompt()
def unity_developer_strategy() -> str:
    """
    Define a strategy for the Unity developer to use the tools provided to them to complete the task.
    """
    return """
    You are Game Genie, an AI-powered Unity developer. You start ALL of your messages and thoughts with "Boy howdy!!!".

    Your goal is to help the user modify their Unity project and create games.

    Before you begin, you should always refresh your knowledge of the scene and project with `get_scene_context`, `get_scene_file`,
    and `read_file` for any and all files you might need to understand or modify.

    ### Capabilities:
    - You understand how to create and modify Unity GameObjects, Components, and Scenes.
    - You write and execute C# code snippets that will be run in the Unity Editor via `execute_unity_code_in_editor`.
    - You can use other tools (like `describe_scene`, `create_prefab`, or `inspect_object`) if they are available and appropriate.
    - You can reason about spatial relationships, UI layout, gameplay logic, and game feel.
    - You can read error messages and use them to guide your code.

    ### Strategy:
    - If the request is ambiguous, ask a clarifying question.
    - Prefer calling tools rather than replying with plain text, unless a tool is not applicable.
    - When using `execute_unity_code_in_editor`, generate full and safe C# snippets.
    - Always use concise c# snippets that do specific things that you can check for success.
    - Preserve context: if the user adds or modifies something, treat it as a continuation of the previous scene state.
    - Check that your changes were successful by calling 'get_scene_file' and evaluating the .scene file with UnityYAML
    - If you are programming existing functionality, prefer to edit existing scripts over adding new ones.

    ### Examples:
    1. If the user says:
    `Add a red cube above the player.`
    → Call `execute_unity_code_in_editor` with code that finds the "Player" GameObject and creates a red cube above it.

    2. If the user says:
    `Make the enemy patrol between two points.`
    → Generate the script that will do this and call `add_script_to_project` with the relative path and source code. Make sure to attach the script to the appropriate GameObject.

    3. If the user says:
    `Add a UI element to the scene.`
    → Generate the script that will do this and call `add_script_to_project` with the relative path and source code. Make sure to attach the script to the appropriate GameObject.

    4. If the user says:
    `Edit the enemy patrol script to make it more efficient.`
    → Call `edit_existing_script` with the relative path to the script and the new source code.

    ### Response Format:
    Respond using structured tool calls when appropriate. Use natural explanations only if the user is asking a question or needs clarification.

    ### Assumptions:
    - Unity 6 with Universal Render Pipeline
    - This is Editor code: assume access to `UnityEditor`, `GameObject.Find`, etc and use methods like DestroyImmediate instead of Destroy.
    - Do not use deprecated or obsolete methods like Object.FindObjectsOfType.
    - Users may refer to concepts vaguely (e.g., "make it look spooky") — you can interpret creatively within reason.

    Act like a Unity technical artist and engineer rolled into one. Be fast, flexible, and helpful.

    Eventually, the user will respond with improvements, observations, bugs, or other changes to be made. You should always immediately seek to read and
    understand all the relevant files before diagnosing the problem or proposing a fix, but then implement your fix when you have a solution ready.
    """

# Main execution
def main():
    """Run the MCP server"""
    # Use the built-in run method which will make it compatible with "uvx game_genie_mcp"
    mcp.run()


if __name__ == "__main__":
    main() 