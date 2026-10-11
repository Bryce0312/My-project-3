# Legacy Unity Content Archive

此目录由 Git 跟踪，但位于 Unity 项目的 `Assets` 目录之外，因此 Unity 不会在打开项目时自动导入或加载其中的旧场景、模型、工具与贴图。

每个子目录都按项目根目录的结构存放内容，恢复时把子目录里的文件复制或移动回项目根目录的对应位置即可。

## OldApartment_20261008

恢复目标：项目根目录 `Assets/`

`_Archive/OldApartment_20261008/Assets/`

旧内容包括原公寓模型以及 `SampleScene`、`SampleScene_edit` 和 `scenes1`。日常进行 SceneNew 评审时无需恢复这些文件。

恢复后重新打开 Unity，或在 Project 窗口中执行 `Assets > Refresh`，等待资源重新导入。

## MaterialRepair_20261011

恢复目标：项目根目录 `Tools/`

`_Archive/MaterialRepair_20261011/Tools/material-repair/`

这是 2026-10-03 修复 FBX 公寓材质引用时使用的一次性脚本与报告，修复结论已记录在 `CHANGELOG.md` 的 0.2.0 与 0.3.0 条目中。被修复的模型与贴图见 `OldApartment_20261008`。

**单独恢复此目录没有实际作用**，脚本依赖以下已不在工作区的内容：

- `inspect-fbx.cjs` 与 `plan.cjs` 需要读取 `Assets/Scenes/Models/Apartment/VLOGGER'S APARTMENT (FBX).FBX`，该模型见 `OldApartment_20261008`。
- `restore.cjs` 与 `verify.cjs` 需要回滚备份 `Backups/material-repair-2026-10-03T06-28-13-535Z/`。该目录被 `.gitignore` 排除，工作区中已不存在，因此**当时的回滚能力已经丢失**。
- `repair.cjs` 是一次性写入脚本，重新运行会再次改写材质引用。

需要查阅当时的修复明细时，直接阅读该目录下的 `README.md`、`material-status.json` 与 `applied-repair.json` 即可，不必放回 Unity 项目。
