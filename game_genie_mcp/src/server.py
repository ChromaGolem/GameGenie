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

# Configure logging
logging.basicConfig(
    level=logging.INFO, 
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler("/tmp/genie_mcp_server.log")
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
    EXECUTE_UNITY_CODE = "execute_unity_code_in_editor"
    ADD_SCRIPT_TO_PROJECT = "add_script_to_project"

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

    # Send a message to the Unity editor
    # this is how the MCP communicates with the Unity editor and other applications
    async def send_command_to_unity(self, command: str, params: Dict[str, Any]) -> str:
        if not self.unity_client:
            return "no Unity client connected"
        
        message = json.dumps({"command": command, "params": params})
        
        # Send to all connected clients
        try:
            await self.unity_client.send(message)
        except Exception as e:
            logger.error(f"Error sending to Unity client: {str(e)}")
        
        return f"Message sent to {len(self.connected_clients)} clients"
        
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

# tools
    

@mcp.tool()
async def get_scene_context() -> str:
    """
    Extract the current Unity scene context including hierarchy, selected objects, and settings.

    Returns:
        A JSON string containing the scene context information.
    """
    logger.info("Extracting scene context from Unity...")

    try:
        # get the current scene context
        result = await server.send_command_to_unity(UnityTools.GET_SCENE_CONTEXT, {})

        return f"Scene context extracted successfully: {result}"
    except Exception as e:
        logger.error(f"Error extracting scene context: {str(e)}")
        return f"Error extracting scene context: {str(e)}"


@mcp.tool()
async def execute_unity_code(code: str) -> str:
    """
    Execute C# code in the Unity editor to modify the scene.

    Args:
        code: The C# code to execute in the Unity editor

    Returns:
        A message indicating the result of the code execution.
    """
    logger.info(f"Executing code in Unity (length: {len(code)})...")

    try:
        # TODO: execute the code in Unity
        result = await server.send_command_to_unity(UnityTools.EXECUTE_UNITY_CODE, {"code": code})
        # Return the successful result
        logger.info(f"Unity code execution successful")
        return f"Code executed successfully: {result}"
    except Exception as e:
        logger.error(f"Error executing code: {str(e)}")
        return f"Error executing code: {str(e)}"

@mcp.prompt()
def unity_developer_strategy() -> str:
    """
    Define a strategy for the Unity developer to use the tools provided to them to complete the task.
    """
    return """
    You are a Unity developer. Use the tools provided to you to complete the task.

    You will be given a task to complete.
    You will need to use the tools provided to you to complete the task.
    """

# Main execution
def main():
    """Run the MCP server"""
    # Use the built-in run method which will make it compatible with "uvx game_genie_mcp"
    mcp.run()


if __name__ == "__main__":
    main() 