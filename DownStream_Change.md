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
| 14 | 强化文件完整性校验：分离签名 | 新增 Core 签名/摘要与测试、`SecuritySettingsConfig.ConfigIntegrityMode` | 中（新增能力，默认仍为本地指纹） |

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
| `scripts/generate-android-keystore.ps1` | 生成本仓库自有 Android 发布密钥并打印需要配置的四个 secret。**keytool 调用必须把 stderr 并进宿主输出**：Java 工具把提示写到 stderr，而脚本开头是 `$ErrorActionPreference='Stop'`，stderr 会被转成终止性 `NativeCommandError`，且**中断发生在密钥写盘之前**——每次运行都会失败且不留产物。workflow 里的两步 keytool 校验本来就用 `2>&1`，此处与之保持一致，不得"简化"掉。**默认输出目录必须锚定在仓库根**（`Split-Path -Parent $PSScriptRoot` 再拼 `artifacts/android-keystore`），不能用相对路径——以管理员身份按绝对路径调用时当前目录是 `C:\Windows\System32`，相对默认值会被解析成 `C:\Windows\System32\artifacts` 并因权限被拒 |

**修改文件**：

| 文件 | 改动 |
|------|------|
| `.github/workflows/build_publish.yml` | ① 四处 `Get Latest Tag` / `Get Version` 增加无 tag 兜底（`try/catch` / `|| true`，回退 `git tag --sort=-v:refname`，再回退**主号取自 `PluginApiVersions.Current`** 的 `<major>.0.0-dev`）；② Android 四个签名 secret 缺失时改为告警 + debug 签名，产出 `-android-<arch>-unsigned.apk` 并跳过 keystore/证书校验（secret 齐全时严格校验不变）；③ `Combine Artifacts` 过滤 `-unsigned.apk`，避免 debug 签名包进入 release 与清单；④ 三个第三方 action 由浮动主标签改为 **commit SHA 绑定**（见下） |
| `SecRandom.Android/SecRandom.Android.csproj` | `ApplicationDisplayVersion` 优先取 `$(Version)`，仅在为空/`1.0` 时回退 `$(GitTag)`（与 iOS 一致） |

**第三方 action 必须 SHA 绑定，不得回退成 `@v1`**：`@v1` 这类浮动主标签可以在不通知使用方的情况下被重指向，而 `ncipollo/release-action` 正是发布 job 里**创建 GitHub Release 并上传全部产物**的那一步，一旦被替换就是供应链入口。绑定写法为 `uses: <owner>/<repo>@<40 位 commit sha> # <原标签>`，保留尾注释是为了让 Dependabot 仍能识别版本并提出升级 PR。当前已绑定 `ncipollo/release-action`、`NuGet/login`、`maxim-lobanov/setup-xcode`。

**必须区分附注标签与轻量标签**：`v1` 是哪种标签决定该钉哪个 SHA。附注标签的 `refs/tags/v1` 是**标签对象**，直接钉它 GitHub Actions 会在解析阶段失败；必须钉 `refs/tags/v1^{}` 的 deref commit。当前 `ncipollo/release-action` 的 `v1` 是轻量标签（两者同一 SHA），`NuGet/login` 与 `maxim-lobanov/setup-xcode` 的 `v1` 是附注标签，已按 deref commit 绑定。核对方式：`git ls-remote --tags https://github.com/<owner>/<repo>`，出现 `^{}` 行即为附注标签。

**`actions/*` 仍保持主标签**：`actions/checkout`、`setup-dotnet`、`upload-artifact`、`download-artifact`、`setup-java` 以及 `github/codeql-action/*` 均为 GitHub 官方维护，未做 SHA 绑定，以免在上游频繁改动这些行时制造无谓冲突。

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
| `SecRandom.Shared/Models/Verification/DrawProof.cs` | `DrawProofBeacon` 新增 `cipherSuite`（默认 `-1` 表示未记录；校验方必须**拒绝**未知套件，而不是假定一种）与 `beaconSources`（每个贡献种子的信标源一条稳定标识；当前恒为一条，为后续**信标混杂**预留形状，使校验方始终知道该拿哪家权威的证书验签）。二者**只影响 Own 参考节点**，Up 链字节形态与上游兼容不变 |
| `SecRandom.Core/Models/Verification/BeaconPulse.cs` | 新增 `CipherSuite`（尾随默认参数，不破坏既有构造调用）。此前 `NistBeaconClient` 解析了 `cipherSuite` 却没有传出，导致验签所需的规范字节序无法拼出 |

**行为与边界**：
- Up 链字节形态与上游一致（已置空信标），提交 `fair.sectl.cn` 与 TSA 的字段集不变。
- Own **仅**在启用信标熵时写入；它是参考声明，不提供额外密码学可信度，也不参与公平性判定。
- Own 的 TSA 令牌先于 Up 申请，用于说明参考节点不是事后补加。
- Own 导出失败**不得**影响已完成的抽取（`DrawProofExportService.Save` 内捕获并告警）。

**链判决的用户可见性**：服务端在每次抽取回执里返回的链锚点判决（`chain.status`）**每次新抽取都会更新一次**。本分支把它从「计数摘要里的一段文字」提升为**独立、加粗、告警色**的提示（`ProofChainAlertText`），因为 `behind` 是「本地链被删过或倒退过」唯一的外部信号，埋在摘要里太容易被忽略。它**不做持久化**（`ChainAlertCount` 仍是进程内计数，重启归零），也不参与证据评分。

**信标脉冲验签尚未实现**：需要按 `cipherSuite` 拼出权威机构的规范字节序、按 `certificateId` 取证书，并决定信任锚点。本分支已先补齐缺失的 `cipherSuite` 与来源标识，**落地验签时不必再改 Own 格式**；但已写出的旧 Own 节点缺 `cipherSuite`，只能判定为「无法验签」而不是「验签失败」。

**信标来源默认列表（`BeaconSourceCatalog`）**：内置两个可选来源，**同一时刻只有一个生效**（种子派生只取单一来源的脉冲，所以是二选一而不是混合）。两者都自带验签材料，因为「从网络取来却不校验」等于只信任应答的中继：

| 来源 | period | genesis | 验签材料 |
|---|---|---|---|
| `nist-beacon-v2` | 60s | — | **无法内置公钥**：叶子证书按 `certificateId` 约每半年轮换，签发者是公共 WebPKI 的 DigiCert，不是 NIST 自有 CA。因此内置的是**身份约束** `CN=engine.beacon.nist.gov` |
| `drand-quicknet` | **3s** | 1692803367 | **必须内置** BLS 链公钥（96 字节）+ 链哈希；链公钥对该链永久不变，内置它才能阻止恶意中继用自己的密钥应答 |

选 quicknet 的理由是算术上的：`60 / 3 = 20` 整除，且 `1692803367 = 3 × 564267789` 余数为 0，所以它的轮次网格与整分同相，每个 NIST 窗口正好 20 轮、每个 10 秒桶 3 轮。它还是 `bls-unchained-g1-rfc9380`（**unchained**）——任意单轮只需链公钥即可独立验证，历史验证无需重放整条链，这正是「事后拉历史脉冲」需要的前提。

**尚未接线**：来源选择目前只是 Core 目录（含 `RoundAt` / `RoundTime` 换算），还没有配置字段、设置页选项、三语文案，也还没有 drand 客户端。`RoundAt`/`RoundTime` 只对 drand 来源有效，NIST 的 `period` 在 API 上以**毫秒**返回（60000），目录里统一为秒，接线时必须换算。

**参考链展示**：设置页现同时展示参考链（Own）的节点数、已时间戳数、链头与保留底线，并在 `IsIntact` 为假时单列「被篡改 / 序号异常」。**它刻意不参与 `IsHealthy`，因此不得因为 Up 链健康就在展示上被吞掉**——`FormatIntegrityReport` 必须保留这个独立分支。

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

**已收口**：`SectlAuthService.RefreshCoreAsync` 现在先查 `SectlTrafficPolicy.IsEgressAllowed`，未同意跨境须知时**不发出任何请求**（连 refresh token 也不送到令牌端点），返回可重试的 `cross_border_not_accepted`；并**刻意不清除本地令牌**——撤回同意不应销毁账号会话，重新同意后仍可继续轮换。`AnnouncementService.GetAsync` 同样先判闸门，未同意时直接返回空列表而非向 Appwrite 网关发请求（公告是可选内容，静默留空）。`SectlHeartbeatService` 本就有同一闸门。`PlatformVersionReportService` 仍是**已记录在案**的唯一例外，上述路径都不属于例外。

**补签就地完成**：未同意出境须知时，`S_AttestationUpload` 的单选组会置灰，同时在**该组外面**出现说明与「立即同意」按钮——补签入口必须放在被禁用的组之外，否则会跟着一起禁用。出境需要**两个**同意（上游在线服务须知与本分支跨境须知），按钮按顺序补齐缺失的那个（对话框由各自的策略负责），成功后**就地刷新**并立即解锁，因此不必离开页面重进。本节的出境闸门相关事项至此全部收口。

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

**刻意保留与后续核对**：
1. `SecRandomDocumentsProvider` 的文档树 `RootId` 仍为 `secrandom`——Android 内容提供程序内部节点 ID，不是产品身份，**刻意保留**，不是待办。
2. `ChangeLog.md` 与 README 的下载/协议说明在正式发版时同步核对（README 目前未提及协议标识）；这是发版动作，不是当前缺口。
3. CI 产物名与发布任务正则的一致性以完整 CI 跑通为准。

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


## 16. 普通模式证明可信度评分（本分支新增）

**意图**：给普通模式的证明加一个**可解释的可信度**判断——由用户选择「您所信任的来源方」，程序从满分开始，按「有哪些证据真的能支撑」扣分。它是一份**证据覆盖度**报告，不是密码学强度结论。

**评分规则（`ProofTrustScorer`，Core 纯函数）**：

| 规则 | 行为 |
|---|---|
| 来源方缺失 | 每个被勾选的来源方权重 **25**，分数 = 已获支持权重 / 已选权重 × 100（归一化，故只选一个且成立时也是 100） |
| 链自身完整性不通过 | **直接置零**（自证不成立则无从评分），其余证据不再计入 |
| 脉冲时效 | 交给 **`BeaconPulsePeriodPolicy`**（唯一规范，链校验器用的是同一份）判定：同周期不扣分；上一周期内（容忍 1 个周期）**减半**（`PreviousPeriodFactor = 0.5`）；超出容忍**扣四分之三**（`BeyondToleranceFactor = 0.25`） |
| 脉冲**晚于**时间戳超过策略的 60 秒时钟偏移 | 判为超出容忍——未来脉冲不可能是本次抽取的种子来源。**不得**回退成「只比绝对秒数」：那会把未来脉冲当成完全正常 |
| 脉冲时效的夹逼 | 结果始终夹逼到 `[0, 100]`，**不会为负**；档位以 `ProofTrustPulseTier` 返回（SamePeriod / PreviousPeriod / BeyondTolerance） |
| 社会见证 | 由用户提供「记忆中大致抽取时间」与「时间置信度」；高/中/低分别断言 5 分钟 / 30 分钟 / 2 小时的窗口，误差在窗口内**不扣分**；超出后按 `(误差−窗口)/窗口` 线性衰减，最多扣光该因子 |

**设计与边界**：
- 四个来源方**等权**，刻意不把密码学锚点排在人的记忆之上（那会变成密码学强度主张）。
- **每个证据都按其方向内可实现的最高规格校验，而不是只判断「存在」**：链交给 `ProofIntegrityVerifier`——它是唯一能发现断链、超出链头、以及未记入删除台账的缺口的实现，读的是已持久化的 `chain-head.json`，因此**不需要新增任何持久化**；时间戳必须 `TimestampAuthorityClient.Validate` 通过（验不过的 token 不构成证据）；回执必须经 `WitnessClient.TryValidateStoredReceipt` 校验**服务端签名 + 与本证明的绑定**（签名只证明「发过某个回执」，绑定比对不可省）。
- **打分器不判断是否存在被移除的链尾。** 链判定只覆盖**仍然存在**的证明：因保留期或存储上限被正常删除的旧证明，由 `chain-head.json` 的删除台账与 `RetainedFromIndex` 底线解释，属正常保留行为而非篡改。这条必须保持——否则按期清理证明会被误报成链断，评分与完整性报告都会失真。
- Core **不产出面向用户的文案**：因子结果以 `ProofTrustFactorState`（Satisfied/Missing/PartiallySatisfied/NotAssessed）+ 数值返回，由调用方本地化。
- 评估取**最新一份** `.srproof.json`：链位置、回执、时间戳来自 Up 文件，脉冲来自**参考兄弟文件**（`*.ownproof.json`）。**离线只是当前实现方式，不是约束**——凡证据只能对着权威机构确认的方向，允许联网取（例如信标脉冲的签名验证要取 NIST 对应 `certificateId` 的证书）；时间戳的**可信时间**由 `TimestampAuthorityClient.Validate` 校验后取得（验签 + 摘要绑定 + 证书链）。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Services/Verification/ProofTrustScorer.cs` | `ProofTrustSource` / `WitnessTimeConfidence` / `ProofTrustFactorState` / `ProofTrustInput` / `ProofTrustFactor` / `ProofTrustReport` / `ProofTrustScorer` |
| `SecRandom.Core.Tests/ProofTrustScorerTests.cs` | 16 个用例：满分、链断置零、缺因子按比例扣、只选一个的归一化、不选来源为 0、上一周期（90 秒）减半、**110 秒边界仍属容忍**、超过容忍扣四分之三、**未来脉冲（晚于时间戳 10 分钟）判为超出容忍**、**仅略新（晚 30 秒）仍属同周期**、减半/扣分不为负、同周期不罚、社会见证窗口内不扣 / 窗口外衰减 / 严重偏离不扣、未填时间视为缺因子 |

**修改文件**：`SecRandom/Views/SettingsPages/General/VerificationSettingsPage.axaml(.cs)`（新增 `S_ProofTrust` 区块：四个来源方勾选、日期+时间输入、置信度下拉、评估按钮与结果文本；`AssessProofTrust_OnClick` 组装输入并格式化结果，其四项证据改为**校验后**传入而非仅判存在）、`SecRandom/Services/Verification/WitnessClient.cs`（新增 `internal static TryValidateStoredReceipt`，把 `AttestAsync` 的绑定比对复用于「重新校验已存储回执」）、`SecRandom.Core/Services/Verification/ProofTrustScorer.cs`（脉冲档委托给 `BeaconPulsePeriodPolicy`，并删除此前重复的 `MaximumPulseAge` 上限）、`SecRandom/Langs/SettingsPages/General/Verification/Resources{,.en-US,.ja-JP}.resx` + Designer（新增 21 个键）。

**成员名已证实**：`DatePicker.SelectedDate` 与 `TimePicker.SelectedTime` 是 Avalonia 12 的正确成员名，已由既有用例互证——上游带进来的 `HistoryManagementSettingsPage` 用 `DatePicker.SelectedDate`，`TimerView.axaml` 绑定 `TimePicker.SelectedTime`。不要改名。

---

## 17. 强化文件完整性校验：分离签名

**意图**：现有本地指纹存放在凭据信封的**明文部分**，代码注释自己写明「能改凭据文件的人也能改这份指纹」，因此只能发现误改、不能防本地攻击者。本节的分离签名把**签发能力搬离抽取机**：私钥只存在于主持人自己的可信设备，抽取机只携带公钥，因此它能验证策略但永远无法自行签发。

**必须遵守的设计决定（不得回退）**：
- **签名私钥绝不由用户密码派生，必须随机生成。** 密码是低熵的：若私钥由它确定性派生，任何拿到「公钥 + 一条签名」的人都能离线穷举密码（对每个候选密码派生公钥去比对），签名会变成密码破解的验证器；且改密码会让已部署到抽取机的历史签名全部失效。密码/口令只在**私钥落盘时用于加密保护**，不参与密钥本身。`ConfigSignatureTests.ThePrivateKeyIsNeverDerivedFromAUserPassword` 锁住这一点。
- **Argon2id 是口令 KDF，不是签名方案**；PKCS#8 是容器格式不是算法。实际形态：私钥 PKCS#8、公钥 SPKI、算法 **ECDSA P-256 + SHA-256**——复用本仓库既有的加密栈（`WitnessClient` 已用 `ECDsa` + `ImportSubjectPublicKeyInfo` + `DSASignatureFormat.Rfc3279DerSequence`），不引入新依赖。
- **签名对象是策略子集的规范化摘要，不是整份 `settings.json`。** 该文件含大量运行期可变字段（窗口尺寸/最大化、最近使用列表、计时器预设、主题色等），抽取机正常使用就会改写它们；若覆盖这些，签名会在第一次普通交互后失效，功能反而不可用。
- **公钥不做持久化钉住**（按当前决定）。指纹比对因此是**唯一**的防替换手段，界面必须显著展示简短摘要并明确要求与可信设备逐字核对；文案不得暗示比实际更强的保护——本地攻击者若同时替换公钥与签名，程序无法自行察觉。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Enums/Configs/ConfigIntegrityMode.cs` | `Off` / `LocalFingerprint` / `SignedPolicy`；放在叶子命名空间以避免 `Core.Models` 与 `Core.Services` 相互依赖 |
| `SecRandom.Core/Services/Security/ConfigSignature.cs` | `ConfigSignatureDocument`（formatVersion / algorithm / scope / publicKey / digest / signature / signedAtUtc / signerLabel）与 `CreateKeyPair` / `ComputeDigest` / `Sign` / `Verify` / `Fingerprint` / `IsDocumentWellFormed`；签名按**摘要原值**进行（`SignHash`），不再二次哈希 |
| `SecRandom.Core/Services/Security/ConfigPolicyDigest.cs` | 覆盖范围：`general.securitySettings`（**排除完整性字段自身**：`SettingsIntegrityCheckEnabled` / `SettingsIntegrityAction` / `SettingsIntegrityRestoreSource` / `ConfigIntegrityMode`）、`general.verification`、`general.privacySettings`、`linkageSettings`、`fairDrawSettings`、`moreSettings.lotteryEnabled`；`ScopeId = "verirandom-config-policy/v1"`，覆盖字段集变化必须同步改该 id；投影递归**按成员名排序**，因此调整声明顺序不算策略变化 |
| `SecRandom.Core.Tests/ConfigSignatureTests.cs` | 8 个用例：只对原摘要成立、篡改摘要被拒、**换密钥对不能冒充签发者**、翻转签名位被拒、畸形输入返回 false 而不抛、指纹稳定且与密钥绑定、文档格式校验（错误算法/版本/空 scope 被拒）、两次生成必须互相独立 |
| `SecRandom.Core.Tests/ConfigPolicyDigestTests.cs` | 5 个用例（含 1 个 6 分支 Theory）：确定性；改动范围内设置必变；**改动运行期状态（窗口几何、计时器预设、默认名单、主题色）必须不变**；**改动完整性检查自身必须不变**；端到端微缩流程（宿主签名 → 抽取机重建摘要验签通过 → 抽取机改策略后验签失败） |
| `SecRandom/Services/Security/ConfigPolicySignatureService.cs` | 读写部署文档 `data/config/config-policy.json`：`Verify()`（模式未开→NotConfigured；文档缺失/不可读/scope 不符→DocumentMissing/DocumentMalformed；摘要不符→SignatureInvalid；通过→Verified 并给出签名者与公钥指纹）、`Sign(privateKey, signerLabel)`（**私钥以参数传入，本类不持有**，原子写入）、`ReadDocument()`；文档只含公钥与签名，因此可安全随备份分发 |
| `SecRandom.Core/Services/Security/ConfigSignature.cs` 增加 `PublicKeyFromPrivateKey` | 主持人粘贴既有私钥时能派生公钥，无需重新生成密钥对 |

**启动闸门**：`SecRandom/App.axaml.cs` 的既有设置完整性闸门扩展为「本地指纹 **或** 分离签名」——本地指纹无异常时再校验签名，失败则复用同一套处理（`SettingsIntegrityAction.AutoRestore` 走自动恢复，否则走需要安全密码的确认闸门），并写入启动诊断。DI 注册 `ConfigPolicySignatureService`。

**设置页 UI**：`SecRandom/Views/SettingsPages/General/SecuritySettingsPage.axaml(.cs)` 的「防篡改」区块新增 `S_SignedPolicy` 分组——三选一模式下拉、公钥简短指纹**显著展示**（`SelectableTextBlock` + 加粗的逐字核对提示 + 状态行）、签名者标签、私钥粘贴框，以及「生成新密钥对」「签发当前配置」两个按钮。两条约定必须保持：
- **模式切换走既有的安全授权链路**（`ApplySecuritySettingsUpdateAsync`），与本地指纹、恢复来源共用同一条密码闸门，不得直接落盘；
- **生成密钥对**使用随机 `ECDsa` P-256（`ConfigSignature.CreateKeyPair`），私钥只经**剪贴板**交给用户带走，本机既不落盘也不存入任何字段；剪贴板不可用时宁可不生成，避免造出一个用户无法带走的私钥。**签发**经 `AuthorizeAsync(ChangeSecuritySettings)`：未启用安全保护时不弹窗，启用后按已选因子校验，被拒时保留输入框内容。

**新增文案**：`SecRandom/Langs/SettingsPages/Security/` 三语各新增 20 键（`S_SignedPolicy`、`S_SignedPolicy_D`、`O_SignedPolicy_{Off,LocalFingerprint,Signed}`、`C_SignedPolicy_{Fingerprint,VerifyHint,Generate,PrivateKey,Signer,Sign}`、`M_SignedPolicy_Status_{NotConfigured,DocumentMissing,DocumentMalformed,SignatureInvalid,Verified}`、`M_SignedPolicy_{KeyGenerated,Signed,SignFailed,ClipboardUnavailable}`）。注意该页的强类型访问器在手写 partial `Resources.Security.cs`（`=> Text(nameof(...))` 风格）而**不在 `Resources.Designer.cs`**，新增键必须加到前者，否则 XAML 的 `{x:Static}` 解析不到。

**已知缺口**：Core 用例、服务层、启动闸门与设置页 UI 已由 CI 编译验证；指纹核对仍是**人工**比对，按当前决定不做持久化钉住，因此本地攻击者同时替换公钥与签名时程序无法自行察觉。

**修改文件**：`SecRandom.Core/Models/SubConfigs/SecuritySettingsConfig.cs` 新增 `ConfigIntegrityMode`（默认 `LocalFingerprint`，保持既有行为不变）；`SecRandom/App.axaml.cs` 扩展启动闸门并注册服务；`SecRandom/Views/SettingsPages/General/SecuritySettingsPage.axaml(.cs)` 与 `SecRandom/Langs/SettingsPages/Security/` 承载设置页 UI 与文案。

---

## 18. 双抽取预承诺协议（服务端对接文档）

**意图**：为「双抽取」预承诺模式提供**服务端对接规格**。客户端向自建服务端索取一个 nonce，服务端当场生成并**先落盘再响应**，同时在响应里**点名**一个**尚未发布**的脉冲来源（NIST v2 或 drand quicknet，二选一）；客户端随后对该承诺申请 TSA，等该脉冲发布后以其为唯一未来熵导出种子。目的是让「用了某个承诺却不说」在外部**可被发现**。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `docs/dual-draw-server-integration.md` | 服务端对接文档（协议 v1）：目的与信任边界、术语、时序、承诺记录模型、六个端点、规范哈希（`inputDigest` / `commitmentHash` / 响应签名投影）、两种脉冲来源的身份格式与内置验签材料、哈希链与公开归档要求、**强制对账流程**、客户端会做的校验、**第三方（接收方）验证的两层模型**（单件离线可验证 + 拉取该身份全量历史逐条对账）、错误与边界情形、安全运维要求、与客户端既有字段的对应，以及已确认的设计决定清单 |

**必须保留的设计决定**：
- **承诺必须覆盖抽取输入**（`rosterDigest` + 人数 + 抽样模式 + 算法配置档），不能只承诺 nonce。脉冲一发布种子即确定，若此时名单与规则尚未被承诺，操作者可以在**所有校验都通过**的情况下挑名单来操纵结果。
- **`nonce` 必须来自 CSPRNG 且 ≥256 位**，不得用计数器或时间戳；签发必须**先落盘再响应**，存储层不得存在 UPDATE/DELETE 路径。
- **客户端把 nonce 混入种子的那一步是确定性 KDF，不是随机数发生器**——文档必须这样写。否则实现者很可能真的调用随机数发生器，种子将不可复现，整套重放验证失效。
- **指派脉冲必须点名到具体身份**（drand 用 `chainHash` + `round`，NIST 用 `chainIndex` + `pulseIndex` + `timeStamp`），且 `publishedAt` 严格晚于 `issuedAtUtc`。签发后不得改派，指派脉冲缺席时也**不得静默换源**。
- **定期上传 GitHub 不构成防篡改**：记录必须哈希链化，链头需在第二处可见。公开归档是冗余的第二份记录，**信任根是客户端持有的预抽取 TSA 令牌**——正因如此服务端响应必须自带签名，使归档自证。
- **对账流程必须强制并留书面结论**：未消费承诺是「用了又不说」的唯一可见信号，差额无人追查等同于没有这项要求。
- **预承诺记录不得放进既有 Own 链**：`OwnProofChainStore` 的不变式（每节点对应一个 Up 序号、序号从 0 公差 1、`ComputeSelfHash` 的覆盖字段）会被「一个 Up 序号对两个 Own 节点」破坏。**Up 链保持不变**，上游兼容性不受影响。
- **信任边界必须写进宣传口径**：只在「抽取者 ≠ 服务端运营者」时对抗抽取者；同一人自建自抽时它只是自我背书，且协议不证明名单真实或完整。

**设计状态**：文档末节的六项取舍**已全部确认采纳**（承诺输入范围、哈希链、强制对账、脉冲余量、默认来源、预承诺独立存储），实现时必须满足。

---

## 19. 发布元数据与版本标签

**意图**：`metadata.yaml` 是更新客户端「频道 → 发布标签」的解析来源，因此它必须与本分支的**产品名与发布标签**一致，否则客户端会直接拒绝整份元数据。

**必须保持的两条**：
- **`product` 必须是 `VeriRandom`。** `UpdateCenterService` 的 `Product = "VeriRandom"` 会与这份元数据做**精确匹配**（不相等即抛异常）。因此本仓库的 `metadata.yaml` 永远不能是 `product: SecRandom`——那是上游的值，会让本分支自己的元数据被自己的客户端拒绝。`MobileUpdateService` 目前仍指向上游仓库的 `metadata.yaml`，与桌面端的指向不一致，属遗留项。
- **`channels.<频道>.tag` 必须与发布标签完全相等。** `scripts/validate-release-metadata.ps1` 在每次手动发布前做这道闸门，而发布任务检出的正是该标签，所以修正必须落在被标签指向的那个提交里。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `metadata.yaml` | `product` 改为 `VeriRandom`（**必需**，见上）；`repository` / `manifest.file_name` / `manifest.signature_file_name` / `public_key_id` 改为本分支自有值，与客户端实际使用的仓库常量、清单文件名和内置验证公钥保持一致。客户端**只读取** `schema_version` / `product` / `channels`，其余字段为说明性质 |
| `.github/workflows/build_publish.yml` | 第三方 action 固定到 commit SHA（见 §6） |
| `scripts/gen-release-note.ps1` | 发布说明**优先取本仓库的 `ChangeLog.md`** 中该标签的章节，`CHANGELOG/` 只作回退。`CHANGELOG/` 是上游的树，直接读它会把**上游的发布说明与横幅**（含 `appwrite.sectl.cn` 图片与上游链接）当作本分支的发布说明发布出去，而描述的却是本分支没有的功能。下载表的镜像列也改为本分支自己的镜像前缀 `ghproxy.sectl.cn`，不再指向 `stk.sectl.cn/SecRandom` |
| `SecRandom/Services/Mobile/MobileUpdateService.cs` | `Repository` 与 `MetadataUri` 改为本分支（`WinFunan/VeriRandom`），此前移动端仍指向上游仓库与上游元数据，与桌面端 `UpdateCenterService` 不一致 |

**发布标签的三个注意点**：
1. 本分支与上游共用 `vX.Y.Z` 标签体系，因此**移动一个已存在的标签前必须确认它尚未发布过产物**；标签一旦被推送并有人据此构建，移动它就会让同一版本号对应两份不同的构建。
2. `scripts/validate-release-metadata.ps1` 只接受 `-alpha.N` / `-beta.N` 或无连字符的稳定标签，所以**不能用 `-fork.N` 这类后缀**走发布闸门——尽管 `ChangeLog.md` 的维护约定允许该后缀区分多次修订。两者冲突时**以发布闸门为准**。
3. 无 tag 的检出会被 `GlobalConstants.ReadMetadata` 回退成主号取自 `PluginApiVersions.Current` 的 `<major>.0.0-dev`（见 §15），因此发布前必须让标签落在被构建的那个提交上。

---

## 20. 品牌图标与首启引导的社区入口

**意图**：本分支的产品图标与社区入口必须是自有的，不能再显示上游 SecRandom/SECTL 的图标与链接。

**图标母版**：仓库根目录的 `logo.jpg`（529×529、`Format24bppRgb`）是全部软件图标的**唯一母版**，由 PowerShell + `System.Drawing` 离线派生，不依赖任何外部转换工具。

| 目标 | 尺寸 | 说明 |
|------|------|------|
| `SecRandom/Assets/AppLogo.png` | 512×512 | 托盘、窗口与界面内 logo 的源图。原为 128×128；所有引用处都显式指定宽高，放大不改变布局，且 HiDPI 下更清晰 |
| `SecRandom/Assets/AppLogo.ico` | 16/24/32/48/64/128/256 共 7 档 | `System.Drawing` 的 `Icon.Save` 只能写单一尺寸，故按 ICO 容器格式**手写多尺寸**（PNG 压缩条目，Vista 起支持），保证资源管理器与任务栏取到清晰档位 |
| `SecRandom.Android/Icon.png` | 512×512 | `AndroidManifest.xml` 的 `android:icon="@drawable/Icon"` 指向它 |
| `resources/secrandom-icon-paper.png` | **256×256** | 同时供 README 与 Linux 包使用；Linux 会把它装到 `usr/share/icons/hicolor/**256x256**/apps/verirandom.png`，原来 128×128 与目标目录声明不符，属顺带修正 |
| `SecRandom.iOS/.../AppIcon.appiconset/Icon-*.png` | 逐个沿用**原有像素尺寸** | 共 15 个（含 `-ipad` 变体），尺寸取自原文件，避免与 `Contents.json` 不一致 |

**圆角与 alpha**：母版是 JPEG 且**无 alpha 通道**，所以圆角必须在派生时切出来——桌面与 Android 那套用「反相路径（外方框 + 内圆角，`FillMode.Alternate` 只剩四角）+ `CompositingMode.SourceCopy`」把四角替换成透明，比例 **22.37%**（macOS 图标比例，可调），因此它们是 `Format32bppArgb` 且四角透明。**iOS 的 AppIcon 必须保持方形且不透明**：iOS 会自行套用圆角遮罩，带 alpha 的 AppIcon 也不被接受，所以那 15 个文件**不参与圆角处理**，脚本改动它们即为错误。

**修改文件**：

| 文件 | 改动 |
|------|------|
| `SecRandom/Views/SettingsPages/About/AboutSettingsPage.axaml` | 版权区新增一行 `Copyright (c) 2025-<年> WinFunan`，位于原 SECTL 版权行**之上**。两行放进同一个 `StackPanel`——`FASettingsExpanderItem` 只接受单个内容子元素 |
| `SecRandom/Views/FirstRunOobeWindow.axaml` | 首启引导结束页的链接由上游站点、上游作者 B 站与爱发电、上游 QQ 群，换成本分支社区入口：QQ 群、邮箱、哔哩哔哩、Issue、贡献指南（与关于页 `S_Community` 分组一致）。**上游链接不再出现在该页** |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` + `Resources.Designer.cs` | 新增 5 个 `C_Community*` 键与访问器。注意该页 Designer 是**传统 ResX 生成风格**（`ResourceManager.GetString`），不是 `Verification` 页的 `Get(nameof(...))` 风格，插入时必须匹配对应形态 |

**保留未改**：`xmlns:sr="http://secrandom.sectl.cn/schemas/xaml/core"` 是**代码级内部标识**（见 §7），保持 `secrandom.sectl.cn` 不变；上游的 `C_SectlWebsite` / `C_Documentation` / `C_AuthorBilibili` / `C_Sponsor` / `C_Community` 五个键**保留未删**（只是不再被该页引用），避免误删仍被别处引用的键。

---

## 21. 上游 v3.1.5 同步（合并 `6782f5fa`）与冲突处置

**意图**：把上游 v3.1.5（集控节点、移动端云备份、安全设置移出 `settings.json`、移动端 OAuth、iOS 独立工作流）同步进来，同时**保住本分支的每一条分歧**。这是一次大合并：**35 个冲突 + 354 项非冲突改动**。

| 类别 | 冲突数 | 处置 |
|------|--------|------|
| `.resx` | 23 | 取两侧并集，逐文件验证「键集 = 双方并集、无重复键、XML 合法、无标记」 |
| 测试调用点 | 3 | `SectlAuthService` 构造签名取两侧并集。**实际有 9 处调用，其中 6 处在上游新增的测试文件里仍是 5 参调用——没有冲突标记，桌面发布任务又不编译测试工程，所以首轮 CI 也看不到**（见下） |
| `SectlAuthService.cs` | 2 | 构造函数**同时**保留 `configHandler`（我方出境闸门）与 `redirectBrokerFactory`（上游移动端回调）；`SignInAsync` 保留我方闸门，丢弃上游删掉的 `port`/`redirectUri` 两行 |
| `SecuritySettingsPage.axaml.cs` | 1 | using 取并集 |
| `THIRD-PARTY-NOTICES.md` | 1 | 上游版详尽许可分析 + 保留我方 `## SecRandom` 上游署名段 |
| `scripts/gen-release-note.ps1` | 1 | **互补合并**：我方「优先取 `ChangeLog.md`」+ 上游「去掉正文已有下载表」，形成三级回退（我方 ChangeLog → 上游 body → 上游 CHANGELOG → 占位）。不得回退成只读 `CHANGELOG/`——那是上游的树，会把上游发布说明与横幅当作本分支的 |
| 三份 AGENTS | 6 | 以上游为基础 + 补回我方独有条目。`VeriRandomLauncher` 等**产品身份类标识不得**被上游版本取代（上游那行与之只差一个词，机械合并会静默改回） |
| `build_publish.yml` | 1 | 上游把 `build_ios` job 整体移入独立的 `build_ios.yml`，故删除我方那个 job；`publish` 头部改为声明本分支禁用 iOS |

**本分支的新决定（属分歧，必须保留）**：**iOS 构建禁用**。`.github/workflows/build_ios.yml` 改名为 **`build_ios.yml.disabled`**（文件改名即失活，保留便于恢复，与仓库既有 `Build.yml.bak` 同一做法）；该文件内的 `maxim-lobanov/setup-xcode` 一并改为 SHA 绑定，使 AGENTS 中「第三方 action 已绑定」的说法为真。上游的 IPA 回填路径（`validate_release` 派发 + `backfill_release`）**有意不接入**；`build_ios` job 删除后，`publish` 的 `needs`/`if` **不得**再引用 `build_ios`。

**必须记住的三个坑（本次合并亲历）**：
- **上游把 `SecuritySettingsConfig` 移出 `MainConfigModel`**（移入加密的 `data/config/security/settings.json`，经 `SecuritySettingsStore`）。这**不会产生冲突标记**，却会让本分支 §17 的代码编译失败。已改为：`ConfigPolicyDigest.Compute(model, securitySettings, rosterDigest)` 显式接收安全设置；`ConfigPolicySignatureService` 注入 `SecuritySettingsStore`。**每次同步上游都要优先复查这一点**。
- **给某个类型的构造函数加参数时，上游新增的测试文件会静默编译失败**。本分支把 `MainConfigHandler configHandler` 插进 `SectlAuthService` 的主构造函数（第 5 位、`IAuthRedirectBrokerFactory` 之前），而上游 v3.1.5 新增的 `ControlNodeEnrollmentChannelTests`、`NodeEnrollmentTests`、`MobileRemoteDrawTests` 仍按 5 参调用——**没有冲突标记**，且桌面发布任务不编译测试工程，连首轮 CI 也照不出来。**结论：改完任何构造签名后，必须全仓检索该类型的全部构造点逐个核对参数个数，不能只信冲突列表。**
- 同一处还触发了 **CS0051**：`ConfigPolicySignatureService`（我方 §17 的 `public` 服务）持有上游的 `internal` 类型 `SecuritySettingsStore`，`public` 类的构造函数不能暴露更不可见的参数类型。已把它改为 `internal sealed class`，与其三个同类（`SecurityService`、`SettingsIntegrityService`、`SettingsIntegrityRecoveryService`）保持一致——它只在应用程序集内被 `IAppHost` 解析，改 internal 不涉及任何上游分歧。
- **取 git 里的文件内容绝不能走 PowerShell 管道**：`& git show ":2:路径"` 会让输出经控制台代码页处理，CJK 被替换成 `?`，写回即污染文件。必须用 `cmd /c 'git show ... > 文件'` 或 .NET 文件 API。失败特征是「纯 ASCII 文件全部通过、CJK 文件成片失败」。
- **CJK 不得写进 `.ps1` 字面量**（本次在文档合并脚本里又犯了一次）。中文只应作为从文件读出的数据出现。

**已知缺口**：
1. `build_publish.yml` 的 `validate_release` 仍保留上游的 iOS 派发步骤（`actions: write` 权限 + `Trigger the iOS build` + `gh workflow run build_ios.yml` + 告警兜底）。工作流已改名，因此它**每次发布都会走到那条 `::warning::`**（不会失败），建议连该步骤与那行权限一起删除。
2. 三份 AGENTS 用机械规则合并（上游为基础 + 补回我方独有条目），有 **13 处**「按同一项处理、取了上游版本」需人工复核是否吞掉了分支特有事实。
3. OOBE 步骤编号不自洽：`FirstRunOobeViewModel.IsCrossBorderStep` 判 `SelectedStep == 2`，而上游把顺序改为 8 页（`StepCount => 9` 两侧共有因而**没有冲突标记**），需对着 `FirstRunOobeWindow.axaml` 的实际页面顺序核对。
4. 本次合并（约 390 项）已由 CI 编译验证；运行时行为仍需按上面的条目在真机逐项核对。

---

## 22. 分离签名的指纹徽标（§17 的可见性补充）

**变更**：分离签名生效时，除安全设置页之外，**抽签主界面（`main.rollCall`）也显示签名公钥指纹**。

**为什么**：指纹是逐字核对签名者身份的唯一手段，只在设置页显示意味着日常抽签的人看不到「本机抽取到底由谁签发」。徽标内容是 `已锁定 · 指纹 <32 位大写十六进制>`，悬停显示签名者与签发时间。

**实现要点（都要保留）**：
- 只区分「未启用（不显示）」与「已验证（显示指纹）」两态：验签失败的策略在启动闸门（`App.OnFrameworkInitializationCompleted`）就被拦下，到不了抽签界面。
- 徽标放在 `RollCallPage.axaml` 根 `Grid` 第 1 列左上角，**不放进** `RollCallControlPanelTemplate`：那个面板会随 `MoreSettings.RollCallControlPanelPosition` 左右换边，里面每个控件还有各自的显隐开关，放进去的徽标会跟着跳边。
- `ConfigPolicySignatureService` 新增 `PolicyChanged` 事件：安全设置保存时（完整性模式住在那里）或签发文档时触发。抽签页 ViewModel 比「操作员去签发」早得多就建好了，只靠构造时取一次或只在导航时刷新，都会让操作员在设置页启用签名后回到抽签界面看到旧状态。事件的 `add`/`remove` accessor 只在第一个订阅者到来时挂上存储的 `Saved`，最后一个订阅者离开时摘掉。
- 事件可能来自非 UI 线程（设置存储也会被后台路径保存），所以 ViewModel 用 `Dispatcher.UIThread.Post` 回到 UI 线程再改属性。
- 文案键 `C_SignedPolicyBadgeFormat` / `C_SignedPolicyBadgeTipFormat` 放在 `Langs/MainPages/RollCall/`（中英日三语齐备），经该页 `Resources.Designer.cs` 的手写属性暴露。该 designer 是签入的自动生成文件，IDE 重新生成时会从 resx 恢复这两个属性，所以手写是自愈的。
- 目前只挂在点名页；`LotteryPage` 结构相同，需要时把同一块 `StackPanel` 加进它的根 `Grid` 并在 `LotteryPageViewModel` 加同一组属性即可（文案键可直接复用 `Langs/MainPages/RollCall` 的）。

---

## 23. 分离签名覆盖名单（§17 的接线）

**变更**：`ConfigPolicySignatureService` 现在真的把**名单摘要**喂给 `ConfigPolicyDigest.Compute`（`Sign()` 与 `Verify()` 两个调用点都补上第三个参数）。此前该参数一直省略，`RosterPolicyDigest` 因此写好了却零引用。服务新增第四个构造参数 `IProfileCatalogManager`（只经 DI 构造，全仓无手工构造点）。

**覆盖什么**：只有**身份映射**——学生的学号/姓名/分组/性别/停用，奖品的编号与名称。**不含**奖品权重与库存，也不含学生权重（学生记录根本没有权重字段）。由 `SecuritySettingsConfig.ConfigIntegrityIncludeRosters` 控制，**默认关闭**：打开意味着任何一次增删改名都会让签名失效，那必须是显式选择。

**「要不要覆盖名单」这件事本身由 `rosters` 键的存在来记录**，而不是由开关字段记录（`ConfigPolicyDigest.Compute` 只在 `rosterDigest` 非空时写入该键）。由此：
- 打开覆盖会改变摘要 → 旧签名立刻失效。这正是要的：不能靠关掉开关悄悄缩小范围
- 名单为空时 `RosterPolicyDigest.Combine([])` 仍返回真实摘要，所以「开着但还没有名单」与「关着」不会长得一样
- 因此 `ConfigIntegrityIncludeRosters` 被加进 `SelfReferentialFieldNames`：**必须**排除在投影之外，否则开启后这个开关会被自己的策略锁住，操作员再也翻不回来（与完整性模式、篡改动作、恢复源同一类自指字段）

**还没做（下一步，别丢）**：
1. 安全设置页缺一个「覆盖名单」开关与三语文案，现在只能改配置文件才能打开——这就是之前登记的「`IncludeRosters` UI 开关」缺口
2. 名单被覆盖后**列表页的编辑不会被锁**（`ConfigPolicyLock` 只锁设置页控件）。这是有意的：锁只是防误操作的示能，真正的强制是摘要不符时由启动闸门拦下。但设置页文案必须写明「改动名单会让签名失效、需要重新签发」
3. `RosterPolicyDigest` 经 `IProfileCatalogManager.Load*` 读名单，而该方法在**数据需要规范化时会顺手写回**（补 RecordId、应用排序）。规范化幂等且确定，所以第一次之后不再写盘、两侧摘要也一致；但若将来把摘要算进抽取热路径，这一点会被放大

---

## 24. 闪抽：焦点回落选项 + 信标预取（已侦察，待实现）

**需求（用户指定）**：（1）为闪抽及其快捷键调用途径增加选项——在调用前记录当时的前台/活动窗口，闪抽窗口关闭后把焦点还回去；（2）在闪抽动画切换时提前 0.5~1 秒异步拉取信标，以减少网络表观延迟。

**侦察结论：两处与直觉不同，动手前必须先看**

- **「丢弃式」预取是有害的**。`BeaconEntropyProvider.ReserveAsync`（`SecRandom/Services/Verification/BeaconEntropyProvider.cs:44-85`）在**返回种子之前就把 `data/proofs/beacon-state.json` 落盘**，因此一次没被用掉的预取会**消耗一个脉冲序号并留下一个可见的证明缺口**（该行为被 `SecRandom.Core.Tests/BeaconEntropyTests.cs:120-132` 钉住）。所以预取必须把 `BeaconSeedReservation` **交给抽取复用**，绝不能在抽时再调一次。`_gate` 是 `SemaphoreSlim(1,1)`：串行但**不合并**，第二次调用会等完再占一个新序号。
- **它今天已经基本被动画盖住了**。信标在 `VerificationDrawCoordinator.cs:180` 被 await，而承载它的 `verificationDrawTask` 是在 `QuickDrawPageViewModel.cs:246` 创建的——滚动动画在 `:233` 就已发起、`:253` 开始预览，同一轮 UI 内并发。也就是说**内置通知动画开着时，网络延迟已经被动画掩盖**。真正还能抢回来的只有两段空档：`await BeginClassIslandAnimationAsync`（`:238`，ClassIsland IPC，可能很慢）之前，以及 `showBuiltInNotificationAnimation == false`（`:231/:244`，`skipPreview: true`，**根本没有动画**）这条路径——后者收益最大。
- **焦点回落没有单一入口**：开窗入口是 `App.ShowQuickDrawWindowAsync()`（`SecRandom/App.axaml.cs:2646`），但 **IPC 路径绕开了它**（`Services/Ipc/ProtocolCommandRouter.cs:436`）；而六条路径最终都汇到 `QuickDrawPageViewModel.StartAuthorizedTriggeredDrawAsync()`（`ViewModels/MainPages/QuickDrawPageViewModel.cs:191`）。
- **抓焦点必须早于密码弹窗**：`App.axaml.cs:2646` 里的 `AuthorizeTriggeredDrawAsync()` 在 `:2656`；`ProtectQuickDrawStart` 开着时会弹 `SecurityVerificationWindow`，而 `App.GetRootWindow()`（`:167-186`）自身会 `floatingRoot.Activate()` ⇒ 弹窗之后再抓，抓到的是**我们自己的窗口**。

**实现要点（按此写，别改顺序）**

1. 平台能力必须**有状态**，不能塞进 `IWindowFeatureService`（那是无状态开关契约，只有"目标句柄 + 布尔"两个输入，表达不了"记住另一个窗口、稍后把它提到前台"）。照 `IRemovableStorageCatalog` 的形状做：`SecRandom.Platforms.Abstractions/` 新增接口（如 `PlatformWindowHandle? Capture()` / `bool TryRestore(PlatformWindowHandle)`）+ `IPlatformServiceRoot` 上加一个属性 + `SecRandom.Platforms/PlatformServiceCollectionExtensions.cs:15-21` 一行注册 + `SecRandom.Platforms` 里的 Unsupported 桩 + Windows/Linux/macOS 各一实现 + `SecRandom/Mobile/MobilePlatformServiceRoot.cs` 空实现。
   **必须一次全做完**：只加接口或只加 root 属性会让 5 个 root 全部编译不过，不存在"安全的中间态"。
2. Windows 实现用 `GetForegroundWindow` + `SetForegroundWindow`——全仓目前**零命中**，是全新原生代码（`SecRandom.Desktop/UiAccessStartup.cs` 不做前台窗口操作，它只复制提升后的令牌并设 `TokenUIAccess`）。`SetForegroundWindow` 受 Windows 前台锁限制，要在**我们还持有前台时**调用（`Closed` 处理器内、窗口真正销毁前），并跳过 `App.IsStopping`；外部通知路径（`App.axaml.cs:2664-2671`）**根本没有窗口**，不能回落。
3. 落点：捕获 → `ShowQuickDrawWindowAsync` 顶部（`App.axaml.cs:2646`，**必须早于 `:2656`**）+ `ProtocolCommandRouter.HandleQuickDrawAsync`（`:436`）；回落 → 扩展现有唯一的 `Closed` 处理器（`App.axaml.cs:2725-2729`，那里已经清空 `_quickDrawWindow`/`_quickDrawNotificationSettings`）。
4. 选项放**浮动窗口设置页**（`settings.personalized.floatingWindow` / `FloatingWindowSettingsConfig`），照 `S_Interaction_Draggable` 行写（XAML `Views/SettingsPages/Personalized/FloatingWindowSettingsPage.axaml:117-124`；代码后置**没有逐字段接线**，靠 `Settings.PropertyChanged → ConfigHandler.Save()`，`OnLoaded`/`OnUnloaded` 订阅）。该页 i18n 目录是 `SecRandom/Langs/SettingsPages/FloatingWindow/`（三语齐备）。
   ⚠️ 新增的公开属性会**同时被集控读写**（`ControlSettingsCatalog.cs:74` 的反射映射）：若不允许远程改，必须加进 `ControlSettingsCatalog.cs:143` 的 `ReadOnlyPaths`，并在 `ControlSettingsLabels` 的 `floating_window` 映射里补文案。
5. 信标预取：把 `ReserveAsync` 提到 `await BeginClassIslandAnimationAsync` 之前并发等待，把得到的 `BeaconSeedReservation` 传进抽取路径**复用**；不要新增第二次 `ReserveAsync`（见上）。无动画路径（`skipPreview`）优先。

**可选更省方案（未采纳，待用户拍板）**：`WindowFeatures.NoActivate` 已声明且 Windows 已实现（`Platforms.Windows/WindowsWindowFeatureService.cs`），但**全仓无人请求**；闪抽窗口也没设 `ShowActivated`（`App.axaml.cs:2692`，只有浮动窗设了 `ShowActivated="False"`）。若改成"永不抢焦点"，就完全不需要原生焦点代码——代价是闪抽窗口不再接收键盘输入，且要在 `:2602/:2633/:2676` 去掉 `Activate()`。

---

## 25. 闪抽焦点回落 + 信标预取（已实现，落地 §24 的两个需求）

**两个开关都默认关闭、都做进了 UI**：

| 开关 | 位置 | 配置字段 |
|------|------|----------|
| 闪抽结束后归还焦点 | 浮动窗口设置页 · 交互组（`S_Interaction_RestoreFocusAfterQuickDraw`） | `FloatingWindowSettingsConfig.RestoreFocusAfterQuickDraw` |
| 抽签前预取信标脉冲 | 抽取验证页 · 信标组（`S_BeaconPrefetch`） | `VerificationSettingsConfig.BeaconPrefetchEnabled` |

**焦点回落**：新增平台能力 `IForegroundWindowService`（`Platforms.Abstractions`：`IsSupported` / `Capture()` / `TryRestore()`）。它**必须**是有状态契约，不能塞进 `IWindowFeatureService`——后者是"给一个句柄套一个布尔属性"的无状态开关，表达不了"记住另一个窗口、稍后再送给它"。Windows 实现是 `Platforms.Windows/WindowsForegroundWindowService.cs`（`GetForegroundWindow`/`SetForegroundWindow`，**本分支首次出现的原生前台窗口代码**）；Linux/macOS/桩/移动端一律返回 `SecRandom.Platforms.UnsupportedForegroundWindowService.Instance` 并因此报告 `IsSupported=false`（按 `Platforms.Abstractions/AGENTS.md`"不实现就不许声称支持"），这些平台上开关**不生效**，页面描述已写明"目前仅 Windows 支持"。

接线落在 `App.ShowQuickDrawWindowAsync` 顶部与 `GetOrCreateQuickDrawWindow` 里唯一的 `Closed` 处理器：
- 捕获**必须早于** `AuthorizeTriggeredDrawAsync`——`ProtectQuickDrawStart` 会弹验证窗，而 `App.GetRootWindow` 自身会 `Activate()` 浮动窗，晚于此处的捕获会记到我们自己的窗口
- 回落放在 `Closed` 里，因为 `SetForegroundWindow` 一旦失去前台就会被拒；应用正在退出时不碰别人的窗口（**注意写法**：`IsStopping` 是**实例**成员，而承载它的辅助方法与 `Closed` 处理器都是 `static`，所以必须写成 `Current is { } app && !app.IsStopping`——直接写 `IsStopping` 就是 CS0120。这条 CI 报错最初正是它，由 PR #2 修正）
- 拒绝授权与"外部通知"两条路径会清空捕获（后者根本没有窗口可回落）
- **IPC/集控路径不走 `ShowQuickDrawWindowAsync`，因此不参与这个开关**

**信标预取**：`BeaconEntropyProvider` 新增 `Prefetch()`，**只取脉冲、不占序号**，并把结果连同**当时的端点**一起缓存；`ReserveAsync` 改走 `TakePrefetchedPulseAsync`：端点没变就用缓存（取缓存失败则安静地重新获取），端点变了就丢弃——否则换过端点后会把旧端点的脉冲当作新端点的记录进证明。本次实现**修正了 §24 的原始设想**：那里担心"预取会消耗序号并留下证明缺口"，实际上序号只在 `ReserveAsync` 内落盘，所以只预取脉冲完全无副作用；剩下的代价只有"脉冲跨周期后评分减半"，可以接受。调用点同样在 `ShowQuickDrawWindowAsync` 顶部（比绘制任务早，把 ClassIsland IPC 与开窗耗时也一起盖住）。

⚠️ **`ConfigPolicyDigest.ScopeId` 已从 `verirandom-config-policy/v2` 升到 `v3`**：`BeaconPrefetchEnabled` 落在被策略整棵投影的 `General.Verification` 子树里，覆盖字段集变了就必须换 id（该文件的显式要求）。后果是**任何已签发的旧策略文档都会变成 `DocumentMalformed`**，进而触发启动完整性闸门——本次尚未发布，重新签发即可；将来再有"往被覆盖子树里加字段"的改动，务必一起升 id 并提前告知使用方。

**未做**：§24 末尾的"永不抢焦点"替代方案仍未采纳。另外顺带查清——`FloatingWindowSettingsConfig.DoNotStealFocus` 是**死设置**：只有 `ControlSettingsLabels` 的标签映射、`SettingsSearchService` 的搜索 id 与 designer 引用它，**没有任何页面绑定、也没有任何运行时代码读它**。本次没有动它。

**⚠️ 一个必须记住的编译边界（本轮第一次 CI 就栽在这里）**：让抽签页显示指纹时，最初把 `ConfigPolicySignatureService` 当作 `RollCallPageViewModel` 的**构造参数**——它是 `internal`，而那个 ViewModel 是 `public`（手机壳与 XAML 都用它），于是 `CS0051`。这与 §21 里那条正好相反：那次能收窄的是**我方**类型，这次消费者是**上游的 public VM，收窄不了**，所以正确做法是**不把它当参数**，改在构造时用 `IAppHost.TryGetService<ConfigPolicySignatureService>()` 静态解析（`ConfigPolicyLock` 早就这么做）。附带好处是保持了"取不到就不显示徽标"的可选语义。**不要把它改回构造注入**——那会重新引入 CS0051，而且编译器只在拥有该 public 成员的程序集里报出来。

---

## 26. 发布说明不再带下载链接表

**变更**：`scripts/gen-release-note.ps1` 不再生成「下载链接」表（原本每个资产两列：GitHub 与 `https://ghproxy.sectl.cn/...` 镜像 ✗）。SHA256 表保留，说明改为「请在下方 **Assets** 中下载文件；下载后请核对 SHA256」。

**为什么**：本分支还没有搭建任何中转/镜像服务，那一列是**死链**；而 GitHub Release 页面本身就在 Assets 里列出全部产物，自造一张链接表只会让用户以为存在第二条下载路径。

**注意**：
- 发布说明是在**标签处**生成的（`build_publish.yml` 的 `Generate Release Note` 步骤执行 `./scripts/gen-release-note.ps1`），所以这类改动**必须活在被打标签的提交里**，否则发出去的仍是旧说明
- 脚本仍保留「从正文里剥掉一张自带下载表」的逻辑（`**下载链接**` 标记），这样万一 `$BodyPath` 带着旧表也不会重复
- `metadata.yaml` 的 `github_mirror.prefix` 仍是 `https://ghproxy.sectl.cn/`——那是给**更新客户端**解析元数据用的另一条路径，本次没有改动；中转服务不存在时客户端的镜像尝试会失败并回退到 GitHub（客户端既有行为）
- iOS 回填路径（`scripts/ReleaseManifest/Update-ReleaseManifest.ps1`）在本分支**已禁用**，本次没有动它；已确认它**不**造镜像链接（该目录脚本里没有任何 `ghproxy` / `下载链接` / `releases/download` 引用）
- 顺带修掉两个真实缺陷：
  1. 脚本原先用 `Get-Content "./ChangeLog.md"` 切行，而本仓库自己的文件是 **LF-only**、上游的是 CRLF；在 **Windows PowerShell 5.1** 下 `Get-Content` 会把多处 LF 断行并成长行，**两个 bullet 因此被并成一行**（产物里肉眼看不见）。现改为 `Get-Content -Raw` + 显式按 `\r\n|\n|\r` 切分，两种 PowerShell 都正确。已用"受控迷你样本 + 真实 ChangeLog"各跑一遍验证：源 10 个 bullet → 产物 10 个 ✓
  2. 该脚本**必须带 BOM** 保存：它是本仓库唯一含中文字面量的 `.ps1`（其它脚本都是纯 ASCII，所以都没有 BOM），而 5.1 会把无 BOM 脚本按 ANSI 解码，产物里那段中文就成乱码。CI 用 `pwsh`，本来不受影响，加 BOM 是为了保护在 5.1 本地跑这个脚本的人

---

## 27. About 页：移除横幅占位、接入本分支头像

**变更**：
1. `Views/SettingsPages/About/AboutSettingsPage.axaml` 的**横幅占位框已删除**。横幅本身仍是可选的（`LoadBanner()` 只认 `Assets/Banners/verirandom-banner-{cn,en,ja}.png`，缺 zh/ja 时回退到 `-en.png`，并**刻意不回退**到上游的 `secrandom-banner-*.png`）；没有独立横幅时页面不再显示任何占位框，只留标题。资源键 `C_BannerPlaceholder` 因此成为**死键**（`Langs/SettingsPages/About/` 三语 resx + designer），本次保留未删。
2. 新增资源 `SecRandom/Assets/ForkAvatar.png`（164×164 PNG），并把「社区」分组展开器的图标从 Fluent 字形 `PeopleFilled` 换成它——与上游作者分组使用 `AuthorAionflux.png` 的写法完全一致（`<fa:FAImageIconSource Source="/Assets/ForkAvatar.png" />`）。社区分组里那个宽条占位已替换为 `Assets/WinFunan.png`（200×200，按高度 64 等比缩放）；行文案已从「资源待补：本分支社区图片（占位）」改为「社区图片」。键名已在补上资源后从 `C_CommunityImagePlaceholder` 改名为 **`C_CommunityImage`**（三份 resx + designer + XAML 的 `x:Name` 与 `Content` 一并改），全仓代码与资源中已无旧名，旧名只作为历史说明留在该行注释与本节里。

**注意**：`FAImageIconSource` 指向**不存在**的资源会抛异常，所以这张图必须与代码一起提交（`git status` 里它显示为 `?? SecRandom/Assets/ForkAvatar.png`，容易漏）。

---

## 28. 证明重放：从"可信与否"到"本该是谁被抽中"

**背景**：此前的证明只给出可信度评分，无法回答"按这份证明本该抽中谁"。调查结论是**材料早已齐备**：`payload` 是 base64url 的二进制帧，其中含**明文种子**（偏移 17..48）与每个候选的裸 `RecordId` + `weightMicros` + `occurrenceIndex` + `isGuaranteed`，响应帧里是**裸 Guid 的中奖者**（无盐）。缺的只是解码器与重放器。

**本轮改动（全部在 Core，保持无文件 I/O）**：
- `VerificationWireCodec` 新增 `TryDecodeProofPayload` / `TryDecodeDrawRequest`（并补上缺失的 `ReadByte` / `ReadInt64` 私有助手）——此前这段帧布局知识在 `VerificationDrawCoordinator` 与 `WitnessClient` 里**各重复实现一遍**，现在有了唯一解码入口
- 新增 `ProofReplayService.Replay(payload, mode, engineVersion)`：解码 → `ManagedVerificationKernel.Draw` → 与记录的中奖者**逐位置**比对，返回 `Reproduced` / `Mismatch` / `NoLocalSeed`（正式公证的种子来自服务端签名的锁单，文件里那 32 字节是**全零**，不可由文件重放）/ `UnsupportedFormat`（负载解不开或引擎版本不符）
- 新增 `SecRandom.Core.Tests/ProofReplayTests.cs`：往返成功 + 篡改中奖者 → `Mismatch` + 篡改权重 → `Mismatch` + 正式公证 → `NoLocalSeed` + 异版本/垃圾负载 → 拒绝而不抛

**必须遵守的两条**：
- **只重放"抽样"这一步**：候选池、权重、抽样模式、数量一律取**记录里的**，绝不能拿当前名单或当前历史重算（那会重放另一次抽取，而不是证明所描述的那次）
- 重放证明的是"**记录输入 → 记录结果**"的自洽，**不是**内核无偏（那是 `scripts/FairnessAudit/` 统计线的职责），也**不能**证明种子诚实随机、名单就是真实班级名单、抽前没有筛过名单

**应用层与界面（本轮已完成）**：
- `SecRandom/Services/Verification/ProofReplayFileService.cs`：遍历 `DrawProofExportService.EnumerateProofPaths()`（`data/proofs/<yyyy-MM>/<yyyy-MM-dd>/*.srproof.json` 是**递归**布局，没有扁平目录），按 `proof.Chain.Index` 排序；base64url 自行解析（不依赖 `WitnessClient` 里那些 `internal` 助手）；名单文件按根属性 `prizes` / `students` 判别后用 `ConfigServiceBase.JsonOptions` 反序列化（注意 `Name` 是 `[JsonIgnore]`，名单名来自文件名），并以 `RosterDigest.Compute` 与审计负载里的 `rosterDigest` 比对——**该字段不是证明的独立字段**，藏在 `auditPayload` 的 JSON 里，且"抽取时的活动档案 ≠ 被抽名单"时会被**静默省略**
- `SecRandom/Services/Verification/ProofReplayViewService.cs` + `Views/Verification/ProofReplayView.axaml(.cs)`：独立**模态**视图（view id `main.proofReplay`、桌面用 `ViewPresentation.Modal` + `NewHost` 开新窗口）。**不可编辑画布**：`Canvas` + 双向 `ScrollViewer`，节点按链序等间距排布，节点上显示**抽取时间**与链序号，节点间画连线；只能点击选中，无拖动、无缩放。右侧面板显示选中节点详情、选择名单、执行重放，并列出**记录**与**重放**的中奖者（给了名单就显示**姓名**，否则显示 recordId 前 8 位）
- 入口：抽取验证页 `S_ProofFolder` 行的第三个按钮 `C_OpenProofReplay` → `OpenProofReplay_OnClick`
- **手机分支也必须注册该视图**：`App.axaml.cs` 有两处视图链（手机分支 `AddView<MobileRootView>…` 不带 presentation 参数，桌面分支带 `ViewPresentation.Modal`），而该设置页在**所有宿主**上都注册，只注册桌面会让手机端点按钮抛 `KeyNotFoundException`。两个服务注册在**共享**分支
- 文案 22 个键复用**抽取验证页自己的**本地化目录（`Langs/SettingsPages/General/Verification/`，该目录 designer 是 `Get(nameof(...))` 简式），三语齐备、无 `。` 结尾；没有新建目录，因为该视图就是该页的一个入口
- **已知限制**：`ProofChainHead` 不暴露 `RemovedIndices`（保留期删除记录只存在于 `internal` 的文档类型里），所以窗口只能显示 `RetainedFromIndex` 以下的"保留期截断"，无法区分"保留期删除"与"真正的缺口"；名单摘要缺失时核对结果会显示"不一致"而不是"未知"（文案已把这两种情况合并说明）

---

## 29. 信标「每分钟后台自动预取」开关（默认关闭）

**变更**：新增 `VerificationSettingsConfig.BeaconAutoPrefetchEnabled`（默认 `false`，抽取验证页 · 信标组第二行）。它与 §25 的 `BeaconPrefetchEnabled` **同时开启**才生效：后台每分钟取一次最新脉冲放入 `BeaconEntropyProvider` 的单槽缓存，若在这一分钟内开始抽取，`ReserveAsync` 直接消费缓存、没有任何网络等待。

**三处必须一起保留**：

1. **缓存陈旧上限**（`BeaconEntropyProvider.PrefetchedPulseMaxAge = 60 秒`）：缓存条目带取回时刻，超过**一个脉冲周期**即丢弃。没有它，每分钟预取会让几小时前的脉冲一直躺在缓存里等下一次抽取——跨周期只影响评分，但预取的意义是省成本，不该反手把半个评分赔进去
2. **`ConfigPolicyDigest.ScopeId` 再次升级**（`v3` → `v4`）：新字段落在被策略**整棵投影**的 `General.Verification` 子树里，覆盖字段集变了就必须换 id（与 §25 同一条规则，链是 `v1 → v2 → v3 → v4`）。已签发的旧策略文档会变成 `DocumentMalformed`，重新签发即可；本分支目前没有真实用户持有已签发策略，影响为零
3. **预取只取脉冲、不占序号**（§25 已确立）：`BeaconPrefetchScheduler` 只调用 `Prefetch()`，序号仍在 `ReserveAsync` 内落盘，所以定时预取即使一整天没人抽签也不消耗序号、不在证明链上留缺口——这是「每分钟跑一次」能够安全存在的前提

**实现落点**：`SecRandom/Services/Verification/BeaconPrefetchScheduler.cs`（`BackgroundService`，`TickInterval = 1 分钟`，整轮 try/catch，失败只记 debug 并在下一分钟重试），在 `App.axaml.cs` 抽取证明那组 hosted service 之后注册（**共享分支**，桌面/平板/手机都有信标设置）。

**与 §25 的关系**：§25 记录的「按动画剩余 5 秒取脉冲」那版调参**从未保留**（用户决定改为周期预取，已回退）；`Prefetch()` 与 `TakePrefetchedPulseAsync` 的端点匹配逻辑仍按 §25。

---

## 30. 证据打分并入证明重放窗口（共用自由画布）+「跳转到最新证明」

**变更**：`S_ProofTrust` 那张卡从抽取验证页**整体移入** `main.proofReplay` 窗口的右侧面板，与重放共用同一块自由画布：**选中哪个节点就评哪一份证明**（不再是「永远只看最新一份」）。画布右上角新增「跳转到最新证明」按钮（`C_ProofReplay_JumpLatest`），一键选中链尾并滚到最右端。

**为什么合并**：重放回答「按这份记录本该是谁中」，打分回答「这份记录本身有什么支撑」——两个问题指的是**同一份证明**。分成两处时，用户得先在设置页对最新一份打分，再开另一个窗口按链序号找同一份，纯属自找错位。

**两条实现要点**：

1. **链完整性改成按节点判定**（`ProofReplayFileService.IsChainIntactFor`）：旧的设置页用法是整条链 `Verify().IsHealthy` 一刀切，因为那里只评最新一份；窗口既然允许选任意节点，就必须让**自己文件被改过的那一个**归零，而不是让它搭别人的便车。因此判定是「链级断裂（gap / broken link / beyond head）照旧一票否决」+「`Modified` / `Unreadable` / `UnsupportedVersion` 这三类问题只要点名了本节点就否决」。除此之外仍完全委托 `ProofIntegrityVerifier`，不自建校验
2. **评估所需的四项证据全部来自磁盘上的同一对文件**：Up 文件给链位、回执与时间戳，参考兄弟件（`OwnProofPaths.FromUpPath`）给信标脉冲；时间戳必须验签通过才算数，回执必须绑定到这份证明。`ProofReplayFileService` 因此新增 `OwnProofExportService` / `ProofIntegrityVerifier` 两个构造依赖（都是宿主单例）

**顺带修正的历史描述**：§16 的「修改文件」一段写的 `VerificationSettingsPage.axaml(.cs)` 新增 `S_ProofTrust` 区块，**位置已被本条取代**——打分触发与 `FormatTrustReport` 等格式化助手现在都在 `Views/Verification/ProofReplayView.axaml(.cs)`；那批键继续复用抽取验证页自己的本地化目录，没有新建目录。设置页那句「按最新一份打分」的描述随卡片一起消失，别再按旧位置找它。

**布局注意**：画布外的 `ScrollViewer` 更名 `ChainScroll`。「跳转到最新证明」的滚动**必须 post 到布局之后**——点击时 `Extent` 还是布局前的旧值，按它算出的偏移会被夹回 0。

---

## 31. 参考名单：`使用当前名单` + 名单页 `导出证明参考文件`（为什么 CSV/XLSX 不行）

**两个入口**：

1. **重放窗口 · 使用当前名单**（`C_ProofReplay_UseCurrentRoster`）：按**选中节点**的抽取类型取当前活动的点名名单 / 奖池（`IProfileService.CurrentStudentList` / `CurrentPrizeList`），先用 `FAContentDialog` 弹一个**只读成员预览**（编号、姓名、分组/性别或数量，停用成员标出），确认后才作为参考名单。这是窗口里**唯一**允许读实时状态的地方，而且正是名单摘要存在的意义——"我现在这份名单是不是抽取时那份"；**重放本身仍然只用证明里的候选池、权重与种子**
2. **名单/奖池页 · 导出证明参考文件**（`C_ExportProofReference`，两个页面各一颗按钮）：`ProofRosterReferenceExporter` 生成纯 JSON，形状就是磁盘上的名单文件（`students` / `prizes` 根属性），只拷贝 `RecordId`、`Id`、`Name`、`Group`、`Gender`、`Exists`（奖品另加 `Count`、`Weight`）与 `Tags`，**不带附件设置**；重放窗口 `LoadRoster` 原样就能读

**为什么现成的导出（CSV/XLSX）不能当参考名单**（这是本轮唯一的"否"，写下来免得反复问）：

- 历史导出**故意不写 `RecordId`**（AGENTS 明令：`RecordId`/`DrawRoundId` 绝不进导出文件），而重放需要的两件事都以它为准：中奖者在证明里记的是裸 `RecordId`，名单摘要算的也是 `RecordId → 显示信息` 的映射。没有 `RecordId`，**姓名对不回中奖者，摘要也算不出来**
- 把导出的 CSV/XLSX 再导入成一份新名单**也救不了**：导入会给每条记录**新分配** `RecordId`，于是摘要必然不同——界面会说"不一致"，而实际是同一批人，这个坑比不支持更糟
- 因此参考名单只有两种合法来源：**当前名单**（实时读取）或**本功能导出的 JSON**（携带 `RecordId`）。若将来想让导出文件也能当参考，唯一路径是给历史导出**可选**加一列 `RecordId`（默认关闭），那要先改 AGENTS 里"绝不进导出文件"的约定，属于单独决策

**必须一起保留的约束**：

- `ProofRosterReferenceExporter` 拷贝的字段**必须与 `RosterDigest.Compute` 哈希的字段完全一致**（学生：`Id`/`Name`/`Group`/`Gender`/`Exists`；奖品：`Id`/`Name`/`Count`/`Weight`/`Exists`，加上各自的 `RecordId`）。少拷一个字段，摘要就变了，之后每一次核对都会报"不一致"，而界面上没有任何线索能解释
- 参考文件与**名单传输导出**（Excel/CSV/二维码）是两件事，两个按钮分开正是为了让"CSV 也能当参考"这种预期不可能出现

---

## 32. 下游版本号：`vX.Y.Z Mu N`（不递增上游 SemVer）

**目标**：本分支的版本号跟随上游，但需要在上游版本不变的前提下发布下游自有修订。约定为在上游版本后追加 **Mu 序号**：`v3.1.5 Mu 1`、`v3.1.5 Mu 2` …。排序键是**（数值核心, Mu 序号）**的元组——`Mu 2 > Mu 1`，且任何 Mu 版本都 `> 上游 v3.1.5`、`< 上游 v3.1.6`。

**为什么不用 SemVer 的 `-mu.1`**：SemVer 把 `-x` 视为"该版本的预发布"，不仅在语义上把下游的成品说成"上游版本的预览"，还会让 `3.1.5-mu.1` **排在 `3.1.5` 之下**——于是「上游发布 3.1.5 之后我们补一版」永远无法作为更新推给已经装了 3.1.5 的用户。用一个排序上位于 `3.1.5` 之后的后缀是这条需求的硬要求，这也正是它必须落在自有解析器而不是 `Version.TryParse` 上的原因。

**唯一规则**：`SecRandom.Core/Helpers/ReleaseVersion.cs`（`ReleaseVersion.TryParse` → `ReleaseVersionKey(Version Core, int MuOrdinal)`）。分隔符可以是空格、`-`、`_`、`+`、`.`，`Mu` 不区分大小写，所以 `v3.1.5 Mu 1` / `v3.1.5-Mu.1` / `v3.1.5_Mu_1` / `v3.1.5+Mu1` 等价；尾部的 SemVer 预发布后缀照旧被丢弃（`v3.0.0-beta.1` → 核心 3.0.0、序号 0）。

**所有闸门都改成走这条规则**（缺一处就会让 Mu 版本在那一处退化成"和上游版本相同"或"解析失败"）：

| 位置 | 原来的行为 | 现在 |
|---|---|---|
| `SecRandom.Core/Services/Archive/DataArchiveService.IsSupportedV3ProducerVersion` | 只剥 `-`/`+` 后缀，再 `Version.TryParse` | 走 `ReleaseVersion`，Mu 版本能恢复它自己导出的归档（否则**拒绝自己的备份**） |
| `SecRandom/Services/Updates/UpdateVersionComparer` | 同样只剥后缀再比数值核心 | 走 `ReleaseVersion` 比较（核心, 序号），于是 Mu 2 能推给 Mu 1 |
| `SecRandom.Launcher/Program.cs` `ParseVersion` | `Version.TryParse`，Mu 名字直接失败 → 两个 payload 并列 | 返回 `(Version, Mu)` 元组并按它排序（该程序集是独立可执行，无法引用 Core，故为**镜像实现**，改规则时两边一起改） |
| `scripts/validate-release-metadata.ps1` | 只认 `-alpha`/`-beta`，其余带 `-` 一律 `Unsupported prerelease tag` | Mu 标签归 `release` 频道；`tag:` 的取值正则允许空格（`[^\r\n]+?`），否则带空格的标签永远匹配不上 |
| `.github/workflows/build_publish.yml` | `-p:Version=` 与 `PKG_VERSION` 直接用标签原文 | 先过 `scripts/Format-ReleaseVersion.ps1` 变成 `3.1.5-mu.1`（MSBuild/NuGet 不接受空格）；发布产物的 prerelease 标志改为只认 `-alpha`/`-beta`/`-rc`，Mu 版本按正式版发布 |

**标签可以带空格**：`git tag "v3.1.5 Mu 1"` 之后 GitHub 的 `ref_name`、`git describe`、GitInfo 的 informational version 都会原样带回空格，因此界面显示的就是 `v3.1.5 Mu 1`。文件名的空格由 GitHub 自行 URL 编码，产物名沿用标签原文；只有 `-p:Version` 与 NuGet 包版本必须是无空格的 SemVer 形式。

**发布流程不变**：把 `metadata.yaml` 的 `channels.release.tag` 改成与 Mu 标签**完全相等**（含空格）再打标签，`validate-release-metadata.ps1` 会核对这一点；更新客户端据此解析频道 → 标签。

---

## 33. 点名结果浮窗（把抽取结果缩成置顶小窗）

**功能**：点名页控制面板新增「结果浮窗」按钮，点击后打开一块**无边框、置顶、可拖动**的小窗显示当前抽取结果，同时把主窗口收起——课堂演示课件时也能一直看见谁依次回答问题。

**实现（沿用计时器小窗那条已验证的路子，不是新机制）**：

- `Views/MainPages/ResultMiniWindow.axaml(.cs)`：直接派生 `Window`（`WindowDecorations=None`、`Topmost=True`、`ShowInTaskbar=False`、`CanResize=True`、透明背景 + 半透明圆角卡片）。内容就是主界面那个 `RollCallResultPresenter`，套在 `Viewbox` 里（设计尺寸 620×420，向下缩放），因此**结果、显示设置、动画全部仍由 Host 单例 `RollCallPageViewModel` 提供**，浮窗没有复制任何结果状态
- `Services/ViewEngine/ResultMiniWindowService.cs`：开窗/复用/还原。**顺序是承重的**：先 `Show()` 浮窗，再 `App.SetMainWindowVisibility("hide")` 收起主窗口——Avalonia 在最后一个窗口关闭时会退出应用，反过来做会在浮窗出现之前把整个应用关掉（与计时器小窗同一条规则）
- 关闭浮窗（右上角按钮或窗口关闭）一律**还原主窗口**：让用户停在"只剩一张浮窗卡片、看不到任何窗口"的状态比多一次点击更糟
- **桌面专有**：`RollCallPageViewModel.SupportsResultWindow => App.IsDesktop` 决定按钮是否出现，`ResultMiniWindowService` 只注册在 `BuildHost()` 的桌面分支。手机/平板把结果留在主界面里显示（`MobileDrawPage` 用的就是同一个 presenter）

**拖动**：整块可拖动（`PointerPressed` → `BeginMoveDrag`），但按在按钮上时交给按钮（`IsButtonDescendant`），与计时器小窗一致。

**触控与大屏（本版新增）**：

- **拖动接受触控**：`IsPrimaryContact` 在 `IsLeftButtonPressed` 之外同时接受 `PointerType.Touch`。原来的判断只看鼠标左键，所以在手指上**根本不响应**——而这块卡片最主要的用途恰恰是教室大屏
- **可缩放**：8 块透明热区（边 22、角 48）覆盖在内容之上，各自 `BeginResizeDrag(WindowEdge, e)`，走平台自己的缩放循环而不是自己算指针位移——后者在任何显示缩放下都会抖，而且手指离开热区后继续缩放只有原生循环能做到。右下角额外画一条斜向握把痕迹（`Path` + `IsHitTestVisible=False`），否则没人知道这块能抓。`MinWidth/MinHeight` 240×220 防止被压成一团
- **尺寸记忆**：`MoreSettings.ResultMiniWindowWidth/Height`（默认 360×320），关闭浮窗时写入，开窗时读回并用 `Sanitize` 夹住（非有限值、小于最小尺寸或大于 4000 的读数一律回退默认）。**位置不记忆**：卡片按当前显示器居中，记下的坐标在换成另一块屏时会把它放到屏幕外
- **还原按钮 32 → 40**：大屏上 32px 的圆钮经常按偏

**边界**：它只是"换个容器显示同一份结果"，不是新的绘制通道、不新增**设置页**入口、不写证明与信标相关配置；尺寸记忆只落在 `settings.json` 的 `moreSettings` 里，而配置策略只覆盖 `moreSettings.lotteryEnabled`，因此**不触发** `ConfigPolicyDigest.ScopeId` 升级。信标/证明/提交路径完全不受影响。

---

## 34. 外部熵源可选：NIST / drand / random.org（补完 §3 的"尚未接线"）

**变更**：`VerificationSettingsConfig.BeaconSourceId`（默认 `nist-beacon-v2`，未知 id 回退到 NIST 而不是"没有熵"），抽取验证页 · 信标组新增「随机源」下拉，三项：

| 来源 | 取回时验签 | 事后可复核 | 说明 |
|---|---|---|---|
| `nist-beacon-v2` | **是** | 是 | 原有路径，证书 subject 固定 + 签名校验，未改动 |
| `drand-quicknet` | 否 | **是** | 记录 `round`/`randomness`/`signature`，用目录里内置的链公钥可事后复核 BLS 签名 |
| `random-org` | 否 | **否** | 只记录取回到的 32 字节；它是大气的真随机，但没有任何签名或时间轴，主持人可以多次取回挑一份 |

**为什么 drand 不在取回时验签**：验 drand 的 BLS12-381 threshold 签名需要配对运算实现，本仓库没有；引入第三方库是一次供应链决策，用户选择"记录但不验签"。因此 drand 走的是与 Own 链相同的**先声明、后核验**路线：本机不拦，但任何人事后拿链公钥都能判定这一轮是不是真的。

**random.org 的特殊处理**：它不是一个 beacon——没有脉冲索引、没有周期、没有链位。所以：

- `BeaconPulse.PulseIndex = 0`、`ChainIndex = 0`、`TimeStamp = 取回时刻`，种子唯一性来自**取回的字节本身**
- `ReserveAsync` 对它**跳过 `BeaconSequencePolicy` 与 `beacon-state.json` 落盘**：没有公开时间轴就没有"缺口"语义，硬套序号反而会因为"同索引不同输出"被判成冲突而**让每次抽取都失败**
- 输出固定 32 字节（64 个十六进制字符），走同一条种子派生——但这**要求 `DecodeOutputValue` 同时接受 256 位与 512 位**，见 §36

**两处实现细节**：

- 预取缓存改为按 **source id + endpoint** 键控（原来只有 endpoint），换源后旧缓存不会被误用；外部请求统一 8 秒超时（在抽取关键路径上）
- drand 的**链哈希借 `CertificateId` 字段记录**：证明里没有别的空位承载"是哪条链"，而它与证书 ID 回答的是同一个"谁发布的"问题

**`ConfigPolicyDigest.ScopeId` 第三次升级**（`v4` → `v5`）：新字段又落在被整棵投影的 `General.Verification` 里（§25/§29 同一条规则）。

**已知边界**：这项功能只改变"种子从哪来"，不改变任何可复核性保证的边界——random.org 与本地 CSPRNG 一样无法阻止预先挑种子，差别只在于随机性来自外部物理噪声。

---

## 35. 周期预取改成对齐发布网格 + 成功路径不再写日志

**两个问题**：`BeaconPrefetchScheduler` 原本是"启动后自由跑 60 秒"，一是相位随运行时间漂移（一天之内会跨过所有可能的相位），二是取脉冲成功的调试日志落在 `Debug` 级，Debug 构建下每次预取都留一行。

**对齐**：每个 tick 睡到「当前 UTC 分钟边界 + 偏移」，而不是固定 60 秒。这样做是因为三个来源的发布都落在分钟边界上——NIST 每分钟一次，drand 3 秒一次（3 整除 60，所以每一轮也都在分钟边界上起算）；在边界之后取，拿到的才是**当前周期**的脉冲而不是上一个周期的。

**偏移自适应**：初值 `BaseOffset = 5 秒`。每次预取后按实测 RTT 把偏移收敛到 `RTT + 2 秒`，并夹在 `MinOffset = 2 秒`..`MaxOffset = 20 秒`；每 tick 最多移动 1 秒，避免一次抖动就重排时间表。网络快 → 偏移尽早取（脉冲更新鲜）；网络慢 → 提前起步，保证请求仍落在同一分钟内。

**日志静默**：

| 位置 | 改动 |
|---|---|
| `NistBeaconClient` / `ExternalEntropySourceClient` 取脉冲成功 | `Debug` → **`Trace`**（Release 与 Debug 都不写，正常预取彻底无日志） |
| drand 单个前端失败 | `Warning` → **`Debug`**（否则网络受限时每分钟两条 Warning 写盘） |
| 预取整体失败 | 调度器记一条 `Debug`；抽取侧仍会以 `BeaconEntropyException` 报出具体来源与主机 |

**结论**：成功路径不产生任何日志行；失败路径每分钟最多一条 Debug（Release 不写），真正需要用户看到的那次失败由抽取本身报。

---

## 36. 修复：drand / random.org 一律被判"输出值形状不合法"（以及换源后的序号锚点）

**现场证据**（设置页「拉取一次」，来源=drand，全局代理已开）：

```
熵源 drand 的所有前端都取回失败：Beacon output value must be 128 hexadecimal characters.
```

请求本身是通的——**是我这边的形状校验写死了 NIST 的宽度**：`BeaconSeedDerivation.DecodeOutputValue` 只接受 128 个十六进制字符（NIST 的 512 位），而 drand 的 `randomness` 是 256 位（64 字符），random.org 取的 32 字节同样只有 64 字符。两个来源在**进入派生之前**就被拒，所以既完不成抽取、也写不出证明——与网络无关。

**修法一：`DecodeOutputValue` 接受 256 位与 512 位两种宽度**。派生的 `material` 本来就按 `output.Length` 分配缓冲，因此接受更窄的输出不改变任何 NIST 派生的种子（逐字节一致），也不需要新的 DerivationId；其他长度仍被拒绝，绝不补齐或截断。`BeaconPulse` 的文档同步改成"宽度随来源"。

**修法二：序号锚点按来源区分**（这是换源后立刻会踩的第二个坑）。`beacon-state.json` 原来只记 `PulseIndex/ChainIndex/OutputValue/Sequence`，但 NIST 数的是**脉冲**（现场值 1 996 869），drand 数的是**轮次**（四千万量级）：切到 drand 会通过，切回 NIST 则必然触发 `PulseIndexRegression` 而**拒绝每一次抽取**。现在状态文档带 `SourceId`：

- 记录的来源与当前来源不一致 → 视为**没有锚点**，从新来源自己的时间线重新开始（这不是"回退"，是两个不相干的时间线）
- 旧文档没有 `SourceId`（当时只可能是 NIST）→ 按 NIST 解释，升级后 NIST 的序号连续性不受影响
- 进程内缓存也按来源失效，切源后立刻重读

**仍然存在的边界**：`BeaconPulsePeriodPolicy.Match` 的"周期预设"以时间戳周期为基准，drand 的 3 秒周期与它并不比 60 秒更自然；这条只影响**事后核对/可信度评分**的档位，不影响抽取与证明写入。

---

## 37. 证明重放可读取任意文件夹（跨设备核验）

**功能**：重放窗口新增「证明文件夹」按钮（复用同一本地化目录里已有的 `S_ProofFolder` 文案，**没有新增键**），用系统目录选择器挑任意文件夹即可加载其中的证明；摘要区第一行显示当前目录。默认仍是本机 `data/proofs`（窗口每次打开都从本机开始）。

**读取**：`ProofReplayFileService.LoadNodes(string? rootDirectory = null)`。传入目录时按 `*.srproof.json` **递归**枚举（本机布局是 `<yyyy-MM>/<yyyy-MM-dd>/`，导出的目录保持同一形状），单文件读取仍走同一个 `TryRead`，因此损坏文件照旧被跳过并记日志。

**链因子的判定必须分两种**——这是这次改动里唯一有语义风险的地方：

| 证明来源 | "链完整"的含义 |
|---|---|
| 本机 `data/proofs` | 完整本机判定：链头、保留底线、删除台账、缺口，全部沿用 `ProofIntegrityVerifier` |
| 用户选的文件夹 | **只按该证明自身的 `selfHash` 重算**（`ProofChainStore.ComputeSelfHash(index, prevHash, attestedHash)`） |

理由：别人的文件夹无法证明"它包含整条链"，拿本机链的健康状况去回答外来文件的问题，是答非所问——而且方向反了更糟（本机链健康就默认外来文件也健康）。反之，外来文件能不能自证"没被改过"是**可以**判定的，所以这一档仍然保留。

**已知边界**：外来目录**不核对链联通性与缺口**。跨设备核验时应把对方**完整**的证明目录一并拷来；只挑几个文件拷过来时，缺失的部分这里不会报，但每个文件自身的自哈希仍会核对。

---

## 38. random.org 不参与预取 + 证明重放的批量评估

**一、random.org 永不被预取**

`BeaconEntropyProvider.Prefetch()` 与 `PrefetchAsync()` 在来源为 `RandomOrgEntropy` 时**直接返回**。理由只有一条：random.org 按**来源 IP** 计配额（免费额度每天有限），预取等于替一次可能根本不发生的抽取花掉额度；而「每分钟自动预取」会让它一天烧掉上千次。

影响面：快捷抽取的预取、`BeaconPrefetchScheduler` 的周期预取，都对 random.org 变成空操作，**只有真正抽取时才会请求一次**。NIST / drand 的预取不受影响（它们是公开脉冲，可以随便取）。

**二、批量评估**

重放窗口新增「批量评估」：一次对当前画布上**所有**证明执行同样的评分，并把分数直接渲染到画布节点上（`#索引 · 87%`），便于先扫一眼再点开。

- **社会见证因子在批量里被强制排除**：它比较的是"一份证明 + 一个你回忆的抽取时间"，批量没有单一取值可套。执行期间该复选框被取消并禁用，结束后恢复可点，配置本身不改
- 批量与单条的评分规则完全一致（同一个 `ProofReplayFileService.AssessTrust`），所以节点上的分数和点开后的分数是同一个数
- 外来目录（§37）同样可批量：每个节点仍走"只按自身 selfHash"的链判定

---

## 39. random.org 签名接入的第一步：由用户自己申请，程序只引导与存放

**为什么不自动化申请**：random.org 的密钥是**网页表单 + 邮箱确认**，没有公开的发放接口；代用户提交注册信息、代收邮件，等于替用户对第三方作出同意，而且它的封禁是按 **IP/UA** 做的——一旦被判定为自动化注册，坏的不仅是签名接口，**连现在免密钥的 `integers` 抽取也会一起失效**。因此自动化止步于"打开正确的页面"。

**为什么不用嵌入式 WebView**：Avalonia 11 没有内置 WebView，三端各要一份运行时（WebView2 / WKWebView / WebKitGTK），而且登录会话与密码会落在本进程内，与"凭据只进私有目录"的边界冲突。用**系统默认浏览器**（`IExternalLauncher`）既零新增依赖，也与 SECTL 登录的既有做法一致。

**落地形态**：抽取验证页 · 信标组的「随机源」旁边，选中 random.org 时出现「接入 random.org 签名」按钮，`FAContentDialog` 里三步：

1. 「打开账户页面」→ `https://accounts.random.org/`（注册 / 登录）
2. 「打开 API 面板」→ `https://api.random.org/dashboard`（申请密钥）
3. 粘贴密钥 → 「验证密钥」真的调用一次 JSON-RPC `getUsage`，把剩余额度显示出来（失败时把 random.org 的原话带出来），确认后「保存密钥」

**凭据边界**：`RandomOrgCredentialStore` 只写 `data/config/randomorg/key.json`（原子替换），**不进 `settings.json`、不进备份、不进日志、不进 Android provider**；日志只记长度不记内容。密钥被拒（random.org 的 `-10xx` 错误段）单独抛 `RandomOrgKeyRejectedException`，不会被当成"网络问题"糊弄过去。

**边界（本步还没做的）**：抽取仍走**未签名**的 `integers` 接口——保存的密钥目前只用于校验与额度查询。把 `signedInteger` 接进抽取路径、验签并把「熵源签名有效，但结果仍可能被筛选」这句话接进打分结果，是下一步。

---

## 40. 有密钥时改用 random.org 的签名接口，并把签名记进证明

**取数**：`RandomOrgApiClient.GenerateSignedBytesAsync` 调 JSON-RPC `generateSignedIntegers`（`n=32`、`min=0`、`max=255`、`replacement=true`），取回数据 + `signature` + `serialNumber`。`ExternalEntropySourceClient` 在**存在密钥时走这条**，没有密钥时退回原来的未签名 `integers` 接口——签名是能力增强，不是前置条件。

**记录**：沿用证明里已有的两个字段，不新增模型字段——

| 字段 | 内容 |
|---|---|
| `SignatureValue` | random.org 返回的签名原文（base64） |
| `CertificateId` | `serialNumber`（"这是哪一份签名声明"，与 drand 借该字段放链哈希同理） |

**为什么这里不做密码学验签**：验 random.org 的签名需要①它当前使用的**签名公钥**（会轮换）②它签名时的**确切字节布局**（字段顺序、哈希算法、序列化方式）。这两样既不在本仓库里，我也无法离线核对——写一个"看起来对"的验签实现，结果只会是每次都显示无效，比不做更糟。因此本步的**如实结论**是：签名已记录，可事后核验；本构建不声称"签名有效"。

**所以第三条（把"熵源签名有效，但可能被筛选"接进打分）没有做**，因为那句话在有真验签之前就是假的。可交付的降级版是：在打分结果里显示"该来源的签名已记录但本构建未校验，随机数仍可能被挑选"——一句话的事，等你决定要不要这个措辞。

---

## 41. 离线验签落地 + 打分里如实说明签名状态

**验签**：按官方 Signed API 文档——签名是 random.org 私钥对 **`random` 对象 JSON 原文的 SHA-512 摘要** 的签名，用 `https://api.random.org/server.crt` 的公钥校验。`RandomOrgSignatureVerifier` 因此对 `JsonElement.GetRawText()`（**原文，不重新序列化**）做 `RSA / SHA-512 / PKCS#1 v1.5` 校验；证书在桌面端是松散文件，故**在代码里钉死其 SHA-256**（`ovIWQvHn8gUpUyZfRtw9qoJhPRWsMo7OpGhu7bcgM1E=`），文件被替换只会得到"本构建无法担保"，而不是"伪造"。验签结论在取数时写入日志，**不因验签失败而让抽取失败**——在 canonical JSON 未经实测确认前，fail-closed 的风险高于收益。

**一个必须记录的事实**：random.org 公布的验签证书 **已于 2025-11-15 过期**（`CN=RANDOM.ORG`，DigiCert EV RSA CA G2，RSA 4096）。RSA 数学不受影响，但"证书链当前有效"这句话不能说；`RandomOrgSignatureCheck.CertificateExpired` 单独上报。

**打分窗口的说明**：`ProofReplayFileService.SignatureNote` 只回答"这份证明记录了什么"，并在打分结果下追加一句：

| 证明里的情况 | 显示 |
|---|---|
| 有签名（random.org 签名接口） | 该熵源记录了签名，但证明里没有签名所覆盖的原始响应，因此本窗口不重复验签；随机数仍可能被挑选 |
| 无签名（未签名接口 / 未配密钥） | 该熵源没有签名：无法证明随机数的来源，也可能被挑选 |
| 其它来源（NIST / drand / 无信标） | 不追加任何一句 |

**为什么不显示"签名有效"**：证明只保存了签名与序列号，**没有保存被签名的原始响应**，所以窗口无法重新推导该结论；把取数瞬间的结论写进界面会让"跨设备核验"变成一句无法复核的断言。要真正做到"签名有效"随证明走，需要给证明加字段保存原始响应——**那是下一步**，且会改变证明格式（§34 的字段扩容流程）。

---

## 42. 自有证明格式扩展：保存被签名的原始响应，重放时本地重验

**格式变更**（只动 Own 节点，Up 证明一个字节未变）：`DrawProofBeacon` 新增两个**可选**字段

```jsonc
"signaturePayload": "<被签名的 random 对象 JSON 原文>",
"signatureVerified": true        // 取数瞬间的结论，仅作声明
```

**向下兼容的三条**（这是别人担心的地方，逐条核过）：

1. 旧版本读到未知字段：`System.Text.Json` 默认忽略 → 不报错
2. **不进 `ComputeAttestedProofHash`**：该哈希覆盖固定字段表，服务端按同一张表重算；新字段进去会让每一次上报复核 `proof_hash_mismatch`
3. **`OwnProof.Format` 仍是 `verirandom-own-proof/v1`**，没有升版 → 旧版本不会把它判成"不认识的格式"（升版才是会带来意外后果的那条路）

**重放时的重验**：`ProofReplayFileService.VerifyRecordedSignature` 在 `signaturePayload` 存在时，用内置证书对原文重算 RSA/SHA-512/PKCS#1 校验；`signatureVerified` **不被信任**，只是声明。窗口据此显示：

| 情况 | 文案键 |
|---|---|
| 重验通过、证书未过期 | `M_ProofReplay_SignatureVerified` — 熵源签名已复核通过，但结果仍可能被筛选 |
| 重验通过、证书已过期（**当前就是这个**，2025-11-15） | `M_ProofReplay_SignatureVerifiedExpired` — 复核通过，但证书已于 {0} 过期，链的当前有效性无法确认 |
| 重验失败 | `M_ProofReplay_SignatureInvalid` — 数据与签名不符，请勿仅凭这份证明认定结果 |
| 有签名但无原文（旧证明） | `M_ProofReplay_SignatureRecorded` — 签名已记录，本窗口不重复验签 |
| 无签名（未签名接口） | `M_ProofReplay_UnsignedSource` — 无法证明来源，也可能被挑选 |

**代价**：证明文件每份增大约 1–2 KB，且 `hashedApiKey`（API Key 的 SHA-512，不可逆）会随原文进入文件——用户已确认接受这个取舍，换的是"签名有效性可以被第三方复核"。

---

## 43. 主界面「随机数」页（`main.rawRandom`）：裸随机数 + 拒绝采样

| 文件 | 改动性质 |
|---|---|
| `SecRandom.Core/Services/Draw/RejectionSampler.cs` | 新增（Core 纯函数） |
| `SecRandom.Core/Models/Verification/RawNumberProof.cs` | 新增：裸随机数记录模型 + 链哈希规则 |
| `SecRandom.Core/Services/Verification/RawNumberSeedDerivation.cs` | 新增：多源种子派生（Core 纯函数） |
| `SecRandom.Core/Models/SubConfigs/MoreSettingsConfig.cs` | 新增字段 `RawRandomLowerBound` / `RawRandomUpperBound` |
| `SecRandom.Core/Models/SubConfigs/SecuritySettingsConfig.cs` | 新增 `ProtectRawRandomSettings`（锁定属于防护，进加密文件） |
| `SecRandom/Services/Security/SecurityService.cs` | 快照 / Capture / Restore / `IsProtectionDowngrade` / `DisableOperationProtections` 全部加入新防护 |
| `SecRandom/Services/Verification/RawNumberProofService.cs` | 新增：`data/proofs/raw` 写盘、独立哈希链、写入后自校验 |
| `SecRandom/ViewModels/MainPages/RawRandomPageViewModel.cs` | 新增 |
| `SecRandom/Views/MainPages/RawRandomPage.axaml(.cs)` | 新增（`main.rawRandom`） |
| `SecRandom/Langs/MainPages/RawRandom/` | 新增三语资源与 designer |
| `SecRandom/App.axaml.cs` | `AddMainPage<RawRandomPage>` + `AddSingleton<RawRandomPageViewModel>` |
| `SecRandom/SecRandom.csproj` | 登记 base resx + designer |

**为什么不是第四个抽签页**：`DrawEngine` 要求具体候选池，而裸随机数的范围可达 1..10⁹——物化候选既浪费又无意义。这里用**拒绝采样**：种子经 SHA-256 计数器模式展开，落在尾部不满一整块的值丢弃重来，因此不存在取模偏差（`value % span` 在 `span` 不整除 2⁶⁴ 时偏向低位）。放回语义不考虑，一次只产出一个数。

**与信标路径的分工**：不复用 `BeaconEntropyProvider`。那个服务按 `BeaconSourceId` 绑死单一来源，并为**抽签**信标序号写锚点（一次抽取占一个序号）；本页允许一次勾选多个来源，走"就地取数 + 哈希合并"（`SHA256("VeriRandomRaw/v1" ‖ 各源 id ‖ 各源输出)`），不占抽签序号，也不写抽签证明。

**可复核性的边界（界面必须说清）**：
- 只勾选**单一**可验证源（NIST / drand）→ 结果可用该源公布的公开脉冲重算
- 混入 **random.org 或本地 CSPRNG** → 可复核性受损，界面给出警示色说明，而不是脚注
- 无论哪种组合，它都**不能**证明"抽中了某个人"：没有名单就没有 `rosterDigest`

**锁定语义（属于防护，不属于界面偏好）**：锁定状态是 `SecuritySettingsConfig.ProtectRawRandomSettings`，存在**加密的** `data/config/security/settings.json` 里，`settings.json` 无法表达它，导入配置也改不了它。页面内的开关永远可点，但**任何一次翻转都走 `ISecurityService.UpdateSecuritySettingsAsync`**：关掉锁定是"放宽防护"，必须重新输一次密码（设置页 sudo 与全局 sudo 都不满足），这正是"用安全密码保护这个锁"的含义；没有配置安全凭据时（`SecurityEnabled == false`）才直接落盘，否则会弹出一个根本无法满足的密码框。`SecuritySettingsSnapshot` 已同步加入该字段，否则解锁会被当成"没有放宽"而免验证；`DisableOperationProtections` 也在保护整体关闭时一并清掉它。上下界仍按"两侧都能解析且范围一致"才写入 `MoreSettings`，避免输入 `100` 的过程中把 `1` 写回去。

**证明（已接入，本地链）**：每次生成都写一份 `data/proofs/raw/*.rawproof.json`，不提供"存这一份"按钮——能只存喜欢的结果，就等于选择性地留证据。记录包含范围、结果、每个熵源的原文（`OutputValue`、脉冲序号、时间、签名）、种子摘要，以及独立哈希链（`index` / `prevHash` / `selfHash`，头文件 `chain-head.json`）。**与抽签链分开**：`ProofIntegrityVerifier` 用抽签内核判定抽签链，把无法解码的记录塞进去会让每一次裸随机数都变成"链有缺口"。写入后立刻用记录自身**重算种子与结果**并复核链哈希，不通过就记警告并把界面文案改成"请勿使用这份记录"。

**证明的边界**：它**不**提交给 `fair.sectl.cn`。那个服务按固定字段允许列表和固定算法 id 校验匿名审计载荷，裸随机数既没有名单也没有抽签载荷，提交只会得到永久失败的证明画像。`Verifiable` 字段的含义仅是"事后有权威物可核"（恰好一个发布型来源），混合源或本地种子照样记录、照样自洽，只是无法重新锚定。

**校验入口**：证明重放窗口右侧新增「校验随机数记录」（`VerifyRawProofs_OnClick` → `RawNumberProofService.VerifyAll`），逐条用记录自带的熵源原文重算种子与结果、复核它自己那条链，结果按"全部通过 / 哪些文件没过"给出。**这些记录不进链画布**：画布走的是抽签内核重放，裸随机数没有候选池，混进去只会被报成"链有缺口"。锁定开关同时出现在 `settings.general.security` 的独立卡片（`S_RawRandomOperations`）与随机数页上，两处都走同一条密码闸门。

---

## 44. 课件标记触发（第一层：参数、标记编解码、时机判定、存储）

**目标**：让老师在任意课件里放一张标记，翻到那一页就自动点名——专门为**只用翻页笔**的场景（翻页笔只会发 PageUp/PageDown，点不了超链接、也点不了浮窗按钮）。

| 文件 | 内容 |
|---|---|
| `SecRandom.Core/Models/SlideTrigger/SlideTriggerParameters.cs` | 参数集 + `Normalize()` 夹取范围 |
| `SecRandom.Core/Models/SlideTrigger/SlideTriggerDefinition.cs` | 标记定义（id / 名称 / 参数 / 会话消费状态）+ `SlideTriggerId` 生成与校验 |
| `SecRandom.Core/Services/SlideTrigger/SlideTriggerPayload.cs` | 自包含标记载荷的二进制编解码（`VRT1:<base64url>`），参数全部在二维码里 |
| `SecRandom.Core/Services/SlideTrigger/SlideTriggerScheduler.cs` | 纯函数：已武装的标记何时该触发 |
| `SecRandom/Services/SlideTrigger/SlideTriggerStore.cs` | `data/config/slide-trigger/triggers.json`，原子替换，宿主单例 |

**参数随二维码走，不存在本机**。课件是老师在**自己的电脑**上做的（而且往往不是上课那台），只在课堂机器上放 id、让 id 去查本机配置，会让标记在它自己的出生地以外**完全失效**。因此载荷自包含：

```
version(1) flags(1) count(1) delay×10(1) inputs(1) armedSeconds(1) expiresUnixDays(4) id(8)
+ 三个长度前缀 UTF-8 串：名单 / 性别 / 分组（长度 0 表示不限制）
```

**用二进制而不是 JSON**：同样的内容用 JSON 是 300–400 字符，二维码矩阵会被推到投影+截图难以稳定解码的尺寸；打包后带头部只有 18 字节，带上名单与分组约 50 字节，矩阵小而密。

**解码端把所有值重新 `Normalize()`**：标记是"从屏幕上读下来的数据"，不是本进程写出来的，范围必须重新夹一遍。失败按原因分类（不是标记 / 版本不支持 / 损坏 / 已过期），因为"截图里没有标记"是常态，必须便宜地拒掉。

**id 格式**：8 字节随机 → 13 位 Crockford 风格字符（去掉 `I/L/O/U`）。13 位恰好是 64 bit 的 base32 长度（4 + 12×5）。id 只承担"这节课这一页抽过了就不再抽"，不再是查表键。

**参数集**（按需求逐条）：抽取名单、抽取人数、是否可重复、是否忽略历史权重、侦测后多少秒自动触发、是否显示动画、是否启用语音、是否等下一次/第 N 次键鼠输入再触发、性别与分组限制。`AllowRepeat` 与 `IgnoreHistoryWeights` **拆成两个开关**：「不回避同一个人」和「不按历史加权」是两种课堂意图，忽略权重但仍遵守重复控制的组合是合法的。

**时机判定**（`SlideTriggerScheduler`，纯函数）：`AfterDelay` 到点即触发；`AfterInputs` 等第 N 次输入，并受 `ArmedTimeoutSeconds` 约束——没有这个上限，第 12 页武装的标记会在二十分钟后因为一次无关点击而开抽，这是整个功能**唯一会被课堂立刻察觉**的失败模式。输入条件优先于超时（按键恰好落在窗口关闭那一刻，也应该抽）。

**会话消费**：`LastConsumedSession` 记录在哪个会话抽过，`StartNewSession()` 只能手动调用——「这节课结束了」应用无从推断，自动重挂会让同一页在一节课里抽两次。

**存储**：`data/config/slide-trigger/triggers.json`（宿主单例，临时文件 + `File.Move` 覆盖）。**不进 `settings.json`**：标记是每台机器的课堂配置、有自己的增删生命周期，混进主设置会让它出现在每次设置 diff 与策略投影里；它不含密钥也不含敏感信息（只有 id 与抽取参数），因此**不需要**从档案/云端备份中排除。

**尚未实现**（下一层）：`GetLastInputInfo` 事件驱动采样、放映态判定、区域截图与 ZXing 解码、临时热键与生成弹窗、桌面快捷方式。

**已实现（本层）**：`settings.slideTrigger` 设置页（`Views/SettingsPages/SlideTrigger/`）——参数表单、生成并复制（QRCoder → `SetBitmapAsync` + `FlushAsync`）、另存 PNG、标记清单（复制/另存/删除）、滚动会话。页面**只在桌面分支注册**（与 `SlideTriggerStore`、`SlideTriggerMarkerImageService` 同处），因为手机宿主既没有这两个服务，也没有可抓的放映画面。页面不放 ViewModel：没有需要同步的共享状态，标记清单本身就是事实来源，再加一份 VM 只会多一个可能与磁盘不一致的副本。触发执行时必须**复用** `LinkageDrawCoordinator.AuthorizeAsync`（课程时段与集控锁）、`ISecurityService`（`LinkageAction`）、`RollCallDrawService`/`IDrawCommitService`（事务提交）与证明链，不得另开抽签路径；并且要先判断"这次触发会不会弹验证框"，会弹就放弃——否则老师翻到那页会撞上一个没人能输入的密码框。

**安全边界（v1，必须说清）**：标记**没有签名**。任何能把这一页放到投影上的人都能在**任意一台**装了本应用的机器上触发，这与"绑机器"的设计正好相反——自包含是为了能在别的电脑上生效，代价就是不再有机器绑定。当前缓解有三条：同一会话内同 id 只触发一次、可选的过期日期、触发时界面明确显示是哪个标记。要真正防住"学生拍下这页提前显示"，需要非对称签名（老师电脑持私钥、教室机器持公钥）——那是比本功能更重的一套配对流程，未实现前应按**已知风险**对待。

**已生成标记留一份本地清单**（`MarkerText` + 参数 + 过期时间）：清单**不是**触发器的事实来源，只是让设置页能重新导出 PNG、并回答"我这份课件里到底放的是什么"。改参数会重新编码 `MarkerText`，此时 PPT 里那张旧图不会自己变——页面必须提示"需要重新导出并替换"。

---

## 45. 随机数页：批量、指定集合、高级输出；证明详情；浮窗按钮；课件标记触发

同一批下游改动，按提交顺序记录（`880a2fda` → `37152121`、`0107dc80`、`0e4b4378` 等）：

| 文件 | 改动 |
|---|---|
| `SecRandom.Core/Services/Draw/RejectionSampler.cs` | 新增 `SampleFromSet` / `SampleMany` / `SampleManyFromSet`；批次每项用 `SHA256("VeriRandomRaw/v1/batch" ‖ i ‖ 主种子)` 派生独立种子 |
| `SecRandom.Core/Models/Verification/RawNumberProof.cs` | 新增 `Values`（候选集合）、`Results`（整批结果）；两者**只在存在时**进入载荷哈希，否则老记录摘要会变 |
| `SecRandom.Core/Services/Verification/RawEntropyOutput.cs` | 新增：按位展开（SHA-256 计数器模式）、大写十六进制、可打印 ASCII（**拒绝采样 188 = 2×94**，不用取模）、香农熵测量 |
| `SecRandom/Services/Verification/RawNumberProofService.cs` | `Verify` 增加批次/集合分支（整批逐位比对，不是只看第一位） |
| `SecRandom.Core/Models/SubConfigs/MoreSettingsConfig.cs` | `RawRandomUseValues` / `RawRandomValuesText` / `RawRandomAdvancedOptionsEnabled` / `RawRandomRawEntropyEnabled` / `RawRandomBitCount` / `RawRandomAsciiOutput` |
| `SecRandom.Core/Models/SubConfigs/FloatingWindowSettingsConfig.cs` | `ShowRawRandomButton`，**默认 false** |
| `SecRandom/Views/FloatingWindow.axaml.cs` | 工厂 `"raw_random"` + 可见列表 + `GetRawRandomButton`（`App.ToggleMainWindow("main.rawRandom")`） |
| `SecRandom/Views/SettingsPages/More/MoreSettingsPage.axaml` | 「随机数高级输出」开关（`S_RawRandomAdvanced`），高级块的总闸 |
| `SecRandom/ViewModels/MainPages/RawRandomPageViewModel.cs`、`Views/MainPages/RawRandomPage.axaml` | 生成个数、指定集合、高级折叠块（默认收起）、信息熵行 |

**批量为什么不是"连续抽几次"**：每个位置的值只依赖（主种子，索引），所以校验端可以单独重算第 N 个值，而整个批次仍绑定同一个种子。重算入口是 `MatchesRecordedValues`。

**集合为什么必须进证明**：区间能从 `lower/upper` 复原，集合不能；不记录就等于这份证明无法复核。重复值在解析时折叠成 `SortedSet`，否则输入 `5,5,10` 会静默给 5 双倍权重。

**高级输出记种子、不记展示文本**：文本是 `(种子, 位数, 格式)` 的确定性函数，`SeedDigest` 已入证明，再存一份只是重复。位数越宽不等于熵越多，界面同时显示实测香农熵与熵源上限（256 bit）。

**浮窗按钮默认关闭**：浮窗是教室里长期挂着的东西，每个按钮都是"点错就抽一次"的操作面。

**证明详细信息**（`0107dc80`）：`ProofReplayFileService.TryBuildDetails` 重读文件构建 `ProofDetails`，弹窗顶部固定提示"为证明文件自身宣称的信息"。**审计承诺是本机重算的** `SHA256(auditPayload)`，不是读取的字段。

**结果浮窗触控**（`89279662` / `bc18d804`）：鼠标走 `BeginMoveDrag`/`BeginResizeDrag`；**触控走手动移动与缩放**（原生循环是鼠标语义，手指按下去毫无反应），并用 `_manualPointer` 闸门保证同一时刻只有一个指针（此前第二根手指重入原生循环直接崩溃）。尺寸记忆在 `MoreSettings.ResultMiniWindowWidth/Height`，位置刻意不记。

**课件标记自动识别**（`2965f24f` → `0e4b4378`）：`SlideTriggerWatcher` 每 100 ms 读 `GetLastInputInfo`，输入变化且前台是全屏放映时，按 +200/600/1200 ms 抓帧解码；`SlideTriggerExecutor` 先 `EvaluateGate(RollCallStart)`，不允许就放弃（不弹密码框），否则 `AuthorizeAsync` → `RollCallDrawService.DrawAsync`。**「可重复」「忽略历史权重」在 `RollCallDrawRequest` 里没有位置，因此命中即跳过并记 warning，而不是按全局配置硬抽**。手动「检测当前画面」改读**所有显示器**（`CaptureAllDisplays`），因为点它时前台一定是设置窗口。

---

## 46. 配置策略摘要扩容（v6）、设置摘要徽标、候选空间与高级输出

### 46.1 策略摘要覆盖范围变更（**作废已签发文档**）

`SecRandom.Core/Services/Security/ConfigPolicyDigest.cs`

| 项 | 变化 |
|---|---|
| 新增覆盖 | `defaultDrawSettings`、`rollCallSettings`、`quickDrawSettings`、`lotterySettings` 四棵子树 |
| `ScopeId` | `verirandom-config-policy/v5` → **`v6`** |

**为什么必须补**：这四棵子树决定重复模式、抽取数量、闪抽名单、动画等**直接影响抽取结果**的设置，此前完全不在签名范围内——改掉它们，签名照样"验证通过"，而文档写着"关键抽取策略被签名锁定"。这是一个真实的声明与实现的偏差，不是形式问题。

**后果**：已按 v5 签发的策略文档会被判为 `DocumentMalformed`，需在可信设备上重新签发。该文件自身的规则就是"覆盖字段集变化必须改 id"，因此这一步不可省。

**仍然不覆盖**（有意）：`appearance`、`floatingWindow`、`timer`、`voice`、`historyManagement`、`notification`、`update`、`moreSettings`（除 `lotteryEnabled`）。理由：它们不影响抽取结果。这条判断记录在此，便于将来被推翻时有据可查。

`SecRandom.Core.Tests/ConfigSignatureTests.cs` 里的 `Scope = "verirandom-config-policy/v1"` 只用于测 `IsDocumentWellFormed` 的非空判断，**不受**本次升级影响。

### 46.2 设置摘要徽标（本地哈希模式）

| 文件 | 改动 |
|---|---|
| `SecRandom.Core/Models/SubConfigs/MoreSettingsConfig.cs` | 新增 `ShowSettingsDigestBadge`（**默认 true**） |
| `SecRandom/ViewModels/MainPages/RollCallPageViewModel.cs` | 新增 `TrySetLocalDigestBadge()`，挂在原「策略徽标」的**未验签分支**上 |
| `SecRandom/Views/SettingsPages/More/MoreSettingsPage.axaml` | 「显示设置摘要徽标」开关（`S_SettingsDigestBadge`） |

行为：分离签名已验证 → 仍显示公钥指纹（不受开关影响）；未启用签名且 `ConfigIntegrityMode.LocalFingerprint` → 显示设置摘要前 12 位，悬停看完整 SHA-256 与路径；其余 → 不显示。

**摘要是现算的**（重读 `data/config/settings.json` 再 SHA-256），不从凭据信封读记录值：徽标必须显示"此刻磁盘上是什么"，否则它只是另一份可能过期的声明。读不到文件就隐藏徽标，真实问题由启动闸门报告。

### 46.3 候选空间：列表 + 区间 + 排除 + 枚举生成器

| 文件 | 内容 |
|---|---|
| `SecRandom.Core/Services/Verification/RawCandidateSpec.cs` | 新增：解析 → 合并区间 → 相减 → 基数 → 索引抽样（`Sample`/`SampleMany`） |
| `SecRandom.Core/Services/Verification/RawPredicate.cs` | 新增：受限判定式求值器（递归下降，仅 `X` 一个变量） |
| `SecRandom.Core/Models/Verification/RawNumberProof.cs` | 新增 `Intervals`（`List<long[]>`）+ `Cardinality`，仅在存在时进入载荷哈希 |

关键决定：

- **区间表示**：`1-1000000 排除 7` 是两段区间、基数 999999，不物化、不拒绝循环。抽样是"均匀索引 → 按段长定位"，一次抽中。
- **证明记解析后的区间，不记表达式**：记表达式就得在重放时重新解析/重新求值，一次语义变化会让**老记录重放出不同结果而文件本身看不出异常**。
- **判定式不是脚本引擎**：手写递归下降，语法只有整数、`+ - * / %`、比较、`&& || !`、括号、`X`。没有赋值、循环、I/O、反射。除零/模零**报错**而不是丢值。
- **相邻区间合并**：`5-6,7-8` → `5-8`（`nextStart <= end + 1`），否则基数会算少。
- **重复值折叠**：集合模式用 `SortedSet`，否则输入 `5,5,10` 会静默给 5 双倍权重。

### 46.4 高级输出：位宽、三种表示、拼接熵

| 项 | 决定 |
|---|---|
| 位宽 | `NumericUpDown` 可手输（8–16384），32/64/128/1280 只是快捷按钮 |
| 表示方式 | 十进制（`BigInteger` 无符号大端）、十六进制（大写）、可打印 ASCII |
| ASCII | 字符数**独立于位宽**（1–4096）；拒绝采样 `188 = 2×94`，不用取模 |
| 拼接熵 | `RawRandomEntropyConcatEnabled`：每轮重新取数、串接派生材料；文案写明只有本地 CSPRNG 与 random.org 会带来新熵 |
| 实测熵 | 按**源的发布宽度**累加（NIST 512 bit 脉冲 = 512，不是派生种子的 256），并显示熵源上限 |

`SecRandom.Core/Services/Verification/RawEntropyOutput.cs`（新增）：`Expand`（SHA-256 计数器模式）、`ToHex`、`ToDecimal`、`ToPrintableAscii`、`Analyze`（香农熵）。

**记录的是种子而非展示文本**：输出是 `(种子, 位数, 格式)` 的确定性函数，`SeedDigest` 已入证明；再存一份只是重复。日志与证明里都不出现那段 hex/ASCII。

### 46.5 其它小项

- **随机数记录进重放窗口**（`5f7d4666`）：`RawNumberProofService.Replay` 返回理论重算值，窗口用「包含随机数记录」开关显示，**不进抽签链画布**（无候选池，抽签内核无法重放）。
- **结果可选中 + 一键复制**（`6dc193b0`）：`SelectableTextBlock` + 「复制结果」；长输出（1280 bit = 320 字符）此前连选中都做不到。
- **锁定覆盖候选块与生成器**（`9cba043a`）：改为整块包 `IsEnabled`，不再逐控件绑——逐控件绑必然漏。`生成个数`不受锁（属"抽几次"，非"从哪抽"）。
- **`More/Resources.Designer.cs` 由 resx 整体重新生成**（`6fe915f0`）：该文件是多行块属性结构，局部插入连续出错三次（其中两次还被"括号平衡"掩盖）。以后修改 designer 一律重新生成 + 深度校验。
- **已知误导项（未修）**：高级模式（原始熵 / 按位）生成的随机数记录，在重放窗口显示为 `记录值 0 · 重算值 0 · 一致`。这条路径本来就只记种子（`SeedDigest`），`0` 是刻意留空的占位——两个 0 相等属实，但对读者没有任何信息量，反而像"核对通过了"。理想做法是把这类记录在列表上标成"熵记录"而不是数字。

---

## 47. 课件标记可编辑与快捷键复制；随机数「熵记录」可复核

**动机**：交接清单里的两条半成品——标记只能新建不能改参数、课上取标记必须打开设置页；以及随机数高级模式的记录在重放窗口显示「记录值 0 · 重算值 0 · 一致」，那个「一致」不含任何信息。查证时还发现一个**真实缺陷**：拼接轮数 > 1 的熵记录自校验必然失败（`SeedDigest` 记的是多轮拼接摘要，`Verify` 却按单轮重算），`SaveAsync` 只记 warning，`VerifyAll` 会把这类文件列为无效。

### 47.1 课件标记

| 项 | 文件 | 说明 |
|---|---|---|
| 编辑已有标记 | `SecRandom/Views/SettingsPages/SlideTrigger/SlideTriggerSettingsPage.axaml(.cs)` | 行内「编辑」经 `FillForm` 把参数读回表单，保存走**同一 id**（`SlideTriggerStore.Update`），保存后自动把新 PNG 放进剪贴板并提示替换课件里的旧图片；`RemainingDays` 把绝对过期时刻折回天数，避免每次编辑都把有效期往后推 |
| 更新原子化 | `SecRandom/Services/SlideTrigger/SlideTriggerStore.cs` | `Update` 先 `SlideTriggerPayload.Encode` 再改记录（超长名单名抛 `ArgumentException` 时不留半更新记录）；页面传**分离副本**（`editing` 是 store 自己的实例）；`Find` 落空时拒绝复制空标记文本 |
| 拒绝不可表达的参数 | 页面 + `SlideTriggerExecutor` | 移除 `AllowRepeatBox`/`IgnoreWeightsBox` 与资源 `C_AllowRepeat`/`C_IgnoreWeights`；运行时拒绝（`UnsupportedParameters`）保留；`DescribeParameters` 对旧标记追加 `M_UnsupportedParamsBadge`，探测结果同样带上 |
| 快捷键复制 | `SecRandom/Services/SlideTrigger/SlideTriggerQuickCopyService.cs`（新增）、`SecRandom/Services/Desktop/GlobalShortcutService.cs`、`SecRandom.Core/Models/SubConfigs/MoreSettingsConfig.cs` | 新增 `ShortcutAction.CopySlideTriggerMarker` + `MoreSettings.CopySlideTriggerMarkerShortcut`（空 = 不注册，注册仍受 `EnableShortcut` 总开关约束，实测变更即时生效走既有 `WmReload`）；`SlideTriggerCatalog.QuickMarkerId`（`Create` 覆盖为新标记、`Remove` 清空、`SetQuickMarker` 校验存在），经 `SlideTriggerStore.QuickMarker` 解析 |
| 剪贴板目标窗口 | `SlideTriggerQuickCopyService.FindTopLevel` | 从 `IClassicDesktopStyleApplicationLifetime` 取 `MainWindow`，否则任一枚举窗口；**不弹窗**——弹窗会从放映中的课件抢焦点，而隐藏窗口同样持有平台剪贴板 |
| 文案 | `SecRandom/Langs/SettingsPages/SlideTrigger/` | 三语新增 14 条（`C_Edit`、`C_CancelEdit`、`M_EditModeFormat`、`M_EditedFormat`、`M_EditCanceled`、`M_UnsupportedParamsHint`、`M_UnsupportedParamsBadge`、`C_QuickCopyShortcut`、`C_ShortcutWatermark`、`C_UseAsQuickCopy`、`M_QuickCopyHint`、`M_QuickCopyTargetFormat`、`M_QuickCopyTargetNone`、`M_QuickCopyTargetSetFormat`），删除 2 条 |

**未做**：原设想的「生成弹窗」（按键 → 小窗改参数 → 复制）改为「按键 → 直接复制已选标记」。课堂现场需要的是零交互，弹窗会抢走放映焦点；改参数仍回设置页。

### 47.2 随机数熵记录

- `SecRandom.Core/Models/Verification/RawNumberProof.cs`：新增 `AdvancedOutput`、`EntropyRoundSize`、`[JsonIgnore] IsEntropyRecord`。`IsEntropyRecord` 同时识别**旧记录**（无区间 / 集合 / 批量且数值全为占位 0），旧记录因此不必改文件就能被正确识别。
- `RawNumberProofHashing.ComputePayloadHash`：仅在存在时追加 `|advanced` 与 `|roundsize:N`，**旧记录摘要逐字不变**（与 `Values`/`Results`/`Intervals` 同一约定）。
- `SecRandom/Services/Verification/RawNumberProofService.cs`：`DeriveEntropySeed` 按 `EntropyRoundSize` 分轮派生后拼接；新增 `ReplayEntropy` + `RawDigestVerdict`（`Matches`/`Mismatch`/`Unverifiable`）；`RawNumberVerification` 增加 `ValueApplicable`，熵记录不比较占位值，但 `Succeeded` 仍要求摘要与链哈希。
  - 旧的多轮熵记录没有分轮信息、无法重组，重放如实显示「无法复核」，**不冒充「不一致」**。
- `SecRandom/Views/Verification/ProofReplayView.axaml.cs`：熵记录分支不再打印 `0 / 0`，改为核对种子摘要并说明「展示文本不保存在记录里」；列表后缀 `C_RawNodeEntropy`。
- `SecRandom/ViewModels/MainPages/RawRandomPageViewModel.cs`：`EntropyBitsOf` 按 `(SourceId, PulseIndex)` 去重（本地 CSPRNG 每次取数仍算新熵），拼接未带来新熵时在实测行追加 `M_ConcatNoGain`；排除项未命中任何候选时在候选行追加 `M_ExclusionsIneffective`（判定：以空排除重解一次并比较基数）。
- `SecRandom/Langs/SettingsPages/General/Verification/`：三语新增 5 条；`SecRandom/Langs/MainPages/RawRandom/`：三语新增 2 条。
- **筛选条件持久化**：`MoreSettingsConfig` 新增 `RawRandomExclusionsText`、`RawRandomGeneratorFromText`、`RawRandomGeneratorToText`、`RawRandomPredicateText`（默认空串），构造函数在 `_isLoading` 保护下回填，四个 `On...Changed` 统一走 `PersistFilterText`（一次性把 `_isLoading` 守卫与 `Save()` 收在一处，否则四个副本里漏一个就会在构造时把默认值写回磁盘）。锁定语义不变：页面把候选/排除/生成器整块放在 `IsEnabled="{Binding !ViewModel.IsSettingsLocked}"` 里，锁上即不可改，因此持久化天然受同一把锁约束。未进入 `ConfigPolicyDigest`（同 §47.2 的既有结论）。

**未改**：`ConfigPolicyDigest` 未新增覆盖字段（快捷键与标记库属于本机操作设置，不在签名范围）；`EntropyRoundSize` 只影响记录自校验，不进入抽签证明。

**待真机确认**：快捷键在放映全屏时是否被放映程序吞掉（`RegisterHotKey` 的既有语义如此，但需要在 PPT 全屏下实测）；编辑后的标记图片在投影分辨率下的解码率与新建时是否一致。

---

## 48. 证明自洽核对：重算输入承诺、结果帧对账、候选数有序性

**动机**：重放只信任 payload 帧；`inputHash` 从写入后再没被重算过；`result.winnerRecordIds` 与 payload 结果帧不一致时，界面显示一套、判定另一套；audit 的候选数只被逐字展示。这三处都是「宣称」，不是「核对」。

### 48.1 实现

| 位置 | 变更 |
|---|---|
| `SecRandom.Core/Services/Verification/ProofConsistency.cs`（新增） | `ProofConsistency.Check(DrawProof) → ProofConsistencyReport`，三项检查见下 |
| `SecRandom/Services/Verification/ProofReplayFileService.cs` | `Describe` 与 `TryBuildDetails` 各调用一次；`ProofReplayNode`、`ProofDetails` 增加 `Consistency` 字段 |
| `SecRandom/Views/Verification/ProofReplayView.axaml.cs` | 节点详情面板新增一行「自洽核对」，证明详情窗新增一行；三态（通过 / 部分通过 · 旧引擎 / 具体问题） |
| `SecRandom/Langs/SettingsPages/General/Verification/` | 三语 7 条：`C_ProofDetail_SelfCheck`、`M_SelfCheckOk`、`M_SelfCheckLegacyEngine`、`M_SelfCheckUnavailable`、`M_SelfCheckInputMismatch`、`M_SelfCheckResultMismatch`、`M_SelfCheckCountsImpossible` |
| `SecRandom.Core.Tests/ProofConsistencyTests.cs`（新增） | 6 例 |

三项检查：

1. **输入承诺**：用 payload 解出的 `VerificationDrawInput` 加上 proof 里真实的 audit 字节，走 `VerificationWireCodec.ComputeInputHash` 重算，与记录的 base64url 比对。**只在 `AlgorithmEngineVersion` 等于当前引擎时判定**，否则 `InputHashCheckable = false`——旧引擎的帧编码可能不同，把「编码版本不同」报成「被改过」会冤枉正确记录。
2. **结果帧对账**：`result.winnerRecordIds` 与 payload 响应帧的中奖者**按位置**比较。页面显示取前者、重放判定取后者，两者不一致时以前无人察觉。
3. **候选数有序性**：`after > 0 && after <= before && payload 候选数 <= after`。**刻意不做等值判断**：门槛与重复规则会减人，`FreezeCandidates` 还会因零概率内部规则再减人，等值检查会在每次带内部规则的抽取上误报；能说的只有「只能少、不能多」。奖池证明没有 `fairness` 块，旧记录这两个计数为 0，两种情况都**弃权**而不是猜。

### 48.2 为什么门槛证据没有进 payload / audit（重要）

这是本次唯一没做的部分，原因是硬约束，不是取舍：

- **wire payload 与 audit JSON 都被 `fair.sectl.cn` 消费**：服务端按固定字段白名单校验匿名审计载荷，并按固定编码后台重放。改 `RequestFormatVersion` 或给 audit 加属性，会让**每一条普通存证**失败在 `audit_payload_invalid` 或解码阶段。
- **`WitnessClient.ComputeAttestedProofHash` 的字段清单也不能追加**：服务端回执与 RFC 3161 时间戳都按它绑定，本机口径一动就与服务端分叉。
- 因此「门槛前集合 + 每人次数 + 门槛规则版本」只能走 **fork-local 兄弟文件**（与 `*.ownproof.json` 同构：引用 Up 节点的 `chain.selfHash`、自带链、永不外发）。落地前需要同时决定归档根、保留策略与删除语义（它必须随 Up 文件一起删除，否则会留下指向不存在证明的证据）。

### 48.3 残余缺口（如实登记）

- 门槛本身仍不可验：证明只有权重，没有门槛前集合，也没有每人历史次数。
- 即使前述兄弟文件落地，也只能证明「按自报的次数与规则，只少了这些人」，不能证明次数是真的——历史文件可被清空或整包导入覆盖，那些路径不产生证明。
- `ProofConsistency` 只回答「文件是否自相矛盾」，不回答「抽签是否公平」；公平仍由 `scripts/FairnessAudit` 负责。

---

## 49. 平均差距门槛的证据（fork-local 兄弟文件 `*.gateevidence.json`）

**动机**：门槛是唯一决定「谁不能抽」的硬过滤，而证明里只留下两个计数——无法区分「按次数移除」与「随手剔掉三人」。

**为什么不是 payload 字段**：wire payload 与匿名审计载荷都由 `fair.sectl.cn` 按固定编码/字段白名单消费（见 §47.3），动它们会让每条普通存证失败；`WitnessClient.ComputeAttestedProofHash` 的字段清单同理被服务端回执与 RFC 3161 绑定。因此新证据只能落成 **fork-local 兄弟文件**，与 `*.ownproof.json` 同构。

### 49.1 实现

| 层 | 位置 | 内容 |
|---|---|---|
| 模型 | `SecRandom.Shared/Models/Verification/GateEvidence.cs`（新增） | format `verirandom-gate-evidence/v1`；字段 `proofId`/`upChainIndex`/`upChainHash`/`gateRuleId`/`threshold`/`candidateCountBefore`/`candidateCountAfter`/`removed[{recordId,drawCount}]`/`createdAtUtc`/`selfHash`；`GateEvidenceHashing.ComputeSelfHash`（域分隔 `VeriRandomGateEvidence/v1`，成员按 `recordId` 排序后入哈希，因此枚举顺序不影响结果） |
| 快照 | `SecRandom.Core/Models/Verification/GateEvidenceSnapshot.cs`（新增） | `GateEvidenceDiff.FromSurvivors(preGate, survivors, threshold)`；**无人被移除时返回 null**——「没有文件」因此是有意义的 |
| 载体 | `SecRandom.Core/Models/Verification/VerificationDrawInput.cs` | 新增 `GateEvidence` 属性，**刻意不进 wire 帧**：`EncodeInput` 只写显式字段，故 `InputHash` 不变 |
| 采集 | `SecRandom.Core/Services/Draw/DrawEngine.Verification.cs` | `BuildGateEvidence`：仅 `DrawType.Fair`；阈值取**实际生效**的 `AverageGapProtectionThreshold`（不是设置模型里的 `GapThreshold`）；次数取门槛所用 `historyCache[...].TotalCount` |
| 写盘 | `SecRandom/Services/Verification/GateEvidenceExportService.cs`（新增） | `GateEvidencePaths`：`.srproof.json` ↔ `.gateevidence.json`，含反向 `ToUpPath`；`Create` 绑定 Up 的 `chain.selfHash`；原子写（临时文件 + move） |
| 导出顺序 | `SecRandom/Services/Verification/DrawProofExportService.cs` | Up 写盘 → Own → 门槛证据 → 存储上限清理。顺序保证**不可能存在没有 Up 的孤儿证据**；异常只记 warning，绝不让已完成的抽取失败 |
| 删除配对 | 同上 | `DeleteSiblings(upPath)` 统一删除两个兄弟，年龄清理与存储清理各调用一次，避免将来新增第三种兄弟时漏掉一处 |
| 校验 | `SecRandom/Services/Verification/ProofIntegrityVerifier.cs` | `VerifyGateEvidence` → `GateEvidenceReport(Nodes, Modified, Detached, RemovedMembers)`；校验 `selfHash`，并校验 Up 绑定（`chainHash`/`chainIndex`/`proofId` 三者）。**不计入 `IsHealthy`**——与 Own 报告同一规则 |
| 重放窗 | `SecRandom/Services/Verification/ProofDetails.cs`、`ProofReplayView.axaml.cs` | 详情新增「门槛证据」一行：移除人数、候选前后、阈值，以及「与本次证明绑定一致 / 不一致 / 内容校验失败」；**无证据时明写「没有门槛证据」**，绝不按通过处理 |
| 文案 | `SecRandom/Langs/SettingsPages/General/Verification/` | 三语 6 条（`C_ProofDetail_GateEvidence`、`M_GateEvidenceNone/PresentFormat/Bound/Detached/SelfHashInvalid`） |
| 测试 | `SecRandom.Core.Tests/GateEvidenceTests.cs`（新增） | 5 例：只报缺失者、无移除则无证据、哈希与顺序无关、计数变化即变更、哈希覆盖 Up 绑定 |

### 49.2 顺手修掉的测试侧断裂

`SecRandom.Core.Tests/PluginDrawServiceTests.cs` 的 DI 容器注册了 `DrawProofExportService` 却没有注册它依赖的 `OwnProofChainStore`/`OwnProofExportService`，一旦运行测试就会在解析时抛异常。CI 的 `Build.yml` 没有测试步骤，只有 `.bak` 里提到过 `dotnet test`，所以这类断裂不会被构建发现。本次一并补上，并给三个测试容器加注 `GateEvidenceExportService`。

### 49.3 残余缺口（如实登记）

- **次数仍是自报的**：历史文件可被清空或被整包导入覆盖，那些路径不产生证明，所以「每人几次」无法从链上独立重算。这份证据把「按次数移除」从**隐含**变成**可复算**，但没有消除「次数是真的」这个假设。
- **存储上限只统计 `*.srproof.json` 的字节**，兄弟文件不计入（`*.ownproof.json` 早已如此）。本次刻意不改，以免改动既有保留语义；代价是 `data/proofs` 实际占用可超过配置上限。
- **归档**：`proofs` 根按扩展名无关递归打包，新证据自动进入本地/自动/云端备份；诊断包不含证明文件（这一点未变）。
- 旧证明与「本次未移除任何人」都表现为没有证据文件，界面对这两种情况给出同一句说明，无法区分——这是格式上没有写入内容的必然结果。

## 50. 随机数的机密模式（不写记录、结果遮盖、状态不持久）

**动机**：随机数页目前的规则是「每次生成都立即写一份证明」，理由写在代码注释里——没有记录的数字正是这个页面要避免的情况。但课堂上确实存在「这个数不该留下痕迹」（临时口令、分组种子）的用法，因此增加一个**显式例外**，并让它把代价写在脸上。

### 50.1 实现

| 位置 | 变更 |
|---|---|
| `SecRandom/ViewModels/MainPages/RawRandomPageViewModel.cs` | 新增 `ConfidentialMode`、`IsResultRevealed`、计算属性 `DisplayResult`/`IsConfidentialMasked`/`RevealButtonText`、`ToggleResultVisibility` 命令；`DisplayResult` 在机密且未显示时返回固定 12 个 `*`（**固定宽度而非按长度**：十进制位数本身就是提示，位宽用户本来就知道） |
| 生成路径 | 普通与高级两条分支在 `SaveAsync` **之前**判断机密模式：直接返回，不构造、不写入 `RawNumberProof`；状态行改为 `M_ConfidentialGenerated` |
| 可复核提示 | `RefreshVerifiabilityNotice` 在机密模式下优先返回 `M_ConfidentialNotice` 并置 `IsVerifiabilityReduced`（红色），因为「能不能复核」已经被「没有东西可复核」回答了 |
| 状态不持久 | `ConfidentialMode` **刻意不进 `MoreSettings`**：其它开关是偏好，这一项是「这次不留证据」的承诺；持久化会让忘记关掉的人连续几周产出不可复核的数字。每次启动重新勾选 |
| 遮盖与复制 | 结果区绑定改为 `DisplayResult`，遮盖时提供「显示/隐藏结果」按钮；复制按钮仍取真实值 `ResultText`，状态行在机密模式下改为 `M_CopiedConfidentialFormat`，明说内容已进入系统剪贴板（含剪贴板历史） |
| 锁定 | 机密模式与「输出原始熵」同样受 `IsSettingsLocked` 约束：锁定态下不可改，避免在被锁定的配置上悄悄关掉取证 |
| 文案 | `SecRandom/Langs/MainPages/RawRandom/` 三语 8 条（`C_ConfidentialMode`、`C_RevealResult`、`C_HideResult`、`M_ConfidentialMasked`、`M_ConfidentialGenerated`、`M_ConfidentialNotice`、`M_ConfidentialHint`、`M_CopiedConfidentialFormat`） |

### 50.2 能保证什么、不能保证什么

- **能**：不产生任何证明文件、不写配置、不写日志；种子与结果只作为进程内对象存在，关闭应用即失去。
- **不能**：`*` 遮盖只挡肉眼与截屏，不挡**进程内存**——操作系统仍可能把内存页换出，崩溃转储也可能包含它；一旦点了复制，内容进入系统剪贴板并可能进入剪贴板历史，这一步应用无法撤回。文案把这两条写出来，而不是让「机密」二字替用户做决定。
- 机密模式与「随机数证明只存在本机」并不冲突：它只是把「本机有记录」也一并去掉。

## 51. 仓库根新增活的威胁模型 `THREAT_MODEL.md`

**动机**：可验证性已经被切成多个机制（证明重放、自洽核对、门槛证据、服务端回执、TSA、预承诺协议），但**每个机制各自能证明到哪一步**散落在代码注释、台账与对话里。结果是：一个新功能落地后，没人能一句话回答「现在这块挡得住什么」。

**新增文件**：`THREAT_MODEL.md`（仓库根，fork 自有；上游没有对应文件）

结构固定为八节：状态图例（✅ / 🟡 / ❌ / ⛔，其中「已覆盖」只表示本机能查出来）→ 范围与非目标 → 资产 → 参与者与信任边界 → 攻击面（T1–T14，按结果/门槛/链条/随机源分组）→ 机制现状表（逐条对应到代码路径）→ 闭合路径与推荐顺序 → 不可证命题（禁止文案越界）→ 带日期的变更记录。

**与既有文档的分工**（不要合并）：

| 文件 | 职责 |
|---|---|
| `THREAT_MODEL.md` | **当前代码**实际挡住什么，逐条状态与残留 |
| `docs/dual-draw-server-integration.md` | **预承诺协议**的设计（服务端契约、承诺链、未消费承诺） |
| `DownStream_Change.md` | 与上游的差异登记（本文件即登记在此） |

**写死的维护规则**（同时写进 `AGENTS.md` 的 MAINTENANCE CHECKLIST 与 WHERE TO LOOK）：

> 改动 `ProofConsistency`、`ProofIntegrityVerifier`、`GateEvidence`/`GateEvidenceExportService`、`DrawEngine.Filter.cs`/`DrawEngine.Verification.cs` 里的门槛规则、预承诺客户端、或证明保留默认值时，**必须在同一次提交里**更新 `THREAT_MODEL.md` 的：对应攻击行状态 + 机制表 + 变更记录一行。

这条规则的措辞刻意写了「**不得因为数据已存在就标为已覆盖，只有检查真正在跑才算**」——T3 当前正是这种状态（证据数据齐、复算器未实现，标 🟡），这是本文件存在的意义所在。

**登记时点的状态**（供上游合并时对照）：T1 ✅；T3/T6/T7/T13 🟡；T4/T5/T14/T11 ❌；T9 ⛔；其余 🟡。三次状态变化（`af9b70c1`、`6b17c9d2`、本次建立）已写入文件内的变更记录表。
