# FishClock（钓鱼时钟）

游戏内的钓鱼时钟：不用切出游戏打开浏览器，就能实时查看各类鱼的开放窗口、天气条件与当前地图天气预报。数据来源于 [鱼糕](https://fish.ffmomola.com/)（ffmomola.com），已内置在插件中，无需联网。

## 功能

- **钓鱼时钟表**：列出限时鱼 / 天气鱼的最近开放窗口，实时倒计时（等待时显示距开放时间，开放中显示剩余时间）
- **开放条件**：显示每条鱼的艾欧泽亚时间区间、所需天气、前置天气、是否需要传承录；鼠标悬停可查看同模鱼 / 前置鱼信息
- **当前地图天气预报**：窗口顶部显示当前地图未来数个天气周期的天气预报
- **窗口提醒**：钓鱼窗口开启时自动发送聊天栏提醒，可选提示音
- **搜索与筛选**：按鱼名搜索；可切换「仅当前地图」「隐藏常驻鱼」

## 安装

1. 从 [Releases](https://github.com/Lotusloyi/FFXIV-Dalamud-Plugins/releases) 下载 `FishClock-latest.zip`
2. 解压得到 `FishClock.dll` 与 `FishClock.json`
3. 在卫月（Dalamud）设置 → 插件 → 开发工具（Developer Settings）→ 添加本地插件（Install from local file），选择 `FishClock.dll` 即可

## 使用

- 聊天框输入 `/fishclock` 打开 / 关闭窗口
- `/fishclock <鱼名>`：打开窗口并直接搜索该鱼
- 也可在卫月插件列表中点击设置按钮打开

## 说明

- 鱼名、钓点、天气名称均从游戏本地数据读取，随游戏版本自动更新
- 鱼类开放窗口数据来自鱼糕网站公开数据，如与鱼糕有出入请以鱼糕为准，欢迎提 Issue
- 国服 / 国际服卫月（Dalamud API 15）均可使用
