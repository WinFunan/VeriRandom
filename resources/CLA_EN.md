# Contributor License Agreement (CLA)

**Version:** 1.0
**Effective date:** October 2, 2026
**Repository:** WinFunan/VeriRandom

> [!IMPORTANT]
>
> This document is a courtesy translation. If it conflicts with the Simplified Chinese version (`../CLA.md`), **the Simplified Chinese version prevails**.

By making a contribution to this project, and by signing as described in Section 8, you accept all terms of this agreement.

---

## 1. Definitions

- **"Project"**: this repository (VeriRandom) and the software, documentation, and assets maintained by the maintainers in it.
- **"Maintainer"**: the owner of this repository and the administrators the owner designates.
- **"You"**: the individual or legal entity making a contribution (including on behalf of an employer or another entity).
- **"Contribution"**: any work you submit to the Project in any form, including source code, tests, build and release scripts, documentation, designs, images, audio or video assets, and patches or snippets submitted in issues or comments.
- **"Upstream Project"**: SecRandom (<https://github.com/SECTL/SecRandom>) and its authors and rightsholders.
- **"Upstream Code"**: code and assets in this Project that are inherited from, or otherwise introduced from, the Upstream Project and are owned by third parties.

## 2. Grant of Copyright License

While you retain copyright in your Contribution, you grant the Maintainer a **worldwide, non-exclusive, perpetual, irrevocable, royalty-free, sublicensable, and transferable** copyright license to:

1. reproduce, modify, adapt, compile, publicly distribute, publicly communicate, publicly display, and publicly perform your Contribution and derivative works of it;
2. combine or merge your Contribution with other works;
3. subject to Section 4, **license or relicense your Contribution under any license terms, including open-source or commercial terms different from those in effect when you submitted it**.

## 3. Grant of Patent License

You grant the Maintainer and users of the Project a **worldwide, perpetual, irrevocable, royalty-free, and sublicensable** patent license, solely to make, have made, use, sell, offer for sale, import, and otherwise practice your Contribution.

If you (or an affiliate) bring a patent action against the Maintainer or a user of the Project alleging that the Project or your Contribution infringes a patent (including cross-claims and counterclaims in an action), the patent license granted in this Section 3 terminates for you as of the date that action is filed.

## 4. License Changes and Notice

1. **Default outbound license**: until the Maintainer changes it under this Section, your Contribution is distributed with the Project under **GNU GPLv3**.
2. **Right to change**: the Maintainer may change the outbound license terms that apply to your Contribution (and to the combined work that includes it).
3. **Notice obligation**: if the change would alter the license that applies to your Contribution, the Maintainer must give **at least 30 calendar days'** public notice in this repository, by release notes, README, a dedicated notice file, or an equivalent channel, stating the proposed new license and its effective date.
4. **Objection period**: during the notice period any contributor may raise an objection through this repository's issue tracker or the Maintainer's email. The Maintainer must negotiate in good faith, but may still proceed with the noticed change if no agreement is reached.
5. **Exclusions**: this Section does **not** apply to:
   1. Upstream Code or any other work provided by a third party under GPLv3 or another license; that work always remains under its original license terms, which this agreement cannot change;
   2. any work for which the Maintainer has not obtained the necessary authorization.
6. **No retroactivity**: contributions that already existed before this agreement took effect are governed by this Section only if their copyright holder separately agrees.

## 5. Your Representations and Warranties

You represent and warrant that:

1. you own the copyright in your Contribution, or have sufficient authorization to grant the rights in Sections 2 and 3;
2. your Contribution is your original work, or you have clearly marked its source and applicable license and comply with that license;
3. if you contribute on behalf of an employer or another entity, you are authorized by that entity;
4. your Contribution does not infringe any third party's copyright, patent, trademark, or other rights;
5. your Contribution contains no confidential or third-party proprietary information you are not permitted to disclose.

## 6. Moral Rights

To the maximum extent permitted by applicable law, you waive, or agree not to assert, moral rights in your Contribution against the Maintainer and users of the Project. In jurisdictions where moral rights cannot be waived, you agree to exercise them in a way that does not defeat the purpose of this agreement.

## 7. No Obligation and No Warranty

1. The Maintainer is **not obligated** to use, merge, or publish any of your contributions.
2. Except as expressly stated here, your Contribution is provided **"AS IS"**, without any express or implied warranty, including merchantability, fitness for a particular purpose, and non-infringement.

## 8. Notice and Signing

1. **How to sign**: you sign this agreement by posting the following statement in a pull request to this Project (the Maintainer may designate an equivalent method, such as a CLA bot or a `Signed-off-by` trailer):

   > I have read the CLA Document and I hereby sign the CLA
   >
   > 我已阅读 CLA 文件并在此签署本协议

2. **Withdrawal**: you may withdraw your acceptance at any time by email to the Maintainer or by an issue statement. Withdrawal applies only to **future** contributions; it does not affect the rights granted for contributions you submitted before the withdrawal.
3. **Contact**: `love-code-yeyixiao@outlook.com`.

## 9. Governing Law and Dispute Resolution

This agreement is governed by the laws of the **People's Republic of China**, applied without regard to conflict-of-law rules. The parties will first attempt to resolve any dispute in good faith; failing that, the dispute will be submitted to the **Ningxiang City People's Court, Hunan Province**.

---

## Appendix A: Existing Code in This Project

This repository is an **open-source fork of SecRandom** and contains code the Upstream Project released under GNU GPLv3. Therefore:

1. The Maintainer **cannot** change the license of Upstream Code through this agreement. Upstream Code is always licensed by the Upstream Project and its rightsholders under GPLv3.
2. This agreement binds **only the contributions of the people who sign it**. Contributions that already existed before it took effect require separate consent from their copyright holders, or keep their original license.
3. "Changing the license of existing code" is therefore effective in practice only for:
   - parts the Maintainer owns the copyright in;
   - parts submitted by contributors who have explicitly agreed to the change.
4. Because the Project as a whole is a derivative/combined work of GPLv3 Upstream Code, **any distribution must still satisfy GPLv3**; this agreement does not change that.

To relicense existing code you must first: map copyright ownership per part → obtain the necessary consents → complete public notice → update `LICENSE`, `THIRD-PARTY-NOTICES.md`, and per-file headers.

---

## Appendix B: Corporate Contributions

If you contribute on behalf of an employer or another legal entity, that entity should sign a corporate CLA (which can extend Sections 2 and 3) identifying its authorized contributors and scope. Until a corporate CLA is in place, do not submit code owned by that entity.
