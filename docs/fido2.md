# FIDO2 硬件安全密钥支持

本文件描述本分支的 FIDO2 / WebAuthn 实现：它既是一个**验证因子**（和主密码、TOTP、USB 绑定并列），也是**分离配置的签发手段**（让不受信任的机器在不持有私钥的情况下签发签名文档）。

## 目标与非目标

目标：

- 能把一把漫游安全密钥（YubiKey 等）或平台验证器登记到本机。
- 能要求该密钥重新证明"还是同一把钥匙"（签名一次新 challenge），并识别被复制的密钥。
- 能用同一把密钥对当前配置签发分离签名文档，签发机器不需要任何私钥。
- Windows / Linux / macOS 同一套实现，不引入原生 USB/HID 依赖。

非目标：

- 不校验 attestation（不判断厂商、不做密钥白名单）。
- 不替代主密码：`data/config/security/credentials.json` 的信封语义不变，密码仍是基础因子。
- 不在本层决定"哪些操作要求几步验证"：那是 `ISecurityService` 与设置页的决定。

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

Core 里的 `WebAuthn*` 只做解析与验签，不接触 Avalonia、文件或网络，因此可以在桌面/移动共用，也能被其他宿主直接引用。`WebAuthnOriginPolicy` 是两个校验入口共用的回环来源检查。

## 因子流程

### 登记

1. `WebAuthnOptionsBuilder.CreateChallenge()` 生成 32 字节 challenge；用户句柄首次生成、之后复用，保证同一用户的多把密钥是同一个 WebAuthn user。
2. `BuildCreationOptions(...)`：`rp.id = localhost`、`attestation = none`、`authenticatorAttachment = cross-platform`、`userVerification = preferred`、已登记凭据进 `excludeCredentials`。
3. `Fido2BrowserTransport` 起回环监听、用系统浏览器打开一次性页面，页面调用 `navigator.credentials.create` 回传 `clientDataJSON` / `attestationObject`。
4. `WebAuthnVerifier.VerifyRegistration(...)` 校验 type/challenge/origin、`rpIdHash`、UP 标志、attested credential data，并解析 COSE 公钥。
5. 记录写入 `data/config/security/fido2-keys.json`；设置页要求先通过一次安全授权。

### 验证

1. `SecurityService.AuthorizeAsync`/`AuthorizeSettingsAsync` 构建验证请求时调用 `IssueFido2Challenge`，把一次性 challenge 放进 `SecurityVerificationRequest.Fido2Challenge`。
2. 验证对话框的"使用安全密钥"按钮调用 `Fido2SecurityKeyService.AssertAsync(null, challenge)`，成功后把 `Fido2AssertionProof` 放进响应。
3. `SecurityService.VerifyAsync` 调用 `VerifyFido2`：用凭据文件里的 COSE 公钥验证 `authData ‖ SHA256(clientDataJSON)`，challenge 必须等于本次发放值，origin 必须回环，计数器不前进判为 `CloneSuspected`。
4. 该 challenge 无论成功失败都被清空——一次断言只能回答一次验证。

密码仍是基础因子：`NormalizeSettings` 在没有主密码或没有已登记密钥时关闭 `Fido2Enabled`。在"任意已选验证方式"模式下，安全密钥可以像 USB 一样单独通过（不解锁凭据信封）。

## 分离配置签发

`ConfigSignature` 有两种算法：

| 算法 id | 私钥在哪 | 用于 |
|---|---|---|
| `ecdsa-p256-sha256` | 可信设备上的 PKCS#8 文件 | 原有方式 |
| `webauthn-es256-v1` | 安全密钥内部，永不导出 | 在不受信任的机器上当地签发 |

签发流程：

1. `ConfigPolicyDigest.Compute(...)` 得到配置摘要（作用域与本地指纹模式一致）。
2. `ConfigSignature.CreateConfigChallenge(digest)` 派生 challenge：
   `Base64Url(SHA256("VeriRandom/ConfigSignature/webauthn-v1\0" ‖ digest))`。
3. 浏览器断言签名覆盖 `authData ‖ SHA256(clientDataJSON)`，而 clientDataJSON 里带着上面的 challenge。
4. `ConfigPolicySignatureService.SignWithSecurityKeyAsync` 把 `clientDataJSON` / `authenticatorData` / `signature` / COSE 公钥 / origin / 计数写成 `config-policy.json`。

验证端（`ConfigSignature.VerifyWebAuthn`）：

- origin 必须是回环页面；
- clientDataJSON 的 challenge 必须等于按当前配置摘要重算的值（因此签名不能挪到另一份配置）；
- 用文档里的 COSE 公钥验签；
- 不比较签名计数器（文档是唯一的断言，新鲜性由一次性 challenge 保证）。

`ConfigPolicySignatureService.Verify()` 按算法分派，ECDSA 与安全密钥文档并存；旧客户端读到新算法会判为格式不符，而不是误判为已签名。

## 安全边界

- **RP ID 固定 `localhost`**：WebAuthn 禁止 IP 作为 RP ID，`localhost` 在所有浏览器里都是安全上下文。凭据绑定本机回环源。
- **回环页面的约束**：一次性会话 nonce 出现在 URL 且 POST 必须回传，监听只接受一次完成；响应带 `Cache-Control: no-store`、`Referrer-Policy: no-referrer`、`nosniff` 与 `default-src 'none'` 的 CSP。
- **凭据文件是明文但属主独占**：可写该文件的人能回退计数器（掩盖复制检测）或替换公钥（导致断言失败，即拒绝服务，不构成绕过）。
- **不信任 attestation**：`fmt` 任意接受、`attStmt` 忽略，只取真实公钥。
- **EdDSA（COSE -8）拒绝**：BCL 没有跨目标一致的 Ed25519 实现，宁可报 `UnsupportedAlgorithm`。
- **计数器语义**：因子验证用存储计数比较（`stored > 0 && new > 0 && new <= stored` 才是回退）；分离配置文档不比较计数。

## 已知限制

- iOS 上 `IsSupported` 为 false：需要 head 注入 `ASAuthorization` 之类的原生 seam。
- 浏览器页面本身不写说明文字，所以没有新增页面资源；所有文案都在安全设置页的三语资源里。
- 本机若 `ECDsa.Create(ECCurve)` 不可用（CNG 提供程序缺失），`WebAuthnVerifierTests` 的 ES256 用例会 `Assert.Skip`；RS256 用例仍会执行。

## 测试

| 文件 | 覆盖 |
|---|---|
| `SecRandom.Core.Tests/WebAuthnVerifierTests.cs` | 合成认证器下的登记/断言全部接受与拒绝路径，含计数器回退与 RS256 |
| `SecRandom.Core.Tests/WebAuthnOptionsBuilderTests.cs` | 选项 JSON 的线格式字段名、base64url 形状、challenge 长度 |
| `SecRandom.Core.Tests/Fido2CredentialStoreTests.cs` | 持久化、两种 id 查找、重登记去重、计数器只增不减、损坏文件保留 |
| `SecRandom.Core.Tests/ConfigSignatureWebAuthnTests.cs` | 配置 challenge 的确定性与域分离、按摘要验证、换摘要/换 origin/改签名失败 |
| `SecRandom.Core.Tests/SecurityServiceTests.cs` | 因子级：安全密钥单独通过、旧 challenge 被拒、同一断言不能重放 |
