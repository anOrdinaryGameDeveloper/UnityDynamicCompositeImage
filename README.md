# UnityDynamicCompositeImage
unity动态合图技术可行性测试项目

## 项目说明
本项目用于验证Unity引擎中动态合图（Texture Atlas）生成技术的可行性，主要目标包括：
- 实现运行时动态纹理合并
- 优化Draw Calls数量
- 验证大纹理内存占用情况
- 支持多分辨率适配
- 目前仅支持Image组件

## 技术方案
### 核心实现
- 使用Texture2D.PackTextures进行纹理打包
- 支持自动纹理尺寸计算（2^n适配）
- 提供纹理合并策略配置（按尺寸/按使用频率）
- 包含纹理回收机制（LRU缓存算法）

### 性能优化
- 异步加载纹理数据
- 支持纹理压缩格式选择（ETC2/ASTC）
- 提供合并粒度控制（按场景/按图集）

## 使用说明
### 依赖环境
- Unity 2022.3 LTS 或更高版本
- 需启用以下Unity模块：
  - 2D Sprite
  - 2D Animation
  - TextMeshPro

### 使用步骤
1. 导入项目
2. 在Assets/Scripts/Utils/TexturePackage目录下找到核心脚本
3. 将DynamicAtlasManager组件挂载到场景管理对象
4. 将DynamicAtlasImage挂载到需要动态合图的对象

### 技术参考
https://www.freesion.com/article/7015120459/
https://github.com/DaVikingCode/UnityRuntimeSpriteSheetsGenerator