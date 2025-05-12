#if UNITY_EDITOR
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections;
#endif
#if UNITY_EDITOR
[CustomEditor(typeof(Tile))]
public class TileEditor : Editor 
{
    // Animation test parameters
    private float targetYPosition = 0.4f;
    private float duration = 1.2f;
    private Ease easeType = Ease.InOutQuad;
    private bool useShake = true;
    private float shakeStrength = 0.04f;
    private int shakeVibrato = 12;
    private bool shouldLoop = false;
    private int loopCount = 1;
    private LoopType loopType = LoopType.Restart;
    private float initialYPosition = 0.2f;
    
    // Liquid parameters
    private bool useRippleEffect = true;
    private float rippleStrength = 0.02f;
    private float rippleSpeed = 12f;
    private float bubblingStrength = 0.03f;
    private float surfaceTensionStrength = 0.06f;
    
    // Volume expansion parameters
    private bool useVolumeExpansion = true;
    private float expansionFactor = 1.1f;
    
    // Bubbling parameters
    private bool useBubbling = true;
    private int bubbleCount = 4;
    private float bubbleHeight = 0.05f;
    
    // Bouncy overflow parameters
    private bool useOverflowEffect = true;
    private float overflowAmount = 0.08f;
    
    // Save original values
    private Vector3 originalPosition;
    private Vector3 originalScale;
    private bool valuesInitialized = false;
    
    // Reference to the active sequence
    private Sequence activeSequence;
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        // Draw the default inspector
        DrawDefaultInspector();
        
        // Get reference to the Tile
        Tile tile = (Tile)target;
        
        if (!valuesInitialized && tile.waterTransform != null)
        {
            originalPosition = tile.waterTransform.localPosition;
            originalScale = tile.waterTransform.localScale;
            initialYPosition = originalPosition.y;
            valuesInitialized = true;
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Liquid Animation Tester", EditorStyles.boldLabel);
        
        // Core Animation Parameters
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Basic Parameters", EditorStyles.boldLabel);
        
        // Target Y Position
        targetYPosition = EditorGUILayout.Slider("Target Height", targetYPosition, 0.2f, 1.0f);
        
        // Duration
        duration = EditorGUILayout.Slider("Duration", duration, 0.1f, 5.0f);
        
        // Ease Type
        easeType = (Ease)EditorGUILayout.EnumPopup("Ease Type", easeType);
        EditorGUILayout.EndVertical();
        
        // Liquid Surface Effects
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Liquid Surface Effects", EditorStyles.boldLabel);
        
        // Ripple Effect
        useRippleEffect = EditorGUILayout.Toggle("Surface Ripples", useRippleEffect);
        if (useRippleEffect)
        {
            EditorGUI.indentLevel++;
            rippleStrength = EditorGUILayout.Slider("Ripple Strength", rippleStrength, 0.005f, 0.1f);
            rippleSpeed = EditorGUILayout.Slider("Ripple Speed", rippleSpeed, 1f, 20f);
            surfaceTensionStrength = EditorGUILayout.Slider("Surface Tension", surfaceTensionStrength, 0.01f, 0.2f);
            EditorGUI.indentLevel--;
        }
        
        // Bubbling
        useBubbling = EditorGUILayout.Toggle("Bubbling Effect", useBubbling);
        if (useBubbling)
        {
            EditorGUI.indentLevel++;
            bubbleCount = EditorGUILayout.IntSlider("Bubble Count", bubbleCount, 1, 10);
            bubbleHeight = EditorGUILayout.Slider("Bubble Height", bubbleHeight, 0.01f, 0.2f);
            bubblingStrength = EditorGUILayout.Slider("Bubbling Strength", bubblingStrength, 0.01f, 0.1f);
            EditorGUI.indentLevel--;
        }
        
        // Volume Expansion
        useVolumeExpansion = EditorGUILayout.Toggle("Volume Expansion", useVolumeExpansion);
        if (useVolumeExpansion)
        {
            EditorGUI.indentLevel++;
            expansionFactor = EditorGUILayout.Slider("Expansion Factor", expansionFactor, 1.0f, 1.3f);
            EditorGUI.indentLevel--;
        }
        
        // Overflow Effect
        useOverflowEffect = EditorGUILayout.Toggle("Overflow Effect", useOverflowEffect);
        if (useOverflowEffect)
        {
            EditorGUI.indentLevel++;
            overflowAmount = EditorGUILayout.Slider("Overflow Amount", overflowAmount, 0.01f, 0.2f);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
        
        // Shake Parameters
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Additional Effects", EditorStyles.boldLabel);
        
        useShake = EditorGUILayout.Toggle("Add Container Shake", useShake);
        
        if (useShake)
        {
            EditorGUI.indentLevel++;
            shakeStrength = EditorGUILayout.Slider("Shake Strength", shakeStrength, 0.01f, 0.2f);
            shakeVibrato = EditorGUILayout.IntSlider("Vibrato", shakeVibrato, 1, 20);
            EditorGUI.indentLevel--;
        }
        
        // Loop Parameters
        EditorGUILayout.Space();
        
        shouldLoop = EditorGUILayout.Toggle("Loop Animation", shouldLoop);
        
        if (shouldLoop)
        {
            EditorGUI.indentLevel++;
            loopCount = EditorGUILayout.IntField("Loop Count (0 = Infinite)", loopCount);
            loopType = (LoopType)EditorGUILayout.EnumPopup("Loop Type", loopType);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
        
        // Animation Buttons
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation Controls", EditorStyles.boldLabel);
        
        // Stop all animations
        if (GUILayout.Button("Stop All Animations"))
        {
            StopAllAnimations(tile);
        }
        
        // Create a horizontal layout for the main buttons
        EditorGUILayout.BeginHorizontal();
        
        // Water Flow Up - main liquid animation
        if (GUILayout.Button("Play Liquid Flow Up", GUILayout.Height(30)))
        {
            PlayLiquidFlowUpAnimation(tile);
        }
        
        // Water Flow Down
        if (GUILayout.Button("Liquid Flow Down", GUILayout.Height(30)))
        {
            PlayLiquidFlowDownAnimation(tile);
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Reset Button
        EditorGUILayout.Space();
        if (GUILayout.Button("Reset Water Position"))
        {
            if (tile.waterTransform != null)
            {
                StopAllAnimations(tile);
                tile.waterTransform.localPosition = originalPosition;
                tile.waterTransform.localScale = originalScale;
            }
        }
        
        // Set Initial Position
        if (GUILayout.Button("Set Current Y as Initial Position"))
        {
            if (tile.waterTransform != null)
            {
                initialYPosition = tile.waterTransform.localPosition.y;
            }
        }
        
        // Preview with scene view update
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Note: Animations will play in edit mode. Adjust parameters to create realistic liquid movement.", MessageType.Info);
        
        // Ensure the scene view updates to show the animation
        SceneView.RepaintAll();
        
        serializedObject.ApplyModifiedProperties();
    }
    
    private void StopAllAnimations(Tile tile)
    {
        if (tile.waterTransform != null)
        {
            // Kill any existing animations
            DOTween.Kill(tile.waterTransform);
            
            if (activeSequence != null)
            {
                activeSequence.Kill();
                activeSequence = null;
            }
        }
    }
    
    private void PlayLiquidFlowDownAnimation(Tile tile)
    {
        if (tile.waterTransform == null) return;
        
        // Stop any current animations
        StopAllAnimations(tile);
        
        // Create a new sequence
        activeSequence = DOTween.Sequence();
        
        // Add the main downward movement with a liquid-like ease
        activeSequence.Append(tile.waterTransform.DOLocalMoveY(initialYPosition, duration * 0.7f)
            .SetEase(Ease.InCubic));
        
        // Scale back to original size
        activeSequence.Join(tile.waterTransform.DOScale(originalScale, duration * 0.7f)
            .SetEase(Ease.InOutSine));
        
        // Ensure the animation updates in editor mode
        activeSequence.SetUpdate(true);
    }
    
    private void PlayLiquidFlowUpAnimation(Tile tile)
    {
        if (tile.waterTransform == null) return;
        
        // Stop any current animations
        StopAllAnimations(tile);
        
        // Create a new sequence
        activeSequence = DOTween.Sequence();
        
        // Store the starting position and scale
        Vector3 startPos = tile.waterTransform.localPosition;
        
        // 1. Initial slight compression before rising (like water gathering)
        if (useVolumeExpansion)
        {
            Vector3 compressedScale = originalScale;
            compressedScale.y *= 0.9f;
            compressedScale.x *= 1.05f;
            compressedScale.z *= 1.05f;
            
            activeSequence.Append(tile.waterTransform.DOScale(compressedScale, duration * 0.15f)
                .SetEase(Ease.OutQuad));
        }
        
        // 2. Main upward movement - this is the core rise
        Tweener mainRise = tile.waterTransform.DOLocalMoveY(targetYPosition - (useOverflowEffect ? 0 : overflowAmount), 
            duration * 0.6f)
            .SetEase(easeType);
        
        activeSequence.Append(mainRise);
        
        // 3. Volume expansion during the rise
        if (useVolumeExpansion)
        {
            Vector3 expandedScale = originalScale;
            expandedScale.y *= expansionFactor;
            expandedScale.x *= 1.0f;
            expandedScale.z *= 1.0f;
            
            activeSequence.Join(tile.waterTransform.DOScale(expandedScale, duration * 0.7f)
                .SetEase(Ease.OutQuad));
        }
        
        // 4. Overflow effect - the water rises a bit too high and then settles
        if (useOverflowEffect)
        {
            // Overshoot
            activeSequence.Append(tile.waterTransform.DOLocalMoveY(targetYPosition + overflowAmount, duration * 0.15f)
                .SetEase(Ease.OutQuad));
            
            // Settle back
            activeSequence.Append(tile.waterTransform.DOLocalMoveY(targetYPosition, duration * 0.25f)
                .SetEase(Ease.InOutSine));
        }
        
        // 5. Continuous ripple effect on the surface
        if (useRippleEffect)
        {
            // Create a custom callback for ripples on the surface using sine waves of different frequencies
            activeSequence.OnUpdate(() => {
                if (!DOTween.IsTweening(tile.waterTransform)) return;
                
                Vector3 currentPos = tile.waterTransform.localPosition;
                float time = Time.realtimeSinceStartup * rippleSpeed;
                
                // Use multiple sine waves of different frequencies for a more natural ripple
                float xRipple = 
                    Mathf.Sin(time) * rippleStrength * 0.7f + 
                    Mathf.Sin(time * 2.3f) * rippleStrength * 0.3f;
                
                float zRipple = 
                    Mathf.Cos(time * 1.3f) * rippleStrength * 0.5f + 
                    Mathf.Cos(time * 3.1f) * rippleStrength * 0.5f;
                
                // Apply surface tension effect - more movement when the water is higher
                float heightFactor = Mathf.Clamp01((currentPos.y - initialYPosition) / (targetYPosition - initialYPosition));
                float surfaceFactor = heightFactor * surfaceTensionStrength;
                
                // Apply the ripple effect to the current position
                tile.waterTransform.localPosition = new Vector3(
                    startPos.x + xRipple * (1f + surfaceFactor),
                    currentPos.y, // Keep Y as controlled by the tweens
                    startPos.z + zRipple * (1f + surfaceFactor)
                );
                
                // Subtle scale effect for ripples
                if (useVolumeExpansion && heightFactor > 0.7f)
                {
                    Vector3 currentScale = tile.waterTransform.localScale;
                    float scaleRipple = Mathf.Sin(time * 8f) * 0.01f * heightFactor;
                    
                    // Apply subtle scale ripples only when near the top
                    tile.waterTransform.localScale = new Vector3(
                        currentScale.x + scaleRipple,
                        currentScale.y - scaleRipple * 0.5f, // Conserve volume illusion
                        currentScale.z + scaleRipple
                    );
                }
            });
        }
        
        // 6. Add bubbling effect - small, quick movements upward
        if (useBubbling)
        {
            // Add random bubble movements throughout the animation
            for (int i = 0; i < bubbleCount; i++)
            {
                float startTime = duration * 0.1f + (duration * 0.6f * i / bubbleCount);
                float bubbleDuration = duration * 0.2f;
                
                // Randomize positions for bubbles
                float xPos = startPos.x + Random.Range(-bubblingStrength, bubblingStrength);
                float zPos = startPos.z + Random.Range(-bubblingStrength, bubblingStrength);
                
                // Create bubble effect as a quick Y movement
                float bubbleStart = Mathf.Lerp(initialYPosition, targetYPosition, i / (float)bubbleCount);
                
                // Insert bubble rise animation
                activeSequence.Insert(startTime, DOTween.To(() => 0f, x => {
                    // Only modify if still tweening (avoid errors if animation is stopped)
                    if (!DOTween.IsTweening(tile.waterTransform)) return;
                    
                    // Calculate current vertical position in the animation
                    float currentHeight = tile.waterTransform.localPosition.y;
                    float bubbleY = bubbleStart + x * bubbleHeight;
                    
                    // Only show bubble if it's below the current water level
                    if (bubbleY < currentHeight)
                    {
                        // Use to create small visual effects (could be shader effects in a real implementation)
                        // For editor preview, we'll do small position adjustments
                        float bubbleFactor = x * (1 - x) * 4; // Parabolic function, maximal at x=0.5
                        
                        // Apply bubble effect to water transform temporarily
                        tile.waterTransform.localPosition = new Vector3(
                            tile.waterTransform.localPosition.x + Random.Range(-0.005f, 0.005f) * bubbleFactor,
                            currentHeight,
                            tile.waterTransform.localPosition.z + Random.Range(-0.005f, 0.005f) * bubbleFactor
                        );
                    }
                }, 1f, bubbleDuration).SetEase(Ease.InQuad));
            }
        }
        
        // 7. Add horizontal container shake if enabled
        if (useShake)
        {
            // Create shake pattern divided through the animation for a more organic feel
            int shakeSegments = 3;
            float segmentDuration = duration / shakeSegments;
            
            for (int i = 0; i < shakeSegments; i++)
            {
                float shakeDelay = i * segmentDuration;
                float shakeDuration = segmentDuration * 0.8f;
                
                // Stronger shake during overflow and initial rising
                float strengthFactor = (i == 0 || i == shakeSegments - 1) ? 1.0f : 0.5f;
                
                // Add X shake (doesn't affect Y)
                activeSequence.Insert(shakeDelay, DOTween.Sequence()
                    .Append(tile.waterTransform.DOShakePosition(shakeDuration, 
                        new Vector3(shakeStrength * strengthFactor, 0, shakeStrength * strengthFactor), 
                        shakeVibrato, 90, false, false))
                    .SetEase(Ease.OutQuad));
            }
        }
        
        // Apply looping if needed
        if (shouldLoop)
        {
            activeSequence.SetLoops(loopCount, loopType);
        }
        
        // Ensure the animation updates in editor mode
        activeSequence.SetUpdate(true);
    }
}
#endif