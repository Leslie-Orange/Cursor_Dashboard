# Cursor 额度仪表盘

macOS 菜单栏与 Windows 托盘小工具，显示当前 Cursor 账号的剩余额度。

Windows 和 macOS 1.5.1 的详情显示主限额与额外额度池，支持周限额及旧月套餐。Windows 托盘图标以两条动态进度条显示剩余比例，鼠标悬停显示两行数值。点击状态栏 / 托盘图标后展开详情；点击外部或按 Esc 关闭弹窗。右键可立即刷新、打开官方用量页或退出。

本工具读取的是 **Cursor 套餐用量**，不是 ChatGPT 或 Codex 的限额窗口。额度和账单最终以 Cursor 官方页面为准。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `README.md` | 开发与使用说明 |
| `Mac/Codings/` | macOS 源码与构建脚本 |
| `Mac/Packages/` | macOS 安装盘（`.dmg`） |
| `Windows/Codings/` | Windows 源码与构建脚本 |
| `Windows/Packages/` | Windows 安装包（`.exe`） |

## 功能

- 菜单栏 / 托盘常驻，无需打开主窗口
- macOS 1.5.1 详情采用系统原生玻璃材质，旧版 macOS 使用匹配的半透明效果
- Windows 1.5.1 跟随系统浅色或深色应用模式，并支持高对比度回退；应用及托盘图标使用立方体标记
- 按接口的套餐周期显示周限额或月限额，额外额度池显示可用的余额 / 总额
- 约 30 秒自动刷新；弹窗内或右键可立即刷新
- 剩余不足时进度条和数值变黄（≤30%）或变红（≤10%）
- 接口暂时失败时可回退本机上次成功快照，并标记为「快照」及显示错误
- `--probe` 输出额度、套餐和采样时间，不输出会话令牌

## 额度数据来源

程序**不保存、不上传**访问令牌，也不会把密钥写进仓库。每次刷新时，按下面顺序找到本机已有登录态，再向 Cursor 用量接口查询你自己的账号数据。

### 凭证（按优先级）

1. 环境变量 `CURSOR_SESSION_TOKEN`  
   可以是 Cursor 会话 JWT，或浏览器 Cookie `WorkosCursorSessionToken` 的完整值（`sub::jwt`）。
2. 本地配置文件  
   macOS：`~/Library/Application Support/CursorQuotaPet/session-token`  
   Windows：`%APPDATA%\CursorQuotaPet\session-token`  
   内容为一行令牌，格式同上。该目录不会被提交到 git。
3. macOS 钥匙串  
   `cursor-access-token`（`cursor-agent` CLI 登录后可能写入）。
4. 本机 Cursor 已登录会话（默认、推荐）  
   macOS：`~/Library/Application Support/Cursor/User/globalStorage/state.vscdb`  
   Windows：`%APPDATA%\Cursor\User\globalStorage\state.vscdb`  
   读取 `cursorAuth/accessToken`，必要时同时读取本地套餐名 `cursorAuth/stripeMembershipType`。Windows 支持 Cursor 3.27 的 `cursor-safe-storage:v1:` 加密凭据，使用当前 Windows 用户及 Cursor 本地密钥在内存中解码；还会尝试 `cachedAccessToken`、`webAccessToken` 和本地 `auth.json` 中的登录态。遇到未认证时可尝试其他候选凭证。

只要 Cursor 应用已登录，一般无需再配置。会话过期时，重新在 Cursor 里登录即可。

### 用量接口

优先使用 Cursor 当前计费周期接口，并合并套餐信息与网页用量摘要：

- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage`  
  本周期套餐用量（已用金额、额度上限、模型池百分比、账单周期）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo`  
  套餐名称、价格、包含额度。
- `GET https://cursor.com/api/usage-summary`  
  网页用量摘要（套餐类型、周期、模型池百分比、按量消耗）。
- `GET https://api2.cursor.sh/auth/usage`  
  回退：旧版按「请求次数」计量的分桶用量。
- `GET https://api2.cursor.sh/auth/full_stripe_profile`  
  回退：订阅类型（Free / Plus / Premium / Pro / Ultra / Team 等）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetCreditGrantsBalance`
  只读信用额度池余额、总额与已用金额（`creditBalanceCents`、`totalCents`、`usedCents`）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetUserPrepaidBilling`
  没有活动信用额度池时，只读预付额度余额、充值总额与已用金额（`availableCents`、`fundedCents`、`spentCents`）。

以上额度接口只读，不会创建或修改额度。金额字段以美分为单位，详情按美元格式显示，例如 `25` 表示 `$0.25`。这些是 Cursor 仪表盘使用的内部接口，不是公开稳定 API，字段可能变化。金额与百分比仍以官方页面为准：

<https://cursor.com/dashboard/usage>

### Windows 1.5.1 展示字段

| 位置 | 内容 |
| --- | --- |
| 托盘图标及悬浮提示 | 两条进度条对应主限额和额外额度池；悬浮提示显示两行剩余比例 |
| 弹窗标题 | 套餐名称和可用的重置时刻 |
| 主卡片 | 周限额或月限额的剩余比例和可用的重置时间 |
| 额外卡片 | 剩余比例及余额 / 总额；金额未知时显示 `— / —` |
| 底部状态 | 刷新中、最近采样时间或快照状态；刷新失败时显示原因 |

Windows 按剩余比例设置黄色（≤30%）和红色（≤10%）提示，无法确定时显示缺失状态。

## macOS 1.5.1 额度口径

新套餐若 `includedUsagePeriod` 表示 weekly，主卡按当前周期接口的 `100 - planUsage.totalPercentUsed` 显示剩余百分比；不会采用 Auto 独立池字段或旧月度摘要的金额、百分比。没有可信周数据时显示缺失状态。重置时间优先使用 `includedUsageResetsAt`；旧月套餐仍使用月周期字段。主卡标题会显示「周限额」或「月限额」。旧月套餐主卡沿用 PR 的金额口径，优先按接口返回的额度金额计算剩余；只有金额数据缺失时才回退到模型池百分比。

1.5.1 修复了旧月度摘要已用 0 覆盖实际周额度已用 100% 的问题。快照格式升级为 3；旧周快照中的主额度值会等待实时刷新，额外池余额和旧月快照继续兼容。

额外池优先使用信用额度接口的 `creditBalanceCents / totalCents`；没有可用信用额度时回退到预付额度的 `availableCents / fundedCents`。详情卡显示余额 / 额度池总额，状态栏第二行在金额数据可用时显示对应剩余比例。旧月套餐也兼容 `apiLimit - apiSpend` 这组直接金额字段。只有 `apiPercentUsed` 时不会据此推算金额；金额数据缺失时详情显示 `— / —`，额外池不继承主卡的周重置时间。

## Windows 1.5.1 额度口径

接口的 `includedUsagePeriod` 表示 weekly 时，主卡使用当前周期接口的 `100 - planUsage.totalPercentUsed`，重置时间使用 `includedUsageResetsAt`；没有可信周数据时显示缺失状态，不用旧月摘要覆盖。非周套餐优先根据返回的额度金额计算，金额缺失时回退至可用的模型池百分比。

额外池优先使用信用额度的 `creditBalanceCents / totalCents`，没有可用信用额度时回退至预付额度的 `availableCents / fundedCents`；旧月套餐也兼容 `apiLimit - apiSpend` 这组金额字段。金额缺失时不根据百分比推算，零余额仍显示为零。额外池不继承主卡的周重置时间。

Windows 快照格式升级为 3，旧版 Windows 快照不会迁移；升级后需成功刷新一次才能建立新快照。刷新失败时，如有新格式快照则显示快照和错误信息。并发刷新会合并，候选凭据各自分配请求预算并共享总时限。只有部分额度可用时显示「部分」及缺失原因。

## macOS 安装（推荐）

1.5.1 安装盘：[Cursor额度仪表盘-1.5.1.dmg](Mac/Packages/Cursor额度仪表盘-1.5.1.dmg)。把 **Cursor额度仪表盘** 拖到 **应用程序** 即可。

这是菜单栏应用：启动后请看屏幕右上角，不会在 Dock 常驻。首次打开若被系统拦截，按住 Control 单击应用并选择「打开」。

重新打包安装盘：

```zsh
chmod +x "./Mac/Codings/pack-dmg.sh"
./Mac/Codings/pack-dmg.sh
```

会生成通用二进制（Apple Silicon + Intel）安装盘：`Mac/Packages/Cursor额度仪表盘-1.5.1.dmg`。

## macOS 从源码启动

双击 `Mac/Codings/Start-CursorQuotaPet.command`。首次运行会编译并打开 `Mac/Codings/CursorQuotaPet.app`；之后也可以直接双击该应用。

如果系统阻止 `.command` 或 `.app`，请在「系统设置 → 隐私与安全性」中允许打开，或先在终端执行：

```zsh
chmod +x "./Mac/Codings/Start-CursorQuotaPet.command" "./Mac/Codings/build-mac.sh"
```

## macOS 编译与自检

需要已安装 Xcode Command Line Tools（提供 `swiftc`）。

```zsh
./Mac/Codings/build-mac.sh
./Mac/Codings/CursorQuotaPet.app/Contents/MacOS/CursorQuotaPet --probe
./Mac/Codings/test-appearance.sh
```

`build-mac.sh` 会按当前 Mac 的 CPU 架构编译，并以 macOS 13.0 为最低兼容版本。自检成功时输出类似：

```json
{
  "IncludedRemain" : 76,
  "OtherRemain" : 55,
  "PlanNote" : "月度双池",
  "PlanType" : "pro",
  "PrimaryBadge" : "内置",
  "SecondaryBadge" : "其他",
  "SourceName" : "cursor-api",
  "Status" : "ok"
}
```

自检失败时只会说明原因（未登录、网络错误等），不会打印令牌。

外观预览可以离线运行，不需要 Cursor 登录态，也不会读取或写入快照缓存、请求网络：

```zsh
./Mac/Codings/CursorQuotaPet.app/Contents/MacOS/CursorQuotaPet --preview-ui=normal --appearance=light
./Mac/Codings/CursorQuotaPet.app/Contents/MacOS/CursorQuotaPet --preview-ui=warning --appearance=dark
./Mac/Codings/CursorQuotaPet.app/Contents/MacOS/CursorQuotaPet --preview-ui=snapshot --appearance=dark --preview-reduce-transparency --preview-reduce-motion
```

`--preview-ui=` 接受 `normal`、`warning`、`critical`、`empty`、`snapshot` 和 `error` 六种样例：前 3 种显示当前样例，`empty` 显示无数据状态，`snapshot` 显示快照，`error` 显示网络错误与快照。`--appearance=light|dark` 只覆盖预览进程的应用外观；两个 `--preview-reduce-*` 参数用于检查降低透明度和减少动态效果时的界面。预览会自动打开详情。

要用本机真实额度直接打开详情（会读取登录态并正常联网刷新），运行：

```zsh
./Mac/Codings/CursorQuotaPet.app/Contents/MacOS/CursorQuotaPet --show-details
```

`test-appearance.sh` 会在临时目录编译离线断言套件，不启动 `--probe`，也不构建或打包应用。

## Windows 安装

运行 [`Windows/Packages/CursorQuotaPet-Setup.exe`](Windows/Packages/CursorQuotaPet-Setup.exe)。若本机已经装过，会装回原来的目录；否则默认安装到 `%LOCALAPPDATA%\Programs\Cursor仪表盘`，并创建开始菜单与桌面快捷方式。

这是托盘应用：启动后请看任务栏通知区域。鼠标放在图标上时，图标上方显示两行剩余比例；左键打开详情。

### Windows 卸载

在 Windows「设置 → 应用 → 已安装的应用」中找到 **Cursor仪表盘**，选择「卸载」；也可以运行安装目录中的 `Uninstall.exe`。卸载会移除本工具的程序文件、对应快捷方式及系统登记，保留 Cursor 编辑器、独立保存的登录信息和缓存，以及安装目录中的未知用户文件。

`Windows/Codings/install.ps1` 同样通过正式安装包安装，包含系统卸载入口。

### 从源码编译

在 Windows 上需要 .NET Framework 4.x 的 `csc.exe`。

```powershell
cd Windows\Codings
.\build-win.ps1
.\build-package.ps1
```

安装包输出到 `Windows/Packages/CursorQuotaPet-Setup.exe`。

自检与离线外观预览：

```powershell
.\CursorQuotaPet.exe --self-test
.\CursorQuotaPet.exe --preview-ui=normal --appearance=light
.\CursorQuotaPet.exe --preview-ui=warning --appearance=dark
.\CursorQuotaPet.exe --render-previews
```

预览支持 `normal`、`warning`、`critical`、`empty`、`snapshot`、`error`、`partial` 七种状态和 `light` / `dark` 外观，不读取登录态或快照，也不联网；预览中的刷新按钮不执行刷新。批量预览输出到 `Windows/Previews/1.5.1/`。`--probe` 则使用真实登录态与网络：完整取数时 `Status` 为 `ok`；主额度缺失而保留额外池时为 `partial`，退出码为 1；刷新失败而回退到快照时为 `snapshot`，退出码为 1；无可用数据时为 `unavailable`，退出码为 1。

## 配置

大多数情况：打开 Cursor 并登录，再启动本工具即可。

若本机读不到会话（例如 Cursor 装在其他用户目录、会话过期、只想用浏览器 Cookie）：

1. 打开 <https://cursor.com/dashboard/usage> 并登录。
2. 浏览器开发者工具 → 应用程序 / Application → Cookies → `https://cursor.com`。
3. 复制 `WorkosCursorSessionToken` 的值。
4. 任选一种方式提供给本工具（不要提交到 git）：

```zsh
# 方式 A：环境变量（当前终端有效）
export CURSOR_SESSION_TOKEN='user_…::eyJhbGci…'

# 方式 B：本地配置文件
# macOS
mkdir -p "$HOME/Library/Application Support/CursorQuotaPet"
printf '%s\n' 'user_…::eyJhbGci…' > "$HOME/Library/Application Support/CursorQuotaPet/session-token"
chmod 600 "$HOME/Library/Application Support/CursorQuotaPet/session-token"

# Windows PowerShell
New-Item -ItemType Directory -Force -Path "$env:APPDATA\CursorQuotaPet" | Out-Null
Set-Content -Path "$env:APPDATA\CursorQuotaPet\session-token" -Value 'user_…::eyJhbGci…' -Encoding ascii
```

Cursor 用户 API Key（`crsr_…`）**不能**读取用量，请不要把它当作本工具的凭证。

## 菜单

| 操作 | 作用 |
| --- | --- |
| 左键点击状态栏 / 托盘 | 打开 / 关闭额度详情 |
| 右键点击状态栏 / 托盘 | 打开菜单 |
| 显示额度 | 展开详情弹窗 |
| 立即刷新 | 马上重新请求用量 |
| 打开 Cursor 用量页 | 在浏览器打开官方仪表盘 |
| 退出 | 退出本工具 |

## 常见问题

**Windows 额度一直显示「—」**

先确认 Cursor 已登录，可运行 `--probe` 查看未找到登录态、接口错误或快照状态。登录后等待刷新，或右键「立即刷新」。周限额缺少可信当前周期数据时会显示「—」；额外额度缺少金额时显示 `— / —`。

**提示会话过期 / 未认证**  
在 Cursor 中重新登录，或按上面步骤更新 `CURSOR_SESSION_TOKEN` / 配置文件。Cookie 和本地 `accessToken` 都会过期。

**数字和官网页不一致**  
请核对套餐周期、主限额和额外额度池的口径，并以 [Cursor 官方用量页](https://cursor.com/dashboard/usage)为准。本工具约 30 秒刷新一次；安装更新后请重新启动托盘程序。接口暂时失败时显示的可能是标记为「快照」的上次数据。

**是否安全？**  
令牌只在本机读取，请求只发往 `cursor.com` 与 `api2.cursor.sh`。应用不把令牌写入项目目录或自检输出。请不要把 `session-token` 文件或环境变量提交到 git。

**和官方用量页的关系**  
这是第三方客户端，不是 Cursor 官方应用。套餐规则、限速和账单以 Cursor 官方说明为准。
