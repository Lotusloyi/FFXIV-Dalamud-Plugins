# FFXIV Dalamud Plugins（国服卫月插件库）

专为《最终幻想 14》国服卫月（Dalamud / XIVLauncherCN）打造的插件合集，每个插件独立一个子目录，单文件 DLL 本地导入即用。

## 插件列表

| 插件 | 说明 | 命令 | 最新版本 |
| ---- | ---- | ---- | -------- |
| [FateTracker](FateTracker/) | 简洁的 FATE 追踪器：实时显示当前地图 FATE 的名称 / 等级 / 进度 / 剩余时间 / 距离，新 FATE 出现时聊天栏提醒 + 可选提示音 | `/fate` | [v1.0.1](../../releases/tag/v1.0.1) |
| [FishClock](FishClock/) | 游戏内钓鱼时钟：实时显示限时鱼 / 天气鱼的开放窗口与倒计时、当前地图天气预报，窗口开启自动提醒（数据来自鱼糕） | `/fishclock` | [v1.1.0](../../releases/tag/v1.1.0) |
| [FriendCompass](FriendCompass/) | 好友罗盘：按 DR 周边玩家的原生角色范围追踪好友，显示方向、距离、连线、屏幕边缘及地图标记；支持分流切换和上下线提醒（跨服传送需 [Lifestream](https://github.com/NightmareXIV/Lifestream)） | `/fcompass` | [v1.3.6](../../releases/tag/v1.3.6) |

## 安装方法（所有插件通用）

### 方式一：自定义插件源（推荐，无开发警告、可自动更新）

1. 游戏内打开卫月设置（`/xlsettings`）→ **实验**（Experimental）→ **自定义插件仓库**（Custom Plugin Repositories）
2. 添加仓库地址：

   ```
   https://raw.githubusercontent.com/Lotusloyi/FFXIV-Dalamud-Plugins/master/pluginmaster.json
   ```

3. 保存后打开插件安装器（`/xlplugins`），在「可用插件」中搜索插件名安装，之后每次发布新版本会自动提示更新

### 方式二：本地导入

1. 到 [Releases](../../releases) 下载对应插件的 `latest.zip` 并解压，得到 `<插件名>.dll` 和 `<插件名>.json`
2. 打开卫月设置（`/xlsettings`）→ **实验** 选项卡
3. 在「开发插件位置 / Dev Plugin Locations」中添加该 DLL 的完整路径
4. 打开插件安装器（`/xlplugins`）→ **开发工具 → 已安装的开发插件**，启用即可

> 注意：方式二属于开发插件导入，插件列表会显示「开发警告」，改用方式一的自定义源即可消除。

## 开发

前置条件：

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 已通过国服卫月启动过一次游戏（本机存在 `%AppData%\XIVLauncherCN\addon\Hooks\dev\` 目录）

```bash
cd <插件目录>
dotnet build -c Release
```

每个插件使用 [Dalamud.CN.NET.Sdk](https://github.com/Dalamud-DailyRoutines/Dalamud.CN.NET.Sdk)（API 15 / .NET 10），面向国服卫月。

## 新增插件

1. 在库根目录新建子文件夹（如 `MyPlugin/`），放入 `MyPlugin.csproj`（SDK 用 `Dalamud.CN.NET.Sdk`）与源码
2. 在上表登记一行，并在 `.github/workflows/build.yml` 的构建 / 产物路径中追加该插件
3. 打 `v*` 标签推送，Actions 会自动构建全部插件并发布 Release

## 许可证

[MIT](LICENSE)

> 本项目与 SQUARE ENIX CO., LTD. 无任何关联。使用第三方插件存在账号风险，请自行斟酌。
