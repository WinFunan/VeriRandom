# FIDO2 硬件安全密钥支持

本文件描述本分支新增的 FIDO2 / WebAuthn 基础层。它**尚未**接入任何验证界面或 `ISecurityService` 因子选择：能力已经完整可用并有测试，但"哪些操作要求安全密钥"仍待决定。

## 目标与非目标

目标：

- 能把一把漫游安全密钥（YubiKey 等）或平台验证器登记到本机。
- 能要求该密钥重新证明"还是同一把钥匙"（签名一次新 challenge），并识别被复制的密钥。
- Windows / Linux / macOS 同一套实现，不引入原生 USB/HID 依赖。

非目标：

- 不校验 attestation（不判断厂商、不做密钥白名单）。
- 不替代主密码：`data/config/security/credentials.json` 的信封语义不变。
- 不在本层做"要求几步验证"的策略：那是 `ISecurityService` 与设置页的决定。

## 分层

```
浏览器页面（navigator.credentials）
        │  base64url JSON
        ▼
SecRandom/Services/Security/Fido2/Fido2BrowserTransport.cs   回环监听 + 页面
        ▼
SecRandom/Services/Security/Fido2/Fido2SecurityKeyService.cs 会话、存储、结果映射
        ▼
SecRandom.Core/Services/Security/Fido2/WebAuthnVerifier.cs   纯校验（无 UI、无文件、无网络）
        ▲
SecRandom/Services/Security/Fido2/Fido2CredentialStore.cs    凭据文件
```

Core 里的 `WebAuthn*` 只做解析与验签，不接触 Avalonia、文件或网络，因此既可以在桌面/移动共用，也可以在将来被其他宿主（例如自建校验服务）直接引用。

## 流程

### 登记

1. `WebAuthnOptionsBuilder.CreateChallenge()` 生成 32 字节 challenge；用户句柄在首次登记时生成，之后复用，保证同一用户的多把密钥是同一个 WebAuthn user。
2. `WebAuthnOptionsBuilder.BuildCreationOptions(...)` 生成 `publicKey` 选项：`rp.id = localhost`、`attestation = none`、`authenticatorAttachment = cross-platform`、`userVerification = preferred`、已登记凭据进 `excludeCredentials`。
3. `Fido2BrowserTransport` 起回环监听、用系统浏览器打开一次性页面，页面调用 `navigator.credentials.create`，把 `clientDataJSON` / `attestationObject` 回传。
4. `WebAuthnVerifier.VerifyRegistration(...)` 校验 type/challenge/origin、`rpIdHash`、UP 标志、attested credential data，并解析 COSE 公钥。
5. 成功记录写入 `data/config/security/fido2-keys.json`。

### 断言

1. 取出一把已登记凭据（未指定时用第一条），生成新 challenge，`allowCredentials` 只放这一把。
2. 页面调用 `navigator.credentials.get`，回传 `authenticatorData` / `signature` / `clientDataJSON`。
3. `WebAuthnVerifier.VerifyAssertion(...)` 用**存储时的公钥**验证 `authData ‖ SHA256(clientDataJSON)`，再检查计数器。
4. 成功只把计数器向前推进；`CounterRegression` 返回 `CloneSuspected = true`，调用方必须当作硬拒绝。

## 安全边界

- **RP ID 固定 `localhost`**：WebAuthn 禁止 IP 作为 RP ID，`localhost` 在所有浏览器里都是安全上下文。凭据绑定本机回环源，不能对别的 RP 重放。
- **回环页面的约束**：一次性会话 nonce 出现在 URL 且 POST 必须回传，监听只接受一次完成；响应带 `Cache-Control: no-store`、`Referrer-Policy: no-referrer`、`nosniff` 与 `default-src 'none'` 的 CSP。
- **凭据文件是明文但属主独占**：`data/config/security` 目录沿用既有加固。可写该文件的人能回退计数器（掩盖复制检测）或替换公钥（导致断言失败，即拒绝服务，不构成绕过）——与锁定状态同一边界。
- **不信任 attestation**：`fmt` 任意接受、`attStmt` 忽略，只取真实公钥。目标是证明持有，不是厂商白名单。
- **EdDSA（COSE -8）拒绝**：BCL 没有跨目标一致的 Ed25519 实现，宁可登记时报 `UnsupportedAlgorithm`，也不假装校验过。
- **计数器语义**：`stored > 0 && new > 0 && new <= stored` 才是回退；任一侧为 0 表示该密钥没有计数器，属于正常情况。

## 已知限制

- iOS 上 `Fido2SecurityKeyService.IsSupported` 为 false：没有可用的回环监听 + 浏览器路径，需要 head 注入 `ASAuthorization` 之类的原生 seam。
- 页面本身不写任何说明文字，因此本轮没有新增三语资源；浏览器自己的安全密钥提示已本地化。真正接入界面时，错误码到文案的映射应放进对应设置页的 `Langs/`。
- 本机若 `ECDsa.Create(ECCurve)` 不可用（CNG 提供程序缺失），`WebAuthnVerifierTests` 的 ES256 用例会 `Assert.Skip`；RS256 用例仍会执行。

## 后续接入点

1. `SecurityFactor` 增加 `Fido2` 值，`SecurityVerificationRequest` 带上"需要安全密钥"标志；`SecurityService.VerifyAsync` 在验证密码/TOTP/USB 之后调用 `Fido2SecurityKeyService.AssertAsync`。
2. 设置页新增"安全密钥"卡片：`ListCredentials` + `RegisterAsync` + `Remove`，并在 `S_*`/`M_*` 三语资源里补齐文案（错误码 → 文案）。
3. 只在**需要用户在场的保护操作**上要求断言；后台任务（备份、上报）不得弹出浏览器。
4. 若要支持"仅安全密钥"启动，需要先决定凭据信封在没有主密码时的密钥来源；本层的存储已经不受该限制。

## 测试

| 文件 | 覆盖 |
|---|---|
| `SecRandom.Core.Tests/WebAuthnVerifierTests.cs` | 合成认证器下的登记/断言全部接受与拒绝路径，含计数器回退与 RS256 |
| `SecRandom.Core.Tests/WebAuthnOptionsBuilderTests.cs` | 选项 JSON 的线格式字段名、base64url 形状、challenge 长度 |
| `SecRandom.Core.Tests/Fido2CredentialStoreTests.cs` | 持久化、两种 id 查找、重登记去重、计数器只增不减、损坏文件保留 |
