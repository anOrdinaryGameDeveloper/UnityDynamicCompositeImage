using UnityEngine;

namespace DaVikingCode.AssetPacker
{
    public class TextureToPack
    {
        public string file;
        public string id;
        public Texture2D texture; // 新增：直接存储纹理引用

        public TextureToPack(string file, string id, Texture2D texture = null)
        {
            this.file = file;
            this.id = id;
            this.texture = texture;
        }
    }
}