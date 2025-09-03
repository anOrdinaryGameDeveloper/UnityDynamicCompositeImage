using UnityEditor;
using UnityEngine;

public class AtlasDebugger : MonoBehaviour
{
    [SerializeField] private bool showDebugInfo = true;
    
    void OnGUI()
    {
        if (!showDebugInfo || DynamicAtlasManager.Instance == null) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("动态图集系统状态");
        GUILayout.Space(10);
        
        GUILayout.Label($"已加载图集: {DynamicAtlasManager.Instance.AtlasCount}");
        GUILayout.Label($"已合并精灵: {DynamicAtlasManager.Instance.SpriteCount}");
        
        GUILayout.Space(10);
        GUILayout.Label($"Draw Calls: {UnityStats.drawCalls}");
        GUILayout.Label($"SetPass Calls: {UnityStats.setPassCalls}");
        GUILayout.Label($"批处理次数: {UnityStats.batches}");
        
        GUILayout.EndArea();
    }
}