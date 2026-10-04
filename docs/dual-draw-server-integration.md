# 双抽取预承诺协议 · 服务端对接文档

> 面向：自建预承诺服务端的实现者
> 客户端：VeriRandom（本仓库）· 协议版本：`1`
> 状态：**设计已确认**（§13 的六项取舍已全部采纳），尚未有实现。

---

## 0. 目的、信任边界与非目标

### 目的

1. **强制披露**：使「用了某个承诺却不说」在外部可被发现。
2. **结果不可事后操纵**：承诺必须覆盖抽取输入，使得指派脉冲发布之后**无法再挑名单或改规则**。

### 信任边界（必须在宣传口径里讲清）

| 场景 | 这套协议是否有意义 |
|---|---|
| **抽取者 ≠ 服务端运营者**（例：教师抽取，管理员跑服务端） | ✅ 服务端记录可作为独立见证 |
| **抽取者 = 服务端运营者**（同一人自建自抽） | ⚠️ **自我背书**，挡不住运营者本人 |

协议**不**证明：名单真实、名单完整、被抽者知情、程序二进制未被替换。
协议**不**防：服务端与抽取者合谋（此时记录可被一致地伪造）。

### 关键区分（务必对齐预期）

| 目标 | 只承诺 `nonce` 是否足够 |
|---|---|
| 强制披露「用了哪个承诺」 | ✅ 足够 |
| **结果不可操纵** | ❌ **不够**，必须连同名单摘要与抽取参数一起承诺 |

---

## 1. 术语

| 术语 | 含义 |
|---|---|
| **承诺**（commitment） | 服务端为一次抽取签发的记录：`nonce` + 指派脉冲 + 输入摘要 + 时间 |
| **指派脉冲**（assigned pulse） | 服务端指定的、**在签发时刻尚未发布**的某个具体脉冲 |
| **消费**（consume） | 客户端用该承诺完成抽取并公开证明 |
| **未消费承诺** | 已签发但没有任何证明与之对应的承诺——**这正是要暴露的东西** |
| **承诺摘要**（commitment hash） | 覆盖承诺核心字段的规范哈希，客户端对它申请 TSA |

---

## 2. 时序总览

```
客户端                                            服务端
  │                                                │
  │── POST /v1/commitments ──────────────────────> │ 1. 生成 nonce（CSPRNG，≥256 位）
  │   {rosterDigest, 抽取参数, …}                   │ 2. 选定指派脉冲（尚未发布）
  │                                                │ 3. 追加到哈希链并落盘（先落盘再响应）
  │                                                │ 4. 对整个响应签名
  │ <── 200 {commitmentId, nonce, assignedPulse,   │
  │          commitmentHash, chainHead, signature} │
  │                                                │
  │ 5. 立即对 commitmentHash 申请 TSA ← 预承诺证据
  │ 6. 等待指派脉冲发布，按内置材料校验其签名
  │ 7. seed = KDF(nonce ‖ 脉冲身份 ‖ 脉冲值)   ← 确定性，禁止任何随机数发生器
  │ 8. 抽取、落证明
  │── POST /v1/draws（披露消费，见 §8） ─────────> │ 9. 标记已消费
```

**第 5 步是整个协议的证据核心**：预抽取的 TSA 令牌由客户端持有，即使服务端事后否认、
或公开归档被改写，该令牌仍能证明「这个承诺在脉冲发布之前就已存在」。

**第 7 步必须写成确定性 KDF。** 客户端代码里不得出现任何 `RandomNumberGenerator` /
`Random` 调用——否则种子不可复现，整套重放验证失效。

---

## 3. 数据模型：承诺记录

服务端**先落盘、再响应**。落盘失败必须返回错误，不得签发。

```
CommitmentRecord {
  sequence        : 单调递增整数（从 0 或 1 起，全库唯一，不得回退）
  commitmentId    : UUID
  issuedAtUtc     : RFC3339 UTC，服务端时钟
  clientId        : 客户端标识（设备 UUID）
  clientNonce     : 客户端幂等标识（可选）
  inputDigest     : 见 §5
  nonce           : base64url，≥32 字节，CSPRNG
  assignedPulse   : 见 §6
  commitmentHash  : 见 §5
  prevHash        : 上一条记录的 recordHash
  recordHash      : 见 §7
  consumed        : null | { proofId, reportedAtUtc, ... }   // 由 §8 的披露写入
}
```

**只可追加**：存储层不得存在 UPDATE / DELETE 路径。`consumed` 是唯一允许的原地更新字段，
且必须单独记录（或改为追加一条「消费披露」记录，推荐后者，见 §7）。

### 幂等

同一 `clientId` + `clientNonce` 重复请求必须返回**同一条**承诺，不得签发新的。
否则客户端可以靠重试批量要 nonce。

### 并发与配额

- **建议**：对同一 `clientId` + `drawKind` 限制「同时只有一个未消费承诺」。
- **必须**：无论是否限制配额，**每一次签发都要入库**。批量索取在记录上必须留下痕迹。

---

## 4. 端点

### 4.1 `POST /v1/commitments`

**请求**

```json
{
  "protocolVersion": 1,
  "clientId": "b1f3…-device-uuid",
  "clientNonce": "9XtQ…",
  "drawKind": "roll-call",
  "count": 1,
  "samplingMode": "<客户端 VerificationSamplingMode 标识>",
  "algorithmProfile": "<客户端 VerificationAlgorithmProfile 标识>",
  "rosterDigest": "<hex，64 字符，见 §5>",
  "rosterSize": 42
}
```

**响应**

```json
{
  "protocolVersion": 1,
  "sequence": 12345,
  "commitmentId": "8f2c…",
  "issuedAtUtc": "2026-10-04T04:59:30.123Z",
  "nonce": "<base64url，32 字节>",
  "inputDigest": "<hex>",
  "commitmentHash": "<hex>",
  "assignedPulse": {
    "source": "drand-quicknet",
    "chainHash": "52db9ba70e0cc0f6eaf7803dd07447a1f5477735fd3f661792ba94600c84e971",
    "round": 987654,
    "publishedAt": "2026-10-04T05:00:00.000Z"
  },
  "chainHead": { "sequence": 12345, "recordHash": "<hex>" },
  "serverKeyId": "<密钥标识>",
  "signature": "<base64url，ECDSA P-256 + SHA-256，DER，覆盖 §5 的响应投影>"
}
```

**硬性要求**

| 项 | 要求 |
|---|---|
| `nonce` | CSPRNG，**≥ 256 位**；不得用计数器、时间戳或任何可预测值 |
| `assignedPulse` | 必须**点名到一个具体脉冲**（§6），不得是「下一个」这类相对描述 |
| 未发布 | `assignedPulse.publishedAt` 必须**严格晚于** `issuedAtUtc`，且**至少留 1 个完整周期**（§13-4），便于对外表述为「请求时刻该脉冲不可能已知」 |
| 落盘顺序 | 先写入并 fsync，再返回 |
| 签名 | 整个响应必须签名，使公开归档自证，而不必「去信 GitHub 上那份快照」 |
| 不得改派 | 签发后**不得**更换来源或脉冲 |

### 4.2 `GET /v1/commitments/{commitmentId}`

返回该承诺记录原文（含 `sequence`、`recordHash`、`prevHash`、签名），用于对账与举证。

### 4.3 `GET /v1/commitments/issued?fromSequence=&limit=`

分页列出已签发承诺（**含未消费的**）。审计的最小输入。

### 4.4 `GET /v1/chain/head`

```json
{ "sequence": 12345, "recordHash": "<hex>" }
```

客户端与审计方在承诺时刻记下它；日后链头若与该值矛盾即可发现改写。

### 4.5 `GET /v1/chain/proof/{sequence}`（建议）

给出某条记录的链上包含证明，使第三方无需下载全量日志即可验证。

### 4.6 `POST /v1/draws`（披露消费）

```json
{
  "protocolVersion": 1,
  "commitmentId": "8f2c…",
  "proofId": "<客户端证明 ID>",
  "proofHash": "<hex>",
  "consumedAtUtc": "2026-10-04T05:00:03.412Z"
}
```

**这不是权威声明**，只是诚实客户端的主动披露，用来把证明与承诺对上。
不披露不会让作弊消失——它会让该承诺**永久显示为未消费**，而这正是要暴露的信号（§8）。

---

## 5. 规范哈希

### `inputDigest`

覆盖**抽取输入**，使脉冲发布后无法改名单或改规则：

```
inputDigest = SHA-256(
  "VeriRandomCommitmentInput/v1" ‖
  drawKind ‖
  count ‖
  samplingMode ‖
  algorithmProfile ‖
  rosterSize ‖
  rosterDigest
)
```

- 各字段按 `字段名=值` 升序、`\n` 分隔后 UTF-8 编码（实现须逐字节一致）。
- `rosterDigest` **沿用客户端既有算法**：对「中奖 RecordId → 展示信息」的规范映射求 SHA-256，
  条目按 record id 排序。服务端**不重算**它（没有名单），只登记。

### `commitmentHash`（客户端对它申请 TSA）

```
commitmentHash = SHA-256(
  "VeriRandomCommitment/v1" ‖
  commitmentId ‖
  sequence ‖
  issuedAtUtc ‖
  inputDigest ‖
  nonce ‖
  assignedPulse.source ‖
  assignedPulse.chainHash(or chainIndex) ‖
  assignedPulse.round(or pulseIndex) ‖
  assignedPulse.publishedAt
)
```

时间一律用 RFC3339 UTC、**毫秒精度且固定 3 位小数**（`2026-10-04T04:59:30.123Z`），
否则两侧哈希不一致。

### 响应签名投影

对 `commitmentHash` + `chainHead.recordHash` + `serverKeyId` 的同样规范编码签名。
服务端必须**同时返回 `commitmentHash`**，便于客户端交叉核对。

---

## 6. 指派脉冲的身份格式

### 6.1 `drand-quicknet`

| 字段 | 值 |
|---|---|
| `source` | `drand-quicknet` |
| `chainHash` | `52db9ba70e0cc0f6eaf7803dd07447a1f5477735fd3f661792ba94600c84e971` |
| `period` | **3 秒** |
| `genesis` | `1692803367` |
| `scheme` | `bls-unchained-g1-rfc9380` |
| 链公钥（96 字节 hex） | `83cf0f2896adee7eb8b5f01fcad3912212c437e0073e911fb90022d3e760183c8c4b450b6a0a6c3ac6a5776a2d1064510d1fec758c921cc22b0e17e63aaf4bcb5ed66304de9cf809bd274ca73bab4af5a6e9c76a4bc09e76eae8991ef5ece45a` |

- 轮次与时间：`round = floor((t − genesis) / period)`；`publishedAt = genesis + round × period`
- `unchained` ⇒ 任意单轮**只需链公钥独立可验**，历史验证无需重放整条链。
- `60 / 3 = 20` 整除，且 genesis 落在 3 秒网格上 ⇒ 每个 NIST 分钟正好 20 轮，
  每 10 秒 3 轮。

取脉冲：`GET https://api.drand.sh/{chainHash}/public/{round}`

### 6.2 `nist-beacon-v2`

| 字段 | 值 |
|---|---|
| `source` | `nist-beacon-v2` |
| 端点 | `https://beacon.nist.gov/beacon/2.0/` |
| `period` | **60 秒**（注意：API 的 `period` 字段以**毫秒**返回，值为 `60000`） |
| 身份 | `chainIndex` + `pulseIndex` + `timeStamp`（观察值 `chainIndex = 2`；**以 NIST 实际返回为准，不得写死**） |
| 证书 Subject 约束 | `CN=engine.beacon.nist.gov` |

- 时间戳**精确落在整分**（如 `2026-10-04T04:42:00.000Z`）。
- **没有可内置的固定公钥**：叶子证书按 `certificateId` 约每半年轮换，签发者是公共 WebPKI 的
  DigiCert，不是 NIST 自有 CA。因此校验方内置的是**身份约束**（Subject 必须等于上式），
  CA 链交给系统信任库。
- 取证书：`GET https://beacon.nist.gov/beacon/2.0/certificate/{certificateId}`

### 6.3 来源选择

- 签发时**二选一**（当前不支持混合）。
- **默认来源为 `drand-quicknet`**（§13-5）：NIST 周期 60 秒意味着承诺后最长等待一分钟，
  且 NIST 有停摆历史；而在预承诺模式下指派脉冲缺席**不能换源**（换源即毁承诺）。
- **必须**定义「指派脉冲在超时窗口内未发布」的处理：承诺标记为 `pulse-missing` 并**公开**，
  抽取失败。**不得静默换源。**

---

## 7. 哈希链与公开归档

```
recordHash = SHA-256(
  "VeriRandomCommitmentChain/v1" ‖
  sequence ‖
  prevHash ‖
  commitmentHash
)
```

- `sequence` 从 0（或 1）起单调递增且无缺号，`prevHash` 指向 `sequence − 1` 的 `recordHash`。
- 服务端对外提供 `GET /v1/chain/head`；客户端在承诺时刻留存该链头。

### 定期上传 GitHub 的要求

「定期把本地缓存上传」本身**不构成**防篡改——定期上传可以被重写、被跳过、被删条目。因此：

1. 上传内容必须包含**截至某个 `sequence` 的全部记录** + 对应的链头哈希。
2. **不得改写已发布过的 `sequence`**：任何回退都会破坏链，外部可发现。
3. 链头需在**第二个位置**可见（例如每次承诺响应里回带，由客户端各自留存）。
4. 归档应可**自证**：记录带服务端签名，校验者无需信任 GitHub 的托管方。

> 定位：GitHub 归档是**冗余的第二份记录**，不是信任根。信任根是客户端手上的预抽取 TSA 令牌。

---

## 8. 对账流程（强制）

没有这一步，「用了又不说」根本不会被发现——数据在，但没人看。

**对账输入**

- A = 某时间窗内 `GET /v1/commitments/issued` 返回的**全部**承诺（含未消费）
- B = 已公开证明中披露的 `commitmentId` 集合（来自 §4.6 或客户端证明索引）

**对账规则**

| 情形 | 结论 |
|---|---|
| `c ∈ A` 且 `c ∈ B` | 正常：可进一步校验该证明用的脉冲与 `c` 指派的脉冲一致 |
| `c ∈ A` 且 `c ∉ B` | **未消费承诺**——必须追查并记录原因（放弃抽取／程序崩溃／见不得光） |
| `c ∈ B` 且 `c ∉ A` | **服务端缺记录**——链被改写或记录丢失，视为严重事件 |

**结论必须落到书面**：每一笔未消费承诺都要有归档结论。否则「差额存在但无人追查」
与「没有差额」在外部看起来一样。

---

## 9. 客户端会做的校验（服务端必须满足这些前提）

1. 响应签名在服务端公钥下验证通过
2. `assignedPulse.publishedAt > issuedAtUtc`
3. `inputDigest` 与客户端自行重算的值一致
4. `commitmentHash` 与客户端自行重算的值一致
5. 对 `commitmentHash` 取得的 TSA 令牌验证通过（签名 + 证书链）
6. 指派脉冲按 §6 的内置材料验证通过
7. `seed = KDF(nonce ‖ 脉冲身份 ‖ 脉冲值)`，**不含任何本地随机数、时钟或调用方数据**

任何一条不成立，客户端必须**中止抽取**，而**不得**回退到本地熵或另一个来源。

---

## 10. 错误与边界情形

| 情形 | 要求 |
|---|---|
| 脉冲超时未发布 | 承诺标记 `pulse-missing` 并公开；抽取失败；**不得换源** |
| 客户端在承诺与抽取之间崩溃 | 承诺保持未消费并公开可见；重启后**不得**重新选脉冲 |
| 重复请求（同 `clientNonce`） | 返回同一承诺，不新签 |
| 落盘失败 | 返回错误，**不签发**（避免「签发了但无记录」） |
| 时钟不可信 | `issuedAtUtc` 只有在服务端时钟可信时才有意义；**必须**同步 NTP 并记录偏差 |
| 指派脉冲存在但校验失败 | 视为严重事件（来源被替换），公开记录；抽取失败 |

---

## 11. 安全与运维要求

- **传输**：仅 HTTPS/TLS；客户端不得接受明文降级。
- **签名密钥**：服务端签名密钥独立于 TLS 证书；公钥需有稳定的公开位置供客户端内置或钉住。
- **存储**：承诺链存储层禁止 UPDATE/DELETE；建议 WAL + fsync 后再响应。
- **日志**：不得记录名单内容（只有摘要）；不得把 `nonce` 之外的敏感信息写入日志。
- **不做**：不把 `nonce` 的生成依赖任何可被客户端影响的状态（如客户端提供的种子）。
- **宣传口径**：必须写明「只在抽取者与服务端运营者分离时有意义」，且不证明名单真实性。

---

## 12. 与客户端既有字段的对应

| 本文档字段 | 客户端对应物 |
|---|---|
| `rosterDigest` | 客户端 `RosterDigest.Compute` 的输出（`RecordId` → 展示信息的规范映射） |
| `samplingMode` | `VerificationSamplingMode` 标识 |
| `algorithmProfile` | `VerificationAlgorithmProfile` 标识 |
| `drawKind` | 点选（`roll-call`）／抽奖（`lottery`） |
| 预承诺记录 | 落在 **Own 侧新增的独立存储**，**不进 Up 链** |

> **Up 链保持不变**，因此上游 SecRandom 仍可直接识别。预承诺是 fork 自有的旁挂记录，
> 且**不得**放进既有 Own 链——`OwnProofChainStore` 的不变式（每节点对应一个 Up 序号、
> 序号从 0 公差 1、`ComputeSelfHash` 覆盖字段）会被「一个 Up 序号对两个 Own 节点」破坏。

---

## 13. 已确认的设计决定

以下六项**已确认采纳**，实现时必须满足。「理由」列说明每一条为什么存在——每项都对应一个具体的失败模式，不是风格偏好。

| # | 必须实现 | 理由 |
|---|---|---|
| 1 | 承诺必须包含 `rosterDigest` 与抽取参数（§5） | 脉冲发布后即可算出种子，此时改名单或改规则能让**所有校验都通过**却操纵结果 |
| 2 | 记录必须哈希链化，链头在第二处可见（§7） | 定期上传可被重写／跳过；缺此则「强制披露」不成立 |
| 3 | 对账流程必须强制且留书面结论（§8） | 差额无人追查，等同于没有披露要求 |
| 4 | 指派脉冲至少留 1 个完整周期（§3） | 无法对外表述「请求时刻该脉冲不可能已知」（**非**硬性安全需求） |
| 5 | 默认来源取 `drand-quicknet`（§6.3） | NIST 60 秒等待 + 停摆即卡死，且承诺后不能换源 |
| 6 | 预承诺不进 Own 链，单独存储与校验（§12） | 否则破坏既有 Own 链不变式，需要改证明格式 |
