# Cursor 额度仪表盘

macOS 菜单栏与 Windows 托盘小工具，显示当前 Cursor 账号的限额与额外额度池。

macOS 1.5.1 的菜单栏显示主限额和额外池的剩余比例；详情卡片按新账户展示周限额和额外池的美元余额 / 总额，同时兼容旧月套餐。Windows 托盘继续显示内置与其他模型池的两行百分比。Windows 鼠标悬停时，余额显示在图标正上方，图标在任务栏或隐藏图标面板里都一样。点击状态栏 / 托盘图标后展开详情；点击外部或按 Esc 关闭弹窗。右键可立即刷新、打开官方用量页或退出。

本工具读取的是 **Cursor 套餐用量**（剩余值、重置时间，以及内置额度和额外额度池），不是 ChatGPT 或 Codex 的限额窗口。

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
- macOS 菜单栏将第二行统一标为「额外」；应用图标与菜单栏标记使用同一套扁平矢量图形
- macOS 1.5.1 按套餐周期显示周限额或旧月限额；额外额度池优先显示信用额度余额 / 总额
- 显示套餐 / 计划名称
- Windows 显示本周期内置模型池和其他模型池的剩余百分比，与官方的两类用量条对齐
- 未返回内置模型池比例时，回退到套餐金额额度，并显示已用金额 / 上限
- 显示计费周期重置倒计时
- 约 30 秒自动刷新；弹窗内或右键可立即刷新
- 剩余不足时圆环和百分比变黄（≤30%）或变红（≤10%）
- 接口暂时失败时，回退到本机上次成功快照，并标记为「快照」
- `--probe` 自检只输出剩余比例、套餐和采样时间，不输出会话令牌

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
   读取 `cursorAuth/accessToken`，必要时同时读取本地套餐名 `cursorAuth/stripeMembershipType`。

只要 Cursor 应用已登录，一般无需再配置。会话过期时，重新在 Cursor 里登录即可。

### 用量接口

优先使用 Cursor 当前计费周期接口，并合并套餐信息与网页用量摘要：

- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage`  
  本周期套餐用量（已用金额、额度上限、内置 / 其他模型百分比、周期起止）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo`  
  套餐名称、价格、包含额度。
- `GET https://cursor.com/api/usage-summary`  
  网页用量摘要（套餐类型、周期、内置 / 其他模型百分比、按量消耗）。
- `GET https://api2.cursor.sh/auth/usage`  
  回退：企业/旧版按「请求次数」计量的分桶用量。
- `GET https://api2.cursor.sh/auth/full_stripe_profile`  
  回退：订阅类型（Pro / Ultra / Team 等）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetCreditGrantsBalance`
  macOS 1.5.1 只读信用额度池余额、总额与已用金额（`creditBalanceCents`、`totalCents`、`usedCents`）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetUserPrepaidBilling`
  没有活动信用额度池时，只读预付额度余额、充值总额与已用金额（`availableCents`、`fundedCents`、`spentCents`）。

以上额度接口只读，不会创建或修改额度。金额字段以美分为单位，macOS 详情按美元格式显示，例如 `25` 表示 `$0.25`。这些是 Cursor 仪表盘使用的内部接口，不是公开稳定 API，字段可能变化。金额与百分比仍以官方页面为准：

<https://cursor.com/dashboard/usage>

### Windows 展示字段

| 位置 | 内容 |
| --- | --- |
| 状态栏第一行 | **内置**模型池剩余百分比；缺少模型池数据时回退为**额度** |
| 状态栏第二行 | **其他**剩余百分比（指定的第三方模型池） |
| 弹窗标题 | 套餐名称，如 `Pro 额度` |
| 第一张卡片 | 内置模型池剩余、重置倒计时、消耗速度；金额额度回退时显示已用金额 / 上限 |
| 第二张卡片 | 其他模型剩余、同一周期的重置倒计时、消耗速度（`x%/天`） |
| 底部状态 | `实时` 或 `快照`、最近采样时间；过期或出错时附加说明 |
| 颜色 | 剩余 ≤30% 黄色，≤10% 红色 |

消耗速度按「本周期已用百分比 ÷ 周期内已过去天数」计算，例如 30 天窗口已过约 3 天、已用 5%，则约为 `1.7%/天`。周期刚开始不足 1 小时时，按 1 小时折算，避免数字被放大。

## macOS 1.5.1 额度口径

新套餐若 `includedUsagePeriod` 表示 weekly，主卡按当前周期接口的 `100 - planUsage.totalPercentUsed` 显示剩余百分比；不会采用 Auto 独立池字段或旧月度摘要的金额、百分比。没有可信周数据时显示缺失状态。重置时间优先使用 `includedUsageResetsAt`；旧月套餐仍使用月周期字段。主卡标题会显示「周限额」或「月限额」。

1.5.1 修复了旧月度摘要已用 0 覆盖实际周额度已用 100% 的问题。快照格式升级为 3；旧周快照中的主额度值会等待实时刷新，额外池余额和旧月快照继续兼容。

额外池优先使用信用额度接口的 `creditBalanceCents / totalCents`；没有可用信用额度时回退到预付额度的 `availableCents / fundedCents`。详情卡显示余额 / 额度池总额，状态栏第二行在金额数据可用时显示对应剩余比例。旧月套餐也兼容 `apiLimit - apiSpend` 这组直接金额字段。只有 `apiPercentUsed` 时不会据此推算金额；金额数据缺失时详情显示 `— / —`，额外池不继承主卡的周重置时间。

## Windows 额度口径

Windows 端仍按**当前计费月**展示。根据 [官方模型与额度说明](https://cursor.com/docs/models-and-pricing#usage-pools)，内置模型（Cursor Models）和其他模型（Other Models）各有独立额度池。Windows 第一行优先按 `100 - autoPercentUsed` 计算内置模型池剩余，第二行按 `100 - apiPercentUsed` 计算其他模型池剩余。接口的这些字段直接使用百分数点，例如 `0.7` 表示已用 `0.7%`，剩余为 `99.3%`。托盘、悬浮提示和详情统一四舍五入为整数百分比。

`includedSpend / limit` 是另一种金额额度口径，可能已达到上限，而内置模型池仍有剩余；Windows 仅在接口未返回内置模型池比例时回退到这一口径。macOS 的周限额与额外额度池按上方说明读取和显示。

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
  "PlanType" : "pro",
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

这是托盘应用：启动后请看任务栏通知区域。鼠标放在图标上时，两行余额出现在图标正上方。左键打开详情，详情同样贴在图标上方。

### 从源码编译

在 Windows 上需要 .NET Framework 4.x 的 `csc.exe`。

```powershell
cd Windows\Codings
.\build-win.ps1
.\build-package.ps1
```

安装包输出到 `Windows/Packages/CursorQuotaPet-Setup.exe`。

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

**状态栏一直是「内置 —」**  
先确认 Cursor 已登录。可运行 `--probe` 查看是「未找到登录态」还是接口报错。登录后等几秒，或右键「立即刷新」。

**提示会话过期 / 未认证**  
在 Cursor 中重新登录，或按上面步骤更新 `CURSOR_SESSION_TOKEN` / 配置文件。Cookie 和本地 `accessToken` 都会过期。

**数字和官网页不一致**  
Windows 第一行应对齐官方设置页或用量页中 Cursor Models（内置模型）的剩余比例，第二行对齐 Other Models（其他模型）。金额栏的 `included usage` 与两类模型池可能采用不同上限，不能直接替代模型池剩余。本工具约 30 秒刷新一次；安装更新后请重新启动托盘程序。

**只有内置、没有其他行**  
部分套餐（尤其是企业按次计费）不返回其他模型分项。此时第二行会尽量显示请求次数剩余，或保持「—」。

**是否安全？**  
令牌只在本机读取，请求只发往 `cursor.com` 与 `api2.cursor.sh`。应用不把令牌写入项目目录或自检输出。请不要把 `session-token` 文件或环境变量提交到 git。

**和官方用量页的关系**  
这是第三方客户端，不是 Cursor 官方应用。套餐规则、限速和账单以 Cursor 官方说明为准。
