<div align="center">

<img src="resources/secrandom-icon-paper.png" width="128" height="128" alt="VeriRandom" />

# VeriRandom

**基于动态权重的公平随机工具，让抽取与决策告别争议**

[![GitHub Issues](https://img.shields.io/github/issues-search/WinFunan/VeriRandom?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/WinFunan/VeriRandom/issues)
[![最新版本](https://img.shields.io/github/v/release/WinFunan/VeriRandom?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/WinFunan/VeriRandom/releases/latest)
[![测试版本](https://img.shields.io/github/v/release/WinFunan/VeriRandom?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/WinFunan/VeriRandom/releases)
[![最后更新](https://img.shields.io/github/last-commit/WinFunan/VeriRandom?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/WinFunan/VeriRandom/commits/master)
[![累计下载](https://img.shields.io/github/downloads/WinFunan/VeriRandom/total?style=for-the-badge&color=00b4ab&label=累计下载)](https://github.com/WinFunan/VeriRandom/releases)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%20%7C%20768421833-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/EvhyCJWqCA)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE)

**语言** [ **简体中文** | [English](resources/README_EN.md) | [日本語](resources/README_JA.md) ]

</div>

> [!IMPORTANT]
> 此产品是 SecRandom 的一个开源分支，不应视作 SecRandom 本身

> [!NOTE]
> VeriRandom 以 GNU GPLv3 协议发布！您可以修改和再发布源代码，但再发布的衍生作品也必须遵循 GNU GPLv3

## VeriRandom

VeriRandom 是面向课堂、团队、活动、决策、重要利益分配等场景的公平抽取应用，同时也提供高度自定义化的随机数/随机字符串生成能力，相比 SecRandom ，它更注重抽取结果的可验证性以及密码学可靠性，旨在为抽取结果提供高度不可抵赖性与可复核性。

## 独有软件功能

1. **NIST Beacon 外部信标熵源 + Up/Own 双链证明**
可开启外部权威信标作为抽取种子，主持人无法自定义注入随机熵;Up链节点在迁移到上游后仍可兼容识别

2. **抽取可信度评分（普通模式证明评估）**
内置证据打分器，用户选择信任来源（链完整性、时间戳、服务端回执、人工时间佐证），自动生成可解释的可信度报告，直观看到这份抽签证明的证据完备程度，不再只能靠主观判断。

3. **可独立关闭 TSA 时间戳**
时间戳不再强制静默发起，增加开关；关闭时会弹窗告知信标周期锚定的耦合影响；隐私页明确披露：时间戳请求会向沃通发送去标识化信息。

4. **跨境数据传输闸门 + 抽取上传二选一，无默认上传**
SECTL 服务服务器位于境外，所有出境路径统一收敛到独立闸门；抽取结果上传给上游见证服务不再默认开启。

5. **隐私默认更保守：遥测、在线状态默认关闭**
首次开箱默认关闭遥测上报、在线状态。

6. **配置分离签名防篡改（主持人可信签发模式）**
新增「签名策略」安全模式：私钥由主持人在可信设备保管，抽取机只存放公钥。关键抽取策略（核验规则、公平配置等）签名锁定；一旦配置被篡改，启动时直接拦截。支持展示公钥指纹，抽签主界面可实时显示锁定状态指纹徽标，方便核对。

7. **安全规则调整：禁用仅 TOTP 单独验证**
出于安全风险考量，不允许只依靠TOTP验证码作为唯一验证方式，必须搭配密码或者USB令牌；升级时自动清理遗留的独立TOTP明文种子文件，减少明文密钥暴露风险。

8. **独立产品身份，可和原版 SecRandom 同机共存**
整套标识完全独立：独立程序名、安装目录、URI协议 `verirandom://`、单实例隔离、独立安装ID，不会和原版SecRandom抢占、覆盖、互相冲突；可选兼容开启旧 `secrandom://` 协议。

9. **闪抽两项易用增强**
焦点回落：闪抽弹窗关闭后，自动把输入焦点归还到之前的前台窗口（快捷键闪抽后翻页笔不会丢失课件焦点）
信标预取：开启信标时提前预拉脉冲，消除闪抽时网络卡顿等待感

10. **证明重放**
上游的客户端及验证软件仅验证证明文件宣称的抽取结果是否与理论输出吻合，不验证是否与显示结果匹配（可能客户端被替换）**证明重放**允许用户在本地输入一个受认可的名单文件，验证理论抽取**姓名**是否与抽取显示结果相匹配。

11. **裸随机数页（可复核的裸随机数生成）**
主界面新增「随机数」页：在给定范围生成一个裸随机数，每次生成都会立即写入独立证明链，事后可用熵源公布的公开脉冲重算同一个数。熵源可一次勾选多个，混入 random.org 或本地 CSPRNG 时当场提示可复核性受损；范围与熵源可由安全密码锁定，解锁必须重新验证，解锁状态随安全设置存进加密文件。

12. **课件标记（翻页笔也能自动抽取）**
把一张本应用生成的二维码贴进课件的那一页，翻到该页就按码里写好的参数自动抽取——翻页笔只需要翻页，不用点任何按钮。名单、人数、时机（延时或等你下一次按键）、是否显示动画与语音都编在二维码里，所以课件拿到别的电脑上照样可用；一节课只需点一次「开始新一节」，同一页一节课只触发一次。识别只在按下按键后读取一次放映画面，静止时不读屏，画面不落盘也不外传。

13. **证明详细信息**
在证明重放窗口选中任意一份证明即可查看与服务端验证页同款的公开参数：输入承诺、审计承诺、随机材料、候选池规则、抽样策略、平均差距保护、share-debt 参数等。弹窗顶部明确标注这些是**证明文件自身宣称的信息**，可信度需结合证据打分与重放结果判断。

14. **结果浮窗可缩放、也认得手指**
结果浮窗的四个边与四个角都能拖动缩放，调好的尺寸会被记住；拖动与缩放同时支持鼠标与触控（大屏上单指即可），双指乱按不会崩溃。

## ToDo List
- [ ] 基于信标预承诺的服务端nonce发放能力（避免基于等待的种子研磨）
- [ ] 多客户端集群可验证抽取（分布式VRF）

## 上下游共有特性

### 抽取流程

- **点名**：支持普通随机、历史平衡及重复控制
- **闪抽**：通过独立悬浮窗快速抽取学生
- **抽奖**：支持奖品盘和库存抽取，学生与奖品独立管理
- **丰富呈现**：统一配置动画、结果、语音、音乐和通知，并支持通知失败回退

### 公平与名单管理

- 根据历史次数、抽取间隔、分组、性别等因素动态调整权重，降低重复与分布失衡
- 使用稳定内部标识维护历史，学号、编号和名称仅作为显示信息
- 支持多名单、多奖品池及 `.xlsx`、`.xls`、`.csv` 导入、映射和预览
- 每轮抽取均保存历史，方便查询和回顾

### 抽取结果可复查

- 每次抽取都会自动保存证明记录文件
- 可以选择让服务器参与并见证抽取过程
- 可以通过官方渠道重新检查抽取结果

### 数据、隐私与安全

- 设置、名单和历史记录都可以导入、导出、备份和恢复
- 备份可以包含名单、历史、抽取证明、图片、音频等信息，但不会包含密码等安全信息
- 支持使用密码、TOTP或 U 盘保护重要操作，并可设置哪些操作需要验证

### 验证边界

| 模式 | 可以做到 | 不能证明 |
|---|---|---|
| 离线证明 | 复查已完成的抽取过程；事后改动证明文件、删除旧证明或删光重建都能被发现 | 不能证明抽取**之前**本地程序、随机种子或名单未被改动，也不能排除抽取前反复试验后选择结果 |
| 离线证明（信标，仅本分支） | 复查已完成的抽取过程；事后改动证明文件、删除旧证明、删光重建或恶意种子指定都能被发现 | 在无法确定确切抽取时间时，攻击者可以伺机等到有利结果再执行抽取 |
| 在线见证 | 保护服务端锁定后的抽取流程 | 不能证明名单真实、完整，或提交前未被筛选 |

补充说明：

- 每次抽取都会立即保存本地可重放证明，并把同一份证明提交给服务端重放签名；证明摘要还会交给第三方时间戳服务（只发送摘要，不发送名单或抽取内容）
- 本机证明按追加式哈希链保存，删除或改写会留下缺口；服务端会记住每台设备见过的链头，把本地证明删光重建会被识别为链位置回退
- 证明会承诺抽取时所用名单的「记录 ID → 姓名/学号/组别」映射摘要，因此**抽取之后**改名单、把中签记录指向别人会被发现
- **抽取之前**筛选名单（先删掉或禁用某个学生再抽）无法被证明：摘要只覆盖抽取那一刻的名单，要覆盖这类情况需要「抽取前先向第三方确认名单」的流程，目前不做
- 信标会锁定抽取熵，将信任根转嫁

## 技术演进

| 版本 | 技术栈 | 阶段 |
| --- | --- | --- |
| v1 | Python + PyQt5 + qfluentwidgets | 初代桌面实现 |
| v2 | Python + PySide6 + qfluentwidgets | Qt 技术栈演进 |
| **v3** | **C# + Avalonia + FluentAvalonia** | .NET 桌面重构，持续发展抽取、验证与桌面集成能力 |

## 版本下载与更新

- [GitHub Releases](https://github.com/WinFunan/VeriRandom/releases) 提供各版本的发行包与更新说明

## 上游版本下载与更新

- [GitHub Releases](https://github.com/SECTL/SecRandom/releases) 提供各版本的发行包与更新说明
- [上游仓库官方下载页面](https://stk.sectl.cn/SecRandom) 提供上游版本下载最新版入口
- 自动更新在部署前验证已签名的发布清单以及制品的长度和哈希；请以每个发行版本提供的安装包和说明为准

## 许可证与第三方声明

- VeriRandom 使用 [GNU GPLv3](LICENSE) 协议发布
- 第三方组件、版权和分发审查信息见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
- VeriRandom 通过历史平衡的权重与候选过滤策略帮助降低重复抽取、改善长期分布；它不替代对现实名单、规则或组织流程的管理，也不对这些现实条件作出软件无法验证的保证

## 贡献者和特别感谢

<a href="https://github.com/WinFunan/VeriRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=WinFunan/VeriRandom" alt="VeriRandom contributors" />
</a>

<a href="https://github.com/SECTL/SecRandom/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom" alt="SecRandom contributors" />
</a>

感谢每一位为 VeriRandom 与 SecRandom 提交代码、报告问题、完善文档和提供反馈的贡献者。头像由 GitHub 贡献者数据动态生成，点击可前往[本仓库Github 贡献者页面](https://github.com/WinFunan/VeriRandom/graphs/contributors) 或 [上游仓库GitHub 贡献者页面](https://github.com/SECTL/SecRandom/graphs/contributors) 查看完整统计

## 对本仓库支持与社区
- [QQ群 768421833](https://qm.qq.com/q/EvhyCJWqCA)
- [邮箱](mailto:love-code-yeyixiao@outlook.com)
- [Bilibili 主页](https://space.bilibili.com/510993086)
- [问题反馈](https://github.com/WinFunan/VeriRandom/issues)
- [简体中文贡献指南](CONTRIBUTING.md)

## 对上游仓库支持与社区

- [爱发电支持](https://afdian.com/a/lzy0983)
- [邮箱](mailto:lzy.12@foxmail.com)
- [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [QQ 频道](https://pd.qq.com/s/4x5dafd34?b=9)
- [Bilibili 主页](https://space.bilibili.com/520571577)
- [问题反馈](https://github.com/SECTL/SecRandom/issues)
- [SecRandom 官方文档](https://secrandom.sectl.cn/doc/overview.html)
- [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/SECTL/SecRandom)
- [简体中文贡献指南](https://github.com/SECTL/SecRandom/CONTRIBUTING.md)


**Copyright © 2025-2026 WinFunan**

**Copyright © 2025-2026 SECTL**
