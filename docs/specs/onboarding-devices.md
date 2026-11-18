# 引导流程：支持的设备、识别设备、Magic Mouse 入场动画

状态：已定稿设计，2026-11-17。目标版本：1.3.0 之后的下一个版本。

## 目标

1.3 起 CalmMouse 不只管 Magic Mouse，还管滚轮鼠标和游戏手柄，但首次打开的引导仍然只讲 Magic Mouse。这一版让引导：

1. 先告诉用户 CalmMouse 支持哪些设备，用 high-fi 的产品渲染图。
2. 再识别用户现在连着什么。是 Magic Mouse 就播一段像 iPhone 上 AirPods 首次连接那样的入场动画。
3. 后面的步骤按识别到的设备走，不再对着滚轮鼠标用户讲 Magic Mouse。

顺带把设置 › 设备页左栏里滚轮鼠标和手柄的 SF Symbol 换成同一套渲染，并且像 Magic Mouse 一样能拖着转。

## 不做的（这一版）

- 滚轮鼠标和手柄没有入场视频，只有静态卡片的淡入。
- 设置里的悬停示意动画（故事）不搬到滚轮鼠标和手柄的模型上，仍用现有的 `PreviewCanvas`。
- 权限页和完成页的内容不改（完成页的提示按设备换措辞除外，见下文）。
- 渲染图上不放任何品牌标识（见「渲染」一节）。

## 流程

```
支持的设备 → 你的设备（识别 + 动画）→ 权限 → 设备分支 → 完成
```

原来的「欢迎」页由「支持的设备」页取代。设备分支按识别结果拼出来，顺序固定：

| 识别到 | 加入的步骤 |
|---|---|
| Magic Mouse | 选预设、试一试（现有两页，不变） |
| 滚轮鼠标 | 滚轮设置（新页） |
| 游戏手柄 | 手柄设置（新页） |
| 都没有 | 无，权限之后直接到完成 |

同时连着几种就依次走几段（例如 Magic Mouse + 手柄 = 选预设 → 试一试 → 手柄设置）。

- 步骤序列由 `CalmMouseCore` 里的纯函数决定：`OnboardingRoute.steps(for: Set<DeviceKind>) -> [OnboardingStep]`，`DeviceKind` 为 `magicMouse / wheelMouse / controller`。
- 识别结果在离开「你的设备」页时定下来，之后插拔设备不改路线（避免用户走到一半步骤数变了）。回到「你的设备」页再前进会重新定。
- 底部圆点只数当前路线上的步骤。
- 用户在识别页没等到设备就点继续：按「都没有」走。

## 窗口

引导窗口从 560×500 改为 **680×520**，给三件产品并排的位置。其他页面内容宽度不变（仍是两侧 36 pt 内边距）。

## 第 1 页：支持的设备

- 标题：「CalmMouse 让这些设备更好用」（英文 "CalmMouse makes these work better"）。
- 三件产品并排，各用渲染图的 `home` 帧，下方名字 + 一句话：
  - **Magic Mouse**：「点按时页面不再乱跳」
  - **滚轮鼠标**：「反转方向、平滑滚动」
  - **游戏手柄**：「PS5 触控板，或任何手柄的摇杆」
- 底部一行小字：「触控板不受影响。」
- 三件产品依次淡入并轻微上浮（间隔 0.12 s，各 0.5 s）。开启「减弱动态效果」时直接显示。

## 第 2 页：你的设备

页面打开后每秒扫描一次：`MouseScanner.scan()` 加 `GamepadController` 的已连接手柄列表。页面期间插上设备会立刻出现。

**有 Magic Mouse：**

1. 整页播放入场视频（见下文），白色或黑色按 `magicMouseFinish` 设置选。
2. 视频结束后停在最后一帧，名字、电量、「已连接」依次淡入。
3. 视频只在这一页第一次出现 Magic Mouse 时播一次；后退再回来也只播一次（同一次引导内）。

**只有滚轮鼠标或手柄：** 静态卡片，渲染图淡入并轻微上浮，名字 +「已识别」。

**同时有多个：** 并排列出（最多三张卡片，同类多个只列第一个并标「+1」）。有 Magic Mouse 时先播它的视频，视频结束后其他卡片在旁边淡入，Magic Mouse 缩到卡片大小。

**都没有：** 「连接你的鼠标或手柄」+ 一个缓慢呼吸的占位轮廓，继续按钮照常可用。

**被 CalmMouse 忽略的鼠标**（`kind == nil`，没有触控面也没有滚轮）：不算识别到。

**减弱动态效果：** 不播视频，直接显示最后一帧和文字。

## Magic Mouse 入场视频

Blender 渲染，约 3 秒，30 fps。

| 时间 | 画面 |
|---|---|
| 0–0.5 s | 空场上一道细光横扫过去 |
| 0.3–2.2 s | 鼠标从下方升起，旋转约一圈又四分之一并减速；镜头同时从低角度升到「desk」视角 |
| 2.2–3.0 s | 落定，一道高光扫过玻璃顶面 |

- 最后一帧与 `Resources/MagicMouse/<finish>/home.webp` 完全一致（同一相机、同一姿态），视频结束切静态图看不出接缝。
- 光照沿用现有渲染：白色用 loft hall HDRI，黑色用 reading room HDRI，参数同设置页那套。
- 格式：HEVC 带 alpha 的 `.mov`，白、黑各一段，叠在浅色和深色背景上都干净。每段目标 ≤ 4 MB。
- 位置：`Resources/MagicMouse/<finish>/intro.mov`。
- 播放：`AVPlayerLayer` 包在一个 NSView 里；播完即停，不留任何计时器。

## 渲染：滚轮鼠标和手柄

- **滚轮鼠标**：参考罗技经典人体工学款（MX Master 的造型）——雕塑感机身、拇指托、侧滚轮、主滚轮。石墨色。
- **手柄**：参考 PS5 DualSense——白色外壳 + 黑色中板、触控板、双摇杆、触控板两侧灯条。
- **不放品牌标识：** 不放罗技标志、PlayStation 标志和 PS 键图案；面键只保留按键形状，不印 △○✕□。CalmMouse 是公开分发的 app，这些是罗技和索尼的注册商标，与 Magic Mouse 不放 Apple 标志同理。界面文字里提到「PS5」「DualSense」属于说明兼容性，可以保留。
- 光照、相机与 Magic Mouse 相同（desk 视角，同一 HDRI）。浅色/深色模式不换产品颜色，各只渲一种颜色；光照按 Magic Mouse 白色那套。
- 输出与 Magic Mouse 相同的结构：`home.webp` + `turn-00.webp`…`turn-71.webp`，放在 `Resources/Devices/wheel/` 和 `Resources/Devices/controller/`，`build.sh` 一并复制进 app。每套约 1.5 MB。
- Blender 源文件放 `.claude/design/devices/`（gitignore，长期保留，不放临时目录）。
- 确认顺序：先各出一张 desk 视角静帧给 Malik 确认造型和材质，确认后再渲 72 帧和入场视频。

## 设置 › 设备页左栏

- 滚轮鼠标和手柄的 SF Symbol 换成 72 帧 360° 转台，拖动、惯性、自动归位与 Magic Mouse 相同。
- 悬停设置时仍切到现有的 `PreviewCanvas` 示意动画，不在模型上演故事。
- 代码：`MouseShowcase` / `MouseFrames` 现在假定是 Magic Mouse。抽出一个 `DeviceModel`（帧目录、帧数、画面尺寸、是否有故事），`MouseFrames` 按 `DeviceModel` 加载。Magic Mouse 保留故事和白/黑两色，另外两个只能转。
- 被「当作」改了类型的设备（例如滚轮鼠标被设为 Magic Mouse）显示它被当作的那种模型，与现在的逻辑一致。
- 列宽、最小高度、静止时 0% CPU 的要求不变。

## 新页：滚轮设置

- 标题「调一下你的滚轮鼠标」，下方是滚轮鼠标渲染图（小）。
- 两个开关：「反转滚动方向」（`reverseWheelScroll`）、「平滑滚动」（`smoothWheelScroll`），默认值沿用设置里的（都是关）。
- 一行说明：每只鼠标可以在设置 › 设备里单独调。

## 新页：手柄设置

- 标题「用手柄操作 Mac」，下方是手柄渲染图（小）。
- 开关「用手柄控制指针」（`controllerPointer`，默认关）。
- 一张简表说明映射：触控板 = 移动/点按（仅有触控板的手柄显示这一行）、左摇杆 = 移动、右摇杆 = 滚动、下方键或按左摇杆 = 左键、右侧键或按右摇杆 = 右键。
- 一行说明：同时按住 Options + Create（Xbox：≡ + ⧉）半秒也能开关；游戏在前台时自动停用。

## 完成页

现有三条提示里「Settings → Magic Mouse」相关的那条，只在路线包含 Magic Mouse 时显示；没有 Magic Mouse 时换成「每个设备的设置都在设置 › 设备里」。

## 文案

所有新文案走 `L("…")`，en / zh-Hans / zh-Hant 三份都要有（`LocalizationTests` 会检查）。遵循 CalmMouse 的白话风格：说做什么，不说术语。

## 测试

- 单元测试：`OnboardingRoute.steps(for:)` 覆盖四种单设备、几种组合、空集合；`DeviceModel` 资源路径；新文案键三种语言齐全。
- CI 机器上没有 Magic Mouse 和手柄，测试不能依赖真实设备。
- 实机：用 Developer ID 签名、bundle id 为 com.calmmouse.app 的测试包，用 `open` 启动（直接从 shell 执行的二进制连不上窗口服务器），接 Magic Mouse 和 DualSense 走一遍完整流程；浅色、深色各截每一页；入场视频录一段 GIF 给 Malik 看；设置 › 设备页三种设备都拖转一遍，静止时确认 CPU 回到 0%。
- 体积：记录 app 增大了多少（预计约 11 MB：两段视频约 8 MB + 两套转台约 3 MB）。
