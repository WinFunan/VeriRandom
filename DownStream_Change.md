# DownStream_Change.md — VeriRandom 相对上游 SecRandom 的改动台账

> **这份文件是本分支的强制维护项。**
>
> 本仓库是 [SECTL/SecRandom](https://github.com/SECTL/SecRandom) 的开源分支（VeriRandom）。相对上游的每一处**行为、文件、资源、CI、法务文档**改动都必须登记在这里，否则下一次同步上游时会被静默覆盖或产生难以定位的冲突。
>
> **规则：任何一次改动，只要它新增/重命名/删除了文件，或改变了上游已有文件的行为与文案，就必须在同一个改动里更新本文件。** 未登记的改动视为未完成。

---

## 0. 使用与同步方式

- 上游 remote：`https://github.com/SECTL/SecRandom.git`
- 本仓库：`https://github.com/WinFunan/VeriRandom.git`
- 同步时先看本文件「§8 上游同步注意」，再执行 upstream merge/rebase，并逐条确认本文件登记的内容仍然存在。
- 本文件按「改动主题」而非「提交顺序」组织；新增改动请追加到对应主题，并在文件顶部的「变更索引」中同步。

### 变更索引

| # | 主题 | 影响范围 | 与上游冲突风险 |
|---|------|----------|----------------|
| 1 | 品牌与显示名称改为 VeriRandom | 程序集元数据、窗口标题、移动端清单、CI bundle 名、本地化资源、README | 低（多为文案） |
| 2 | 分支声明、CLA 与治理文档 | README / About / CONTRIBUTING / CLA / PR 模板 / `ChangeLog.md` 自有更新日志 | 低 |
| 3 | NIST Beacon 熵源与 Up/Own 双链证明 | 新增验证路径、配置项、自有证明文件格式 | **高**（动了证明链路与落盘格式） |
| 4 | 遥测与在线状态默认关闭 + SECTL 披露 | 隐私配置默认值、OOBE 法务页、隐私设置页 | 中 |
| 5 | 自动更新禁用（保留能力） | Core 常量、更新调度/入口、更新设置页 | 中 |
| 6 | CI 与构建修复 | `build_publish.yml`、Android 版本号、密钥脚本 | **高**（同一文件多处改） |
| 7 | TSA 时间戳改为可显式关闭 + 沃通隐私提示 | 验证设置页、时间戳客户端、OOBE 隐私政策提示 | 中高（改了验证链路的网络行为开关） |
| 8 | 新增「自有参考链（Own）」补充声明 | 新增 `*.ownproof.json`、自有链头、完整性报告字段 | **高**（新增落盘格式） |
| 9 | 跨境数据传输闸门 + 抽取上传改为无默认二选一 | 新增枚举/配置、OOBE 与设置页、6 类 SECTL 出网路径、`SectlAuthService`/`SectlHeartbeatService`/`PlatformVersionReportService` 构造函数 | **高**（改动了所有出境路径的开关语义，并与上游的令牌轮换重写叠加） |
| 10 | 禁用「仅 TOTP」单一验证 | `GlobalConstants` 调试开关、`SecurityService`/`SecurityCredentialStore`、桌面启动参数 | 中（改了安全验证的判定条件） |
| 11 | 自有标识改名 | 包名、URI、可执行名、安装包标记、安装器、单实例名、macOS/Linux 路径；收尾需改写 §7 禁止改名表 | **高**（改动产品身份与全部兼容契约） |
| 12 | 桌面资源覆盖加载器健壮性修复 | `OverlayAssetLoader`、桌面 `Program` 启动资源诊断 | 中（修的是启动期字体加载失败） |
| 13 | 普通模式证明可信度评分 | 新增 Core 评分器与测试、验证设置页新增评估区块 | 中（新增功能，不动既有证明格式） |

---

## 1. 品牌与显示名称：对外 VeriRandom、对内 SecRandom

**意图**：用户可见的产品名统一为 VeriRandom；产品身份类标识（包名、URI、可执行名、安装目录等）同样自有，边界见 §7。

**新增文件**：无

**修改文件**：

| 文件 | 改动 |
|------|------|
| `AssemblyInfo.cs` | `AssemblyTitle` / `AssemblyProduct` → `VeriRandom` |
| `SecRandom/App.axaml` | macOS `NativeMenuItem Header` → `VeriRandom` |
| `SecRandom/App.axaml.cs` | 主窗口 / 设置窗口 / 闪抽窗口 `Title`、启动失败文案、启动日志行 → `VeriRandom` |
| `SecRandom/Views/MainView.axaml` | `Header` 与标题 `TextBlock` |
| `SecRandom/Views/SettingsView.axaml` | 标题栏 `TextBlock` |
| `SecRandom/Views/FloatingWindow.axaml` | `Title` |
| `SecRandom/Services/ViewEngine/DesktopViewHostProvider.cs` | 默认窗口 `Title` |
| `SecRandom/Services/Desktop/TaskBarIconService.cs` | 托盘 `ToolTipText` |
| `SecRandom/Services/Desktop/DesktopIntegrationService.cs` | macOS URL handler 的 `CFBundleName`、`CFBundleURLName`；Linux autostart `.desktop` 的 `Name=` |
| `SecRandom/Services/CrashRecovery/CrashRecoveryRuntime.cs` | 崩溃报告标题 |
| `SecRandom.Core/Services/SingleInstance/SingleInstanceService.cs` | IPC 未就绪提示文案 |
| `SecRandom/Services/Updates/UpdateNotificationService.cs` | 更新通知标题（Windows toast / macOS / Linux） |
| `SecRandom.Android/Properties/AndroidManifest.xml` | `android:label` |
| `SecRandom.Android/SecRandom.Android.csproj` | `ApplicationTitle` |
| `SecRandom.Android/Storage/SecRandomDocumentsProvider.cs` | 根名称与标签兜底 |
| `SecRandom.iOS/SecRandom.iOS.csproj` | `ApplicationTitle` |
| `SecRandom.iOS/Info.plist` | `CFBundleDisplayName`、`CFBundleName`、相机用途说明 |
| `SecRandom.Launcher/Program.cs` | 控制台错误文案 |
| `.github/workflows/build_publish.yml` | macOS bundle 的 `CFBundleName`/`CFBundleDisplayName` 与 iOS 断言 |
| `SecRandom/Langs/**/Resources*.resx` | 产品名文案（Common / CrashRecovery / FirstRunOobe / Mobile / SettingsView / Notification / Plugins·Overview） |
| `README.md`、`resources/README_EN.md`、`resources/README_JA.md` | 标题、徽章仓库、下载/社区/贡献者分段（见 §2） |

**注意**：§7 把标识分成两类——**产品身份类**已改为自有（包名、`verirandom://`、`VeriRandom.Desktop.exe`、`VeriRandom.package.json`、`.VeriRandom.safety.key`、单实例名、安装目录等），而 **`SecRandom Sync`、`SecRandom4Ci`、`fair.sectl.cn`/`sectl.cn`、插件市场索引、上游仓库链接**属于上游服务或外部契约，必须保持 `SecRandom`。

---

## 2. 分支声明、CLA 与治理文档

**意图**：明确本产品是 SecRandom 的分支，不是 SecRandom 本身；并为贡献建立许可授权机制。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `CLA.md` | 贡献者许可协议（**简体中文为权威版**），v1.0，生效日期 2026-10-02，适用中国法律、湖南省宁乡市人民法院 |
| `resources/CLA_EN.md` | 英文参考译文（声明以中文为准） |
| `resources/CLA_JA.md` | 日文参考译文（声明以中文为准） |
| `.github/pull_request_template.md` | PR 模板，自动带入中英双语 CLA 签署声明 |
| `ChangeLog.md` | 本仓库**自有**的、面向用户的更新日志（只追加、按版本发布时间组织）。与上游 `CHANGELOG/`（随上游同步带来的核心日志）和 `DownStream_Change.md`（分歧工程台账）构成三文件边界：发版写 `ChangeLog.md`，分歧必须已登记在本文件；只出现在 `ChangeLog.md` 而未登记本文件 = 未完成的改动 |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `README.md` / `resources/README_EN.md` / `resources/README_JA.md` | 标题改 VeriRandom；加入 `> [!IMPORTANT]` 分支声明；徽章/下载/社区/贡献者拆分为「本仓库」与「上游」两段；移除 Star History；版权改为 WinFunan + SECTL |
| `SecRandom/Views/SettingsPages/About/AboutSettingsPage.axaml` | `S_App` 头部改 VeriRandom；新增 `S_App_ForkNotice` 行 |
| `SecRandom/Langs/SettingsPages/About/Resources{,.en-US,.ja-JP}.resx` + `Resources.Designer.cs` | 新增 `S_App_ForkNotice` / `M_App_ForkNotice` |
| `CONTRIBUTING.md` / `resources/CONTRIBUTING_EN.md` / `resources/CONTRIBUTING_JA.md` | 标题与正文改 VeriRandom；加入 CLA 小节与签署语；Issue 与 clone 指向本仓库；保留 `upstream` remote 说明 |
| `SecRandom/Views/SettingsPages/About/AboutSettingsPage.axaml(.cs)` + `Langs/SettingsPages/About/Resources{,.en-US,.ja-JP}.resx` + Designer | About 页按当前 README 重组：原 `S_Author`（上游作者/组织/爱发电/哔哩哔哩）整块改为**默认折叠**（`IsExpanded="False"`）并补上 README 的「对上游仓库支持与社区」入口（QQ 群 833875216、QQ 频道、邮箱、官方文档、DeepWiki、上游贡献指南），首行加入范围说明；在原位置新增 `S_Community`（本分支自己的 QQ 群 768421833、邮箱、哔哩哔哩、Issue、贡献指南，全部指向本仓库）。横幅改为本分支自有文件名 `verirandom-banner-{cn,en,ja}.png`（缺失时 `BannerSource` 为 `null`，界面显示带说明的占位框；补资源后自动替换，无需改代码），社区图片同样先放占位。**同时移除贡献者头像的外链预加载**（原实现按接口返回的 `avatar_url` 逐张下载 GitHub CDN 图片并渲染，属供应链面），改为本地 `FluentIcon`，整行保留为跳转到该贡献者 GitHub 主页的链接；`GitHubContributor` 随之去掉 `Avatar`/`AvatarUrl`/`LoadAvatarAsync` 与 `INotifyPropertyChanged`；贡献者抽屉改为**同时拉取本仓库与上游**两个仓库的贡献者，按 GitHub 登录名合并、提交数相加后排序，说明文案同步更新 |

**CLA 的边界（必须保持）**：
- 只约束**签署它的贡献者**的新贡献，不能追溯既有贡献。
- **不能**改变上游 GPLv3 代码的许可证；整体分发仍须满足 GPLv3。
- 调整出站许可前必须提前 ≥30 天公示。
- About 页的作者/组织/鸣谢署名行仍指向上游，属于署名性质，**不要**改成 VeriRandom。

---

## 3. NIST Beacon 熵源与 Up/Own 双链证明

**意图**：在普通模式引入「外部信标熵源」开关，用已发布的 NIST Beacon 脉冲派生抽取种子，使主持人无法注入自定义熵；同一脉冲内用从 0 开始、公差为 1 的序号保证不重复取样。
证明改为**双链双文件**：Up（上游兼容，不含信标）与 Own（自有参考声明）。

**Up 链（`*.srproof.json`，上游兼容）**：
- **不再包含信标字段**。`DrawProof.Beacon` 仅保留读取旧文件的能力，写盘时由 `DrawProofExportService.Save` 置空（`proof with { Beacon = null }`），`WitnessClient.AttestAsync` 也继续在提交体内置空，保证提交/回执/签名摘要的字节形态与上游一致。
- 信标校验改走**预设**：`BeaconPulsePeriodPolicy.Match(stampedAt, pulseTimeStamp, periodSeconds)` 用 TSA 令牌的时间确定周期，接受「当周期脉冲」或「容差内的上一周期脉冲」（`TolerancePeriods = 1`，另有 60 秒时钟偏移容差）；脉冲由权威端点重新抓取，再按同一 KDF 重新派生种子。

**Own 链（`*.ownproof.json`，与 Up 同目录同基名）**：
- **仅在启用信标熵时**写入；它是补充来源，**不提供**额外密码学可信度，也**不参与**公平性判定。
- 每个节点含：所依赖的 Up 节点（`Up.ChainIndex` / `Up.ChainHash`，另存 `Up.ProofHash` 供声明）、信标 `PulseIndex`、同脉冲内的种子递增因子 `Beacon.Sequence`、完整已签名脉冲副本、自身链位置，以及**自身 RFC 3161 令牌**。
- 节点哈希 `OwnProofChainStore.ComputeSelfHash` 覆盖 `(index, prevHash, Up.ChainIndex, Up.ChainHash, PulseIndex, Sequence)`，因此不能被事后改指向另一次抽取。
- **令牌顺序**：Own 的 TSA 令牌**先于** Up 链的令牌申请（`DrawProofAttestationService.TimestampOwnProofAsync` 是队列条目的第一阶段），用于说明参考节点不是「抽取完成、Up 已被盖章之后」补加的。
- 复核要求：同一脉冲的 `Sequence` **从 0 开始、公差为 1、单调递增**；结果只作为 `ProofIntegrityReport.OwnReference` 参考声明，`IsHealthy` **不**受其影响。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Models/Verification/BeaconPulse.cs` | 脉冲 DTO |
| `SecRandom.Core/Services/Verification/BeaconSeedDerivation.cs` | 纯函数种子派生 `SHA256("SecRandomBeacon/v1" ‖ pulseIndex ‖ chainIndex ‖ outputValue ‖ sequence)` |
| `SecRandom.Core/Services/Verification/BeaconSequencePolicy.cs` | 每脉冲序号策略（新脉冲从 0，同脉冲 +1，拒绝回退/同索引异值） |
| `SecRandom.Core/Services/Verification/BeaconEndpointPolicy.cs` | 端点校验（默认官方 NIST v2，远程必须 HTTPS） |
| `SecRandom.Core/Services/Verification/BeaconPulsePeriodPolicy.cs` | 周期预设：按 TSA 时间判定脉冲属当周期/容差内上一周期/超出容差 |
| `SecRandom.Shared/Models/Verification/OwnProof.cs` | Own 合约：`OwnProof` / `OwnProofUpReference` / `OwnProofChain` |
| `SecRandom/Services/Verification/OwnProofChainStore.cs` | 自有参考链头（`data/proofs/own-chain-head.json`）与节点哈希 |
| `SecRandom/Services/Verification/OwnProofExportService.cs` | `OwnProofPaths`（同名基名换扩展名）+ Own 文件读写、孤儿清理、移除登记 |
| `SecRandom/Services/Verification/BeaconContracts.cs` | `INistBeaconClient`、`BeaconSeedReservation`、`BeaconEntropyException` |
| `SecRandom/Services/Verification/NistBeaconClient.cs` | 脉冲抓取与形状校验 |
| `SecRandom/Services/Verification/BeaconEntropyProvider.cs` | 序号锚点持久化（`data/proofs/beacon-state.json`，原子替换 + 信号量） |
| `SecRandom.Core.Tests/BeaconEntropyTests.cs` | 派生/序号/端点/客户端/Provider 测试 |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Shared/Models/Verification/DrawProof.cs` | 新增可选 `Beacon` 字段与 `DrawProofBeacon`；**不进入** `ComputeAttestedProofHash` |
| `SecRandom.Core/Models/SubConfigs/General/VerificationSettingsConfig.cs` | 新增 `BeaconEntropyEnabled`、`BeaconEndpoint` |
| `SecRandom/Services/Verification/VerificationDrawCoordinator.cs` | 普通模式且开关开启时改用信标种子，并把信标元数据写入证明 |
| `SecRandom/Services/Verification/WitnessClient.cs` | 提交给服务端前剥离 `beacon` 字段，保持请求结构不变 |
| `SecRandom/App.axaml.cs` | 注册 `INistBeaconClient` 具名 HttpClient 与 `BeaconEntropyProvider` 单例 |
| `SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml(.cs)` | 开关、端点输入、拉取信标按钮与状态 |
| `SecRandom/Langs/SettingsPages/General/Verification/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增 9 个键 |
| `SecRandom.Core.Tests/PluginDrawServiceTests.cs` | 注册信标桩，满足新构造参数 |

**必须保持的约束**：种子只由信标派生（不掺本地随机/时钟/输入哈希）；信标不可用时**抽取失败**，绝不回退本地种子；序号在返回种子前先落盘。

---

## 4. 遥测与在线状态默认关闭 + SECTL 披露

**意图**：本分支默认不上报；凡暴露开关处均说明数据流向 SecRandom/SECTL；并单独披露 SECTL 是独立主体。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/Models/SubConfigs/General/PrivacySettingsConfig.cs` | `SentryTelemetryEnabled` 默认 `false`、`OnlineStatusMode` 默认 `Off`（遗留配置仍按原值迁移） |
| `SecRandom.Core/Models/SubConfigs/General/BasicSettingsConfig.cs` | 新增 `AcceptedSecRandomServicesVersion` |
| `SecRandom/Services/FirstRun/FirstRunOobeService.cs` | 新增 `CurrentSecRandomServicesVersion = 1`；`Complete(bool secRandomServicesAccepted)` 仅在勾选时记录该版本；**不**再把该版本纳入 `IsPrivacyPolicyOnlyRequired()`（该确认改为可选、按需补签） |
| `SecRandom/ViewModels/FirstRunOobeViewModel.cs` | 新增 `AcceptedSecRandomServices` / `IsSecRandomServicesRequired`；**不**纳入 `CanContinue` / `FinishAsync`（可选项不阻塞流程）；完成后按勾选状态传递给 `Complete(...)` |
| `SecRandom/Views/FirstRunOobeWindow.axaml` | 隐私页新增数据流向提示；法务页新增 SECTL 披露折叠区（条款 + 免责声明 + SecRandom 隐私政策）；新增加粗「（可选）」说明与非必选勾选 |
| `SecRandom/Views/FirstRunOobeWindow.axaml.cs` | 监听隐私开关：启用遥测/在线状态时强制走 SECTL 补签弹窗，拒绝则回退开关 |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` + Designer | 政策改为默认关闭并注明数据流向；新增 `SecRandomPrivacyPolicy`（含 v3.0.0 前置说明）、`C_SecRandomServicesTitle/Clause/Disclaimer/Accept`、`C_SecRandomServicesOptionalNote`、`C_SecRandomServicesAcceptOptional`；`C_PrivacyEncouragement` → `C_PrivacyDataDestination`；`C_LegalDescription` 改默认关闭 |
| `SecRandom/Langs/SettingsPages/General/Privacy/Resources{,.en-US,.ja-JP}.resx` | 两条说明追加「数据将被发送给 SecRandom 与 SECTL」 |
| `SecRandom/Views/SettingsPages/General/PrivacySettingsPage.axaml.cs` | 监听隐私开关：启用遥测/在线状态时强制走 SECTL 补签弹窗，拒绝则回退开关 |
| `SecRandom/Langs/SettingsPages/General/Verification/Resources{,.en-US,.ja-JP}.resx` + Designer | 重写 `M_ModeConfirmFormal`（上游说明 + 标点调整 + 注释）；新增 `M_ModeConfirmFormalNoticeTitle` / `Notice` / `Warning`（加粗）与 `C_ModeConfirmSecRandomServices` |
| `SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml.cs` | 正式公证确认弹窗改为分段渲染（正文 + 加粗「注意」+ 正文 + 加粗结论）；当尚未补签时在同一弹窗内追加 SECTL 条款/免责声明与**必选**勾选，两项都勾选才可切换；确认按钮由 `ConfirmDialogGate` 强制 5 秒后才可点击 |

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom/Services/Consent/SecRandomServicesConsent.cs` | SECTL 在线服务确认的共享实现：`IsRequired` / `MarkAccepted` / `AppendDisclosure` / `EnsureAsync`（二级弹窗，不能静默通过，确认按钮 5 秒后才可点击） |
| `SecRandom/Helpers/ConfirmDialogGate.cs` | 强制阅读门：`MinimumDisplay = 5s`，确认按钮在最短显示时间结束且所有必选勾选完成前保持禁用；正式公证切换弹窗与该 SECTL 补签弹窗都用它，调用方不得缩短该时长 |

**行为**：
- OOBE 中该确认**可选**，勾选才记录版本；未勾选则保持未接受。
- 触发**强制补签**的两个时机：切换到「正式公证」模式；启用 Sentry 遥测或在线状态（≠ Off）。两处都通过同一个二级确认弹窗完成，拒绝则回退对应操作。
- 涉密弹窗的确认按钮受 `ConfirmDialogGate` 约束：弹窗显示满 5 秒且必选勾选全部完成后才可点击，避免瞬时反射式确认；切换回普通模式的同一弹窗也适用。
- 中文文案按项目规范去掉了中文句号 `。`（改为 `；` 或分句）；如需保留原文标点属上游同步时需注意的差异。

**连带影响**：`OnlineStatusMode == Off` 同时关闭 `PlatformUsageReportService`（使用统计）。

---

## 5. 自动更新禁用（保留能力）

**意图**：更新发现仍指向上游 `SECTL/SecRandom` 的元数据与清单，本分支绝不能拉取上游二进制；因此默认关闭更新，等本仓库自建服务后再开启。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/GlobalConstants.cs` | 新增 `UpdatesEnabled = false`（唯一开关） |
| `SecRandom/Services/Updates/UpdateScheduler.cs` | 禁用时直接返回（hosted service 仍注册） |
| `SecRandom/Services/Updates/UpdateCenterService.cs` | 新增 `UpdatesDisabled()` 守卫；四个入口 no-op；`CanCheck`/`CanDownloadAndInstall`/`CanApplyUpdate`/新增 `IsSupported` 反映禁用态 |
| `SecRandom/Views/SettingsPages/Update/UpdateSettingsPage.axaml` | 强制检查项绑定 `IsSupported` |
| `SecRandom/Langs/SettingsPages/Update/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增 `M_UpdatesDisabled` |

**恢复方式**：把 `GlobalConstants.UpdatesEnabled` 改回 `true`，并把 `UpdateCenterService`（及移动端 `MobileUpdateService`，当前未注册）的 `Repository` / 元数据 URL / 镜像前缀 / 清单文件名换成本仓库，同时用自建私钥签新清单。

---

## 6. CI 与构建修复

**意图**：让无 tag 的新 fork 也能跑通构建；Android 尚无签名密钥时可降级；PluginSDK 版本主号与 API 主号保持一致。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `scripts/generate-android-keystore.ps1` | 生成本仓库自有 Android 发布密钥并打印需要配置的四个 secret |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `.github/workflows/build_publish.yml` | ① 四处 `Get Latest Tag` / `Get Version` 增加无 tag 兜底（`try/catch` / `|| true`，回退 `git tag --sort=-v:refname`，再回退**主号取自 `PluginApiVersions.Current`** 的 `<major>.0.0-dev`）；② Android 四个签名 secret 缺失时改为告警 + debug 签名，产出 `-android-<arch>-unsigned.apk` 并跳过 keystore/证书校验（secret 齐全时严格校验不变）；③ `Combine Artifacts` 过滤 `-unsigned.apk`，避免 debug 签名包进入 release 与清单 |
| `SecRandom.Android/SecRandom.Android.csproj` | `ApplicationDisplayVersion` 优先取 `$(Version)`，仅在为空/`1.0` 时回退 `$(GitTag)`（与 iOS 一致） |

**注意**：`build_publish.yml` 与上游差异最大，且上游也在频繁改这个文件，冲突概率最高。

---

## 7. 标识归属：哪些已改为自有、哪些必须保持 SecRandom

本分支与上游 SecRandom **同机共存**，因此产品身份类标识必须自有；而**代码级内部标识**（命名空间、项目名等）保持 `SecRandom` 不变，它们不是产品身份，改名只会制造无谓的合并冲突。

### 7.1 已改为自有标识（不得回退为 SecRandom）

| 类别 | 现值 |
|---|---|
| Android/iOS 包名 | `com.yeyixiao.verirandom` |
| URI 协议（主） | `verirandom://` |
| URI 协议（兼容，默认关闭） | `secrandom://`（可选注册，见 §14） |
| Windows 协议注册表 | `Software\Classes\verirandom` |
| macOS bundle id / 启动项 | `com.yeyixiao.verirandom` / `com.yeyixiao.verirandom.plist` |
| Linux 包名与桌面项 | `verirandom` / `verirandom.desktop` / `/usr/lib/verirandom` / `/usr/bin/verirandom` |
| 桌面可执行 | `VeriRandom.Desktop.exe`（项目名仍为 `SecRandom.Desktop`，靠 `<AssemblyName>` 改名） |
| 便携版启动器 | `VeriRandomLauncher.exe` / `VeriRandomLauncher` |
| 安装包标记 | `VeriRandom.package.json` 且 `product: "VeriRandom"`（由 `SecRandom.Launcher` 与 `UpdateCenterService.Product` 校验） |
| 更新清单 | `VeriRandom-update-manifest.json` / `.sig` |
| 单实例 Mutex / Pipe | `VeriRandom_SingleInstance_VeriRandom_7D5E4C21` / `VeriRandom_IPC_…` |
| USB 绑定标记 | `.VeriRandom.safety.key` |
| 数据根目录 | `LocalApplicationData/VeriRandom/data` |
| 便携版环境变量 | `VERIRANDOM_PACKAGE_ROOT` |
| 安装器 | 自有 `AppId`、`DefaultDirName={autopf|localappdata}\WinFunan\VeriRandom` |
| 程序集 `<Company>` | `WinFunan` |

### 7.2 必须保持 `SecRandom`（代码级内部标识，不是产品身份）

- 命名空间、程序集/项目/文件夹名、`InternalsVisibleTo("SecRandom")`
- `avares://SecRandom/...` 资源 URI、MSBuild 属性 `SecRandomPlatform`
- 证明文件格式：`.srproof.json`、`SecRandomProof/...` 与 `SecRandomBeacon/v1` 域分隔符
- 上游服务与外部契约（本分支只是消费方，改名即失效）：`SecRandom Sync`、`SecRandom4Ci` ClassIsland 插件、`fair.sectl.cn` / `sectl.cn` 端点、插件市场索引位于 `SECTL/SecRandom-PluginIndex` 且其 `product` 字段为 `SecRandom`（`PluginCatalog.Product` / `PluginMarketService` 据此校验）
- 上游仓库链接 `github.com/SECTL/SecRandom`、About 页作者/组织/鸣谢署名行

**判断准则**：标识是否出现在**本程序自己的安装/注册/出网身份**里？是 → 必须自有；只是**编译期或与上游服务对接的名字**？是 → 保持 `SecRandom`。

---

## 8. 上游同步注意事项

1. **先读本文件**，再 merge/rebase 上游。
2. 高冲突文件：`build_publish.yml`、`SecRandom/App.axaml.cs`、`PrivacySettingsConfig.cs`、`UpdateCenterService.cs`、`VerificationDrawCoordinator.cs`、`WitnessClient.cs`、`FirstRunOobeViewModel.cs`、`FirstRunOobeWindow.axaml`、各 `Resources*.resx`。
   已记录的高冲突面：`SecRandom/Services/Verification/DrawProofExportService.cs`（上游给 `RemoveProofsOverStorageLimit` 加了 `protectedPath`，与自有参考证明的成对删除同处一个方法）、`SecRandom/Services/Verification/ProofChainStore.cs`（`RecordRemovedIndices` 的「只能跨连续前缀推进」收紧）、`SecRandom/Services/Auth/SectlAuthService.cs`（上游重写为「单次使用的轮换刷新令牌 + 单飞刷新 + 跨进程锁 `sectl-auth.lock`」，构造函数新增 `SectlTokenStore` 与 `ILogger`，与我们的 `MainConfigHandler` 出境闸门参数叠加）、`SecRandom/Services/Auth/SectlTokenStore.cs`（上游新增），以及三个构造 `SectlAuthService` 的测试文件（`SectlAuthServiceTests`、`SectlCloudStorageClientTests`、`CloudBackupServiceTests`）。
   合并口径：**两侧都要保留**——上游的令牌轮换/链规则与本分支的 `MainConfigHandler` + `SectlTrafficPolicy` 出境闸门必须同时存在于构造函数与调用点。
   AGENTS 类文件同样属于高冲突面：`SecRandom.Core/AGENTS.md`、`SecRandom/AGENTS.md`、`SecRandom/Views/SettingsPages/AGENTS.md` 由上游同步更新；本分支的补充段落需逐条确认仍然存在，尤其 §3（双链）、§4（隐私默认）、§10（TSA 开关）、§12（跨境闸门）。
3. 上游若新增/改动隐私政策或 OOBE 版本，需同步更新 `SecRandomPrivacyPolicy` 的「引用自 SecRandom vX.Y.Z」前置说明与 `CurrentPrivacyPolicyVersion`。
4. 上游若改动 `PluginApiVersions.Current` 主号，CI 回退版本会自动跟随；无需修改工作流。
5. 上游若重新启用/调整更新通道，需重新评估 `GlobalConstants.UpdatesEnabled` 的取值与端点替换。
6. 同步后必须回归验证：构建（含 Android 无签名降级路径）、OOBE 全流程（含新增勾选）、抽取证明链路（含 Beacon 开关）。

---

## 9. 记录维护

- 新增改动：追加到对应主题，并在「§0 变更索引」表格更新范围/风险。
- 与 `ChangeLog.md` 的分工：本文件是**面向工程的分歧台账**（改了什么文件、为什么必须活下来），`ChangeLog.md` 是**面向用户的发版日志**（按版本发布时间、只追加）。发版时两边都要有：`ChangeLog.md` 写用户可见效果，本文件登记分歧与实现边界。上游同步带来的核心日志进 `CHANGELOG/`，不要写进本文件，也不要写进 `ChangeLog.md`。
- 新增主题：在 §1–§12 之后追加一节，并更新索引。
- 删除或回退某改动：保留条目并标注「已回退（日期/原因）」，不要直接删除历史。
- 此后每次改动请**就地更新**，不要依赖事后重建。

---

## 10. TSA 时间戳改为可显式关闭 + 沃通去标识化提示

**意图**：时间戳默认开启，但用户可以显式关闭（不再强制静默进行）；关闭时必须说明与信标周期锚定的耦合；并在自有隐私政策中加粗披露「为获取 RFC 3161 时间戳会把去标识化信息发送给沃通及其时间戳服务」。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/Models/SubConfigs/General/VerificationSettingsConfig.cs` | 新增 `TimestampAuthorityEnabled`，默认 `true` |
| `SecRandom/Services/Verification/TimestampAuthorityClient.cs` | `IsEnabled` 改为读取该设置；被禁用时 `TimestampAsync` 直接抛出不执行网络请求；类注释同步 |
| `SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml` | 新增 `S_TimestampAuthority` 开关行（`TimestampAuthorityToggle`） |
| `SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml.cs` | 关闭时弹出耦合提示弹窗（`C_TimestampDisableTitle` / `C_TimestampDisableBody` / `C_TimestampDisableConfirm`），必须勾选并 `ConfirmDialogGate` 满 5 秒才可确认；拒绝则回退开关 |
| `SecRandom/Langs/SettingsPages/General/Verification/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增上述 5 个键 |
| `SecRandom/Views/FirstRunOobeWindow.axaml` | 隐私政策后新增**加粗** `C_TimestampPrivacyNotice` |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增 `C_TimestampPrivacyNotice` |

**行为**：
- 关闭 TSA 后两条链都不再申请时间戳；信标匹配失去周期锚定，只能按 Own 记录的 `PulseIndex` 回溯脉冲。
- 关闭动作必须经过弹窗确认，不能静默生效。

---

## 11. 抽取证明双链落盘（Up / Own）

**意图**：把「上传排除信标」改为双证明文件，Up 面向上游服务器兼容，Own 作为自有补充声明并用于说明「可以使用信标验证」。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Shared/Models/Verification/OwnProof.cs` | Own 合约（`OwnProof` / `OwnProofUpReference` / `OwnProofChain`，`FormatId = "verirandom-own-proof/v1"`） |
| `SecRandom.Core/Services/Verification/BeaconPulsePeriodPolicy.cs` | 周期预设（当周期 / 容差内上一周期 / 超出容差 / 无法判定） |
| `SecRandom/Services/Verification/OwnProofChainStore.cs` | `OwnProofChainStore`（`data/proofs/own-chain-head.json`，域分隔 `SecRandomProof/v3/own-chain`）+ `OwnChainHead` |
| `SecRandom/Services/Verification/OwnProofExportService.cs` | `OwnProofPaths` + Own 文件读写、`RemoveOrphans`、`RecordRemoved` |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom/Services/Verification/DrawProofExportService.cs` | `Save(..., DrawProofBeacon?)` 写盘时置空 `Beacon`；启用信标时写同基名 `*.ownproof.json`；`DrawProofExportResult` 新增 `OwnPath`；保留/超限清理同时删除并登记 Own 兄弟文件 |
| `SecRandom/Services/Verification/VerificationDrawCoordinator.cs` | `CreateProof` 不再写入 `Beacon`；信标经 `VerificationDrawOutcome.Beacon` 单独传递，由 `Publish` 交给导出服务 |
| `SecRandom/Services/Verification/DrawProofAttestationService.cs` | 新增 `TimestampOwnProofAsync` 作为队列条目**第一阶段**（早于回执与 Up 时间戳）；失败只记录 `_lastError` 并继续上游提交；构造注入 `OwnProofExportService` |
| `SecRandom/Services/Verification/ProofIntegrityVerifier.cs` | 新增 `VerifyOwnReference`，报告节点哈希与「从 0、公差 1」序号，输出 `ProofIntegrityReport.OwnReference`；**不参与** `IsHealthy` |
| `SecRandom/App.axaml.cs` | 注册 `OwnProofChainStore`、`OwnProofExportService`（同步注册到 `ProofChainTests` / `DrawProofAttestationQueueTests` 的测试容器） |

**行为与边界**：
- Up 链字节形态与上游一致（已置空信标），提交 `fair.sectl.cn` 与 TSA 的字段集不变。
- Own **仅**在启用信标熵时写入；它是参考声明，不提供额外密码学可信度，也不参与公平性判定。
- Own 的 TSA 令牌先于 Up 申请，用于说明参考节点不是事后补加。
- Own 导出失败**不得**影响已完成的抽取（`DrawProofExportService.Save` 内捕获并告警）。

**已知缺口**：设置页只展示 Up 链完整性文本，`OwnReference` 计数未渲染到界面。

---

## 12. 跨境数据传输闸门 + 抽取上传改为无默认二选一

**意图**：SECTL 的服务器位于中华人民共和国境外（当前节点：德国巴伐利亚邦阿多特多夫，IP 159.195.70.108），因此把所有可能向 SECTL 发送数据的路径收敛到**一个**「跨境数据传输须知」闸门下；同时把原本不可关闭、默认开启的「普通模式抽取后自动向 SECTL 申请签名」改为**没有任何默认值**的主动二选一。

**判定口径（必须保持）**：
- `IsEgressAllowed` 要求**同时**满足两项确认：上游的 SECTL 线上服务政策（`AcceptedSecRandomServicesVersion`）与本分支的跨境数据传输须知（`AcceptedCrossBorderTransferVersion`）。任一项未接受 → 登录、心跳、云备份、在线状态、使用统计、版本使用量、抽取上传全部停用。
- `AttestationUploadMode.Unset` 表示「还没问过用户」，**必须当作不上传**处理。
- 绝不能用 `OnlineStatusMode` 代表「是否允许访问 SECTL」：上游的 `PlatformVersionReportService` **刻意不受** `OnlineStatusMode` 约束，必须显式走本闸门。
- 本闸门不得拦截 NIST Beacon（`NistBeaconClient` 访问的是 NIST，不是 SECTL）。
- 补签入口 `SectlTrafficPolicy.EnsureTransferAcceptedAsync` **先**收上游线上服务政策确认，**再**收跨境须知；任一被拒绝则闸门保持关闭。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Enums/Configs/AttestationUploadMode.cs` | `Unset = 0` / `Enabled = 1` / `Disabled = 2`，无默认值 |
| `SecRandom/Services/Consent/SectlTrafficPolicy.cs` | 唯一出境闸门：`IsTransferNoticeAccepted` / `IsEgressAllowed` / `IsAttestationUploadAllowed` / `MarkTransferNoticeAccepted` / `EnsureTransferAcceptedAsync`（二级弹窗 + `ConfirmDialogGate` 5 秒） |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/Models/SubConfigs/General/BasicSettingsConfig.cs` | 新增 `AcceptedCrossBorderTransferVersion`（版本化；**不**纳入 `IsPrivacyPolicyOnlyRequired()`，属可选、按需补签） |
| `SecRandom.Core/Models/SubConfigs/General/VerificationSettingsConfig.cs` | 新增 `AttestationUpload`（默认 `Unset`） |
| `SecRandom/Services/FirstRun/FirstRunOobeService.cs` | 新增 `CurrentCrossBorderTransferVersion = 1`；`Complete(...)` 改为接收跨境接受标记与抽取上传选择 |
| `SecRandom/ViewModels/FirstRunOobeViewModel.cs` | 新增 `AcceptedCrossBorderTransfer` / `AttestationUpload`；完成后传入 `Complete(...)` |
| `SecRandom/Services/Verification/DrawProofAttestationService.cs` | `IsEnabled` 追加 `SectlTrafficPolicy.IsAttestationUploadAllowed` |
| `SecRandom/Services/PlatformVersionReportService.cs` | 构造注入 `MainConfigHandler`；上报前显式检查出境闸门 |
| `SecRandom/Services/PlatformUsageReportService.cs` | `IsReportingDisabled()` 追加出境闸门 |
| `SecRandom/Services/OnlineStatusService.cs` | `ResolvePolicy()` 在未同意跨境时强制降级为 `Off` |
| `SecRandom/Services/Auth/SectlAuthService.cs` | 构造注入 `MainConfigHandler`；`SignInAsync` 与 `SendAuthorizedAsync`（受权请求边界，覆盖登录/云备份/用户信息）未同意跨境时直接拒绝 |
| `SecRandom/Services/Auth/SectlHeartbeatService.cs` | 构造注入 `MainConfigHandler`；未同意跨境时跳过一次心跳 |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增 10 个键：`C_CrossBorderTitle/Body/OptionalNote/Accept/AcceptRequired/Confirm`、`C_AttestationTitle/Body`、`O_AttestationOn/Off` |
| `SecRandom.Core.Tests/DrawProofAttestationQueueTests.cs`、`SectlAuthServiceTests.cs`、`CloudBackupServiceTests.cs`、`SectlCloudStorageClientTests.cs`、`PlatformVersionReportServiceTests.cs` | 测试构造处补齐新构造参数，并在测试配置中显式同意跨境（这些测试验证的是「已启用」路径） |

**行为**：
- 未同意跨境须知时：抽取上传、在线状态、使用统计、版本使用量、SECTL 账号登录与其心跳、账号云备份全部停用；拒绝是**可选**路径，不是 OOBE 阻塞项。
- 补签统一走 `SectlTrafficPolicy.EnsureTransferAcceptedAsync`（必选勾选 + 5 秒最短显示）。
- 抽取上传除跨境外还需要 `AttestationUpload == Enabled`，`Unset` 一律不上传。

**已知缺口**：
1. `SectlAuthService.InitializeAsync` 中已登录会话的后台令牌刷新可能绕过 `SendAuthorizedAsync`，尚未收口到出境闸门。
2. 未同意跨境时，`S_AttestationUpload` 单选组会置灰（`RefreshAttestationUpload` 读 `SectlTrafficPolicy`），但**不会**主动引导用户去隐私设置页补签；补签后需要重新进入该页才会解锁。

---


**界面（已落地）**：
- OOBE 轮播新增隐私页之后的独立第 3 页「跨境数据传输须知」：`StepCount` 由 8 改为 9，`IsCrossBorderStep => SelectedStep == 2`，页序为 欢迎(0) → 法务/隐私(1) → **跨境须知(2)** → 数据导入(3) → … → 完成(8)。
- 该页含：须知正文（含 SECTL 节点属地/IP）、**加粗**的可选说明、非必选勾选（`AcceptedCrossBorderTransfer`），以及**无默认**的抽取上传单选对（`AttestationUploadEnabledChoice` / `AttestationUploadDisabledChoice`，两个 `RadioButton` 同组，未选择时两个都未选中；未勾选跨境时单选对置灰）。
- `CanContinue` 与 `FinishAsync` 都要求 `AttestationUpload != Unset`（仅在全量设置流程中；`IsPrivacyPolicyOnly` 路径不受影响）。
- 隐私设置页新增跨境须知行（标题/说明复用 OOBE 资源 `C_CrossBorderTitle` / `C_CrossBorderOptionalNote`，**不复制法律文本**）与 `CrossBorderTransferAccept_OnClick` 补签入口，供首次设置时跳过的安装事后补签。
- 「抽取验证」设置页新增 `S_AttestationUpload` 行，供**已过首次设置的用户事后改选**提交/不提交；控件用 `x:Name` 直接读写（`RefreshAttestationUpload` / `AttestationUpload_OnClick`），不依赖 `INotifyPropertyChanged`，选项文案复用 OOBE 的 `O_AttestationOn` / `O_AttestationOff`。

## 13. 禁用「仅 TOTP」单一验证（可调试开关）

**意图**：TOTP 验证码要脱离主密码校验，就只能保留明文种子副本 `data/config/security/totp-standalone.json`（见 §4 与 AGENTS 的安全条目）。本分支不再接受这份可读种子：**仅 TOTP 的单一验证被彻底禁用**，只有调试构建或显式启动参数才允许启用。USB 单独验证、以及「密码 + 其他因素」的组合不受影响。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core.Tests/TestStandaloneTotpOptIn.cs` | `[ModuleInitializer]` 调用 `GlobalConstants.EnableStandaloneTotpVerification()`；安全测试本来就是在验证「已启用」路径，而 CI 跑 Release，必须显式开启 |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/GlobalConstants.cs` | 新增 `AllowStandaloneTotpVerification`（`= IsDevelopment`，即仅 Debug 构建默认可用于 Windows 开发机）、`EnableStandaloneTotpVerification()`、`EnableStandaloneTotpVerificationIfRequested(args)`（识别 `--allow-standalone-totp`） |
| `SecRandom.Desktop/Program.cs` | 启动时调用 `GlobalConstants.EnableStandaloneTotpVerificationIfRequested(args)`（与 `PluginManager.SetStartupArguments` 并列） |
| `SecRandom/Services/Security/SecurityCredentialStore.cs` | `LoadStandaloneTotp()` 在未允许时**直接返回 `null`**，遗留副本不再被读取 |
| `SecRandom/Services/Security/SecurityService.cs` | `totpPassed` 增加 `AllowStandaloneTotpVerification` 前置条件；凭据保存时未允许则改为 `DeleteStandaloneTotp()`，从而在下一次保存时清除遗留明文文件 |

**行为**：
- 未允许时，"任意已选方式"模式下 TOTP **不能单独通过**；此时不带密码的尝试会走到既有的「免密尝试」分支并计入失败次数（保留对验证码暴破的限制）——该分支无需改动。
- 已有安装升级后，遗留的 `totp-standalone.json` 在下次保存凭据时被删除；在此之前也不会被读取。

**已知缺口**：移动端 head 没有命令行入口，该调试路径只能靠 Debug 构建开启；若需要在移动端也能调试，需再加一个 head 级开关。

---

## 16. 普通模式证明可信度评分（本分支新增）

**意图**：给普通模式的证明加一个**可解释的可信度**判断——由用户选择「您所信任的来源方」，程序从满分开始，按「有哪些证据真的能支撑」扣分。它是一份**证据覆盖度**报告，不是密码学强度结论。

**评分规则（`ProofTrustScorer`，Core 纯函数）**：

| 规则 | 行为 |
|---|---|
| 来源方缺失 | 每个被勾选的来源方权重 **25**，分数 = 已获支持权重 / 已选权重 × 100（归一化，故只选一个且成立时也是 100） |
| 链自身完整性不通过 | **直接置零**（自证不成立则无从评分），其余证据不再计入 |
| 脉冲时效：时间戳 − 脉冲发布时间 **≤ 60 秒**（信标周期即 60 秒，同周期） | 不扣分 |
| 脉冲时效：**60–110 秒**（跨一个周期，属容忍范围） | 分数**减半**（`PreviousPeriodFactor = 0.5`） |
| 脉冲时效：**> 110 秒**（至少老两个周期，超出容忍） | **扣四分之三**（`BeyondToleranceFactor = 0.25`） |
| 脉冲时效的夹逼 | 结果始终夹逼到 `[0, 100]`，**不会为负**；档位以 `ProofTrustPulseTier` 返回（SamePeriod / PreviousPeriod / BeyondTolerance） |
| 社会见证 | 由用户提供「记忆中大致抽取时间」与「时间置信度」；高/中/低分别断言 5 分钟 / 30 分钟 / 2 小时的窗口，误差在窗口内**不扣分**；超出后按 `(误差−窗口)/窗口` 线性衰减，最多扣光该因子 |

**设计与边界**：
- 四个来源方**等权**，刻意不把密码学锚点排在人的记忆之上（那会变成密码学强度主张）。
- Core **不产出面向用户的文案**：因子结果以 `ProofTrustFactorState`（Satisfied/Missing/PartiallySatisfied/NotAssessed）+ 数值返回，由调用方本地化。
- 评估取**最新一份** `.srproof.json`：链位置、回执、时间戳来自 Up 文件，脉冲来自**参考兄弟文件**（`*.ownproof.json`），因此**不需要任何网络往返**；时间戳的**可信时间**由 `TimestampAuthorityClient.Validate` 离线校验后取得。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Services/Verification/ProofTrustScorer.cs` | `ProofTrustSource` / `WitnessTimeConfidence` / `ProofTrustFactorState` / `ProofTrustInput` / `ProofTrustFactor` / `ProofTrustReport` / `ProofTrustScorer` |
| `SecRandom.Core.Tests/ProofTrustScorerTests.cs` | 14 个用例：满分、链断置零、缺因子按比例扣、只选一个的归一化、不选来源为 0、上一周期（90 秒）减半、**110 秒边界仍属容忍**、**超过 110 秒扣四分之三**、减半/扣分不为负、同周期不罚、社会见证窗口内不扣 / 窗口外衰减 / 严重偏离不扣、未填时间视为缺因子 |

**修改文件**：`SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml(.cs)`（新增 `S_ProofTrust` 区块：四个来源方勾选、日期+时间输入、置信度下拉、评估按钮与结果文本；`AssessProofTrust_OnClick` 组装输入并格式化结果）、`SecRandom/Langs/SettingsPages/General/Verification/Resources{,.en-US,.ja-JP}.resx` + Designer（新增 21 个键）。

**已知缺口**：日期/时间输入用的是 Avalonia `DatePicker` + `TimePicker`，本机无法编译验证其成员名（`SelectedDate` / `SelectedTime`）；若 CI 报错，改这两处属性名即可。

---

## 14. 自有标识改名：由「保持 SecRandom」改为「VeriRandom 自有标识」

**意图**：把 §7「内部标识保持 SecRandom」改为自有标识。该约定原本以「不与上游生态冲突」为前提；本产品的定位是**与上游 SecRandom 在同一台机器上共存**，共存意味着包名、URI、可执行名、安装目录、单实例名必须是自有标识，否则会与上游互相抢占或覆盖。§7 的禁止改名表随之整体改写。

**已确定的标识**（域名使用 `yeyixiao.com` 的反写；品牌显示名沿用 `WinFunan`）：

| 项 | 旧值 | 新值 |
|---|---|---|
| Android/iOS 包名 | `cn.sectl.secrandom.mobile` | `com.yeyixiao.verirandom` |
| ContentProvider authority | 派生 | 自动跟随包名 |
| URI 主标识 | `secrandom://` | `verirandom://` |
| URI 兼容选项 | — | 额外注册 `secrandom://`，**默认关闭**，开启时弹窗警告 + 免责 |
| Windows 协议注册表 | `Software\Classes\secrandom` | `Software\Classes\verirandom`（兼容开启时才另加 `secrandom`） |
| 协议显示串 | `URL:SecRandom Protocol` | `URL:VeriRandom Protocol` |
| 桌面可执行 | `SecRandom.Desktop.exe` | `VeriRandom.Desktop.exe` |
| 启动器 | `SecRandomLauncher.exe` | `VeriRandomLauncher.exe` |
| 安装包标记 | `SecRandom.package.json` / `product:"SecRandom"` | `VeriRandom.package.json` / `product:"VeriRandom"`（必须与 Launcher、`UpdateCenterService.Product` 同步） |
| 单实例 Mutex / Pipe | `SecRandom_SingleInstance_SecRandom_3F2A1B0E` / `SecRandom_IPC_…` | `VeriRandom_SingleInstance_…` / `VeriRandom_IPC_…`（新 ID） |
| 自启动 Run 值名 | 产品名 | `VeriRandom` |
| macOS / Linux | `SecRandom.app`、`/usr/lib/secrandom` | `VeriRandom.app`、`/usr/lib/verirandom` |
| csproj `<Company>` | `SECTL` | `WinFunan` |

**不存在的项（已核实，无需处理）**：COM CLSID（唯一 COM 用法是调用系统的 `tabtip.exe`，不属于本产品）、硬编码监听端口（OAuth 回环使用 `TcpListener(Loopback, 0)` 临时端口）、特殊指定窗口类名、托盘唯一标识 GUID（仅有 `ToolTipText`，已是 VeriRandom）。

**已改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom.Android/SecRandom.Android.csproj`、`SecRandom.iOS/SecRandom.iOS.csproj` | `ApplicationId` → `com.yeyixiao.verirandom`（ContentProvider authority 派生自包名，自动跟随） |
| `SecRandom/Services/Desktop/DesktopIntegrationService.cs` | 主协议 `verirandom`、兼容协议 `secrandom`、`ActiveProtocolSchemes`（兼容开关控制）、Windows/macOS/Linux 三端按 scheme 列表注册与反注册、macOS bundle id/启动项/URL handler 处理器名、Linux 桌面项文件名 |
| `SecRandom.Core/Services/Ipc/ProtocolRequestParser.cs` | 新增 `ProtocolScheme` / `LegacyProtocolScheme` / `IsSupportedScheme`：主协议与兼容协议**都接受**（注册与否由 OS 决定，解析层接受两者是无成本的） |
| `SecRandom.Core/Models/SubConfigs/General/BasicSettingsConfig.cs` | 新增 `LegacyUrlProtocol`（默认 `false`） |
| `SecRandom/Views/SettingsPages/General/BasicSettingsPage.axaml(.cs)` | 新增兼容协议行 + `ConfirmLegacyUrlProtocolAsync` 警告弹窗（必选勾选 + `ConfirmDialogGate` 5 秒）；沿用该页既有的 `SettingsOnPropertyChanged` → `TrySetUrlProtocol` 应用/回滚模式，失败只回滚兼容项 |
| `SecRandom/Langs/SettingsPages/General/Basic/Resources{,.en-US,.ja-JP}.resx` + Designer | 新增 5 个键；`S_Behavior_UrlProtocol_D` 的描述改为主协议 `verirandom://` |
| `SecRandom.Core/Services/SingleInstance/SingleInstanceService.cs` | `VeriRandom_SingleInstance_VeriRandom_7D5E4C21` / `VeriRandom_IPC_…` |
| `SecRandom.Launcher/SecRandom.Launcher.csproj`、`SecRandom.Desktop/SecRandom.Desktop.csproj` | `<AssemblyName>` → `VeriRandomLauncher` / `VeriRandom.Desktop`（项目与文件夹名保持 `SecRandom.*`） |
| `SecRandom.Shared/Utils.cs` | 包标记文件名、数据根目录 `LocalApplicationData/VeriRandom/data`、环境变量 `VERIRANDOM_PACKAGE_ROOT`、写探针临时名 |
| `SecRandom.Launcher/Program.cs` | 包标记文件名、`product` 比对值、环境变量名、错误文案 |
| `SecRandom/Services/Updates/UpdateCenterService.cs`、`SecRandom/Services/Mobile/MobileUpdateService.cs` | 包标记与更新清单文件名、`Product`、`Repository` 与 `metadata.yaml` 指向本仓库（更新仍由 `GlobalConstants.UpdatesEnabled = false` 关闭） |
| `SecRandom.Platforms.{Abstractions,Windows,Linux,MacOs}` 的绑定标记 + `SecRandom.Core.Tests/SecurityServiceTests.cs` | 标记文件 `.VeriRandom.safety.key` |
| `Setup.iss` | `MyAppName`/`MyAppPublisher`/`MyAppURL`/`MyAppExeName` → VeriRandom；**自有 `AppId`**（不得与上游共用：共用 AppId 会让 Inno 把两者当成同一产品而互相覆盖或卸载）；`DefaultDirName` → `{autopf}`/`{localappdata}` 下 `WinFunan\VeriRandom`；`OutputBaseFilename` → `VeriRandom-Setup`；`[Code]` 的 As-is 免责声明（英文 ASCII，避免 .iss 编码歧义），声明与 SECTL 无关、非 SecRandom 本身、共存时可能争夺同一系统级注册（`secrandom://` 只能有一个持有者）、由此产生的接口不兼容由用户自行承担 |
| `.github/workflows/build_publish.yml` | 可执行名/包标记/`product`/便携版启动器/更新清单/dist 产物名/Android APK 与 iOS IPA 资产名/发布名/发布任务的产物名正则/Linux 包名与桌面项与图标安装名/macOS bundle id/ISCC 输出名前缀 `/FVeriRandom-…`（`/F` 覆盖 .iss 的 `OutputBaseFilename`，两处必须同时改） |
| `AGENTS.md` §7 与本文 §7 | 由「禁止改名」改写为「标识归属」：产品身份类自有、代码级内部标识保持 `SecRandom` |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` | `C_ExternalIntegrationDescription` 改为主协议 `verirandom://` |

**已知缺口**：
1. `SecRandomDocumentsProvider` 的文档树 `RootId` 仍为 `secrandom`（Android 内容提供程序内部节点 ID，不是产品身份，未改）。
2. `ChangeLog.md` 与 README 里的下载/协议说明需要在正式发版时同步核对（README 目前未提及协议标识）。
3. 本改名涉及 CI 产物名与发布任务正则，需在下次完整 CI 跑通后才能确认端到端一致（本机无 .NET 10 SDK）。

---

## 15. 桌面资源覆盖加载器的健壮性修复

**症状**：启动或退出时崩溃，堆栈落在首次显示窗口的排版阶段，异常为 `Could not create glyphTypeface. Font family: FluentSystemIcons-Resizable (key: avares://secrandom/Assets/Fonts/)`。

**成因（已确认）**：桌面头**刻意不内嵌** `Assets`，全部依赖 `OverlayAssetLoader` 把 `avares://SecRandom/Assets/...` 映射到可执行文件旁的物理目录；映射一旦落空就没有可用的回退，直接表现为首个字形测量时的 `Could not create glyphTypeface`。其中 `IsHandledAvaresUri` 用 **`StringComparison.Ordinal`** 比较 `uri.Authority` 与 `Assembly.GetName().Name`，而送进来的 authority 大小写**不由本程序保证**。

**为什么上游 beta 没暴露**：同一缺陷只在一条路径上出现——**安装版**送进来的 authority 是小写 `secrandom`（比对落空 → 必崩），而**便携版**恰好保留了 `SecRandom`（比对命中 → 正常）。上游 `v3.0.0-beta.1` 与便携版都走命中路径，所以一直未被发现。

**验证**：改为 `OrdinalIgnoreCase` 后的构建运行成功。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom/OverlayAssetLoader.cs` | `IsHandledAvaresUri` 的 authority 比较改为 `OrdinalIgnoreCase`，消除「大小写不中 → 静默回退 → 字体/图片加载失败」这一整类故障 |
| `SecRandom.Desktop/Program.cs` | `BindAssetLoader` 在绑定前检查物理 `Assets/Fonts` 是否存在，缺失时通过 `Trace` 与 `stderr` 明确报出「资源根不完整」并指出目录，避免故障只以字型异常的形式出现 |

**说明**：本节的修复与 §14 的标识改名无关（改名刻意保留了 `avares://SecRandom/...`）。

**同批修复：无 tag 时版本号被工具输出污染**

`AssemblyInfo.cs` 把 `GitInfo.Tag` 直接拼进 `AssemblyInformationalVersion`，而无 tag 仓库里 GitInfo 生成器会把 `git describe` 的 **stderr** 当成 Tag，于是版本号变成 `vfatal: No names found, cannot describe anything.`。危害不止崩溃报告：`producer_version` 变成非版本串后，导入闸门可能**拒绝本构建自己导出的归档**。

| 文件 | 改动 |
|------|------|
| `SecRandom.Core/GlobalConstants.cs` | `ReadMetadata` 新增 `LooksLikeVersionTag`：Tag 必须是「数字 `MAJOR.MINOR`」形态，否则回退到 `v3.0.0-dev`（主号需与 `PluginApiVersions.Current` 保持一致）；同时把「无 informational version」时的旧占位 `v0.0.0.0` 也改为该回退值，因为主号 `0` 与本构建写出的归档主号不符。提交哈希仍照常保留 |
| `SecRandom.Core.Tests/GlobalConstantsTests.cs` | 原 `AssemblyWithoutInformationalVersionKeepsThePlaceholder` 改为断言新回退值；新增 `TaglessGitDescribeOutputIsNotAdoptedAsTheVersion` 覆盖「工具输出不得被采纳为版本」这一回归 |

