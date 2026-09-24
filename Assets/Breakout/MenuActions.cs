using UnityEngine;

/// <summary>
/// Quit action for the start screen. Unity has no built-in component for this, so the Exit button
/// needs it. Scene loading is handled by the Unity Lab Scene Loader component instead.
/// </summary>
public class MenuActions : MonoBehaviour
{
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
