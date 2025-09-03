using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using DaVikingCode.AssetPacker;
using System;
using System.IO;
using UnityEngine.Events;

public class DynamicAtlasManager : MonoBehaviour
{
    public static DynamicAtlasManager Instance { get; private set; }

    [System.Serializable]
    public class AtlasConfig
    {
        public int maxAtlasSize = 2048;
        public int minAtlasSize = 64;
        public int padding = 0;
        public bool useCache = false;
        public float pixelsPerUnit = 100.0f;
        public float batchDelay = 0.1f;
        public bool autoResizeAtlas = true;
        public SizeMode sizeMode = SizeMode.RectanglePowerOfTwo;
    }

    [SerializeField] private AtlasConfig config;

    private Dictionary<string, AssetPacker> atlasDictionary = new Dictionary<string, AssetPacker>();
    private Dictionary<string, Sprite> spriteMapping = new Dictionary<string, Sprite>();
    private Dictionary<string, List<Action<Sprite>>> pendingCallbacks = new Dictionary<string, List<Action<Sprite>>>();
    private Dictionary<string, bool> processingStatus = new Dictionary<string, bool>();
    private Dictionary<string, List<Action>> atlasUpdateCallbacks = new Dictionary<string, List<Action>>();
    private Dictionary<string, int> atlasVersions = new Dictionary<string, int>();
    private Dictionary<string, Coroutine> batchCoroutines = new Dictionary<string, Coroutine>();

    public int AtlasCount => atlasDictionary.Count;
    public int SpriteCount => spriteMapping.Count;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 添加纹理到指定图集 - 现在直接传递原始纹理
    public void AddTextureToAtlas(string atlasName, Texture2D texture, string spriteName, Action<Sprite> onComplete = null)
    {
        if (spriteMapping.ContainsKey(spriteName))
        {
            onComplete?.Invoke(spriteMapping[spriteName]);
            return;
        }

        // 如果图集不存在，创建它
        if (!atlasDictionary.ContainsKey(atlasName))
        {
            CreateNewAtlas(atlasName);
        }

        AssetPacker packer = atlasDictionary[atlasName];

        // 检查图集是否正在处理中
        bool isProcessing = processingStatus.ContainsKey(atlasName) && processingStatus[atlasName];

        if (isProcessing)
        {
            // 如果正在处理，注册回调等待完成
            if (onComplete != null)
            {
                if (!pendingCallbacks.ContainsKey(spriteName))
                    pendingCallbacks[spriteName] = new List<Action<Sprite>>();

                pendingCallbacks[spriteName].Add(onComplete);
            }
            
            return;
        }

        // 添加纹理数据到AssetPacker（直接传递原始纹理）
        packer.AddTextureData(texture, spriteName);

        // 启动或重启批处理延迟
        if (batchCoroutines.ContainsKey(atlasName))
        {
            StopCoroutine(batchCoroutines[atlasName]);
        }
        batchCoroutines[atlasName] = StartCoroutine(BatchProcessAtlas(packer, atlasName, spriteName, onComplete));
    }

    // 批处理图集
    private IEnumerator BatchProcessAtlas(AssetPacker packer, string atlasName, string spriteName, Action<Sprite> onComplete)
    {
        yield return new WaitForSeconds(config.batchDelay);
        
        // 标记为处理中
        processingStatus[atlasName] = true;

        // 处理纹理（AssetPacker负责合图逻辑）
        packer.ProcessTextures();

        // 等待处理完成
        bool isProcessing = true;
        int maxWaitTime = 100;
        int waitCount = 0;

        while (isProcessing && waitCount < maxWaitTime)
        {
            yield return new WaitForSeconds(0.1f);
            waitCount++;

            // 检查处理状态
            Sprite testSprite = packer.GetSprite(spriteName);
            if (testSprite != null)
            {
                isProcessing = false;
            }
        }

        // 标记为处理完成
        processingStatus[atlasName] = false;

        // 获取生成的精灵
        Sprite sprite = packer.GetSprite(spriteName);
        if (sprite != null)
        {
            spriteMapping[spriteName] = sprite;
            onComplete?.Invoke(sprite);
            
            // 处理所有等待的回调
            if (pendingCallbacks.ContainsKey(spriteName))
            {
                foreach (var callback in pendingCallbacks[spriteName])
                {
                    callback.Invoke(sprite);
                }
                pendingCallbacks.Remove(spriteName);
            }
            
            // 通知图集已更新
            NotifyAtlasUpdated(atlasName);
        }
        else
        {
            Debug.LogError($"Failed to generate sprite: {spriteName}");
        }
        
        // 移除批处理协程
        if (batchCoroutines.ContainsKey(atlasName))
        {
            batchCoroutines.Remove(atlasName);
        }
    }

    // 创建新图集
    private void CreateNewAtlas(string atlasName)
    {
        GameObject atlasObj = new GameObject("Atlas_" + atlasName);
        atlasObj.transform.SetParent(transform);

        AssetPacker packer = atlasObj.AddComponent<AssetPacker>();
        packer.pixelsPerUnit = config.pixelsPerUnit;
        packer.padding = config.padding;
        packer.sizeMode = config.sizeMode;
        packer.autoResizeAtlas = config.autoResizeAtlas;
        packer.maxAtlasSize = config.maxAtlasSize;
        packer.minAtlasSize = config.minAtlasSize;
        packer.useCache = config.useCache;
        packer.cacheName = atlasName;

        // 注册处理完成事件
        packer.OnProcessCompleted.AddListener(() => OnAtlasProcessCompleted(atlasName));

        atlasDictionary[atlasName] = packer;
        processingStatus[atlasName] = false;
            
        // 初始化版本号
        if (!atlasVersions.ContainsKey(atlasName))
            atlasVersions[atlasName] = 1;
        else
            atlasVersions[atlasName]++;
    }

    // 图集处理完成回调
    private void OnAtlasProcessCompleted(string atlasName)
    {
        if (atlasDictionary.ContainsKey(atlasName))
        {
            AssetPacker packer = atlasDictionary[atlasName];

            // 标记为处理完成
            processingStatus[atlasName] = false;

            // 更新精灵映射
            foreach (var spriteEntry in packer.SpriteRects)
            {
                string spriteName = spriteEntry.Key;
                Sprite sprite = packer.GetSprite(spriteName);
                
                if (sprite != null)
                {
                    spriteMapping[spriteName] = sprite;

                    // 处理所有等待的回调
                    if (pendingCallbacks.ContainsKey(spriteName))
                    {
                        foreach (var callback in pendingCallbacks[spriteName])
                        {
                            callback.Invoke(sprite);
                        }
                        pendingCallbacks.Remove(spriteName);
                    }
                }
            }
            
            // 通知图集已更新
            NotifyAtlasUpdated(atlasName);
        }
    }

    public Sprite GetSprite(string spriteName)
    {
        return spriteMapping.ContainsKey(spriteName) ? spriteMapping[spriteName] : null;
    }

    public bool IsAtlasProcessing(string atlasName)
    {
        return processingStatus.ContainsKey(atlasName) && processingStatus[atlasName];
    }

    // 注册图集更新回调
    public void RegisterAtlasUpdateCallback(string atlasName, Action callback)
    {
        if (!atlasUpdateCallbacks.ContainsKey(atlasName))
            atlasUpdateCallbacks[atlasName] = new List<Action>();
        
        atlasUpdateCallbacks[atlasName].Add(callback);
    }

    // 取消注册图集更新回调
    public void UnregisterAtlasUpdateCallback(string atlasName, Action callback)
    {
        if (atlasUpdateCallbacks.ContainsKey(atlasName))
        {
            atlasUpdateCallbacks[atlasName].Remove(callback);
            
            if (atlasUpdateCallbacks[atlasName].Count == 0)
            {
                atlasUpdateCallbacks.Remove(atlasName);
            }
        }
    }

    // 通知图集已更新
    private void NotifyAtlasUpdated(string atlasName)
    {
        if (atlasUpdateCallbacks.ContainsKey(atlasName))
        {
            foreach (var callback in atlasUpdateCallbacks[atlasName])
            {
                callback.Invoke();
            }
        }
    }

    public int GetAtlasVersion(string atlasName)
    {
        return atlasVersions.ContainsKey(atlasName) ? atlasVersions[atlasName] : 0;
    }
    
    public int GetAtlasSize(string atlasName)
    {
        if (atlasDictionary.ContainsKey(atlasName))
        {
            return atlasDictionary[atlasName].GetCurrentAtlasSize();
        }
        return 0;
    }

    // 释放图集资源
    public void ReleaseAtlas(string atlasName)
    {
        if (atlasDictionary.ContainsKey(atlasName))
        {
            AssetPacker packer = atlasDictionary[atlasName];

            // 移除精灵映射
            foreach (var spriteName in packer.SpriteRects.Keys)
            {
                if (spriteMapping.ContainsKey(spriteName))
                    spriteMapping.Remove(spriteName);
            }

            packer.Dispose();
            Destroy(packer.gameObject);
            atlasDictionary.Remove(atlasName);

            // 移除处理状态
            if (processingStatus.ContainsKey(atlasName))
                processingStatus.Remove(atlasName);
                
            // 移除批处理协程
            if (batchCoroutines.ContainsKey(atlasName))
            {
                if (batchCoroutines[atlasName] != null)
                {
                    StopCoroutine(batchCoroutines[atlasName]);
                }
                batchCoroutines.Remove(atlasName);
            }
                
            // 移除更新回调
            if (atlasUpdateCallbacks.ContainsKey(atlasName))
                atlasUpdateCallbacks.Remove(atlasName);
                
            // 移除版本信息
            if (atlasVersions.ContainsKey(atlasName))
                atlasVersions.Remove(atlasName);
        }
    }

    // 清空所有图集
    public void ClearAll()
    {
        foreach (var atlasName in new List<string>(atlasDictionary.Keys))
        {
            ReleaseAtlas(atlasName);
        }

        spriteMapping.Clear();
        pendingCallbacks.Clear();
        processingStatus.Clear();
        batchCoroutines.Clear();
        atlasUpdateCallbacks.Clear();
        atlasVersions.Clear();
    }

    void OnDestroy()
    {
        ClearAll();
    }
}