# CalmMouse 1.4 文案草稿

Nothing here has been posted or submitted. Each block is ready to copy; edit freely.
Links: site https://calmmouse.malikzhang.com · repo https://github.com/Malik1942/CalmMouse

---

## 1. GitHub repo metadata

**Description**

> Stop your Magic Mouse scrolling the page every time you click. Also smooth scrolling for wheel mice, and a game controller as a mouse. macOS menu bar app.

**Topics** (limit 20): remove `blender`, `rhino`, `cad`; add `game-controller`, `dualsense`, `smooth-scrolling`.

---

## 2. Awesome lists

### serhii-londar/open-source-mac-os-apps — append to `applications.json`

```json
{
  "title": "CalmMouse",
  "short_description": "Menu bar app that stops the Magic Mouse scrolling the page every time you click, with tap to click, straight-line scrolling and per-app rules; also smooth scrolling for wheel mice and a game controller's touchpad or sticks as a mouse.",
  "categories": ["utilities", "menubar"],
  "repo_url": "https://github.com/Malik1942/CalmMouse",
  "icon_url": "https://raw.githubusercontent.com/Malik1942/CalmMouse/main/docs/images/icon-256.png",
  "screenshots": [
    "https://raw.githubusercontent.com/Malik1942/CalmMouse/main/docs/images/settings-general.png",
    "https://raw.githubusercontent.com/Malik1942/CalmMouse/main/docs/images/welcome-presets.png"
  ],
  "official_site": "https://calmmouse.malikzhang.com",
  "languages": ["swift"]
}
```

PR title: `Add CalmMouse` · Category: Utilities, Menubar · one app per PR.

### iCHAIT/awesome-macOS — Productivity, alphabetical

```markdown
- [CalmMouse](https://calmmouse.malikzhang.com) - Stops the Magic Mouse scrolling the page every time you click, and smooths wheel mice. [![Open-Source Software][OSS Icon]](https://github.com/Malik1942/CalmMouse) ![Freeware][Freeware Icon]
```

PR title: `Add CalmMouse` (fill in the PR template or it gets closed).

### jaywcjlove/awesome-swift-macos-apps — Menubar

```markdown
- [CalmMouse](https://github.com/Malik1942/CalmMouse) ![](https://img.shields.io/github/stars/Malik1942/CalmMouse?style=flat&label=) ![](https://img.shields.io/github/last-commit/Malik1942/CalmMouse?style=flat&label=) - Stop the Magic Mouse scrolling when you click; smooth wheel mice; a game controller as a mouse.
```

(Match the exact badge markup of neighbouring entries when making the edit.)

### HelloGitHub 自荐 issue（[开源推荐]）

- 项目地址：https://github.com/Malik1942/CalmMouse
- 类别：Swift
- 标题：CalmMouse：让妙控鼠标点击时不再滚动页面
- 描述：妙控鼠标的整个上表面都是触控板，点击时手指的微小滑动也会被当成滚动，页面会在点击落下之前先动一下。CalmMouse 是一个 macOS 菜单栏小工具，只拦截"按下时"的那一点滚动，其它照常。现在也支持普通滚轮鼠标的平滑滚动和单独反转方向，还能把 PS5/PS4 手柄的触控板当成触控板用。
- 亮点：开源免费（MIT），已签名公证；只管 Magic Mouse 的滚动，触控板完全不受影响；按 App 单独设置（比如在 Figma 里彻底关掉妙控鼠标滚动）；中英繁三语界面；代码里有手势状态机的单元测试。

---

## 3. AlternativeTo description (paste into the existing listing)

> CalmMouse is a free, open-source macOS menu bar app that stops the Magic Mouse from scrolling the page every time you click. The Magic Mouse's whole top is a touch surface that stays live while you click, so every click is also a tiny swipe; CalmMouse holds the page still during clicks, drags and long presses. It also adds straight-line scrolling, a momentum switch, tap to click, per-app rules and a low-battery warning. It also smooths scroll-wheel mice, reverses each mouse's direction on its own, and turns a game controller into a mouse (a PS5/PS4 touchpad works like a trackpad). Signed and notarized; English, Simplified and Traditional Chinese.

Mark as alternative to: Mac Mouse Fix, Mos, LinearMouse, BetterTouchTool.

---

## 4. X (post 1 carries the 16:9 video)

**Post 1**

> My Magic Mouse scrolled the page a little every time I clicked it. macOS has no setting for that, so I built one.
>
> CalmMouse 1.4 is out: free, open source, and it also smooths wheel mice and turns a PS5 controller's touchpad into a trackpad.
>
> calmmouse.malikzhang.com

**Reply 1**

> Why it happens: the whole top of the Magic Mouse is a touch surface, and it stays live while you click. So every click is also a tiny swipe, and the page moves before the click lands.

**Reply 2**

> And: plug in any controller and use it as a mouse. On a PS5/PS4 pad the touchpad points, taps click, two fingers scroll. The sticks work on every controller. It steps aside the moment a game is in front.

**Reply 3**

> MIT on GitHub, signed and notarized, nothing leaves your Mac: github.com/Malik1942/CalmMouse
> `brew install --cask malik1942/calmmouse/calmmouse`

---

## 5. LinkedIn (link in the first comment)

> I shipped CalmMouse 1.4, a small free Mac app that started with one annoyance: my Magic Mouse scrolled the page every time I clicked.
>
> The top of the mouse is a touch surface, and it stays live while you click, so every click is also a tiny swipe. Links move before the click lands; in Figma the canvas pans as you grab a layer. macOS has no setting for it, so CalmMouse is that setting.
>
> It now goes wider: scroll-wheel mice get smooth scrolling and their own direction, and a game controller can drive the pointer, with a PS5 controller's touchpad working like a trackpad.
>
> The hard part was never the code. It was deciding what counts as intent: when is a touch a click, when is a finger just resting, when should a drag really press down.
>
> Free and open source. Link in the first comment.

---

## 6. r/macapps (flair: Free; I'm the developer)

**Title**

> [OS] I built the missing Magic Mouse setting: the page stops scrolling every time you click. Also wheel mice and game controllers. Free.

**Body**

> (video / GIF)
>
> Dev here. The whole top of a Magic Mouse is a touch surface that stays live while you click, so every click is also a tiny swipe: links move before the click lands, canvases pan when you grab a layer. CalmMouse blocks scrolling only while a click is down, and leaves everything else alone. Your trackpad is never touched.
>
> Also in there: straight-line scrolling, a momentum switch for the Magic Mouse alone, tap to click, and per-app rules (e.g. no Magic Mouse scrolling in Figma).
>
> Also: smooth scrolling and per-mouse direction for wheel mice, and a game controller as a mouse (PS5/PS4 touchpad works like a trackpad; sticks on any controller).
>
> Free, MIT, signed and notarized, no account, no network calls except the update check.
> Site: https://calmmouse.malikzhang.com · Code: https://github.com/Malik1942/CalmMouse
>
> If something feels off, the menu has Send Feedback… which fills in a GitHub issue. Happy to answer anything here.

---

## 7. Show HN

**Title:** `Show HN: CalmMouse – stop the Magic Mouse scrolling every time you click`
**URL:** https://github.com/Malik1942/CalmMouse

**First comment**

> I built this because my Magic Mouse scrolled the page a little on almost every click. The top of the mouse is a multitouch surface that stays live while the button is down, so the finger's tiny roll during a click arrives as scroll events, and they arrive before the click.
>
> CalmMouse sits in a CGEventTap and swallows scroll events while a button is held, plus a short grace period for the finger coming off the shell. The interesting parts were attribution (telling Magic Mouse scrolls apart from the trackpad's, using the event's sender ID and the IORegistry entries behind it) and gesture bookkeeping: a gesture that starts under a click stays swallowed until the finger lifts, and an app that was mid-scroll gets a clean "ended" event so it doesn't hang.
>
> It also handles wheel mice (smooth scrolling, per-mouse direction) and a game controller as a mouse through GameController's background monitoring; a PS5 touchpad behaves like a small trackpad.
>
> MIT, Swift, notarized. I'd love to hear where it misfires, especially from people on the USB-C Magic Mouse.

---

## 8. V2EX 分享创造

**标题**：做了个小工具：妙控鼠标点一下，页面不再跟着滑一下（现在也支持滚轮鼠标和手柄）

**正文**

> 用妙控鼠标的应该都遇到过：点链接的时候页面先动了一下，点歪了；在 Figma 里选个图层，画布跟着平移。原因是妙控鼠标的整个上表面都是触控板，按下去的时候手指的微小滑动也会被当成滚动，而且这些滚动事件比点击先到。
>
> macOS 没有任何设置能关掉它，所以我写了 CalmMouse：一个菜单栏小工具，只在按下的那一刻拦住滚动，平时滚动完全照常，触控板不受任何影响。
>
> 顺手做的：直线滚动（不再斜着漂）、单独关掉妙控鼠标的惯性、轻点点按、按 App 单独设置（比如 Figma 里完全关掉妙控鼠标滚动）、低电量提醒。
>
> 现在还支持：普通滚轮鼠标的平滑滚动和单独反转方向；游戏手柄当鼠标，PS5/PS4 手柄的触控板就像一块小触控板，摇杆在所有手柄上都能用，前台是游戏时自动让开。
>
> 免费开源（MIT），已签名公证，中文界面。
> 官网：https://calmmouse.malikzhang.com
> GitHub：https://github.com/Malik1942/CalmMouse
> Homebrew：`brew install --cask malik1942/calmmouse/calmmouse`
>
> 有问题直接回帖或者在菜单里点「发送反馈…」。

---

## 9. 小红书 / B站（配 9:16 中文版视频）

**标题**：妙控鼠标一点就滑？终于有开关了

**正文**

> 妙控鼠标的上表面整块都是触控板，点击时手指一动，页面就先滑一下 😮‍💨
> 我做了个免费开源的小工具 CalmMouse，点击时把这点滑动拦下来，平时滚动照常。
> 新版还支持普通滚轮鼠标的丝滑滚动，PS5 手柄的触控板也能当触控板用 🎮
> 官网 calmmouse.malikzhang.com
>
> #妙控鼠标 #Mac软件 #macOS #效率工具 #开源 #MagicMouse #Mac必备软件

---

## 10. Apple StackExchange answers (disclose authorship)

**apple.stackexchange.com/questions/280470 — disable scrolling while clicking with the Magic Mouse**

> Disclosure: I wrote the app below.
>
> The scroll you see comes from the Magic Mouse's touch surface: the finger rolls slightly as the button goes down, and those scroll events arrive before the click. The BetterTouchTool setting in the top answer handles it if you already use BTT. Without BTT, CalmMouse (free, MIT, https://github.com/Malik1942/CalmMouse) does just this: it drops Magic Mouse scroll events while a button is held, plus a short grace period (200 ms by default, adjustable) for the finger lifting off. Trackpad scrolling isn't affected. Install with `brew install --cask malik1942/calmmouse/calmmouse`, then grant it Accessibility in System Settings → Privacy & Security.

**apple.stackexchange.com/questions/442517 — PS4 controller as a mouse**

> Disclosure: I wrote the app below.
>
> macOS supports the DualShock 4 and DualSense as game controllers, but has no built-in way to drive the pointer with one. CalmMouse (free, MIT, https://github.com/Malik1942/CalmMouse), version 1.3 or later, adds that: turn on "Controller as mouse" from its menu bar item (or hold Options + Create for half a second). The touchpad then works like a trackpad: slide to point, tap to click, two fingers to scroll or right-click, press to drag. On any controller the left stick moves the pointer, the right stick scrolls, ✕ clicks and ○ right-clicks. It pauses itself while a game is the frontmost app. Requires macOS 13+ and the Accessibility permission.

---

## 11. Press tip (9to5Mac / MacRumors) — you send it, from your own mail

**Subject:** Free Mac app stops the Magic Mouse scrolling when you click (now with PS5 controller support)

> Hi,
>
> I'm the developer of CalmMouse, a free, open-source menu bar app that fixes a long-standing Magic Mouse annoyance: its touch surface stays live while you click, so the page often scrolls a little before the click lands. CalmMouse blocks scrolling only while the button is down.
>
> Version 1.4, out this week, also has smooth scrolling for wheel mice and lets a game controller drive the pointer; a PS5 controller's touchpad works like a trackpad.
>
> 35-second video: (link)
> Site: https://calmmouse.malikzhang.com · Code: https://github.com/Malik1942/CalmMouse
>
> Happy to answer anything.
> Malik

---

## 12. Video files and credits

Promo video (38.5 s), in `~/Movies/calmmouse-promo-1.3/renders/`:
`calmmouse-promo-en-16x9.mp4` (X, LinkedIn, YouTube, HN/Reddit links), `calmmouse-promo-en-9x16.mp4` (Reels/Shorts),
`calmmouse-promo-zh-16x9.mp4` (B站横版), `calmmouse-promo-zh-9x16.mp4` (小红书, 抖音, B站竖屏).

Credits to include where the video is posted publicly (description or first comment):

> Music: Salamander Grand Piano by Alexander Holm (CC BY 3.0).

Check before posting: the licenses of the DualSense and MX Master models CalmMouse 1.4 renders (they appear in the app
footage and the MX Master hero shot); a CC BY model needs its author credited the same way.
