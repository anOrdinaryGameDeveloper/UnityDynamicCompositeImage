using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class DynamicAtlasImage : MonoBehaviour
{
    [SerializeField] private string atlasName = "UI";
    
    private Image imageComponent;
    private string spriteName;
    private Texture2D sourceTexture;
    private Coroutine loadCoroutine;
    private int lastAtlasVersion = 0;
    
    void Awake()
    {
        imageComponent = GetComponent<Image>();
    }
    
    void Start()
    {
        spriteName = imageComponent.sprite.name;
        sourceTexture = imageComponent.sprite.texture;
        
        if (sourceTexture != null && !string.IsNullOrEmpty(spriteName))
        {
            LoadSprite();
        }
        
        // 注册图集更新回调
        if (DynamicAtlasManager.Instance != null)
        {
            DynamicAtlasManager.Instance.RegisterAtlasUpdateCallback(atlasName, OnAtlasUpdated);
            lastAtlasVersion = DynamicAtlasManager.Instance.GetAtlasVersion(atlasName);
        }
    }
    
    void OnEnable()
    {
        // 检查图集是否已更新
        if (DynamicAtlasManager.Instance != null && 
            DynamicAtlasManager.Instance.GetAtlasVersion(atlasName) > lastAtlasVersion)
        {
            OnAtlasUpdated();
        }
    }
    
    void OnDestroy()
    {
        // 取消注册图集更新回调
        if (DynamicAtlasManager.Instance != null)
        {
            DynamicAtlasManager.Instance.UnregisterAtlasUpdateCallback(atlasName, OnAtlasUpdated);
        }
        
        // 停止可能的协程
        if (loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
        }
    }
    
    // 图集更新回调
    private void OnAtlasUpdated()
    {
        if (gameObject.activeInHierarchy)
        {
            LoadSprite();
        }
        lastAtlasVersion = DynamicAtlasManager.Instance.GetAtlasVersion(atlasName);
    }
    
    public void LoadSprite()
    {
        if (loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
        }
        loadCoroutine = StartCoroutine(LoadSpriteCoroutine());
    }
    
    private IEnumerator LoadSpriteCoroutine()
    {
        if (string.IsNullOrEmpty(spriteName) || sourceTexture == null)
        {
            yield break;
        }
        
        bool completed = false;
        Sprite resultSprite = null;
        
        // 先检查是否已有缓存的精灵
        Sprite cachedSprite = DynamicAtlasManager.Instance.GetSprite(spriteName);
        if (cachedSprite != null)
        {
            imageComponent.sprite = cachedSprite;
            lastAtlasVersion = DynamicAtlasManager.Instance.GetAtlasVersion(atlasName);
            yield break;
        }
        
        DynamicAtlasManager.Instance.AddTextureToAtlas(
            atlasName, 
            sourceTexture, 
            spriteName, 
            (sprite) =>
            {
                resultSprite = sprite;
                completed = true;
                
                // 直接在回调中设置纹理，确保及时更新
                if (imageComponent != null && resultSprite != null)
                {
                    imageComponent.sprite = resultSprite;
                    lastAtlasVersion = DynamicAtlasManager.Instance.GetAtlasVersion(atlasName);
                }
            });
        
        // 等待加载完成
        float timeout = Time.time + 10f; // 10秒超时
        while (!completed && Time.time < timeout)
        {
            // 同时检查图集是否仍在处理中
            if (!DynamicAtlasManager.Instance.IsAtlasProcessing(atlasName))
            {
                // 图集已处理完成，但我们的回调可能没被调用
                Sprite sprite = DynamicAtlasManager.Instance.GetSprite(spriteName);
                if (sprite != null)
                {
                    resultSprite = sprite;
                    completed = true;
                    imageComponent.sprite = resultSprite;
                    lastAtlasVersion = DynamicAtlasManager.Instance.GetAtlasVersion(atlasName);
                    break;
                }
            }
            yield return null;
        }
        
        if (!completed)
        {
            Debug.LogError("Failed to load sprite: " + spriteName + " (timeout)");
        }
    }
    
    // 设置纹理（可以从外部调用）
    public void SetTexture(Texture2D texture, string newSpriteName = null)
    {
        sourceTexture = texture;
        
        if (!string.IsNullOrEmpty(newSpriteName))
        {
            spriteName = newSpriteName;
        }
        else if (string.IsNullOrEmpty(spriteName) && texture != null)
        {
            spriteName = texture.name;
        }
        
        if (!string.IsNullOrEmpty(spriteName))
        {
            LoadSprite();
        }
    }
    
    // 设置精灵名称
    public void SetSpriteName(string newSpriteName)
    {
        if (spriteName != newSpriteName)
        {
            spriteName = newSpriteName;
            LoadSprite();
        }
    }
    
    // 在编辑器中设置纹理时自动设置精灵名称
    void OnValidate()
    {
        if (sourceTexture != null && string.IsNullOrEmpty(spriteName))
        {
            spriteName = sourceTexture.name;
        }
    }
}