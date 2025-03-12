using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace GameGenie
{
    public class SceneContextExtractor
    {
        private const int MaxComponentProperties = 10;
        private const int MaxHierarchyDepth = 3;
        
        public string ExtractSceneContext()
        {
            StringBuilder context = new StringBuilder();
            
            try
            {
                // Get basic scene information
                ExtractSceneInfo(context);
                
                // Get selected objects information
                ExtractSelectedObjects(context);
                
                // Get scene hierarchy (limited depth)
                ExtractSceneHierarchy(context);
                
                // Get project settings information
                ExtractProjectSettings(context);
            }
            catch (Exception ex)
            {
                context.AppendLine($"Error extracting scene context: {ex.Message}");
                Debug.LogError($"Game Genie Error: {ex.Message}");
            }
            
            return context.ToString();
        }
        
        private void ExtractSceneInfo(StringBuilder context)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            
            context.AppendLine("## SCENE INFORMATION");
            context.AppendLine($"Name: {activeScene.name}");
            context.AppendLine($"Path: {activeScene.path}");
            context.AppendLine($"Is Loaded: {activeScene.isLoaded}");
            context.AppendLine($"Root Count: {activeScene.rootCount}");
            
            // Get lighting settings
            RenderSettings renderSettings = RenderSettings.GetRenderSettings();
            context.AppendLine("Lighting:");
            context.AppendLine($"  Ambient Mode: {RenderSettings.ambientMode}");
            context.AppendLine($"  Fog Enabled: {RenderSettings.fog}");
            if (RenderSettings.fog)
            {
                context.AppendLine($"  Fog Color: {RenderSettings.fogColor}");
                context.AppendLine($"  Fog Density: {RenderSettings.fogDensity}");
            }
            
            context.AppendLine();
        }
        
        private void ExtractSelectedObjects(StringBuilder context)
        {
            UnityEngine.Object[] selectedObjects = Selection.objects;
            
            context.AppendLine("## SELECTED OBJECTS");
            context.AppendLine($"Count: {selectedObjects.Length}");
            
            if (selectedObjects.Length == 0)
            {
                context.AppendLine("No objects selected.");
                context.AppendLine();
                return;
            }
            
            foreach (UnityEngine.Object obj in selectedObjects)
            {
                if (obj is GameObject gameObject)
                {
                    context.AppendLine($"GameObject: {gameObject.name}");
                    context.AppendLine($"  Active: {gameObject.activeSelf}");
                    context.AppendLine($"  Layer: {LayerMask.LayerToName(gameObject.layer)}");
                    context.AppendLine($"  Tag: {gameObject.tag}");
                    context.AppendLine($"  Position: {gameObject.transform.position}");
                    context.AppendLine($"  Rotation: {gameObject.transform.eulerAngles}");
                    context.AppendLine($"  Scale: {gameObject.transform.localScale}");
                    
                    // Get components
                    Component[] components = gameObject.GetComponents<Component>();
                    context.AppendLine($"  Components ({components.Length}):");
                    
                    foreach (Component component in components)
                    {
                        if (component == null) continue;
                        
                        context.AppendLine($"    - {component.GetType().Name}");
                        
                        // Extract some common component properties
                        ExtractComponentProperties(context, component);
                    }
                    
                    context.AppendLine();
                }
                else
                {
                    context.AppendLine($"Asset: {obj.name} (Type: {obj.GetType().Name})");
                    context.AppendLine();
                }
            }
            
            context.AppendLine();
        }
        
        private void ExtractComponentProperties(StringBuilder context, Component component)
        {
            try
            {
                if (component is Transform transform)
                {
                    // Transform properties already extracted
                }
                else if (component is Renderer renderer)
                {
                    context.AppendLine($"      Material Count: {renderer.sharedMaterials.Length}");
                    for (int i = 0; i < renderer.sharedMaterials.Length && i < 3; i++)
                    {
                        if (renderer.sharedMaterials[i] != null)
                        {
                            context.AppendLine($"      Material {i}: {renderer.sharedMaterials[i].name}");
                        }
                    }
                    context.AppendLine($"      Cast Shadows: {renderer.shadowCastingMode}");
                    context.AppendLine($"      Receive Shadows: {renderer.receiveShadows}");
                }
                else if (component is Collider collider)
                {
                    context.AppendLine($"      Is Trigger: {collider.isTrigger}");
                    context.AppendLine($"      Enabled: {collider.enabled}");
                    
                    if (collider is BoxCollider boxCollider)
                    {
                        context.AppendLine($"      Size: {boxCollider.size}");
                        context.AppendLine($"      Center: {boxCollider.center}");
                    }
                    else if (collider is SphereCollider sphereCollider)
                    {
                        context.AppendLine($"      Radius: {sphereCollider.radius}");
                        context.AppendLine($"      Center: {sphereCollider.center}");
                    }
                    else if (collider is CapsuleCollider capsuleCollider)
                    {
                        context.AppendLine($"      Radius: {capsuleCollider.radius}");
                        context.AppendLine($"      Height: {capsuleCollider.height}");
                        context.AppendLine($"      Center: {capsuleCollider.center}");
                    }
                }
                else if (component is MeshFilter meshFilter)
                {
                    if (meshFilter.sharedMesh != null)
                    {
                        context.AppendLine($"      Mesh: {meshFilter.sharedMesh.name}");
                        context.AppendLine($"      Vertices: {meshFilter.sharedMesh.vertexCount}");
                        context.AppendLine($"      Triangles: {meshFilter.sharedMesh.triangles.Length / 3}");
                    }
                }
                else if (component is Light light)
                {
                    context.AppendLine($"      Type: {light.type}");
                    context.AppendLine($"      Color: {light.color}");
                    context.AppendLine($"      Intensity: {light.intensity}");
                    context.AppendLine($"      Range: {light.range}");
                    context.AppendLine($"      Shadows: {light.shadows}");
                }
                else if (component is Camera camera)
                {
                    context.AppendLine($"      Clear Flags: {camera.clearFlags}");
                    context.AppendLine($"      Background: {camera.backgroundColor}");
                    context.AppendLine($"      Field of View: {camera.fieldOfView}");
                    context.AppendLine($"      Near Clip Plane: {camera.nearClipPlane}");
                    context.AppendLine($"      Far Clip Plane: {camera.farClipPlane}");
                }
                else if (component is AudioSource audioSource)
                {
                    context.AppendLine($"      Clip: {(audioSource.clip != null ? audioSource.clip.name : "None")}");
                    context.AppendLine($"      Volume: {audioSource.volume}");
                    context.AppendLine($"      Pitch: {audioSource.pitch}");
                    context.AppendLine($"      Loop: {audioSource.loop}");
                    context.AppendLine($"      Play On Awake: {audioSource.playOnAwake}");
                }
                else if (component is Rigidbody rigidbody)
                {
                    context.AppendLine($"      Mass: {rigidbody.mass}");
                    context.AppendLine($"      Drag: {rigidbody.drag}");
                    context.AppendLine($"      Use Gravity: {rigidbody.useGravity}");
                    context.AppendLine($"      Is Kinematic: {rigidbody.isKinematic}");
                    context.AppendLine($"      Interpolation: {rigidbody.interpolation}");
                    context.AppendLine($"      Collision Detection: {rigidbody.collisionDetectionMode}");
                }
                else if (component is Rigidbody2D rigidbody2D)
                {
                    context.AppendLine($"      Mass: {rigidbody2D.mass}");
                    context.AppendLine($"      Drag: {rigidbody2D.drag}");
                    context.AppendLine($"      Gravity Scale: {rigidbody2D.gravityScale}");
                    context.AppendLine($"      Body Type: {rigidbody2D.bodyType}");
                    context.AppendLine($"      Interpolation: {rigidbody2D.interpolation}");
                    context.AppendLine($"      Collision Detection: {rigidbody2D.collisionDetectionMode}");
                }
                else if (component is Animator animator)
                {
                    context.AppendLine($"      Controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "None")}");
                    context.AppendLine($"      Avatar: {(animator.avatar != null ? animator.avatar.name : "None")}");
                    context.AppendLine($"      Apply Root Motion: {animator.applyRootMotion}");
                    context.AppendLine($"      Update Mode: {animator.updateMode}");
                    context.AppendLine($"      Culling Mode: {animator.cullingMode}");
                }
                // Add more component types as needed
            }
            catch (Exception ex)
            {
                context.AppendLine($"      Error extracting properties: {ex.Message}");
            }
        }
        
        private void ExtractSceneHierarchy(StringBuilder context)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            
            context.AppendLine("## SCENE HIERARCHY (Limited Depth)");
            
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            context.AppendLine($"Root Objects: {rootObjects.Length}");
            
            foreach (GameObject rootObject in rootObjects)
            {
                ExtractGameObjectHierarchy(context, rootObject, 0);
            }
            
            context.AppendLine();
        }
        
        private void ExtractGameObjectHierarchy(StringBuilder context, GameObject gameObject, int depth)
        {
            if (depth > MaxHierarchyDepth)
            {
                return;
            }
            
            string indent = new string(' ', depth * 2);
            context.AppendLine($"{indent}- {gameObject.name} [{gameObject.GetComponents<Component>().Length} components]");
            
            // Extract children
            foreach (Transform child in gameObject.transform)
            {
                ExtractGameObjectHierarchy(context, child.gameObject, depth + 1);
            }
        }
        
        private void ExtractProjectSettings(StringBuilder context)
        {
            context.AppendLine("## PROJECT SETTINGS");
            
            // Player settings
            context.AppendLine("Player Settings:");
            context.AppendLine($"  Company Name: {PlayerSettings.companyName}");
            context.AppendLine($"  Product Name: {PlayerSettings.productName}");
            context.AppendLine($"  Version: {PlayerSettings.bundleVersion}");
            context.AppendLine($"  Default Orientation: {PlayerSettings.defaultInterfaceOrientation}");
            
            // Graphics settings
            context.AppendLine("Graphics Settings:");
            context.AppendLine($"  Color Space: {PlayerSettings.colorSpace}");
            context.AppendLine($"  Graphics API: {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))}");
            
            // Physics settings
            context.AppendLine("Physics Settings:");
            context.AppendLine($"  Gravity: {Physics.gravity}");
            context.AppendLine($"  Default Contact Offset: {Physics.defaultContactOffset}");
            context.AppendLine($"  Bounce Threshold: {Physics.bounceThreshold}");
            
            // Quality settings
            context.AppendLine("Quality Settings:");
            string[] qualityNames = QualitySettings.names;
            context.AppendLine($"  Quality Levels: {string.Join(", ", qualityNames)}");
            context.AppendLine($"  Current Level: {QualitySettings.GetQualityLevel()} ({qualityNames[QualitySettings.GetQualityLevel()]})");
            
            context.AppendLine();
        }
    }
} 