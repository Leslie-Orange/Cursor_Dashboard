# Cursor 额度仪表盘

macOS 菜单栏与 Windows 托盘小工具，显示当前 Cursor 账号的剩余额度。

Windows 状态栏按套餐显示两行剩余：现行套餐显示 `本周 76%`，第二行显示 `预付 按量` 或 `第三方 不含`；旧档显示 `内置 76%` 和 `其他 45%`。macOS 1.5.1 的菜单栏显示主限额和额外额度池的剩余比例，详情卡片显示周限额和额外池的美元余额 / 总额，同时兼容旧月套餐。Windows 托盘图标显示两行数字；鼠标悬停时，余额显示在图标正上方，图标在任务栏或隐藏图标面板里都一样。点击状态栏 / 托盘图标后展开详情；点击外部或按 Esc 关闭弹窗。右键可立即刷新、打开官方用量页或退出。

本工具读取的是 **Cursor 套餐用量**，不是 ChatGPT 或 Codex 的限额窗口。现行 X Subscription 使用每周刷新的 Cursor 模型池；旧档 Pro / Pro+ 等仍使用两个按月刷新的池。

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
- Windows 显示套餐 / 计划名称和官方包含额度副标题（例如 Premium 为 `4× Plus · 每周`）
- Windows 和 macOS 识别相同的现行与旧版套餐档位
- Windows 现行档位显示本周 Cursor 模型池剩余；Premium 及以上的第二行是第三方预付，Plus 和 Free 标明第三方不含
- 旧档仍显示内置模型池和其他模型池的剩余百分比
- Windows 未返回主模型池比例时按接口金额额度回退；macOS 旧月套餐沿用 PR 的金额口径，详见下方专项说明
- Windows 旧档和企业共享池显示账单周期倒计时；Windows 现行个人档位显示每周重置
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
  macOS 1.5.1 只读信用额度池余额、总额与已用金额（`creditBalanceCents`、`totalCents`、`usedCents`）。
- `POST https://api2.cursor.sh/aiserver.v1.DashboardService/GetUserPrepaidBilling`
  没有活动信用额度池时，只读预付额度余额、充值总额与已用金额（`availableCents`、`fundedCents`、`spentCents`）。

以上额度接口只读，不会创建或修改额度。金额字段以美分为单位，macOS 详情按美元格式显示，例如 `25` 表示 `$0.25`。这些是 Cursor 仪表盘使用的内部接口，不是公开稳定 API，字段可能变化。金额与百分比仍以官方页面为准：

<https://cursor.com/dashboard/usage>

### Windows 展示字段

| 位置 | 现行档位（Free / Plus / Premium / Super / Ultra / Teams） | 旧档（Pro / Pro+ / Teams Standard 等） |
| --- | --- | --- |
| 状态栏第一行 | **本周** Cursor 模型池剩余；Free 为 **有限** | **内置**模型池剩余；缺少比例时回退为 **额度** |
| 状态栏第二行 | Premium 及以上为 **预付**；Plus / Free 为 **第三方 不含** | **其他**模型池剩余 |
| 弹窗标题 | 套餐名称和官方包含额度，如 `Premium 额度`、`4× Plus · 每周` | 套餐名称，如 `Pro 额度`、`月度双池` |
| 第一张卡片 | 本周剩余和 `每周重置`；卡片右侧是该档倍数，如 `4× Plus` | 内置模型池剩余、重置倒计时、消耗速度 |
| 第二张卡片 | 第三方预付。接口若返回余额则显示金额，否则显示 `按量` | 其他模型剩余、同一周期的重置倒计时 |
| 底部状态 | `实时` 或 `快照`、最近采样时间；过期或出错时附加说明 | 同左 |
| 颜色 | 剩余 ≤30% 黄色，≤10% 红色 | 同左 |

Windows 消耗速度只在能确定用量窗口时计算，按「本窗口已用百分比 ÷ 已经过天数」。周期刚开始不足 1 小时时，按 1 小时折算，避免数字被放大。Windows 现行档位的用量按周重置；官方页面没有给出具体的下周重置时刻，因此不把月度账单日当成 Windows 用量重置日。

## 套餐档位与包含额度

档位和包含额度以 2026-10-20 打开的官方页面为准：[定价](https://cursor.com/pricing)、[模型与价格](https://cursor.com/docs/models-and-pricing)、[价格与方案](https://cursor.com/help/account-and-billing/pricing)。现行个人方案是 Free、Plus、Premium、Super、Ultra。包含额度是 Plus 的倍数（Plus 为基础周池，Premium 4×，Super 15×，Ultra 50×），不是已经停用的 premium requests 次数。官方没有公布 Plus 基础池的绝对请求次数或美元上限，Windows 界面不显示这类推测数字。

旧档 Pro、Pro+、Ultra，以及 Teams Standard / Teams Premium，仍可能出现在已有订阅上。它们继续用两个按月重置的池：Cursor Models 和 Other Models。Pro 没有被改名为 Premium；新方案里与它最接近的是 Premium，价格和额度都不同。

Windows 的现行周池和旧月模型池按模型池百分比显示。`autoPercentUsed` 是 Cursor 模型池已用百分比，字段直接使用百分数点，例如 `0.7` 表示已用 `0.7%`，剩余为 `99.3%`；Windows 旧档的其他模型池使用 `apiPercentUsed`。Windows 托盘、悬浮提示和详情统一四舍五入为整数百分比。Windows 仅在接口没有返回主模型池比例时，才回退到 `includedSpend / limit`。Windows 现行档位不把 `apiPercentUsed` 当成已包含的「其他模型」额度，因为第三方模型不再包含在周池里。

## macOS 1.5.1 额度口径

新套餐若 `includedUsagePeriod` 表示 weekly，主卡按当前周期接口的 `100 - planUsage.totalPercentUsed` 显示剩余百分比；不会采用 Auto 独立池字段或旧月度摘要的金额、百分比。没有可信周数据时显示缺失状态。重置时间优先使用 `includedUsageResetsAt`；旧月套餐仍使用月周期字段。主卡标题会显示「周限额」或「月限额」。旧月套餐主卡沿用 PR 的金额口径，优先按接口返回的额度金额计算剩余；只有金额数据缺失时才回退到模型池百分比。

1.5.1 修复了旧月度摘要已用 0 覆盖实际周额度已用 100% 的问题。快照格式升级为 3；旧周快照中的主额度值会等待实时刷新，额外池余额和旧月快照继续兼容。

额外池优先使用信用额度接口的 `creditBalanceCents / totalCents`；没有可用信用额度时回退到预付额度的 `availableCents / fundedCents`。详情卡显示余额 / 额度池总额，状态栏第二行在金额数据可用时显示对应剩余比例。旧月套餐也兼容 `apiLimit - apiSpend` 这组直接金额字段。只有 `apiPercentUsed` 时不会据此推算金额；金额数据缺失时详情显示 `— / —`，额外池不继承主卡的周重置时间。

## Windows 额度口径

Windows 按套餐档位展示额度。现行方案的主池按周重置；旧档的 Cursor Models 和 Other Models 仍按月分别重置。根据 [官方模型与额度说明](https://cursor.com/docs/models-and-pricing#usage-pools)，Windows 第一行使用 `100 - autoPercentUsed` 计算 Cursor 模型池剩余；旧档第二行按 `100 - apiPercentUsed` 计算其他模型池剩余。接口的这些字段直接使用百分数点，例如 `0.7` 表示已用 `0.7%`，剩余为 `99.3%`。托盘、悬浮提示和详情统一四舍五入为整数百分比。

`includedSpend / limit` 是另一种金额额度口径，可能已达到上限，而模型池仍有剩余；Windows 仅在接口未返回主模型池比例时回退到这一口径。

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

**Windows 状态栏一直是「本周 —」或「内置 —」**

先确认 Cursor 已登录。可运行 `--probe` 查看是「未找到登录态」还是接口报错。登录后等几秒，或右键「立即刷新」。

**提示会话过期 / 未认证**  
在 Cursor 中重新登录，或按上面步骤更新 `CURSOR_SESSION_TOKEN` / 配置文件。Cookie 和本地 `accessToken` 都会过期。

**数字和官网页不一致**  
Windows 现行档位的第一行应对齐 Cursor 编辑器设置或 [Cursor 官方用量页](https://cursor.com/dashboard/usage)中的 Cursor 模型周池剩余比例。第二行不是另一个包含额度：Premium 及以上是预付余额或「按量」，Plus / Free 是「不含」。旧档第一行对齐 Cursor Models，第二行对齐 Other Models。金额栏的 `included usage` 不能替代模型池剩余。本工具约 30 秒刷新一次；安装更新后请重新启动托盘程序。

**Windows 第二行不是百分比**

Plus 和 Free 不包含第三方模型，第二行显示「不含」。Premium、Super、Ultra、Teams 和 Enterprise 的第三方模型按 API 标价从预付额度扣除，第二行显示「按量」或接口返回的余额。只有旧档才会把第二行显示成「其他」百分比。

**是否安全？**  
令牌只在本机读取，请求只发往 `cursor.com` 与 `api2.cursor.sh`。应用不把令牌写入项目目录或自检输出。请不要把 `session-token` 文件或环境变量提交到 git。

**和官方用量页的关系**  
这是第三方客户端，不是 Cursor 官方应用。套餐规则、限速和账单以 Cursor 官方说明为准。
