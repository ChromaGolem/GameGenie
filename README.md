# Game Genie - AI-Powered Unity Scene Editor

Game Genie is a Unity editor extension that implements Model Context Protocol (MCP) to allow Claude AI to directly edit Unity scenes through a custom editor window. It enables developers to build functional prototypes of games with simple natural language prompts.

## Features

- **AI-Powered Scene Editing**: Describe what you want to create or modify in your scene, and Claude AI will generate the necessary code to make it happen.
- **Scene Context Extraction**: Automatically extracts the current scene hierarchy, selected objects, components, and project settings to provide context to the AI.
- **Code Preview and Execution**: Review and execute the generated code directly within the Unity editor.
- **Undo Support**: All changes made by Game Genie are registered with Unity's Undo system, so you can easily revert them if needed.
- **Safety Features**: Preview code before execution, error handling, and confirmation dialogs to prevent accidental changes.

## Installation

1. Clone this repository or download the latest release.
2. Copy the `Editor/GameGenie` folder into your Unity project's `Assets/Editor` directory.
3. Open Unity and navigate to `Window > Game Genie` to open the Game Genie window.

## Requirements

- Unity 2020.3 or later
- Claude API key (from Anthropic)
- Newtonsoft.Json package (can be installed via the Unity Package Manager)

## Usage

1. Open the Game Genie window from `Window > Game Genie`.
2. Enter your Claude API key in the settings section.
3. Type your request in the query field, describing what you want to create or modify in your scene.
4. Click "Send to Claude" to generate the code.
5. Review the generated code in the preview section.
6. Click "Execute Code" to apply the changes to your scene.

## Example Prompts

- "Create a simple 3D platformer level with platforms, collectibles, and a player character."
- "Add a day-night cycle system to my scene with dynamic lighting."
- "Create a 2D roguelike for mobile with custom characters, maps, enemies, animations and UI."
- "Set up a basic first-person controller with camera movement and jumping."
- "Create a particle system that simulates rain and thunder."

## How It Works

1. Game Genie extracts the current scene context, including hierarchy, selected objects, components, and project settings.
2. It sends this context along with your query to Claude AI using the Model Context Protocol.
3. Claude generates C# code that can be executed within the Unity editor to implement your request.
4. Game Genie parses the response, extracts the code blocks, and presents them for review.
5. When you execute the code, Game Genie creates a temporary script, compiles it, and runs it within the editor.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- [Anthropic](https://www.anthropic.com/) for the Claude AI model
- [Unity Technologies](https://unity.com/) for the Unity game engine
