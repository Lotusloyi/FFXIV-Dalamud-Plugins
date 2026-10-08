# FateTracker

一款简洁的 FFXIV（最终幻想 14）国服卫月（Dalamud）插件：实时追踪当前地图的 FATE，并在新 FATE 出现时发送提醒。

## 功能

- 📋 **FATE 列表**：实时显示当前地图进行中的 FATE，包括名称、等级、完成进度、剩余时间与距你的距离
- 🔔 **新 FATE 提醒**：新 FATE 出现时自动在聊天栏发送提醒，可搭配提示音
- ⚙️ **按需开关**：可分别开关提示音、距离显示、已完成 FATE 的隐藏
- 🪶 **轻量无依赖**：单文件 DLL，不读写游戏内存、不注入，安全可靠

## 安装（国服卫月）

### 方式一：本地加载（推荐，免安装）

1. 从 [Releases](https://github.com/Lotusloyi/FFXIV-Dalamud-Plugins/releases) 下载 `latest.zip` 并解压，得到 `FateTracker.dll` 和 `FateTracker.json`
2. 进入游戏，打开卫月设置（聊天栏输入 `/xlsettings`）→ **实验** 选项卡
3. 在「开发插件位置 / Dev Plugin Locations」中添加 `FateTracker.dll` 的完整路径
4. 打开插件安装器（`/xlplugins`）→ **开发工具 → 已安装的开发插件**，启用 FateTracker

### 方式二：开发插件目录

将 `FateTracker.dll` 与 `FateTracker.json` 放入 `%AppData%\XIVLauncherCN\devPlugins\FateTracker\` 目录，重启游戏后在 `/xlplugins` 的开发插件页启用。

## 使用

| 命令 | 说明 |
| ---- | ---- |
| `/fate` | 打开 / 关闭 FATE 追踪器窗口 |
| `/fate on` / `/fate off` | 直接打开 / 关闭窗口 |

窗口顶部为设置项，下方为当前地图的 FATE 实时列表。设置会自动保存。

## 自行编译

前置条件：

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 已通过国服卫月（XIVLauncherCN）启动过一次游戏（本机存在 `%AppData%\XIVLauncherCN\addon\Hooks\dev\` 目录）

```bash
dotnet build -c Release
```

编译产物位于 `bin/Release/`：`FateTracker.dll` + `FateTracker.json` 即为可导入的插件，`bin/Release/FateTracker/latest.zip` 为打包好的发布包。

本项目使用 [Dalamud.CN.NET.Sdk](https://github.com/Dalamud-DailyRoutines/Dalamud.CN.NET.Sdk)（API 15 / .NET 10），面向国服卫月；将 csproj 中的 SDK 换成 `Dalamud.NET.Sdk/15.0.0` 并设置 `Use_Dalamud_CN=false` 即可改为国际服构建。

## 发布到 Release

推送到 `v*` 标签即可触发 GitHub Actions 自动构建并上传 `latest.zip`：

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 致谢

- [Dalamud](https://github.com/goatcorp/Dalamud) 与 [Dalamud 开发文档](https://dalamud.dev/)
- [DailyRoutines](https://github.com/Dalamud-DailyRoutines) 提供的 [Dalamud.CN.NET.Sdk](https://github.com/Dalamud-DailyRoutines/Dalamud.CN.NET.Sdk)
- [OmenTools](https://github.com/AtmoOmen/OmenTools) 的国服插件工程实践参考

## 许可证

[MIT](LICENSE)

> 本项目与 SQUARE ENIX CO., LTD. 无任何关联。使用第三方插件存在账号风险，请自行斟酌。
