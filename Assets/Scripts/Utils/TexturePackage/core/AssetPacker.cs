using DaVikingCode.RectanglePacking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace DaVikingCode.AssetPacker
{
    // 新增：尺寸模式配置
    public enum SizeMode
    {
        SquarePowerOfTwo,    // 正方形，边长为2的幂次方
        RectanglePowerOfTwo  // 矩形，两条边都是2的幂次方
    }

    public class AssetPacker : MonoBehaviour
    {
        public UnityEvent OnProcessCompleted = new UnityEvent();
        public float pixelsPerUnit = 100.0f;

        public bool useCache = false;
        public string cacheName = "";
        public int cacheVersion = 1;
        public bool deletePreviousCacheVersion = true;

        public int maxAtlasSize = 2048;
        public int minAtlasSize = 64;
        public int padding = 0;
        public bool autoResizeAtlas = true;
        public SizeMode sizeMode = SizeMode.RectanglePowerOfTwo;

        protected Dictionary<string, Sprite> mSprites = new Dictionary<string, Sprite>();
        protected List<TextureToPack> itemsToRaster = new List<TextureToPack>();

        private List<TextureSize> mTextureSizes = new List<TextureSize>();
        private Vector2Int mCalculatedAtlasSize = new Vector2Int(2048, 2048); // 改为Vector2Int

        // 合图结果
        public Texture2D AtlasTexture { get; private set; }
        public Dictionary<string, Rect> SpriteRects { get; private set; } = new Dictionary<string, Rect>();

        // 添加纹理数据 - 现在直接接收原始纹理，内部处理可读性
        public void AddTextureData(Texture2D texture, string id)
        {
            if (itemsToRaster.Exists(t => t.id == id))
            {
                Debug.LogWarning($"Duplicate texture ID detected: {id}. Skipping.");
                return;
            }

            // 存储纹理尺寸
            mTextureSizes.Add(new TextureSize(texture.width, texture.height));

            // 创建纹理副本（内部处理可读性）
            Texture2D textureCopy = CreateTextureCopy(texture);
            itemsToRaster.Add(new TextureToPack(null, id, textureCopy));
        }

        // 创建纹理副本 - 现在处理不可读纹理
        private Texture2D CreateTextureCopy(Texture2D source)
        {
            // 如果纹理已经是可读的，直接复制
            if (source.isReadable)
            {
                Texture2D copy = new Texture2D(source.width, source.height, source.format, source.mipmapCount > 1);
                copy.SetPixels(source.GetPixels());
                copy.Apply();
                return copy;
            }

            // 处理不可读纹理
            RenderTexture renderTex = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            Graphics.Blit(source, renderTex);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;

            Texture2D readableText = new Texture2D(source.width, source.height);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);

            return readableText;
        }

        // 处理纹理数据
        public void ProcessTextures()
        {
            if (autoResizeAtlas && mTextureSizes.Count > 0)
            {
                mCalculatedAtlasSize = CalculateOptimalAtlasSize(mTextureSizes, padding);
                mCalculatedAtlasSize.x = Mathf.Clamp(mCalculatedAtlasSize.x, minAtlasSize, maxAtlasSize);
                mCalculatedAtlasSize.y = Mathf.Clamp(mCalculatedAtlasSize.y, minAtlasSize, maxAtlasSize);
            }
            else
            {
                mCalculatedAtlasSize = new Vector2Int(maxAtlasSize, maxAtlasSize);
            }

            StartCoroutine(CreateAtlasFromTextures());
        }

        // 直接从纹理数据创建图集
        protected IEnumerator CreateAtlasFromTextures()
        {
            // 使用Stopwatch监控性能
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            // 提前计算所需尺寸
            Vector2Int atlasSize = CalculateOptimalAtlasSize(mTextureSizes, padding);
            atlasSize.x = Mathf.Clamp(atlasSize.x, minAtlasSize, maxAtlasSize);
            atlasSize.y = Mathf.Clamp(atlasSize.y, minAtlasSize, maxAtlasSize);

            // 创建临时纹理进行操作
            Texture2D tempAtlasTexture = new Texture2D(atlasSize.x, atlasSize.y, TextureFormat.RGBA32, false);

            // 使用Array.Clear快速清空纹理
            Color32[] clearColors = new Color32[tempAtlasTexture.width * tempAtlasTexture.height];
            tempAtlasTexture.SetPixels32(clearColors);

            // 使用矩形包装算法计算布局
            RectanglePacker packer = new RectanglePacker(atlasSize.x, atlasSize.y, padding);

            for (int i = 0; i < itemsToRaster.Count; i++)
            {
                Texture2D texture = itemsToRaster[i].texture;
                packer.insertRectangle(texture.width, texture.height, i);
            }

            int packedCount = packer.packRectangles();

            if (packedCount != itemsToRaster.Count)
            {
                Debug.LogError($"Failed to pack all textures. {itemsToRaster.Count - packedCount} textures remain.");
                yield break;
            }

            IntegerRectangle rect = new IntegerRectangle();
            Dictionary<string, Rect> tempSpriteRects = new Dictionary<string, Rect>();
            Dictionary<string, Sprite> tempSprites = new Dictionary<string, Sprite>();

            // 预分配像素数组，避免多次SetPixels32调用
            Color32[] atlasPixels = tempAtlasTexture.GetPixels32();

            // 将纹理复制到临时图集中
            for (int i = 0; i < packedCount; i++)
            {
                rect = packer.getRectangle(i, rect);
                int textureIndex = packer.getRectangleId(i);

                if (textureIndex < 0 || textureIndex >= itemsToRaster.Count)
                    continue;

                Texture2D sourceTexture = itemsToRaster[textureIndex].texture;
                string spriteId = itemsToRaster[textureIndex].id;

                // 直接操作像素数组，而不是调用SetPixels32
                Color32[] sourcePixels = sourceTexture.GetPixels32();
                int sourceIndex = 0;

                for (int y = rect.y; y < rect.y + rect.height; y++)
                {
                    for (int x = rect.x; x < rect.x + rect.width; x++)
                    {
                        int atlasIndex = y * tempAtlasTexture.width + x;
                        atlasPixels[atlasIndex] = sourcePixels[sourceIndex++];
                    }
                }

                // 记录精灵位置信息
                tempSpriteRects[spriteId] = new Rect(rect.x, rect.y, rect.width, rect.height);
            }

            // 一次性设置所有像素
            tempAtlasTexture.SetPixels32(atlasPixels);
            tempAtlasTexture.Apply(false);

            // 创建所有精灵
            foreach (var entry in tempSpriteRects)
            {
                Rect spriteRect = entry.Value;
                tempSprites[entry.Key] = Sprite.Create(
                    tempAtlasTexture,
                    spriteRect,
                    Vector2.zero,
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect
                );
            }

            // 原子性替换所有引用
            lock (this)
            {
                if (AtlasTexture != null)
                {
                    Destroy(AtlasTexture);
                }
                AtlasTexture = tempAtlasTexture;
                SpriteRects = tempSpriteRects;
                mSprites = tempSprites;
            }

            // 清理临时数据
            ClearTemporaryData();

            timer.Stop();
            Debug.Log($"Atlas created in {timer.ElapsedMilliseconds}ms with {packedCount} textures");

            OnProcessCompleted.Invoke();

            yield return null;
        }

        // 清理临时数据
        private void ClearTemporaryData()
        {
            foreach (TextureToPack item in itemsToRaster)
            {
                if (item.texture != null)
                {
                    Destroy(item.texture);
                }
            }

            itemsToRaster.Clear();
            mTextureSizes.Clear();
        }

        // 计算最优图集尺寸（返回Vector2Int）
        private Vector2Int CalculateOptimalAtlasSize(List<TextureSize> textureSizes, int padding)
        {
            if (textureSizes == null || textureSizes.Count == 0)
                return new Vector2Int(minAtlasSize, minAtlasSize);

            // 使用矩形包装算法计算所需尺寸
            RectanglePacker packer = new RectanglePacker(maxAtlasSize, maxAtlasSize, padding);

            for (int i = 0; i < textureSizes.Count; i++)
            {
                packer.insertRectangle(textureSizes[i].width, textureSizes[i].height, i);
            }

            int packedCount = packer.packRectangles();

            if (packedCount != textureSizes.Count)
            {
                Debug.LogWarning($"Not all textures could be packed. {textureSizes.Count - packedCount} textures remain.");
            }

            // 获取实际打包后的尺寸
            int packedWidth = packer.packedWidth;
            int packedHeight = packer.packedHeight;

            // 根据尺寸模式调整
            Vector2Int size;
            switch (sizeMode)
            {
                case SizeMode.SquarePowerOfTwo:
                    // 正方形模式：取较大的边作为正方形尺寸
                    int squareSize = Mathf.Max(packedWidth, packedHeight);
                    squareSize = Mathf.NextPowerOfTwo(squareSize);
                    size = new Vector2Int(squareSize, squareSize);
                    break;

                case SizeMode.RectanglePowerOfTwo:
                default:
                    // 矩形模式：两条边分别调整为2的幂次方
                    int width = Mathf.NextPowerOfTwo(packedWidth);
                    int height = Mathf.NextPowerOfTwo(packedHeight);
                    size = new Vector2Int(width, height);
                    break;
            }

            // 确保尺寸至少能容纳最大的纹理
            int maxTextureWidth = 0;
            int maxTextureHeight = 0;
            foreach (TextureSize textureSize in textureSizes)
            {
                maxTextureWidth = Mathf.Max(maxTextureWidth, textureSize.width);
                maxTextureHeight = Mathf.Max(maxTextureHeight, textureSize.height);
            }

            size.x = Mathf.Max(size.x, maxTextureWidth + padding * 2);
            size.y = Mathf.Max(size.y, maxTextureHeight + padding * 2);

            // 确保尺寸不超过最大限制
            size.x = Mathf.Min(size.x, maxAtlasSize);
            size.y = Mathf.Min(size.y, maxAtlasSize);

            return size;
        }

        public void Dispose()
        {
            if (AtlasTexture != null)
            {
                Destroy(AtlasTexture);
                AtlasTexture = null;
            }

            foreach (var asset in mSprites)
            {
                if (asset.Value != null && asset.Value.texture != AtlasTexture)
                {
                    Destroy(asset.Value.texture);
                }
                Destroy(asset.Value);
            }

            mSprites.Clear();
            SpriteRects.Clear();
            ClearTemporaryData();
        }

        void OnDestroy()
        {
            Dispose();
        }

        public Sprite GetSprite(string id)
        {
            Sprite sprite = null;
            mSprites.TryGetValue(id, out sprite);
            return sprite;
        }

        // 获取当前图集尺寸（最大边）
        public int GetCurrentAtlasSize()
        {
            return Mathf.Max(mCalculatedAtlasSize.x, mCalculatedAtlasSize.y);
        }

        // 新增：获取图集宽度
        public int GetCurrentAtlasWidth()
        {
            return mCalculatedAtlasSize.x;
        }

        // 新增：获取图集高度
        public int GetCurrentAtlasHeight()
        {
            return mCalculatedAtlasSize.y;
        }

        public int GetTextureCount()
        {
            return itemsToRaster.Count;
        }
    }

    // 用于存储纹理尺寸的辅助类
    public class TextureSize
    {
        public int width;
        public int height;

        public TextureSize(int width, int height)
        {
            this.width = width;
            this.height = height;
        }
    }
}