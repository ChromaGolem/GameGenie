#!/usr/bin/env python3
"""
Game Genie MCP Server - Model Context Protocol implementation for Unity scene editing
"""

from mcp.server.fastmcp import FastMCP, Context, Image
import socket
import json
import asyncio
import logging
from dataclasses import dataclass
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any, List
import os
from pathlib import Path
import base64
from urllib.parse import urlparse

# Configure logging
logging.basicConfig(
    level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger("GenieMCPServer")


@dataclass
class UnityConnection:
    host: str
    port: int
    sock: socket.socket = (
        None  # Changed from 'socket' to 'sock' to avoid naming conflict
    )

    def connect(self) -> bool:
        """Connect to the Unity addon socket server"""
        if self.sock:
            return True

        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((self.host, self.port))
            logger.info(f"Connected to Unity at {self.host}:{self.port}")
            return True
        except Exception as e:
            logger.error(f"Failed to connect to Unity: {str(e)}")
            self.sock = None
            return False

    def disconnect(self):
        """Disconnect from the Unity addon"""
        if self.sock:
            try:
                self.sock.close()
            except Exception as e:
                logger.error(f"Error disconnecting from Unity: {str(e)}")
            finally:
                self.sock = None

    def receive_full_response(self, sock, buffer_size=8192):
        """Receive the complete response, potentially in multiple chunks"""
        chunks = []
        # Use a consistent timeout value that matches the addon's timeout
        sock.settimeout(15.0)  # Match the addon's timeout

        try:
            while True:
                try:
                    chunk = sock.recv(buffer_size)
                    if not chunk:
                        # If we get an empty chunk, the connection might be closed
                        if (
                            not chunks
                        ):  # If we haven't received anything yet, this is an error
                            raise Exception(
                                "Connection closed before receiving any data"
                            )
                        break

                    chunks.append(chunk)

                    # Check if we've received a complete JSON object
                    try:
                        data = b"".join(chunks)
                        json.loads(data.decode("utf-8"))
                        # If we get here, it parsed successfully
                        logger.info(f"Received complete response ({len(data)} bytes)")
                        return data
                    except json.JSONDecodeError:
                        # Incomplete JSON, continue receiving
                        continue
                except socket.timeout:
                    # If we hit a timeout during receiving, break the loop and try to use what we have
                    logger.warning("Socket timeout during chunked receive")
                    break
                except (ConnectionError, BrokenPipeError, ConnectionResetError) as e:
                    logger.error(f"Socket connection error during receive: {str(e)}")
                    raise  # Re-raise to be handled by the caller
        except socket.timeout:
            logger.warning("Socket timeout during chunked receive")
        except Exception as e:
            logger.error(f"Error during receive: {str(e)}")
            raise

        # If we get here, we either timed out or broke out of the loop
        # Try to use what we have
        if chunks:
            data = b"".join(chunks)
            logger.info(f"Returning data after receive completion ({len(data)} bytes)")
            try:
                # Try to parse what we have
                json.loads(data.decode("utf-8"))
                return data
            except json.JSONDecodeError:
                # If we can't parse it, it's incomplete
                raise Exception("Incomplete JSON response received")
        else:
            raise Exception("No data received")

    def send_command(
        self, command_type: str, params: Dict[str, Any] = None
    ) -> Dict[str, Any]:
        """Send a command to Unity and return the response"""
        if not self.sock and not self.connect():
            raise ConnectionError("Not connected to Unity")

        command = {"type": command_type, "params": params or {}}

        try:
            # Log the command being sent
            logger.info(f"Sending command: {command_type} with params: {params}")

            # Send the command
            self.sock.sendall(json.dumps(command).encode("utf-8"))
            logger.info(f"Command sent, waiting for response...")

            # Set a timeout for receiving - use the same timeout as in receive_full_response
            self.sock.settimeout(15.0)  # Match the addon's timeout

            # Receive the response using the improved receive_full_response method
            response_data = self.receive_full_response(self.sock)
            logger.info(f"Received {len(response_data)} bytes of data")

            response = json.loads(response_data.decode("utf-8"))
            logger.info(f"Response parsed, status: {response.get('status', 'unknown')}")

            if response.get("status") == "error":
                logger.error(f"Unity error: {response.get('message')}")
                raise Exception(response.get("message", "Unknown error from Unity"))

            return response.get("result", {})
        except socket.timeout:
            logger.error("Socket timeout while waiting for response from Unity")
            # Don't try to reconnect here - let the get_unity_connection handle reconnection
            # Just invalidate the current socket so it will be recreated next time
            self.sock = None
            raise Exception(
                "Timeout waiting for Unity response - try simplifying your request"
            )
        except (ConnectionError, BrokenPipeError, ConnectionResetError) as e:
            logger.error(f"Socket connection error: {str(e)}")
            self.sock = None
            raise Exception(f"Connection to Unity lost: {str(e)}")
        except json.JSONDecodeError as e:
            logger.error(f"Invalid JSON response from Unity: {str(e)}")
            # Try to log what was received
            if "response_data" in locals() and response_data:
                logger.error(f"Raw response (first 200 bytes): {response_data[:200]}")
            raise Exception(f"Invalid response from Unity: {str(e)}")
        except Exception as e:
            logger.error(f"Error communicating with Unity: {str(e)}")
            # Don't try to reconnect here - let the get_unity_connection handle reconnection
            self.sock = None
            raise Exception(f"Communication error with Unity: {str(e)}")


@asynccontextmanager
async def server_lifespan(server: FastMCP) -> AsyncIterator[Dict[str, Any]]:
    """Manage server startup and shutdown lifecycle"""
    # We don't need to create a connection here since we're using the global connection
    # for resources and tools

    try:
        # Just log that we're starting up
        logger.info("UnityMCP server starting up")

        # Try to connect to Unity on startup to verify it's available
        try:
            # This will initialize the global connection if needed
            unity = get_unity_connection()
            logger.info("Successfully connected to Unity on startup")
        except Exception as e:
            logger.warning(f"Could not connect to Unity on startup: {str(e)}")
            logger.warning(
                "Make sure the Unity addon is running before using Unity resources or tools"
            )

        # Return an empty context - we're using the global connection
        yield {}
    finally:
        # Clean up the global connection on shutdown
        global _unity_connection
        if _unity_connection:
            logger.info("Disconnecting from Unity on shutdown")
            _unity_connection.disconnect()
            _unity_connection = None
        logger.info("UnityMCP server shut down")


# Resource endpoints

# Global connection for resources (since resources can't access context)
_unity_connection = None


def get_unity_connection():
    """Get or create a persistent Unity connection"""
    global _unity_connection

    # If we have an existing connection, check if it's still valid
    if _unity_connection is not None:
        try:
            # Test the connection by sending a ping command
            response = _unity_connection.send_command("ping")
            if response.get("status") == "ok":
                return _unity_connection
            else:
                logger.warning("Connection test failed - invalid response")
                _unity_connection.disconnect()
                _unity_connection = None
        except Exception as e:
            # Connection is dead, close it and create a new one
            logger.warning(f"Existing connection is no longer valid: {str(e)}")
            try:
                _unity_connection.disconnect()
            except:
                pass
            _unity_connection = None

    # Create a new connection if needed
    if _unity_connection is None:
        _unity_connection = UnityConnection(host="localhost", port=9876)
        if not _unity_connection.connect():
            logger.error("Failed to connect to Unity")
            _unity_connection = None
            raise Exception(
                "Could not connect to Unity. Make sure the Unity addon is running."
            )
        logger.info("Created new persistent connection to Unity")

    return _unity_connection


@mcp.tool()
async def get_scene_context() -> str:
    """
    Extract the current Unity scene context including hierarchy, selected objects, and settings.

    Returns:
        A JSON string containing the scene context information.
    """
    logger.info("Extracting scene context from Unity...")

    try:
        # get the global connection
        unity = get_unity_connection()

        result = unity.send_command("get_scene_context")
        return f"Scene context extracted successfully: {result.get('result', '')}"
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
    logger.info("Executing code in Unity...")

    try:
        # get the global connection
        unity = get_unity_connection()

        result = unity.send_command("execute_unity_code", {"code": code})
        return f"Code executed successfully: {result.get('result', '')}"
    except Exception as e:
        logger.error(f"Error executing code: {str(e)}")
        return f"Error executing code: {str(e)}"


# Main execution


def main():
    """Run the MCP server"""
    mcp.run()


if __name__ == "__main__":
    main()
