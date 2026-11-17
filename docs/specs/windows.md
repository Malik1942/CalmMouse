# CalmMouse for Windows：可行性研究与规格

状态：草案，2026-11-17。还没写任何代码，等 Malik 拍板。

## 结论先说

1. **能做，但 Magic Mouse 在 Windows 上和在 Mac 上不是一回事。** Windows 自带驱动只把 Magic Mouse 当成一只普通鼠标：能移动、能点，**不能滚动**。要滚动，得先装 Apple 的 Boot Camp 驱动或者第三方驱动（Magic Utilities 等）。CalmMouse 作为普通程序（用户态）**读不到蓝牙 Magic Mouse 的原始触摸数据**，开源项目已经试过，结论一致：打开多点触控需要发一个厂商 feature report，蓝牙下用户态发不出去，只有内核驱动能做（详见 [1.2](#12-magic-mouse-本身能不能自己读触摸数据)）。
   所以 Windows 版的 Magic Mouse 功能是**叠在别人的滚动驱动上面**的：「点按时不滚动」「直线滚动」「防误触」「方向」「每个 App 的规则」都能做；**「轻点来点按」和两种拖移做不了**，除非我们自己写并签名一个内核驱动（放到最后，可能永远不做）。
2. **滚轮鼠标、手柄当鼠标在 Windows 上都能完整做。** 手柄用 SDL3，它是唯一一个能读 DualSense 触控板的成熟方案。
3. **技术栈推荐 C# / .NET 10 + WPF（Fluent 主题）。** 核心逻辑从 Swift 逐行翻成 C#，用「同一份录制好的输入轨迹，两边跑出同样的结果」来锁住行为（见 [2.3](#23-怎么移植-calmmousecore-而不丢掉测试过的行为)）。核心库和它的测试在 Mac 上就能跑，整个 App 在 Mac 上就能交叉编译出 Windows 安装包。
4. **大部分开发可以在 Mac 上做；真机测试离不开一台 x64 Windows 电脑。** Windows 11 ARM 虚拟机能测钩子和界面，测不了蓝牙 Magic Mouse，也装不了 Apple 的 x64 驱动。
5. **先做两三天的实测验证（Phase 0），再决定第一版做什么。** 最关键的未知数是：装了 Apple 驱动或 Magic Utilities 之后，「点按时页面跟着滚」这个毛病还在不在。在，就按原计划先做 Magic Mouse；不在，第一版改成「手柄当鼠标 + 滚轮平滑」。

---

## 1. 逐项功能：用什么 Windows API，能不能做

先说清楚 Windows 和 macOS 输入模型的两点根本差别，后面每一项都受它们影响：

- **没有「是哪个设备发的」这一信息。** macOS 的 `CGEvent` 有个未公开字段能反查设备；Windows 的低级鼠标钩子 `WH_MOUSE_LL` 只给坐标、按键、滚轮量和一个「是不是程序注入的」标志，**没有设备**。设备只在 Raw Input（`WM_INPUT` 的 `RAWINPUTHEADER.hDevice`）里有。
- **滚轮没有手势阶段。** macOS 的连续滚动有 `began / changed / ended` 和单独的惯性阶段；Windows 只有一个个独立的 `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL`，每格 120（高精度滚轮会发更小的值）。驱动模拟出来的「惯性」在 Windows 看来就是一串普通滚轮事件。

### 1.1 每个设备各管各的：钩子 + Raw Input 怎么配合

问题：拦截和改写事件只能用 `WH_MOUSE_LL`，可它不知道设备；Raw Input 知道设备，但只能看、不能拦。

**方案 A（首选）：钩子把物理滚动全部扣下，Raw Input 按设备重新发。**

1. `WH_MOUSE_LL` 里：凡是物理（`LLMHF_INJECTED` 未置位）的滚轮事件，一律吞掉，记一个序号。
2. 同一线程注册 Raw Input（`RIDEV_INPUTSINK`，后台也收）。`WM_INPUT` 带着 `hDevice` 和滚轮量到达，按设备查它的设置（Magic Mouse / 滚轮鼠标 / 其他），交给移植过来的核心逻辑决定：放行、丢弃、改写、平滑。
3. 需要放行或改写的，用 `SendInput` 重新注入，`dwExtraInfo` 打上 CalmMouse 自己的标记，钩子看到这个标记就直接放过，不会循环。

只拦滚轮，**不拦指针移动和按键**（按键只是「看」，用来判断「正在点按」）。这样出错的代价最小：最坏情况是滚动不灵，鼠标本身永远能用。

需要实测确认的两件事（Phase 0 的 S1）：
- 钩子吞掉的事件，Raw Input 是否照样收到。钩子和 `WM_INPUT` 谁先到，网上说法不一，都是经验之谈，没有官方文档。方案 A 不依赖先后顺序，只依赖「吞掉之后 `WM_INPUT` 仍然会来」。
- 重新注入多出来的延迟有多少。目标是不超过 1 帧（8 ms）。

**方案 B（退路）：在钩子里按时间配对。** 钩子回调里先不决定，查最近几毫秒内到达的 `WM_INPUT`，按「滚轮量相同、时间最接近」配对到设备。能用，但两只鼠标同时滚时会配错，只在 A 不成立时用。

**方案 C（不考虑）：内核过滤驱动**（比如 Interception）。每个设备一清二楚，但要装驱动、要签名，对一个「装上就能用」的小工具太重。

**注入的一个坑：管理员窗口。** Windows 的 UIPI 规定，普通权限程序 `SendInput` 进不了以管理员身份运行的窗口（任务管理器、安装程序等）。方案 A 在这些窗口里会把滚动吞掉却发不进去。对策二选一：
- 前台是管理员窗口时，钩子什么都不拦，原样放行（这时 CalmMouse 的功能在这个窗口里暂停）。**推荐，第一版就这样做。**
- 清单里声明 `uiAccess="true"`：要求程序有 Authenticode 签名、装在 `Program Files`，也就意味着安装要管理员权限、更新要走提权。以后需要再说。

另外，`WH_MOUSE_LL` 的回调如果超时（默认约 1 秒以内，由系统设置决定），Windows 会**静默摘掉钩子**。钩子必须跑在专用线程上，回调里只做 O(1) 的事，并且定时自检，掉了就重装。

### 1.2 Magic Mouse 本身：能不能自己读触摸数据

**结论：蓝牙连接时不能（用户态）。USB 线连接时能，但没有实际意义。**

开源项目查到的事实：

- Magic Mouse 默认只发普通鼠标报告。主机要先发一个厂商 feature report 才会开始发触摸数据：第一代 `D7 01`，Magic Mouse 2 和 2024 年 USB-C 款 `F1 02 01`。之后触摸数据走报告 ID `0x29`（一代）/ `0x12`（二代和 USB-C），每个手指 8 字节。格式和 Linux 内核的 `hid-magicmouse.c` 一致。
- **蓝牙下用户态发不出这个 feature report**，`HidD_SetFeature` 返回 `ERROR_INVALID_FUNCTION`。原因有两层：鼠标类 HID 设备被 Windows 独占打开；而且这个 feature report 根本不在设备描述符里，Windows 的 HID 类驱动不认它，就算发了，回来的 `0x12` 报告也会被丢掉。
- 因此做到「蓝牙下读原始触摸」的项目，全都是**内核下层过滤驱动**：
  - [daniel0104/magicmouse-win](https://github.com/daniel0104/magicmouse-win)：KMDF 过滤驱动挂在 `HidBth` 下面，直接在 L2CAP 控制通道上发 `SET_REPORT`，自己解析触摸数据，再通过 VHF（虚拟 HID 框架）造一只带高精度滚轮的虚拟鼠标。目前只能**测试签名**（要关安全启动），正式签名需要 EV 证书 + 微软 Partner Center 的 attestation 签名。作者说驱动是在没有硬件的情况下写的。
  - [LesleyMurfin/magic-mouse-v3-windows-fix](https://github.com/LesleyMurfin/magic-mouse-v3-windows-fix)、[vrAxiom/MagicMouse](https://github.com/vrAxiom/MagicMouse)：同样是 KMDF 驱动。
  - [saulatalibaig/magic-mouse-enhancer](https://github.com/saulatalibaig/magic-mouse-enhancer)：C# 托盘程序。USB 线连接时它在用户态发 feature report、自己读触摸数据，手势全有；蓝牙时只能去装 Apple 签名的 `applewirelessmouse.sys`，然后靠 `WH_MOUSE_LL` 在滚轮事件上加横向手势。这个项目的结论和我们的判断一致。
- USB 线这条路没有意义：Magic Mouse 2 和 USB-C 款的接口都在底面，插着线没法用。

**所以 CalmMouse for Windows 的 Magic Mouse 用户，前提是已经装了一个滚动驱动：**

| 驱动 | 怎么来 | 滚动 | 轻点点按 | 备注 |
|---|---|---|---|---|
| Apple Boot Camp 驱动（`applewirelessmouse.sys`） | Boot Camp 支持软件里带的，Apple 签名 | 有 | 无 | 只有 x64；Boot Camp 许可写的是在 Apple 品牌电脑上用，**CalmMouse 不能打包或代下这个驱动**，只能引导用户自己装 |
| Magic Utilities | 商业软件，订阅制 | 有，带惯性（据其介绍） | 有（据其介绍） | 功能最全，它自己可能已经有部分我们要做的功能（待查，见 S3） |
| 上面那些开源 KMDF 驱动 | GitHub | 有 | 部分有 | 大多要测试签名模式，普通用户装不了 |

**我们自己写驱动值不值？** 那样 CalmMouse 就能做到「装上就能滚，还能轻点」，是 Windows 上真正的卖点。但代价是：内核代码（出错就蓝屏）、EV 证书（每年几百美元）+ Partner Center attestation 签名、每次 Windows 大更新都要回归、ARM64 要单独编译签名。放在 Phase 4，单独再做一次评估。

### 1.3 功能对照表

「叠加」= 叠在已有 Magic Mouse 滚动驱动上做。

| 功能（README「What it fixes」） | Windows 上怎么做 | 能不能做 |
|---|---|---|
| **点按时不滚动**（含松开后的宽限期、手势跨越点按时一直吞掉） | 钩子看按键状态，Raw Input 确认是 Magic Mouse 的滚轮，按 1.1 方案 A 丢弃。没有手势阶段，用「两次滚轮间隔超过 N ms 算新手势」来切分 | ✅ 叠加。前提是实测确认毛病在这些驱动下仍然存在（S2） |
| **直线滚动** | 同一手势内锁定第一个方向，另一个轴的 `WM_MOUSEHWHEEL` / `WM_MOUSEWHEEL` 丢掉 | ✅ 叠加 |
| **防误触（死区）** | 一个手势内累计滚动量，过阈值才放行 | ✅ 叠加。单位从「点」换成「滚轮量」，阈值要实测重定 |
| **关闭惯性** | Windows 看不到惯性阶段。只能靠「手指离开后的减速尾巴」时间特征去猜 | ⚠️ 大概率不做。Magic Utilities 自己有惯性开关 |
| **反转方向（只反 Magic Mouse）** | 方案 A 重新注入时取反 | ✅ 叠加 |
| **轻点来点按、右侧轻点右键、轻点区域** | 需要原始触摸数据 | ❌ 除非 Phase 4 自己写驱动 |
| **双击拖移、双指拖移** | 同上 | ❌ 同上 |
| **滚轮鼠标：平滑滚动** | 方案 A 吞掉原始滚轮，`WheelSmoother` 算出曲线，按 120 Hz 注入小于 120 的滚轮量。大多数现代 App（浏览器、Office、VS Code）支持小于 120 的增量；少数老程序会攒满 120 才动，所以要能按 App 关掉 | ✅ |
| **滚轮鼠标：反转方向（只反这一只）** | 方案 A 注入时取反。（Windows 自带的注册表 `FlipFlopWheel` 也能按设备反转，但要管理员权限、要重新插拔，不用它） | ✅ |
| **滚轮鼠标：按下滚轮时不滚** | 钩子看中键按下状态，丢掉这只鼠标的滚轮 | ✅ |
| **每个 App 的规则** | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 拿前台窗口 → 进程 exe 路径。规则的键从 macOS 的 bundle ID 换成 exe 名（如 `figma.exe`）。UWP 应用挂在 `ApplicationFrameHost.exe` 下，要取它的子进程 | ✅ |
| **修饰键 + 滚动**（横向、反向、缩放） | ⌘ 换成 Ctrl、⌥ 换成 Alt、⌃ 换成 Win（或去掉）、⇧ 不变。横向 = 注入 `WM_MOUSEHWHEEL`；缩放 = 注入 Ctrl+滚轮 | ✅ |
| **每只鼠标各自的指针速度** | Windows 指针速度是全局的（`SPI_SETMOUSESPEED`，1–20）。做法：Raw Input 发现换了一只鼠标在动，就切换全局速度。切换前的头一两个移动事件用的是旧速度，肉眼基本看不出。（另一种做法是拦截所有移动再重新注入，会破坏「提高指针精确度」和管理员窗口，不用） | ✅（有小瑕疵） |
| **电量提醒** | Magic Mouse：电量也在厂商报告里，蓝牙用户态读不到。部分蓝牙低功耗鼠标能从 GATT 电池服务或设备属性读到。手柄：SDL3 的 `SDL_GetGamepadPowerInfo` | ⚠️ 手柄 ✅；Magic Mouse ❌；其他鼠标看型号 |
| **其他鼠标软件冲突提示** | 枚举进程。Windows 名单换成：Magic Utilities、Logi Options+、X-Mouse Button Control、SmoothScroll、DS4Windows、Steam（手柄桌面配置）、AutoHotkey 脚本等 | ✅ |
| **预设** | 只保留 Windows 上有意义的项（没有轻点） | ✅ |
| **应用内更新** | 见第 4 节 | ✅ |
| **英文 / 简体 / 繁体** | 跟随 Windows 显示语言，复用现有译文 | ✅ |
| **菜单栏图标** | 通知区域（托盘）图标，状态颜色规则照搬 | ✅ |
| **开机自启** | 注册表 `HKCU\...\Run`，或 MSIX 的 `StartupTask` | ✅ |

### 1.4 手柄当鼠标

| 方案 | 支持哪些手柄 | DualSense 触控板 | 后台能不能收 | 评价 |
|---|---|---|---|---|
| **XInput** | 只有 Xbox 及兼容手柄，最多 4 个 | ❌ 根本看不到 PlayStation 手柄 | 能 | 不够用 |
| **Windows.Gaming.Input** | Xbox 走 `Gamepad`；其他（含 DualSense）走 `RawGameController`，只有按键和轴 | ❌ 没有触摸接口 | 桌面程序能 | 不够用 |
| **GameInput**（微软新 API，NuGet 可分发） | 多数手柄。3.x 版本给 DualSense 加了灯条、震动、自适应扳机的辅助头文件，只支持 USB 格式，蓝牙不管 | ❓ 没找到 DualSense 触控板的文档。官方说运行时有「触控板支持」，但没说包括 DualSense | 有焦点策略可设 | 太新，DualSense 支持是半成品 |
| **SDL3**（`SDL_gamepad`，C# 用 ppy 维护的 `SDL3-CS`，osu! 在用） | 几乎所有手柄，Xbox / PlayStation / Switch Pro / 8BitDo 等，USB 和蓝牙 | ✅ `SDL_GetGamepadTouchpadFinger`，最多 2 指，坐标 0…1。蓝牙要用「增强报告」模式，SDL 3.2 起默认开 | 设 `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS` | **推荐** |

**选 SDL3。** 它是唯一一个 DualSense 触控板 USB / 蓝牙都能读的现成方案，也给电量。注意两点：
- SDL3 打开增强报告后，PlayStation 手柄要断电重连才会回到普通模式；而且增强报告会让**不用 SDL 的老游戏（DirectInput）读不到这只手柄**。所以「用手柄当鼠标」关着的时候，CalmMouse 不打开手柄，只监听插拔。
- DS4Windows / HidHide 隐藏了手柄、或者 Steam 正在接管它时，我们看不到或者会和它打架，放进冲突提示。

**「游戏在前台时停用」**：Windows 没有 macOS 那样的「应用类别」。用三个信号：
1. `SHQueryUserNotificationState` 返回全屏 D3D / 演示模式；
2. 前台窗口盖满整个显示器（无边框全屏游戏）；
3. 前台 exe 在 Windows 自己记录的游戏列表里（`HKCU\System\GameConfigStore\Children`，Xbox Game Bar 认出来的游戏）。
再加上每个 App 的规则，用户可以手动标记。

**输出**：`SendInput` 发相对移动、点按、`WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL`。`ControllerPointer` 的逻辑整体照搬，映射表照搬（Options + Create 改叫 Options + Create / Xbox 的 ≡ + ⧉，不变）。

---

## 2. 技术栈

### 2.1 候选对比

| | C# / .NET 10 + WPF | C# + WinUI 3 | Rust | C++ | Swift on Windows |
|---|---|---|---|---|---|
| 在 Mac 上编译 Windows 程序 | ✅ `EnableWindowsTargeting`，`dotnet publish -r win-x64 / win-arm64` | ❌ XAML 编译器只能在 Windows 上跑 | ✅ `cargo-xwin` | ⚠️ clang-cl + xwin，能但麻烦 | ❌ 官方不支持从 macOS 交叉编译到 Windows |
| 核心逻辑测试在 Mac 上原生跑 | ✅ 核心库是纯 `net10.0`，`dotnet test` 直接跑 | ✅ 同左 | ✅ `cargo test` | ✅ | ✅（原样复用 Swift） |
| 界面 | WPF + .NET 9/10 自带的 Fluent 主题，接近 Windows 11 原生观感，资料最多 | 最「新」的 Windows 11 观感 | 弱：egui / Slint 不像原生；Tauri（WebView2）能做漂亮但多一层 | Win32 手写，极费时间 | 几乎没有：swift-winrt 还不成熟 |
| 钩子 / Win32 调用 | `CsWin32` 源码生成 P/Invoke，类型安全 | 同左 | `windows` crate，一流 | 原生 | WinSDK 模块，能用 |
| 安装包体积 | 自包含约 60–80 MB（WPF 不支持 Native AOT 和裁剪），依赖框架则很小 | 自包含 + AOT 能小一些 | 几 MB | 几 MB | 加上 Swift 运行时 DLL |
| 钩子回调里的停顿风险 | GC 停顿通常 < 1 ms，在专用线程上可接受 | 同左 | 无 | 无 | ARC，无 GC |
| 把 Swift 核心翻过来 | 结构体 → `record struct`，带关联值的枚举 → `abstract record` + 模式匹配，`OptionSet` → `[Flags] enum`。翻译量中等 | 同左 | 语义最接近（枚举、值类型、Optional 几乎一一对应） | 最费力 | 不用翻 |
| 自动更新 / 打包生态 | Velopack、MSIX、winget 都成熟 | 同左 | 要自己拼 | 要自己拼 | 要自己拼 |

### 2.2 推荐：C# / .NET 10 + WPF

理由，按分量排：

1. **整个开发循环能留在 Mac 上。** 核心库和测试在 Mac 上原生编译运行；整个 App 能在 Mac 上 `dotnet publish` 出 Windows 的 x64 和 arm64 版本。WinUI 3 做不到这一点，这也是没选它的主要原因。
2. **界面能做到 Malik 要的水准。** WPF 的 Fluent 主题加上自己画的 Magic Mouse 示意（`MouseFigure` 的几何逻辑可以照搬，用 WPF 的 `DrawingContext` 画），加上左侧悬停说明，都是 WPF 擅长的。
3. **生态省事。** 签名、Velopack 更新、winget、托盘图标（`H.NotifyIcon.Wpf`）、SDL3 绑定（`SDL3-CS`）都有现成的。

**没选 Rust 的原因**：钩子层它最好，但界面要么不像 Windows，要么得引入 WebView。如果 Phase 0 发现 .NET 的 GC 停顿真的影响钩子（不太可能），退路是把钩子线程单独做成一个 Native AOT 的小 C# 进程，界面进程不变，不需要换语言。

**没选 Swift on Windows 的原因**：看起来最省事（`CalmMouseCore` 只依赖 Foundation，理论上能原样编译），但 (1) 没有能用的 Windows 界面方案，最后还是要用另一门语言写界面，变成两套工具链加一层 C ABI；(2) 不能在 Mac 上交叉编译；(3) Windows 的输入模型不同（没有手势阶段、要配 Raw Input），核心的入口本来就要改，「一行不改」的好处其实拿不到。

### 2.3 怎么移植 CalmMouseCore 而不丢掉测试过的行为

`Sources/CalmMouseCore` 共 13 个文件、约 2,240 行，只依赖 Foundation；`Tests/CalmMouseCoreTests` 有 218 个测试。

**移植范围：**

| 文件 | 移植 | 说明 |
|---|---|---|
| `ScrollBlocker` | Phase 1 | 整个搬。Windows 适配层把滚轮事件按时间间隔合成 `began / changed / ended` 再喂进去，状态机本身不改 |
| `Config`（`BlockerConfig`、`AppRule`、`RuleSet`、`ModifierCombo`） | Phase 1 | `bundleID` 字段在 Windows 上存 exe 名 |
| `MouseKind`、`Presets`、`AppVersion`、`SupportInvite` | Phase 1 | 原样 |
| `MouseUtilityConflicts` | Phase 1 | 逻辑原样，名单换成 Windows 的 |
| `WheelSmoother`、`WheelSettings` | Phase 2 | 原样 |
| `TrackingSpeed` | Phase 2 | 映射目标从 macOS 的分辨率换成 Windows 的 1–20 档 |
| `ControllerPointer` | Phase 3 | 原样 |
| `MouseFigure` | 做设备页时 | 纯几何，原样 |
| `TapRecognizer` | 只有 Phase 4 做驱动时 | 530 行，测试 936 行，是最大的一块 |

**锁住行为的三层办法：**

1. **测试逐个翻译。** 每个 Swift 测试翻成同名的 xUnit 测试，测试名保留原名，方便对照。翻完后两边测试数量对得上，CI 里检查。
2. **黄金轨迹（golden traces），主要的保险。** 在 Swift 这边加一个只在测试里用的小工具：把一串输入事件（JSON）喂给 `ScrollBlocker` / `WheelSmoother` / `ControllerPointer`，把每一步的输出写成 JSON。轨迹文件提交到 `shared/fixtures/core/`。C# 测试读同一批文件，逐步比对输出（浮点用 1e-9 容差）。Mac CI 每次重新生成轨迹，和提交的版本有差异就失败。这和网站上 Settings 窗口用的 Swift ↔ JS 对照守卫是同一个思路。
   轨迹来源：现有测试的输入 + 一批随机生成的事件序列（固定随机种子），覆盖测试没写到的组合。
3. **以后改核心逻辑，两边一起改。** 规则写进 `CLAUDE.md` / PR 模板：动了 `CalmMouseCore` 的行为，就要重新生成轨迹，C# 那边不跟上 CI 就红。

---

## 3. 在 Mac 上开发、构建、测试

### 3.1 在 Mac 上直接做的

- 装 .NET 10 SDK（arm64 原生）。
- `dotnet test windows/tests/CalmMouse.Core.Tests`：核心逻辑测试在 Mac 上原生跑，不需要 Windows。
- `dotnet publish windows/src/CalmMouse.Win -r win-x64 -c Release -p:EnableWindowsTargeting=true`（`win-arm64` 同理）：在 Mac 上产出 Windows 程序。
- Velopack 的打包命令据文档可以在非 Windows 系统上打 Windows 包；签名放在 CI 上做（见第 4 节）。
- 编辑器：VS Code + C# Dev Kit，或者 JetBrains Rider（Mac 版能编辑 WPF 的 XAML，但不能预览）。

### 3.2 GitHub Actions（`windows-latest`）

新增 `.github/workflows/windows.yml`，只在 `windows/**`、`shared/**`、`Sources/CalmMouseCore/**` 改动时触发：

1. `dotnet test`（核心测试 + 黄金轨迹）；
2. 在真 Windows 上 `dotnet build` 整个解决方案，跑一组不需要真实设备的集成测试（比如：装上钩子 → 用 `SendInput` 注入一个带特定标记的滚轮 → 确认钩子按规则放过或吞掉）；
3. 打 tag 时：`publish` x64 + arm64 → 签名 → Velopack 打包 → 上传到 GitHub Release。

现有的 macOS CI 加一步：重新生成黄金轨迹并检查没有差异。

### 3.3 Windows 11 ARM 虚拟机（Parallels / UTM）能测什么

| 能测 | 测不了 |
|---|---|
| 钩子装上、不掉、不卡；`SendInput` 注入；管理员窗口时自动放行 | **蓝牙 Magic Mouse**：它配对在 Mac 上。Parallels 有「与 Windows 共享蓝牙」的选项，但它对 Apple 芯片上的 HID 鼠标是否可用没有可靠说法；而且就算能用，鼠标也会从 Mac 断开。按测不了来规划 |
| 托盘图标、设置窗口、三种语言、高 DPI、深色模式 | **Apple Boot Camp 驱动**：只有 x64 版，ARM 版 Windows 装不上 |
| 每个 App 的规则（前台窗口切换） | 真实的设备区分：虚拟机里所有鼠标输入都来自 Parallels 的虚拟指针设备 |
| 安装、更新、卸载、开机自启 | 性能和延迟手感：虚拟机里的数字不代表真机 |
| **DualSense 用 USB 线直通给虚拟机**：SDL3 读触摸板、摇杆、按键 | DualSense 蓝牙（同 Magic Mouse 的理由） |
| x64 版在 ARM 上的模拟运行 | |

### 3.4 必须用一台真 Windows 电脑的

需要一台 **x64、带蓝牙的 Windows 11 电脑**（二手迷你主机即可）。在它上面测：

- Magic Mouse 蓝牙连接 + Apple 驱动 + Magic Utilities 各自的表现（Phase 0 的核心）；
- 两只鼠标同时连着时设备区分对不对（Magic Mouse + 一只滚轮鼠标）；
- 滚动平滑在 60 Hz / 高刷屏上的手感、注入延迟；
- DualSense 蓝牙；
- 发布前的整体回归。

开发时可以用 Mac 上的 VS Code 远程连这台电脑（SSH），或者把它当作 GitHub Actions 的自托管 runner，在上面跑带设备的测试。

---

## 4. 分发

### 4.1 代码签名（Authenticode）

不签名的话，下载运行时 SmartScreen 会弹「Windows 已保护你的电脑」，而且第 1.1 节提到的 `uiAccess` 也用不了。选项：

| 方案 | 费用 | 条件 | 备注 |
|---|---|---|---|
| **Azure Artifact Signing**（原 Trusted Signing） | 9.99 美元 / 月 | 个人只开放给**美国、加拿大**居民；机构开放给美、加、欧盟、英国 | 最省事，有现成的 GitHub Action。看 Malik 所在地是否符合 |
| **SignPath Foundation** | 开源项目免费 | 项目审核通过；证书署名是 SignPath Foundation，不是 Malik | 有 GitHub Actions 集成 |
| **Certum 开源代码签名证书** | 每年几十欧元 | 个人身份验证；云端密钥（SimplySign），CI 里能用但配置较麻烦 | 署名是 Malik 本人 |
| 商业 OV 证书（DigiCert / Sectigo 等） | 每年 300–500 美元 | 硬件密钥或云 HSM | 贵 |
| **Microsoft Store**（MSIX） | 微软负责签名；个人开发者注册费据报道已免除（待确认） | 过商店审核；需要声明 `runFullTrust` | 同时解决签名和更新，但安装方式和 GitHub 下载分成两条线 |

注意：微软 2024 年调整后，EV 证书已经不能让 SmartScreen 立刻放行（发布前再核实一次），信誉都要靠下载量慢慢积累，所以没必要为此买 EV。

### 4.2 安装和更新

**推荐 Velopack**：
- 安装到当前用户目录（`%LocalAppData%`），不需要管理员权限，不弹 UAC；
- 自带增量更新，更新源可以直接用 GitHub Release，或者把更新清单放在网站上；
- 有托盘程序常用的「后台下载、下次启动生效」流程，界面里照搬 Mac 版的「设置 → 通用」里的更新入口。

代价：装在用户目录意味着不能用 `uiAccess`（它要求装在 `Program Files`）。第一版接受这个限制（管理员窗口里暂停功能）。

**winget**：用 `wingetcreate` 或 GitHub Action（如 `winget-releaser`）在每次发版时自动给 `microsoft/winget-pkgs` 提 PR。包名 `MalikZ.CalmMouse` 之类。用户可以 `winget install CalmMouse`。

**Scoop**：可选，开个 bucket 很便宜。

### 4.3 发布和 Mac 版的更新器互不干扰（重要）

Mac 版的应用内更新读的是 `https://api.github.com/repos/Malik1942/CalmMouse/releases/latest`（`Sources/CalmMouse/UpdateChecker.swift:40`），网站的下载按钮也指向 `releases/latest`。**如果 Windows 版在同一个仓库发一个普通 Release，它就会变成「latest」，Mac 用户会被提示「更新」到一个 Windows 版本，网站下载按钮也会坏。**

对策（同仓库方案下必须做）：
- Windows 版用单独的 tag 前缀：`win-v0.1.0`；
- 发布时加 `--latest=false`，GitHub 就不会把它当成 latest；
- Windows 的更新器不读 `releases/latest`，而是读 Velopack 自己的更新清单（放在 Windows 的 Release 资产里，或者放到网站 `calmmouse.malikzhang.com/windows/` 下）；
- Mac 的 `release.sh` 不用改，但要在文档里写清楚这条规则。

---

## 5. 仓库结构和分阶段计划

### 5.1 同一个仓库还是分开

**推荐同一个仓库**，理由：核心逻辑的黄金轨迹守卫要求 Swift 和 C# 在同一个提交里一起变；网站、README、issue、星标都在一处；Windows 版本来就是同一个产品。代价是第 4.3 节的发布规则和 CI 的路径过滤，都可控。

```
CalmMouse/
  Sources/ Tests/            Mac 版（不动）
  shared/
    fixtures/core/           Swift 生成、两边共用的黄金轨迹
    strings/                 （以后）三种语言的共用译文
  windows/
    CalmMouse.Windows.sln
    Directory.Build.props    EnableWindowsTargeting、版本号
    src/
      CalmMouse.Core/        net10.0，纯逻辑，CalmMouseCore 的 C# 版
      CalmMouse.Win/         net10.0-windows，WPF：钩子、Raw Input、托盘、设置窗口、SDL3
    tests/
      CalmMouse.Core.Tests/  xUnit：翻译过来的测试 + 黄金轨迹
      CalmMouse.Win.Tests/   只在 Windows CI 上跑的集成测试
  docs/specs/windows.md      本文
  .github/workflows/windows.yml
```

### 5.2 分阶段

**Phase 0：实测验证（2–3 天，需要真 Windows 电脑）。** 写一次性的小程序，回答下面这些问题，结论补进本文：

| 编号 | 问题 | 通过标准 |
|---|---|---|
| S1 | 钩子吞掉的滚轮，Raw Input 还收不收得到；重新注入的延迟 | 收得到，延迟 ≤ 8 ms |
| S2 | 装 Apple 驱动 / Magic Utilities 后，「点按时页面跟着滚」还在不在；它们的滚轮事件的 `hDevice` 是不是 Magic Mouse 本身 | 毛病还在，且能认出设备 → 走 Magic Mouse 路线 |
| S3 | Magic Utilities 自己是不是已经有「点按时不滚」之类的选项 | 决定我们对它的用户还有没有价值 |
| S4 | SDL3 在 Windows 上，DualSense 蓝牙下触控板、后台输入是否正常 | 正常 |
| S5 | `dotnet publish -r win-arm64` 在 Mac 上出的 WPF 程序，在 ARM 虚拟机里能跑 | 能跑 |

**决策点**：S1 + S2 通过 → Phase 1 先做 Magic Mouse。S2 不通过（毛病不存在，或者认不出设备）→ Phase 1 改成「滚轮平滑 + 手柄当鼠标」，Magic Mouse 部分降级为「每个 App 的规则 + 方向」或者不做。

**Phase 1：最小可发布版本（约 3–4 周）。**
- 托盘图标（暂停 / 设置 / 退出）、开机自启、首次启动引导（检测到 Magic Mouse 但没有滚动驱动时，用一句话说明并给出 Apple 驱动和 Magic Utilities 的链接，**不代装**）；
- Magic Mouse：点按时不滚动、直线滚动、防误触、反转方向；
- 每个 App 的规则（在 Figma 里不滚动等）；
- 设置窗口：「通用」「设备」「App 规则」三页，左侧悬停说明，英 / 简 / 繁；
- 冲突提示；
- 签名 + Velopack + GitHub Release（`win-v0.1.0`）+ winget；
- 网站下载区加 Windows 入口。

**Phase 2：滚轮鼠标（约 2 周）。** 平滑滚动、反转方向、按下滚轮时不滚、每只鼠标的指针速度、修饰键 + 滚动。

**Phase 3：手柄当鼠标（约 2 周）。** SDL3，映射和 Mac 版一致；游戏在前台时停用；手柄电量。

**Phase 4：自己的 Magic Mouse 驱动（另行评估，可能不做）。** 以 daniel0104/magicmouse-win 的思路为参考，KMDF 下层过滤 + VHF 虚拟鼠标，换来「装上就能滚」和轻点点按。前置条件：EV 证书 + Partner Center 账号 + attestation 签名流程跑通、x64 和 ARM64 都有真机。

---

## 6. 需要 Malik 决定的事

1. **Phase 0 是否先做**，以及是否为此准备一台 x64 带蓝牙的 Windows 电脑。没有它，Magic Mouse 部分的任何结论都只是推测。
2. **第一版的重点**：Magic Mouse（品牌一致，但依赖别人的驱动，受众是已经让 Magic Mouse 在 Windows 上滚起来的人）还是滚轮 + 手柄（受众更广，但 Windows 上已有 SmoothScroll、DS4Windows 等竞品）。推荐：等 Phase 0 的 S2 结果再定。
3. **技术栈**：同意 C# / .NET 10 + WPF 吗？
4. **签名方案**：取决于所在国家 / 地区（Artifact Signing 个人只限美加）。备选 SignPath Foundation（免费，署名不是本人）或 Certum（署名是本人）。也可以考虑 Microsoft Store。
5. **同一个仓库 + `win-v` tag + `--latest=false`**，还是单独开一个 `CalmMouse-Windows` 仓库。
6. **自己写内核驱动（Phase 4）是否列入长期计划。**

---

## 参考

- [daniel0104/magicmouse-win](https://github.com/daniel0104/magicmouse-win)：KMDF 下层过滤 + VHF，蓝牙控制通道上发 `SET_REPORT`
- [saulatalibaig/magic-mouse-enhancer](https://github.com/saulatalibaig/magic-mouse-enhancer)：C# 托盘程序；USB 下用户态读触摸，蓝牙下 `ERROR_INVALID_FUNCTION`，改装 Apple 驱动
- [LesleyMurfin/magic-mouse-v3-windows-fix](https://github.com/LesleyMurfin/magic-mouse-v3-windows-fix)、[vrAxiom/MagicMouse](https://github.com/vrAxiom/MagicMouse)：KMDF 驱动
- Linux 内核 `drivers/hid/hid-magicmouse.c`：feature report 和触摸数据格式
- [HidD_SetFeature（Microsoft Learn）](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature)
- [RAWINPUTHEADER（Microsoft Learn）](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputheader)、[About Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)
- [SDL_GetGamepadTouchpadFinger（SDL3 wiki）](https://wiki.libsdl.org/SDL3/SDL_GetGamepadTouchpadFinger)、[SDL_HINT_JOYSTICK_ENHANCED_REPORTS](https://wiki.libsdl.org/SDL3/SDL_HINT_JOYSTICK_ENHANCED_REPORTS)
- [Microsoft.GameInput（NuGet）](https://www.nuget.org/packages/Microsoft.GameInput)、[GameInput 3.5 的 DualSense 辅助头文件](https://tbreak.com/microsoft-gameinput-dualsense-controls/)
- [NETSDK1100 / EnableWindowsTargeting（Microsoft Learn）](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100)
- [Artifact Signing 价格](https://azure.microsoft.com/en-gb/pricing/details/artifact-signing/)、[个人资格的讨论（2026-03）](https://learn.microsoft.com/en-us/answers/questions/5810735/cant-create-a-new-trusted-signing-individual-ident)
