# Legacy Unity Content Archive

此目录由 Git 跟踪，但位于 Unity 项目的 `Assets` 目录之外，因此 Unity 不会在打开项目时自动导入或加载其中的旧场景、模型与贴图。

## 手动恢复

需要使用旧内容时，将以下目录中的内容复制或移动回项目根目录的 `Assets`：

`_Archive/OldApartment_20261008/Assets/`

恢复后重新打开 Unity，或在 Project 窗口中执行 `Assets > Refresh`，等待资源重新导入。

旧内容包括原公寓模型以及 `SampleScene`、`SampleScene_edit` 和 `scenes1`。日常进行 SceneNew 评审时无需恢复这些文件。
