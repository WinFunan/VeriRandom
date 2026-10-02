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
| 2 | 分支声明、CLA 与治理文档 | README / About / CONTRIBUTING / CLA / PR 模板 | 低 |
| 3 | NIST Beacon 第四条熵源路径 | 新增验证路径与配置项 | **中高**（动了验证链路） |
| 4 | 遥测与在线状态默认关闭 + SECTL 披露 | 隐私配置默认值、OOBE 法务页、隐私设置页 | 中 |
| 5 | 自动更新禁用（保留能力） | Core 常量、更新调度/入口、更新设置页 | 中 |
| 6 | CI 与构建修复 | `build_publish.yml`、Android 版本号、密钥脚本 | **高**（同一文件多处改） |

---

## 1. 品牌与显示名称：对外 VeriRandom、对内 SecRandom

**意图**：用户可见的产品名统一为 VeriRandom；所有内部标识保持 SecRandom 不变（详见 §7）。

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

**注意**：`SecRandom Sync`、`github.com/SECTL/SecRandom`、`SecRandom.safety.key`、`secrandom://`、`SecRandom4Ci` 等**不是**展示名，禁止改（见 §7）。

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

**修改文件**：

| 文件 | 改动 |
|------|------|
| `README.md` / `resources/README_EN.md` / `resources/README_JA.md` | 标题改 VeriRandom；加入 `> [!IMPORTANT]` 分支声明；徽章/下载/社区/贡献者拆分为「本仓库」与「上游」两段；移除 Star History；版权改为 WinFunan + SECTL |
| `SecRandom/Views/SettingsPages/About/AboutSettingsPage.axaml` | `S_App` 头部改 VeriRandom；新增 `S_App_ForkNotice` 行 |
| `SecRandom/Langs/SettingsPages/About/Resources{,.en-US,.ja-JP}.resx` + `Resources.Designer.cs` | 新增 `S_App_ForkNotice` / `M_App_ForkNotice` |
| `CONTRIBUTING.md` / `resources/CONTRIBUTING_EN.md` / `resources/CONTRIBUTING_JA.md` | 标题与正文改 VeriRandom；加入 CLA 小节与签署语；Issue 与 clone 指向本仓库；保留 `upstream` remote 说明 |

**CLA 的边界（必须保持）**：
- 只约束**签署它的贡献者**的新贡献，不能追溯既有贡献。
- **不能**改变上游 GPLv3 代码的许可证；整体分发仍须满足 GPLv3。
- 调整出站许可前必须提前 ≥30 天公示。
- About 页的作者/组织/鸣谢署名行仍指向上游，属于署名性质，**不要**改成 VeriRandom。

---

## 3. NIST Beacon 第四条熵源路径

**意图**：在普通模式引入「外部信标熵源」开关，用已发布的 NIST Beacon 脉冲派生抽取种子，使主持人无法注入自定义熵；同一脉冲内用从 0 开始、公差为 1 的序号保证不重复取样。

**新增文件**：

| 文件 | 说明 |
|------|------|
| `SecRandom.Core/Models/Verification/BeaconPulse.cs` | 脉冲 DTO |
| `SecRandom.Core/Services/Verification/BeaconSeedDerivation.cs` | 纯函数种子派生 `SHA256("SecRandomBeacon/v1" ‖ pulseIndex ‖ chainIndex ‖ outputValue ‖ sequence)` |
| `SecRandom.Core/Services/Verification/BeaconSequencePolicy.cs` | 每脉冲序号策略（新脉冲从 0，同脉冲 +1，拒绝回退/同索引异值） |
| `SecRandom.Core/Services/Verification/BeaconEndpointPolicy.cs` | 端点校验（默认官方 NIST v2，远程必须 HTTPS） |
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
| `SecRandom/Services/FirstRun/FirstRunOobeService.cs` | 新增 `CurrentSecRandomServicesVersion = 1`；纳入 `IsPrivacyPolicyOnlyRequired()` 与 `Complete()` |
| `SecRandom/ViewModels/FirstRunOobeViewModel.cs` | 新增 `AcceptedSecRandomServices` / `IsSecRandomServicesRequired`；纳入 `CanContinue` / `FinishAsync` / 属性通知 |
| `SecRandom/Views/FirstRunOobeWindow.axaml` | 隐私页新增数据流向提示；法务页新增 SECTL 披露折叠区 + 条款 + 免责声明 + SecRandom 隐私政策 + 必选勾选 |
| `SecRandom/Langs/FirstRunOobe/Resources{,.en-US,.ja-JP}.resx` + Designer | 政策改为默认关闭并注明数据流向；新增 `SecRandomPrivacyPolicy`（含 v3.0.0 前置说明）、`C_SecRandomServicesTitle/Clause/Disclaimer/Accept`；`C_PrivacyEncouragement` → `C_PrivacyDataDestination`；`C_LegalDescription` 改默认关闭 |
| `SecRandom/Langs/SettingsPages/General/Privacy/Resources{,.en-US,.ja-JP}.resx` | 两条说明追加「数据将被发送给 SecRandom 与 SECTL」 |

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

## 7. 刻意保留的内部标识（禁止改名）

以下标识在本分支中**必须保持 `SecRandom`**，改名会破坏兼容性：

- 命名空间、程序集/项目名、`InternalsVisibleTo("SecRandom")`
- `avares://SecRandom/...` 资源 URI
- `secrandom://` 协议、`SecRandom.safety.key`
- `SecRandom.package.json` 及其 `product: "SecRandom"`（被 `SecRandom.Launcher` 与 `UpdateCenterService.Product` 校验）
- `SecRandom-update-manifest.json` / `.sig`、`SecRandom-cloud-` 前缀、导出文件名
- `.srproof.json`、`SecRandomProof/...` 与 `SecRandomBeacon/v1` 域分隔符
- `SecRandom Sync` 服务、`SecRandom4Ci`、`fair.sectl.cn` / `sectl.cn` 等上游端点
- 数据根目录 `LocalApplicationData/SecRandom/data`
- Android 包名 `cn.sectl.secrandom.mobile`、iOS bundle id `cn.sectl.secrandom.mobile`、macOS `top.sectl.secrandom`
- 上游仓库链接 `github.com/SECTL/SecRandom`
- About 页作者/组织/鸣谢署名行

---

## 8. 上游同步注意事项

1. **先读本文件**，再 merge/rebase 上游。
2. 高冲突文件：`build_publish.yml`、`SecRandom/App.axaml.cs`、`PrivacySettingsConfig.cs`、`UpdateCenterService.cs`、`VerificationDrawCoordinator.cs`、`WitnessClient.cs`、`FirstRunOobeViewModel.cs`、`FirstRunOobeWindow.axaml`、各 `Resources*.resx`。
3. 上游若新增/改动隐私政策或 OOBE 版本，需同步更新 `SecRandomPrivacyPolicy` 的「引用自 SecRandom vX.Y.Z」前置说明与 `CurrentPrivacyPolicyVersion`。
4. 上游若改动 `PluginApiVersions.Current` 主号，CI 回退版本会自动跟随；无需修改工作流。
5. 上游若重新启用/调整更新通道，需重新评估 `GlobalConstants.UpdatesEnabled` 的取值与端点替换。
6. 同步后必须回归验证：构建（含 Android 无签名降级路径）、OOBE 全流程（含新增勾选）、抽取证明链路（含 Beacon 开关）。

---

## 9. 记录维护

- 新增改动：追加到对应主题，并在「§0 变更索引」表格更新范围/风险。
- 新增主题：在 §1–§6 之后追加一节，并更新索引。
- 删除或回退某改动：保留条目并标注「已回退（日期/原因）」，不要直接删除历史。
- 本台账最初由 AI 助手在无法访问 git 的环境中依据改动内容整理（本机无 `git`，且仓库当时未提供可用的 upstream diff）。此后每次改动请**就地更新**，不要依赖事后重建。
