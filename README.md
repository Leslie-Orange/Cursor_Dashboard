# Cursor 额度仪表盘

macOS 菜单栏与 Windows 托盘小工具，常驻显示当前 Cursor 账号在计费周期内的剩余额度。

状态栏会直接显示两行剩余比例，例如上面是 `内置 76%`，下面是 `其他 45%`。Windows 托盘图标上是这两行数字；鼠标悬停时，余额显示在图标正上方，图标在任务栏或隐藏图标面板里都一样。点击后展开详情；点击外部或按 Esc 关闭弹窗。右键可立即刷新、打开官方用量页或退出。

本工具读取的是 **Cursor 套餐用量**（本月已用 / 剩余 / 重置时间，以及内置模型与其他模型分项），不是 ChatGPT 或 Codex 的限额窗口。

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
- 显示套餐 / 计划名称
- 显示周期内剩余百分比，以及按本周期已用额度折算的消耗速度（`x%/天`）
- 显示内置模型（Auto / Composer）与其他模型（第三方 / 指定模型）的剩余比例
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

这些是 Cursor 仪表盘使用的内部接口，不是公开稳定 API，字段可能变化。金额与百分比仍以官方页面为准：

<https://cursor.com/dashboard/usage>

### 展示字段

| 位置 | 内容 |
| --- | --- |
| 状态栏第一行 | **内置**剩余百分比（Cursor 自带模型：Auto / Composer） |
| 状态栏第二行 | **其他**剩余百分比（指定的第三方模型） |
| 弹窗标题 | 套餐名称，如 `Pro 额度` |
| 第一张卡片 | 内置模型剩余、重置倒计时、消耗速度（`x%/天`） |
| 第二张卡片 | 其他模型剩余、同一周期的重置倒计时、消耗速度（`x%/天`） |
| 底部状态 | `实时` 或 `快照`、最近采样时间；过期或出错时附加说明 |
| 颜色 | 剩余 ≤30% 黄色，≤10% 红色 |

消耗速度按「本周期已用百分比 ÷ 周期内已过去天数」计算，例如 30 天窗口已过约 3 天、已用 5%，则约为 `1.7%/天`。周期刚开始不足 1 小时时，按 1 小时折算，避免数字被放大。

Cursor 的额度按**当前计费月**计算，不是 ChatGPT Codex 那种 5 小时 / 7 天窗口。内置与其他是同一周期内的两个池子：内置对应 Auto / Composer，其他对应 Claude、GPT 等指定模型。百分比取自 Cursor 设置页同一套字段（`autoPercentUsed` / `apiPercentUsed`），与系统用量条对齐。

## macOS 安装（推荐）

双击 [`Mac/Packages/Cursor额度仪表盘-1.3.0.dmg`](Mac/Packages/Cursor额度仪表盘-1.3.0.dmg)，把 **Cursor额度仪表盘** 拖到 **应用程序** 即可。

这是菜单栏应用：启动后请看屏幕右上角，不会在 Dock 常驻。首次打开若被系统拦截，按住 Control 单击应用并选择「打开」。

重新打包安装盘：

```zsh
chmod +x "./Mac/Codings/pack-dmg.sh"
./Mac/Codings/pack-dmg.sh
```

会生成通用二进制（Apple Silicon + Intel）安装盘：`Mac/Packages/Cursor额度仪表盘-1.3.0.dmg`。

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
以 Cursor 设置里的用量条，以及 <https://cursor.com/dashboard/usage> 为准。本工具的「内置 / 其他」对应系统里的 Cursor Models / Other Models 两根用量条，不是把套餐美元额度简单相除。内部接口有缓存，本工具约 30 秒刷新一次。

**只有内置、没有其他行**  
部分套餐（尤其是企业按次计费）不返回其他模型分项。此时第二行会尽量显示请求次数剩余，或保持「—」。

**是否安全？**  
令牌只在本机读取，请求只发往 `cursor.com` 与 `api2.cursor.sh`。应用不把令牌写入项目目录或自检输出。请不要把 `session-token` 文件或环境变量提交到 git。

**和官方用量页的关系**  
这是第三方客户端，不是 Cursor 官方应用。套餐规则、限速和账单以 Cursor 官方说明为准。
