# 公寓材质引用修复

本次采用保守修复：从原始 FBX 的 Material → Texture 连接读取作者的贴图用途，再按文件名唯一匹配项目中已找到的图片，写入 Unity GUID 引用。没有按材质名称猜图片，也没有修改 FBX、模型导入映射或场景。

## 已完成

- 核验 116 个模型材质映射，全部指向现存材质。
- 修复 40 个材质，共 42 个引用：40 个颜色贴图、2 个法线贴图。
- 将 `Textile Normal N.jpg` 设置为 Normal Map；原始图片内容保持不变。
- 保留现有颜色、金属度、光滑度、透明模式等参数。
- Unity 刷新后，当前公寓视图已出现墙面装饰和地面等纹理。没有保存用户尚未保存的场景，也没有运行游戏或检查每个物体的全部视角。
- 校验材质到贴图的 GUID、备份哈希以及改动范围；回滚预检查通过。

## 仍需材质转换，不能当作路径问题处理

下面 6 个材质的颜色贴图没有自动指定：

| 材质 | 原因 |
| --- | --- |
| Fabric Velvet Crushed | 原作者使用多个图片混合 |
| Fabric Velvet Crushed2 | 原作者使用多个图片混合 |
| JianE_Mtl_20201023103059455_2 | FBX 有同名材质，贴图关系不一致 |
| JianE_Mtl_20201215201153928_1 | 原贴图有约 90 度 UV 旋转，需要单独转换或烘焙 |
| Material #4062384 | 两个同名源材质分别使用不同图片 |
| Minotti_White Sofa_069 | 原作者使用多个图片混合 |

此外，Corona 的透明遮罩、反射、光泽度、透光、发光和灰度凹凸图不能直接塞入 Unity Standard 的任意贴图槽。当前保留原设置，具体通道见 `applied-repair.json` 的 `summary.skipped`。

只恢复单一底图的材质也不保证与 Corona 原渲染逐像素一致：源图上的颜色校正、程序化节点等没有烘焙。无图片连接的纯色或程序化材质并不等于贴图丢失。

## 文件和复核

- `material-status.json`：所有 116 个材质的修复状态和待转换项。
- `applied-repair.json`：实际修改、贴图用途、GUID、哈希和备份位置。
- `mapping-plan.json`：从 FBX 解析出的原始路径与本地图片对应关系。
- `fbx-connections.json`：只读提取的 FBX 材质/贴图连接证据。
- `verify.cjs`：校验全部修复引用及最小改动范围。

在项目根目录执行：

```powershell
node Tools/material-repair/verify.cjs
```

## 回滚

修改前的 41 个文件备份在项目根目录 `Backups/material-repair-2026-10-03T06-28-13-535Z/`，位于 Assets 外，不会被 Unity 重复导入。

```powershell
# 只检查能否安全回滚
node Tools/material-repair/restore.cjs
# 恢复本次修改
node Tools/material-repair/restore.cjs --apply
```

回滚脚本会先核对全部文件和备份的哈希；如果修复后又进行了编辑，会停止，避免覆盖后续工作。恢复后在 Unity 使用 Assets → Refresh。

今后在 Unity Project 窗口中移动材质、贴图，让 Unity 同步保留 `.meta` 和 GUID。不要删除 `.meta`，也不必重建作者电脑的磁盘路径。

参考：[Unity 2022.3 Materials 导入与重映射说明](https://docs.unity3d.com/2022.3/Documentation/Manual/FBXImporter-Materials.html)。
