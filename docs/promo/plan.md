# CalmMouse 发布与曝光计划

写于 2026-11-17，11-18 按 1.4.0 更新。取代 `docs/launch-plan.md`（8 月写的，已过时：公证、Homebrew tap、README GIF 都做完了，而且它还写着"只管 Magic Mouse"）。

## 现状

- 8 个 star，约 390 次下载（1.0.0 占 280）；官网 30 天 152 访客，几乎全部来自搜索，没有任何社交流量。
- 已经上架：jaywcjlove/awesome-mac、abordage/awesome-mac、openmacapps.com、AlternativeTo（没有简介）、自建 Homebrew tap。
- 还没发过任何社交帖子。1.3 加了手柄、1.4 加了设备渲染图，第一次能讲完整的故事：**Magic Mouse 的修复 + 普通滚轮鼠标 + 游戏手柄当鼠标**。
- Locant 11 月 18 日上 Product Hunt。CalmMouse 的大动作避开 11/18–11/20，Product Hunt 至少隔两周。

## 讲什么

主线永远是 Magic Mouse：**每次点击也是一次轻微的滑动，页面在点击落下之前就动了**。这是别人做不到、一看就懂的钩子。滚轮鼠标和手柄是第二句话，不是标题：

- 英文：*Your Magic Mouse stops scrolling the page when you click. Now also: smooth wheel mice, and a PS5 controller's touchpad as a trackpad.*
- 中文：*妙控鼠标点一下，页面就跟着滑一下？CalmMouse 修好它。现在也支持普通滚轮鼠标，还能把 PS5 手柄的触控板当触控板用。*

手柄是一个独立的、新奇的角度，适合单独打 r/macgaming、B站、小红书；Show HN 仍以 Magic Mouse 为标题。

## 目标（4 周内）

| 指标 | 现在 | 目标 | 为什么是这个数 |
|---|---|---|---|
| GitHub stars | 8 | 75+ | 75 是 Homebrew 官方 cask 接受**他人**提交的门槛（自己提交要 225） |
| 新安装（`CalmMouse.zip` 下载，扣除更新） | 约 8/周 | 100+/周 | — |
| 官网非搜索来源访客 | ≈0 | 占 30%+ | 在 Vercel 面板看 Referrers |

## 渠道和排期

图例：✅ 我已完成 · 🟡 草稿已写好，等你批准后我来发/提交 · 👤 需要你本人账号或操作

### 第 1 周（11/17–11/22）：铺地基，不需要声量

| 动作 | 状态 | 说明 |
|---|---|---|
| 官网 SEO：canonical、SoftwareApplication 结构化数据、robots.txt、sitemap.xml、描述加上滚轮鼠标和手柄 | ✅ | PR（分支 `am/promo-1.3`） |
| GitHub 仓库简介和 topics 更新（加 `game-controller`、`dualsense`、`smooth-scrolling`，换掉 `blender`、`rhino`、`cad`） | 🟡 | topics 上限 20 个，所以要换 |
| GitHub 仓库 Social preview 图 | 👤 | 只能在网页 Settings 里上传；1200×630 的图已在 `website/public/social-preview.png`（8 月的，可能要换成 1.4 的新图） |
| PR：serhii-londar/open-source-mac-os-apps（51k★） | 🟡 | `applications.json` 条目已写好；他们 PR 积压 579 个，越早排队越好 |
| PR：iCHAIT/awesome-macOS（19.5k★） | 🟡 | Productivity 区，按字母序 |
| PR：jaywcjlove/awesome-swift-macos-apps | 🟡 | Menubar 区；维护者会在 X 上转发收录的 app |
| HelloGitHub 自荐 issue（中文月刊，190k★） | 🟡 | 文案已写好 |
| AlternativeTo 补简介，并标为 Mac Mouse Fix / Mos / BetterTouchTool 的替代品 | 👤 | 文案已写好，复制粘贴即可 |
| MacMenuBar.com 提交 | 👤 | 需要至少 3 张**真实的**菜单栏截图（不接受 mockup），截图必须显示菜单本身在干活 |
| MacUpdate 提交 | 👤 | 需要账号；文案已写好 |
| Apple StackExchange 回答 2 个问题（#280470 点击时滚动，16.9k 浏览；#442517 PS4 手柄当鼠标，0 回答） | 🟡 | 必须写明"我是作者"并真正回答问题；需要你的 SE 账号 |
| r/macapps 先攒 10 点**本版块**的 karma | 👤 | 不然发不了帖；这周在别人的帖子下认真回几条评论 |
| 宣传片（16:9 + 9:16，中英两版，38.5 秒，真实 1.4 录屏） | ✅ | `~/Movies/calmmouse-promo-1.3/renders/`；README 动图 `readme-hero.gif` 交给 README 那个 session 替换 |

### 第 2 周（11/23–11/29）：带着视频发声

| 日期 | 动作 | 状态 |
|---|---|---|
| 周一 11/23 | Peerlist Launchpad（周一开放，投票一周；Product Hunt 之前的小型发布） | 👤 |
| 周二 11/24 上午（太平洋时间） | X 主帖 + 3 条串推，带 16:9 视频，置顶一周 | 🟡 / 👤 |
| 周二 11/24 | LinkedIn，同一个视频（链接放第一条评论） | 🟡 / 👤 |
| 周三 11/25 | V2EX 分享创造；小红书 + B站发 9:16 中文版 | 🟡 / 👤 |
| 周三 11/25 | r/macapps（标题带 `[OS]`，选 Free flair，写明是开发者；每 30 天只能发一次） | 🟡 / 👤 |
| 11/26 美国感恩节 | 不发英文大帖 | — |
| 周日 11/29 约 00:00 UTC（周六下午太平洋时间） | Show HN，链接 GitHub 仓库，自己写第一条评论，当天守着回复 | 🟡 / 👤 |

### 第 3–4 周（11/30–12/13）：放大和长尾

- r/macgaming：只讲手柄当鼠标（沙发上用 Mac 看视频、玩模拟器之间切换）。
- 掘金：技术文章（CGEventTap 区分设备、私有 MultitouchSupport、GameController 后台监听）；知乎：回答已有的妙控鼠标问题。
- 少数派：申请写作权限，投 Matrix「开发者说」。
- Product Hunt：12 月第一或第二周的周二（离 Locant 至少两周）。
- 新闻线索：给 9to5Mac 和 MacRumors 的 tips 邮箱发一段短 pitch + 视频（🟡 草稿已写，你批准后由你发送）。
- 视情况再做：openalternative.co、Uneed、SaaSHub。

## 不做的事

- Homebrew 官方 cask：8 星远低于自提交门槛（225），等 75 星后让用户提交。
- r/MacOS（只允许 App Store 应用自荐）、r/opensource（要求项目一年以上且多人维护）、r/apple（禁自荐）。
- Apple 支持社区：11/20 永久关闭，已只读。
- 各种"Mac 玩儿法"类中文站：多数分发盗版，不去。

## 节奏原则

第一周的任务不是下载量，而是回复。每一条带 `--status` 输出的 bug 报告都是下一个版本的更新说明；在 HN、Reddit、V2EX 上快速、友好、不辩解的回复，才是这些社区真正会顶的东西。

## 怎么衡量

每周一看一次：GitHub release 下载（`CalmMouse.zip` 是新装 + brew，`CalmMouse-update.zip` 是更新）、stars、GitHub traffic referrers、Vercel Web Analytics 的 Referrers（API 会 404，要在面板里看）。
