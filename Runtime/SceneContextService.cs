// This is a service that provides the context of the current scene to the Unity editor
// It is responsible for fetching the scene context from the server and updating the scene context
// It is also responsible for updating the scene context when the user makes changes in the Unity editor

using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GameGenieUnity
{
    public class SceneContextService : MonoBehaviour
    {

        public static string GetSceneFile()
        {
            string sceneFilePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            string[] contents = File.ReadAllLines(sceneFilePath);
            string sceneContent = string.Join("\n", contents);
            return sceneContent;
        }

        public static string ReadFile(string relativePath)
        {
            // Remove "Assets/" from the relative path (if it's there) and combine with Application.dataPath to get a full path
            string relativeSubPath = relativePath.StartsWith("Assets/") ? relativePath.Substring("Assets/".Length) : relativePath;
            string fullPath = Path.Combine(Application.dataPath, relativeSubPath);
            string[] contents = File.ReadAllLines(fullPath);
            string fileContent = string.Join("\n", contents);
            return fileContent;
        }

        public static object GetSceneContext()
        {
            try
            {
                var activeGameObjects = new List<string>();
                var selectedObjects = new List<string>();

                // Use FindObjectsByType instead of FindObjectsOfType
                var foundObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                if (foundObjects != null)
                {
                    foreach (var obj in foundObjects)
                    {
                        if (obj != null && !string.IsNullOrEmpty(obj.name))
                        {
                            activeGameObjects.Add(obj.name);
                        }
                    }
                }

                var selection = Selection.gameObjects;
                if (selection != null)
                {
                    foreach (var obj in selection)
                    {
                        if (obj != null && !string.IsNullOrEmpty(obj.name))
                        {
                            selectedObjects.Add(obj.name);
                        }
                    }
                }

                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var sceneHierarchy = currentScene.IsValid() ? GetSceneHierarchy() : new List<object>();

                var projectStructure = new
                {
                    scenes = GetSceneNames() ?? new string[0],
                    prefabs = GetPrefabPaths() ?? new string[0],
                    scripts = GetScriptPaths() ?? new string[0]
                };

                return new
                {
                    activeGameObjects,
                    selectedObjects,
                    // playModeState = EditorApplication.isPlaying ? "Playing" : "Stopped",
                    sceneHierarchy,
                    projectStructure
                };
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Error getting editor state: {e.Message}");
                return new
                {
                    activeGameObjects = new List<string>(),
                    selectedObjects = new List<string>(),
                    playModeState = "Unknown",
                    sceneHierarchy = new List<object>(),
                    projectStructure = new { scenes = new string[0], prefabs = new string[0], scripts = new string[0] }
                };
            }
        }

        private static object GetSceneHierarchy()
        {
            try
            {
                var roots = new List<object>();
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

                if (scene.IsValid())
                {
                    var rootObjects = scene.GetRootGameObjects();
                    if (rootObjects != null)
                    {
                        foreach (var root in rootObjects)
                        {
                            if (root != null)
                            {
                                try
                                {
                                    roots.Add(GetGameObjectHierarchy(root));
                                }
                                catch (Exception e)
                                {
                                    Logger.AddToLog($"[UnityMCP] Failed to get hierarchy for {root.name}: {e.Message}");
                                }
                            }
                        }
                    }
                }

                return roots;
            }
            catch (Exception e)
            {
                Logger.AddToLog($"Error getting scene hierarchy: {e.Message}");
                return new List<object>();
            }
        }

        private static object GetGameObjectHierarchy(GameObject obj)
        {
            try
            {
                if (obj == null) return null;

                var children = new List<object>();
                var transform = obj.transform;

                if (transform != null)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        try
                        {
                            var childTransform = transform.GetChild(i);
                            if (childTransform != null && childTransform.gameObject != null)
                            {
                                var childHierarchy = GetGameObjectHierarchy(childTransform.gameObject);
                                if (childHierarchy != null)
                                {
                                    children.Add(childHierarchy);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.AddToLog($"[UnityMCP] Failed to process child {i} of {obj.name}: {e.Message}");
                        }
                    }
                }

                return new
                {
                    name = obj.name ?? "Unnamed",
                    components = GetComponentNames(obj),
                    children = children
                };
            }
            catch (Exception e)
            {
                Logger.AddToLog($"[UnityMCP] Failed to get hierarchy for {(obj != null ? obj.name : "null")}: {e.Message}");
                return null;
            }
        }

        private static string[] GetComponentNames(GameObject obj)
        {
            try
            {
                if (obj == null) return new string[0];

                var components = obj.GetComponents<Component>();
                if (components == null) return new string[0];

                var validComponents = new List<string>();
                foreach (var component in components)
                {
                    try
                    {
                        if (component != null)
                        {
                            validComponents.Add(component.GetType().Name);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.AddToLog($"[UnityMCP] Failed to get component name: {e.Message}");
                    }
                }

                return validComponents.ToArray();
            }
            catch (Exception e)
            {
                Logger.AddToLog($"[UnityMCP] Failed to get component names for {(obj != null ? obj.name : "null")}: {e.Message}");
                return new string[0];
            }
        }

        private static object GetProjectStructure()
        {
            // Simplified project assets structure
            return new
            {
                scenes = GetSceneNames(),
                prefabs = GetPrefabPaths(),
                scripts = GetScriptPaths()
            };
        }

        private static string[] GetSceneNames()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        private static string[] GetPrefabPaths()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                // Only add prefabs that have paths starting in the Assets/ folder (ignore built-in prefabs)
                if (AssetDatabase.GUIDToAssetPath(guids[i]).StartsWith("Assets/"))
                    paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
                else
                    paths[i] = null;
            }

            // Clear all null values in the paths array
            paths = paths.Where(x => x != null).ToArray();

            return paths;
        }

        private static string[] GetScriptPaths()
        {
            var guids = AssetDatabase.FindAssets("t:Script");
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                // Only add scripts that have paths starting in the Assets/ folder (ignore built-in scripts)
                if (AssetDatabase.GUIDToAssetPath(guids[i]).StartsWith("Assets/"))
                    paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
                else
                    paths[i] = null;
            }

            // Clear all null values in the paths array
            paths = paths.Where(x => x != null).ToArray();

            return paths;
        }
    }
}