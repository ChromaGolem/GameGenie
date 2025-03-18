using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameGenie
{
    public class SceneContextExtractor
    {
        private const int MaxComponentProperties = 10;
        private const int MaxHierarchyDepth = 3;
        
        [MenuItem("GameGenie/ExtractSceneContextToFile")]
        public static void ExtractSceneContextToFile()
        {
            try
            {
                SceneContextExtractor extractor = new SceneContextExtractor();
                var contextData = extractor.ExtractSceneContextAsJson();
                
                // Ensure the Temp directory exists
                string tempDir = Path.Combine(Application.dataPath, "..", "Temp");
                Directory.CreateDirectory(tempDir);
                
                // Write to file
                string outputPath = Path.Combine(tempDir, "scene_context.json");
                File.WriteAllText(outputPath, JsonConvert.SerializeObject(contextData, Formatting.Indented));
                
                Debug.Log($"Scene context extracted to {outputPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error extracting scene context: {ex.Message}");
            }
        }
        
        public Dictionary<string, object> ExtractSceneContextAsJson()
        {
            var contextData = new Dictionary<string, object>();
            
            try
            {
                // Get basic scene information
                contextData["scene_info"] = ExtractSceneInfo();
                
                // Get selected objects information
                contextData["selected_objects"] = ExtractSelectedObjects();
                
                // Get scene hierarchy (limited depth)
                contextData["scene_hierarchy"] = ExtractSceneHierarchy();
                
                // Get project settings information
                contextData["project_settings"] = ExtractProjectSettings();
                
                // Add metadata
                contextData["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                contextData["unity_version"] = Application.unityVersion;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game Genie Error: {ex.Message}");
                contextData["error"] = ex.Message;
            }
            
            return contextData;
        }
        
        private Dictionary<string, object> ExtractSceneInfo()
        {
            var sceneInfo = new Dictionary<string, object>();
            Scene activeScene = SceneManager.GetActiveScene();
            
            sceneInfo["name"] = activeScene.name;
            sceneInfo["path"] = activeScene.path;
            sceneInfo["is_loaded"] = activeScene.isLoaded;
            sceneInfo["root_count"] = activeScene.rootCount;
            
            // Get lighting settings
            var lightingInfo = new Dictionary<string, object>();
            lightingInfo["ambient_mode"] = RenderSettings.ambientMode.ToString();
            lightingInfo["fog_enabled"] = RenderSettings.fog;
            
            if (RenderSettings.fog)
            {
                lightingInfo["fog_color"] = RenderSettings.fogColor.ToString();
                lightingInfo["fog_density"] = RenderSettings.fogDensity;
                lightingInfo["fog_mode"] = RenderSettings.fogMode.ToString();
            }
            
            // Get URP settings if available
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null)
            {
                lightingInfo["render_pipeline"] = "Universal Render Pipeline";
                lightingInfo["render_scale"] = urpAsset.renderScale;
                lightingInfo["shadow_distance"] = urpAsset.shadowDistance;
            }
            
            sceneInfo["lighting"] = lightingInfo;
            
            return sceneInfo;
        }
        
        private List<Dictionary<string, object>> ExtractSelectedObjects()
        {
            var selectedObjects = new List<Dictionary<string, object>>();
            UnityEngine.Object[] selection = Selection.objects;
            
            foreach (UnityEngine.Object obj in selection)
            {
                if (obj is GameObject gameObject)
                {
                    var objInfo = new Dictionary<string, object>();
                    objInfo["name"] = gameObject.name;
                    objInfo["type"] = "GameObject";
                    objInfo["active"] = gameObject.activeSelf;
                    objInfo["layer"] = LayerMask.LayerToName(gameObject.layer);
                    objInfo["tag"] = gameObject.tag;
                    
                    // Transform info
                    var transformInfo = new Dictionary<string, object>();
                    transformInfo["position"] = new float[] { 
                        gameObject.transform.position.x,
                        gameObject.transform.position.y,
                        gameObject.transform.position.z
                    };
                    transformInfo["rotation"] = new float[] {
                        gameObject.transform.eulerAngles.x,
                        gameObject.transform.eulerAngles.y,
                        gameObject.transform.eulerAngles.z
                    };
                    transformInfo["scale"] = new float[] {
                        gameObject.transform.localScale.x,
                        gameObject.transform.localScale.y,
                        gameObject.transform.localScale.z
                    };
                    objInfo["transform"] = transformInfo;
                    
                    // Components
                    var components = new List<Dictionary<string, object>>();
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        
                        var componentInfo = new Dictionary<string, object>();
                        componentInfo["type"] = component.GetType().Name;
                        componentInfo["properties"] = ExtractComponentProperties(component);
                        components.Add(componentInfo);
                    }
                    objInfo["components"] = components;
                    
                    selectedObjects.Add(objInfo);
                }
                else
                {
                    var assetInfo = new Dictionary<string, object>();
                    assetInfo["name"] = obj.name;
                    assetInfo["type"] = obj.GetType().Name;
                    selectedObjects.Add(assetInfo);
                }
            }
            
            return selectedObjects;
        }
        
        private Dictionary<string, object> ExtractComponentProperties(Component component)
        {
            var properties = new Dictionary<string, object>();
            
            try
            {
                if (component is Renderer renderer)
                {
                    properties["material_count"] = renderer.sharedMaterials.Length;
                    properties["materials"] = renderer.sharedMaterials
                        .Take(3)
                        .Where(m => m != null)
                        .Select(m => m.name)
                        .ToList();
                    properties["cast_shadows"] = renderer.shadowCastingMode.ToString();
                    properties["receive_shadows"] = renderer.receiveShadows;
                }
                else if (component is Collider collider)
                {
                    properties["is_trigger"] = collider.isTrigger;
                    properties["enabled"] = collider.enabled;
                    
                    if (collider is BoxCollider boxCollider)
                    {
                        properties["size"] = new float[] {
                            boxCollider.size.x,
                            boxCollider.size.y,
                            boxCollider.size.z
                        };
                        properties["center"] = new float[] {
                            boxCollider.center.x,
                            boxCollider.center.y,
                            boxCollider.center.z
                        };
                    }
                    // ... Add other collider types as needed
                }
                // ... Add other component types as needed
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error extracting properties for {component.GetType().Name}: {ex.Message}");
            }
            
            return properties;
        }
        
        private List<Dictionary<string, object>> ExtractSceneHierarchy()
        {
            var hierarchy = new List<Dictionary<string, object>>();
            Scene activeScene = SceneManager.GetActiveScene();
            
            foreach (GameObject rootObject in activeScene.GetRootGameObjects())
            {
                hierarchy.Add(ExtractGameObjectHierarchy(rootObject, 0));
            }
            
            return hierarchy;
        }
        
        private Dictionary<string, object> ExtractGameObjectHierarchy(GameObject gameObject, int depth)
        {
            if (depth >= MaxHierarchyDepth) return null;
            
            var hierarchyInfo = new Dictionary<string, object>();
            hierarchyInfo["name"] = gameObject.name;
            hierarchyInfo["active"] = gameObject.activeSelf;
            
            var children = new List<Dictionary<string, object>>();
            foreach (Transform child in gameObject.transform)
            {
                var childInfo = ExtractGameObjectHierarchy(child.gameObject, depth + 1);
                if (childInfo != null)
                {
                    children.Add(childInfo);
                }
            }
            
            if (children.Count > 0)
            {
                hierarchyInfo["children"] = children;
            }
            
            return hierarchyInfo;
        }
        
        private Dictionary<string, object> ExtractProjectSettings()
        {
            var settings = new Dictionary<string, object>();
            
            // Add relevant project settings here
            settings["physics_layer_names"] = GetPhysicsLayerNames();
            settings["tags"] = GetTags();
            
            return settings;
        }
        
        private List<string> GetPhysicsLayerNames()
        {
            var layerNames = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layerNames.Add(layerName);
                }
            }
            return layerNames;
        }
        
        private List<string> GetTags()
        {
            return UnityEditorInternal.InternalEditorUtility.tags.ToList();
        }
    }
} 